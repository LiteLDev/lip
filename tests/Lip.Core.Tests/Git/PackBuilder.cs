using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Lip.Core.Git;

namespace Lip.Core.Tests.Git;

/// Builds packfiles and protocol responses by hand so tests do not need git.
public class PackBuilder {
  private readonly MemoryStream _body = new();
  private int _count;

  public long AddObject(GitObjectType type, byte[] content) {
    long offset = 12 + _body.Length;
    WriteHeader((int)type, content.Length);
    WriteCompressed(content);
    return offset;
  }

  public long AddOfsDelta(long baseOffset, byte[] delta) {
    long offset = 12 + _body.Length;
    WriteHeader(6, delta.Length);
    long distance = offset - baseOffset;
    List<byte> encoded = [(byte)(distance & 0x7F)];
    while ((distance >>= 7) > 0) {
      distance--;
      encoded.Insert(0, (byte)(0x80 | (distance & 0x7F)));
    }
    _body.Write([.. encoded]);
    WriteCompressed(delta);
    return offset;
  }

  public long AddRefDelta(string baseObjectId, byte[] delta) {
    long offset = 12 + _body.Length;
    WriteHeader(7, delta.Length);
    _body.Write(Convert.FromHexString(baseObjectId));
    WriteCompressed(delta);
    return offset;
  }

  public byte[] Build() {
    MemoryStream pack = new();
    pack.Write("PACK"u8);
    pack.Write([0, 0, 0, 2]);
    pack.Write([(byte)(_count >> 24), (byte)(_count >> 16), (byte)(_count >> 8), (byte)_count]);
    pack.Write(_body.ToArray());
    pack.Write(SHA1.HashData(pack.ToArray()));
    return pack.ToArray();
  }

  public static string ObjectId(GitObjectType type, byte[] content) {
    return GitObject.ComputeObjectId(type, content);
  }

  public static byte[] Commit(string treeId) {
    return Encoding.UTF8.GetBytes(
        $"tree {treeId}\nauthor A <a@a> 0 +0000\ncommitter A <a@a> 0 +0000\n\nmessage\n");
  }

  public static byte[] Tag(string objectId) {
    return Encoding.UTF8.GetBytes(
        $"object {objectId}\ntype commit\ntag v1.0.0\ntagger A <a@a> 0 +0000\n\nmessage\n");
  }

  public static byte[] Tree(params (string Mode, string Name, string ObjectId)[] entries) {
    MemoryStream tree = new();
    foreach ((string mode, string name, string objectId) in entries) {
      tree.Write(Encoding.UTF8.GetBytes($"{mode} {name}\0"));
      tree.Write(Convert.FromHexString(objectId));
    }
    return tree.ToArray();
  }

  /// Delta instructions: byte[] inserts literal data, (int Offset, int Size) copies from the base.
  public static byte[] Delta(int baseSize, int resultSize, params object[] instructions) {
    List<byte> delta = [];
    WriteVarint(delta, baseSize);
    WriteVarint(delta, resultSize);
    foreach (object instruction in instructions) {
      if (instruction is byte[] insert) {
        delta.Add((byte)insert.Length);
        delta.AddRange(insert);
      } else if (instruction is (int offset, int size)) {
        byte op = 0x80;
        List<byte> args = [];
        for (int i = 0; i < 4; i++) {
          byte b = (byte)(offset >> (8 * i));
          if (b != 0) {
            op |= (byte)(1 << i);
            args.Add(b);
          }
        }
        for (int i = 0; i < 3; i++) {
          byte b = (byte)(size >> (8 * i));
          if (b != 0) {
            op |= (byte)(0x10 << i);
            args.Add(b);
          }
        }
        delta.Add(op);
        delta.AddRange(args);
      }
    }
    return [.. delta];
  }

  public static byte[] PktLines(params object[] lines) {
    PktLineWriter writer = new();
    foreach (object line in lines) {
      switch (line) {
        case "FLUSH":
          writer.WriteFlush();
          break;
        case "DELIM":
          writer.WriteDelimiter();
          break;
        case string text:
          writer.WriteLine(text);
          break;
        case byte[] data:
          writer.WriteData(data);
          break;
      }
    }
    return writer.ToArray();
  }

  public static byte[] Advertisement(string fetchFeatures = "shallow filter") {
    return PktLines(
        "# service=git-upload-pack", "FLUSH",
        "version 2", "agent=git/test", "ls-refs=unborn", $"fetch={fetchFeatures}",
        "server-option", "object-format=sha1", "FLUSH");
  }

  /// A fetch response with a shallow-info section and the pack split over side-band packets.
  public static byte[] FetchResponse(byte[] pack, bool withShallowInfo = true) {
    List<object> lines = [];
    if (withShallowInfo) {
      lines.AddRange(["shallow-info", $"shallow {new string('a', 40)}", "DELIM"]);
    }
    lines.Add("packfile");
    lines.Add((byte[])[2, .. "Enumerating objects\n"u8]);
    for (int i = 0; i < pack.Length; i += 1000) {
      lines.Add((byte[])[1, .. pack.AsSpan(i, Math.Min(1000, pack.Length - i))]);
    }
    lines.Add("FLUSH");
    return PktLines([.. lines]);
  }

  private void WriteHeader(int type, long size) {
    _count++;
    byte c = (byte)((type << 4) | (int)(size & 0x0F));
    size >>= 4;
    while (size > 0) {
      _body.WriteByte((byte)(c | 0x80));
      c = (byte)(size & 0x7F);
      size >>= 7;
    }
    _body.WriteByte(c);
  }

  private void WriteCompressed(byte[] content) {
    using (ZLibStream zlib = new(_body, CompressionLevel.Optimal, leaveOpen: true)) {
      zlib.Write(content);
    }
  }

  private static void WriteVarint(List<byte> output, int value) {
    while (value >= 0x80) {
      output.Add((byte)(0x80 | (value & 0x7F)));
      value >>= 7;
    }
    output.Add((byte)value);
  }
}
