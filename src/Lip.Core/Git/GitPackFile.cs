using System.Security.Cryptography;

namespace Lip.Core.Git;

/// A version 2/3 packfile backed by a seekable stream (typically a temporary file). Objects are
/// inflated on demand and deltas are resolved against objects in the same pack.
public sealed class GitPackFile : IDisposable {
  private const int OfsDelta = 6;
  private const int RefDelta = 7;
  private const long CacheBudget = 64L * 1024 * 1024;

  /// Upper bound on a single object's size, guarding against decompression bombs.
  public const long MaxObjectSize = 512L * 1024 * 1024;

  private readonly Stream _stream;
  private readonly BufferedByteReader _reader;
  private readonly List<Entry> _entries = [];
  private readonly Dictionary<long, int> _indexByOffset = [];
  private readonly Dictionary<string, int> _indexByObjectId = [];
  private readonly Dictionary<int, (GitObjectType Type, byte[] Content)> _cache = [];
  private readonly HashSet<int> _ofsDeltaBases = [];
  private readonly HashSet<string> _refDeltaBases = [];
  private long _cacheBytes;
  private bool _isFullyIndexed;

  private GitPackFile(Stream stream) {
    _stream = stream;
    _reader = new BufferedByteReader(stream);
  }

  public int Count => _entries.Count;

  /// Opens a packfile, verifying its checksum and scanning all object headers. Takes ownership of
  /// the stream, which must be readable and seekable.
  public static GitPackFile Open(Stream stream) {
    GitPackFile pack = new(stream);
    try {
      pack.Scan();
      return pack;
    }
    catch {
      pack.Dispose();
      throw;
    }
  }

  /// Applies a git delta to a base object.
  public static byte[] ApplyDelta(byte[] baseContent, byte[] delta) {
    int position = 0;
    long sourceSize = ReadDeltaSize(delta, ref position);
    long resultSize = ReadDeltaSize(delta, ref position);
    if (sourceSize != baseContent.Length) {
      throw Corrupt("delta base size mismatch");
    }
    if (resultSize > MaxObjectSize) {
      throw Corrupt("delta result too large");
    }

    byte[] result = new byte[resultSize];
    int outPosition = 0;
    while (position < delta.Length) {
      byte op = delta[position++];
      if ((op & 0x80) != 0) {
        long offset = 0;
        long size = 0;
        for (int i = 0; i < 4; i++) {
          if ((op & (1 << i)) != 0) {
            offset |= (long)ReadDeltaByte(delta, ref position) << (8 * i);
          }
        }
        for (int i = 0; i < 3; i++) {
          if ((op & (0x10 << i)) != 0) {
            size |= (long)ReadDeltaByte(delta, ref position) << (8 * i);
          }
        }
        if (size == 0) {
          size = 0x10000;
        }
        if (offset + size > baseContent.Length || outPosition + size > result.Length) {
          throw Corrupt("delta copy out of range");
        }
        Buffer.BlockCopy(baseContent, (int)offset, result, outPosition, (int)size);
        outPosition += (int)size;
      } else if (op != 0) {
        if (position + op > delta.Length || outPosition + op > result.Length) {
          throw Corrupt("delta insert out of range");
        }
        Buffer.BlockCopy(delta, position, result, outPosition, op);
        position += op;
        outPosition += op;
      } else {
        throw Corrupt("invalid delta opcode");
      }
    }
    if (outPosition != result.Length) {
      throw Corrupt("delta result size mismatch");
    }
    return result;
  }

  /// Resolves and yields every object in pack order. Each object id is computed from the content,
  /// so returned objects are SHA-1 verified.
  public IEnumerable<GitObject> ReadObjects() {
    List<int> deferred = [];
    for (int i = 0; i < _entries.Count; i++) {
      if (!CanResolve(i)) {
        deferred.Add(i);
        continue;
      }
      yield return ReadAndIndex(i);
    }

    // REF_DELTA bases may appear after their deltas.
    while (deferred.Count > 0) {
      List<int> stillDeferred = [];
      foreach (int i in deferred) {
        if (CanResolve(i)) {
          yield return ReadAndIndex(i);
        } else {
          stillDeferred.Add(i);
        }
      }
      if (stillDeferred.Count == deferred.Count) {
        throw Corrupt("missing delta base");
      }
      deferred = stillDeferred;
    }
    _isFullyIndexed = true;
  }

  /// Finds an object by id, or returns null if the pack does not contain it.
  public GitObject? FindObject(string objectId) {
    if (!_isFullyIndexed) {
      foreach (GitObject _ in ReadObjects()) {
      }
    }
    if (!_indexByObjectId.TryGetValue(objectId, out int index)) {
      return null;
    }
    (GitObjectType type, byte[] content) = Resolve(index);
    string actual = GitObject.ComputeObjectId(type, content);
    if (actual != objectId) {
      throw Corrupt($"object {objectId} hashes to {actual}");
    }
    return new GitObject(objectId, type, content);
  }

  public void Dispose() {
    _stream.Dispose();
  }

  private GitObject ReadAndIndex(int index) {
    (GitObjectType type, byte[] content) = Resolve(index);
    string objectId = GitObject.ComputeObjectId(type, content);
    _indexByObjectId[objectId] = index;
    if (_refDeltaBases.Contains(objectId)) {
      AddToCache(index, (type, content));
    }
    return new GitObject(objectId, type, content);
  }

  /// Whether every REF_DELTA base in the entry's delta chain has already been indexed.
  private bool CanResolve(int index) {
    for (int i = 0; i <= _entries.Count; i++) {
      Entry entry = _entries[index];
      if (entry.Type == OfsDelta) {
        index = _indexByOffset[entry.BaseOffset];
      } else if (entry.Type == RefDelta) {
        return _indexByObjectId.ContainsKey(entry.BaseObjectId!);
      } else {
        return true;
      }
    }
    throw Corrupt("delta cycle");
  }

  private void Scan() {
    long length = _stream.Length;
    if (length < 32) {
      throw Corrupt("too short");
    }

    Span<byte> header = stackalloc byte[12];
    _reader.Position = 0;
    _reader.ReadExactly(header);
    if (!header[..4].SequenceEqual("PACK"u8)) {
      throw Corrupt("bad signature");
    }
    uint version = ReadUInt32BigEndian(header[4..8]);
    if (version is not (2 or 3)) {
      throw Corrupt($"unsupported version {version}");
    }
    uint count = ReadUInt32BigEndian(header[8..12]);

    VerifyChecksum(length);

    long end = length - 20;
    Span<byte> baseId = stackalloc byte[20];
    byte[] window = ZlibInflater.CreateWindow();
    _reader.Position = 12;
    for (uint i = 0; i < count; i++) {
      long offset = _reader.Position;
      if (offset >= end) {
        throw Corrupt("object count exceeds data");
      }

      int c = ReadPackByte();
      int type = (c >> 4) & 7;
      long size = c & 0x0F;
      int shift = 4;
      while ((c & 0x80) != 0) {
        if (shift > 56) {
          throw Corrupt("object size too large");
        }
        c = ReadPackByte();
        size |= (long)(c & 0x7F) << shift;
        shift += 7;
      }

      long baseOffset = -1;
      string? baseObjectId = null;
      if (type == OfsDelta) {
        c = ReadPackByte();
        long distance = c & 0x7F;
        while ((c & 0x80) != 0) {
          if (distance > (long.MaxValue >> 8)) {
            throw Corrupt("delta offset too large");
          }
          c = ReadPackByte();
          distance = ((distance + 1) << 7) | (long)(c & 0x7F);
        }
        baseOffset = offset - distance;
        if (distance == 0 || !_indexByOffset.TryGetValue(baseOffset, out int baseIndex)) {
          throw Corrupt("invalid delta base offset");
        }
        _ofsDeltaBases.Add(baseIndex);
      } else if (type == RefDelta) {
        _reader.ReadExactly(baseId);
        baseObjectId = Convert.ToHexStringLower(baseId);
        _refDeltaBases.Add(baseObjectId);
      } else if (type is < 1 or > 4) {
        throw Corrupt($"invalid object type {type}");
      }

      if (size > MaxObjectSize) {
        throw Corrupt($"object of {size} bytes exceeds the {MaxObjectSize} byte limit");
      }

      long dataOffset = _reader.Position;
      ZlibInflater.Skip(_reader, size, window);
      if (_reader.Position > end) {
        throw Corrupt("object data overlaps trailer");
      }

      _indexByOffset[offset] = _entries.Count;
      _entries.Add(new Entry(type, size, dataOffset, baseOffset, baseObjectId));
    }

    if (_reader.Position != end) {
      throw Corrupt("unexpected data after last object");
    }
  }

  private void VerifyChecksum(long length) {
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
    byte[] buffer = new byte[64 * 1024];
    _stream.Position = 0;
    long remaining = length - 20;
    while (remaining > 0) {
      int read = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
      if (read <= 0) {
        throw Corrupt("unexpected end");
      }
      hash.AppendData(buffer, 0, read);
      remaining -= read;
    }
    byte[] trailer = new byte[20];
    _stream.ReadExactly(trailer);
    if (!hash.GetHashAndReset().AsSpan().SequenceEqual(trailer)) {
      throw Corrupt("checksum mismatch");
    }
  }

  private (GitObjectType Type, byte[] Content) Resolve(int index) {
    Stack<int> chain = new();
    int current = index;
    (GitObjectType Type, byte[] Content) resolved;
    while (true) {
      if (_cache.TryGetValue(current, out resolved)) {
        break;
      }
      Entry entry = _entries[current];
      if (entry.Type is not (OfsDelta or RefDelta)) {
        resolved = ((GitObjectType)entry.Type, Inflate(entry));
        if (chain.Count > 0 || _ofsDeltaBases.Contains(current)) {
          AddToCache(current, resolved);
        }
        break;
      }

      chain.Push(current);
      if (chain.Count > _entries.Count) {
        throw Corrupt("delta cycle");
      }
      if (entry.Type == OfsDelta) {
        current = _indexByOffset[entry.BaseOffset];
      } else if (!_indexByObjectId.TryGetValue(entry.BaseObjectId!, out current)) {
        throw Corrupt($"missing delta base {entry.BaseObjectId}");
      }
    }

    while (chain.Count > 0) {
      int deltaIndex = chain.Pop();
      byte[] delta = Inflate(_entries[deltaIndex]);
      resolved = (resolved.Type, ApplyDelta(resolved.Content, delta));
      if (chain.Count > 0 || _ofsDeltaBases.Contains(deltaIndex)) {
        AddToCache(deltaIndex, resolved);
      }
    }
    return resolved;
  }

  private void AddToCache(int index, (GitObjectType Type, byte[] Content) value) {
    if (value.Content.Length > CacheBudget) {
      return;
    }
    if (_cacheBytes + value.Content.Length > CacheBudget) {
      _cache.Clear();
      _cacheBytes = 0;
    }
    if (_cache.TryAdd(index, value)) {
      _cacheBytes += value.Content.Length;
    }
  }

  private byte[] Inflate(Entry entry) {
    byte[] content = new byte[entry.Size];
    _reader.Position = entry.DataOffset;
    ZlibInflater.Inflate(_reader, content);
    return content;
  }

  private int ReadPackByte() {
    int value = _reader.ReadByte();
    return value >= 0 ? value : throw Corrupt("unexpected end");
  }

  private static long ReadDeltaSize(byte[] delta, ref int position) {
    long size = 0;
    int shift = 0;
    int c;
    do {
      if (shift > 56) {
        throw Corrupt("delta size too large");
      }
      c = ReadDeltaByte(delta, ref position);
      size |= (long)(c & 0x7F) << shift;
      shift += 7;
    } while ((c & 0x80) != 0);
    return size;
  }

  private static byte ReadDeltaByte(byte[] delta, ref int position) {
    return position < delta.Length ? delta[position++] : throw Corrupt("truncated delta");
  }

  private static uint ReadUInt32BigEndian(ReadOnlySpan<byte> bytes) {
    return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);
  }

  private static GitProtocolException Corrupt(string reason) {
    return new GitProtocolException($"Corrupt packfile: {reason}.");
  }

  private readonly record struct Entry(
      int Type,
      long Size,
      long DataOffset,
      long BaseOffset,
      string? BaseObjectId);
}
