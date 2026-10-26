namespace Lip.Core.Git;

/// A managed zlib (RFC 1950/1951) decoder that consumes exactly the bytes of one zlib stream.
///
/// Packfiles concatenate zlib streams without recording their compressed lengths, so the parser
/// must know where each stream ends. System.IO.Compression reads ahead and cannot report that.
public sealed class ZlibInflater {
  private const int WindowSize = 32 * 1024;
  private const int FastBits = 9;

  private static readonly int[] s_lengthBase = [
    3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
    35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258,
  ];
  private static readonly int[] s_lengthExtra = [
    0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0,
  ];
  private static readonly int[] s_distanceBase = [
    1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
    257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577,
  ];
  private static readonly int[] s_distanceExtra = [
    0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
    7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13,
  ];
  private static readonly int[] s_codeLengthOrder = [
    16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15,
  ];

  private static readonly Huffman s_fixedLiteralLength = BuildFixedLiteralLength();
  private static readonly Huffman s_fixedDistance = BuildFixedDistance();

  private readonly BufferedByteReader _input;
  private readonly byte[] _output;
  private readonly bool _isRing;
  private readonly long _expectedSize;
  private long _total;
  private ulong _bitBuffer;
  private int _bitCount;
  private int _overrun;

  private ZlibInflater(BufferedByteReader input, byte[] output, bool isRing, long expectedSize) {
    _input = input;
    _output = output;
    _isRing = isRing;
    _expectedSize = expectedSize;
  }

  /// Decompresses one zlib stream that must expand to exactly destination.Length bytes. On return
  /// the input is positioned right after the stream.
  public static void Inflate(BufferedByteReader input, byte[] destination) {
    new ZlibInflater(input, destination, isRing: false, destination.Length).Run();
  }

  /// Decodes and discards one zlib stream that must expand to exactly expectedSize bytes. On
  /// return the input is positioned right after the stream.
  public static void Skip(BufferedByteReader input, long expectedSize, byte[]? window = null) {
    if (window is not null && window.Length != WindowSize) {
      throw new ArgumentException("Window must come from CreateWindow().", nameof(window));
    }
    new ZlibInflater(input, window ?? CreateWindow(), isRing: true, expectedSize).Run();
  }

  /// Creates a scratch window that can be reused across Skip calls.
  public static byte[] CreateWindow() {
    return new byte[WindowSize];
  }

  private void Run() {
    int cmf = GetBits(8);
    int flg = GetBits(8);
    if ((cmf & 0x0F) != 8 || (cmf >> 4) > 7 || (((cmf << 8) | flg) % 31) != 0
        || (flg & 0x20) != 0) {
      throw Corrupt("invalid zlib header");
    }

    bool isLast;
    do {
      isLast = GetBits(1) == 1;
      switch (GetBits(2)) {
        case 0:
          Stored();
          break;
        case 1:
          Codes(s_fixedLiteralLength, s_fixedDistance);
          break;
        case 2:
          Dynamic();
          break;
        default:
          throw Corrupt("invalid block type");
      }
    } while (!isLast);

    // The Adler-32 trailer is not verified; callers verify object SHA-1s and the pack checksum.
    DropBits(_bitCount & 7);
    GetBits(16);
    GetBits(16);

    if (_total != _expectedSize) {
      throw Corrupt($"expected {_expectedSize} bytes but got {_total}");
    }

    // Give back whole bytes that were read ahead into the bit buffer.
    int unreadBytes = _bitCount / 8;
    if (unreadBytes < _overrun) {
      throw Corrupt("unexpected end of data");
    }
    _input.Position -= unreadBytes - _overrun;
  }

  private void Stored() {
    DropBits(_bitCount & 7);
    int length = GetBits(16);
    int lengthComplement = GetBits(16);
    if (length != (~lengthComplement & 0xFFFF)) {
      throw Corrupt("stored block length mismatch");
    }
    for (int i = 0; i < length; i++) {
      Put((byte)GetBits(8));
    }
  }

  private void Dynamic() {
    int literalLengthCount = GetBits(5) + 257;
    int distanceCount = GetBits(5) + 1;
    int codeLengthCount = GetBits(4) + 4;
    if (literalLengthCount > 286 || distanceCount > 30) {
      throw Corrupt("too many codes");
    }

    short[] lengths = new short[320];
    for (int i = 0; i < codeLengthCount; i++) {
      lengths[s_codeLengthOrder[i]] = (short)GetBits(3);
    }
    Huffman codeLengthCode = new(lengths, 0, 19);

    int total = literalLengthCount + distanceCount;
    Array.Clear(lengths);
    int index = 0;
    while (index < total) {
      int symbol = Decode(codeLengthCode);
      if (symbol < 16) {
        lengths[index++] = (short)symbol;
        continue;
      }

      short value = 0;
      int repeat;
      if (symbol == 16) {
        if (index == 0) {
          throw Corrupt("repeat with no previous length");
        }
        value = lengths[index - 1];
        repeat = 3 + GetBits(2);
      } else if (symbol == 17) {
        repeat = 3 + GetBits(3);
      } else {
        repeat = 11 + GetBits(7);
      }
      if (index + repeat > total) {
        throw Corrupt("too many code lengths");
      }
      while (repeat-- > 0) {
        lengths[index++] = value;
      }
    }

    if (lengths[256] == 0) {
      throw Corrupt("missing end-of-block code");
    }

    Codes(new Huffman(lengths, 0, literalLengthCount),
        new Huffman(lengths, literalLengthCount, distanceCount));
  }

  private void Codes(Huffman literalLength, Huffman distance) {
    while (true) {
      int symbol = Decode(literalLength);
      if (symbol < 256) {
        Put((byte)symbol);
      } else if (symbol == 256) {
        return;
      } else {
        symbol -= 257;
        if (symbol >= 29) {
          throw Corrupt("invalid length symbol");
        }
        int length = s_lengthBase[symbol] + GetBits(s_lengthExtra[symbol]);

        int distanceSymbol = Decode(distance);
        if (distanceSymbol >= 30) {
          throw Corrupt("invalid distance symbol");
        }
        int dist = s_distanceBase[distanceSymbol] + GetBits(s_distanceExtra[distanceSymbol]);
        Copy(dist, length);
      }
    }
  }

  private void Put(byte value) {
    if (_total >= _expectedSize) {
      throw Corrupt("output exceeds declared size");
    }
    if (_isRing) {
      _output[(int)(_total & (WindowSize - 1))] = value;
    } else {
      _output[(int)_total] = value;
    }
    _total++;
  }

  private void Copy(int distance, int length) {
    if (distance > _total || (_isRing && distance > WindowSize)) {
      throw Corrupt("distance too far back");
    }
    if (_total + length > _expectedSize) {
      throw Corrupt("output exceeds declared size");
    }

    if (_isRing) {
      for (int i = 0; i < length; i++) {
        _output[(int)((_total + i) & (WindowSize - 1))] =
            _output[(int)((_total + i - distance) & (WindowSize - 1))];
      }
    } else {
      int destination = (int)_total;
      int source = destination - distance;
      if (distance >= length) {
        Buffer.BlockCopy(_output, source, _output, destination, length);
      } else {
        for (int i = 0; i < length; i++) {
          _output[destination + i] = _output[source + i];
        }
      }
    }
    _total += length;
  }

  private int Decode(Huffman huffman) {
    Fill(FastBits);
    ushort entry = huffman.Fast[(int)(_bitBuffer & ((1 << FastBits) - 1))];
    if (entry != 0) {
      DropBits(entry >> 9);
      return entry & 0x1FF;
    }

    // Canonical decoding one bit at a time for codes longer than FastBits.
    int code = 0;
    int first = 0;
    int index = 0;
    for (int length = 1; length < 16; length++) {
      code |= GetBits(1);
      int count = huffman.Count[length];
      if (code - count < first) {
        return huffman.Symbol[index + (code - first)];
      }
      index += count;
      first += count;
      first <<= 1;
      code <<= 1;
    }
    throw Corrupt("invalid Huffman code");
  }

  private void Fill(int bits) {
    while (_bitCount < bits) {
      int value = _input.ReadByte();
      if (value < 0) {
        // Allow peeking past the end; Run() rejects streams that actually consumed these bytes.
        value = 0;
        if (++_overrun > 8) {
          throw Corrupt("unexpected end of data");
        }
      }
      _bitBuffer |= (ulong)value << _bitCount;
      _bitCount += 8;
    }
  }

  private int GetBits(int bits) {
    Fill(bits);
    int value = (int)(_bitBuffer & ((1UL << bits) - 1));
    DropBits(bits);
    return value;
  }

  private void DropBits(int bits) {
    _bitBuffer >>= bits;
    _bitCount -= bits;
  }

  private static GitProtocolException Corrupt(string reason) {
    return new GitProtocolException($"Corrupt zlib stream in packfile: {reason}.");
  }

  private static Huffman BuildFixedLiteralLength() {
    short[] lengths = new short[288];
    for (int i = 0; i < 288; i++) {
      lengths[i] = i switch {
        < 144 => 8,
        < 256 => 9,
        < 280 => 7,
        _ => 8,
      };
    }
    return new Huffman(lengths, 0, 288);
  }

  private static Huffman BuildFixedDistance() {
    short[] lengths = new short[30];
    Array.Fill(lengths, (short)5);
    return new Huffman(lengths, 0, 30);
  }

  private sealed class Huffman {
    public readonly short[] Count = new short[16];
    public readonly short[] Symbol;

    /// Lookup table indexed by the next FastBits input bits: (code length << 9) | symbol, or 0
    /// when the code is longer than FastBits.
    public readonly ushort[] Fast = new ushort[1 << FastBits];

    public Huffman(short[] lengths, int offset, int count) {
      Symbol = new short[count];
      for (int s = 0; s < count; s++) {
        Count[lengths[offset + s]]++;
      }
      if (Count[0] == count) {
        return;
      }

      int left = 1;
      for (int length = 1; length < 16; length++) {
        left <<= 1;
        left -= Count[length];
        if (left < 0) {
          throw Corrupt("over-subscribed Huffman code");
        }
      }

      short[] offsets = new short[16];
      for (int length = 1; length < 15; length++) {
        offsets[length + 1] = (short)(offsets[length] + Count[length]);
      }
      for (int s = 0; s < count; s++) {
        short length = lengths[offset + s];
        if (length != 0) {
          Symbol[offsets[length]++] = (short)s;
        }
      }

      int[] nextCode = new int[16];
      int code = 0;
      for (int length = 1; length < 16; length++) {
        code = (code + (length == 1 ? 0 : Count[length - 1])) << 1;
        nextCode[length] = code;
      }
      for (int s = 0; s < count; s++) {
        int length = lengths[offset + s];
        if (length == 0) {
          continue;
        }
        int assigned = nextCode[length]++;
        if (length > FastBits) {
          continue;
        }
        int reversed = 0;
        for (int i = 0; i < length; i++) {
          reversed |= ((assigned >> i) & 1) << (length - 1 - i);
        }
        for (int i = reversed; i < (1 << FastBits); i += 1 << length) {
          Fast[i] = (ushort)((length << 9) | s);
        }
      }
    }
  }
}
