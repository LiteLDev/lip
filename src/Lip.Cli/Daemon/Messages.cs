using System.Text;

namespace Lip.Cli.Daemon;

/// The payload of Hello: who the client is.
public sealed record Hello(string Version, string Executable) {
  public byte[] Encode() => Messages.Write(w => {
    w.Write(Version);
    w.Write(Executable);
  });

  public static Hello Decode(byte[] payload) =>
      Messages.Read(payload, r => new Hello(r.ReadString(), r.ReadString()));
}

/// The payload of Welcome: who the daemon is, and how many commands it carries out.
public sealed record Welcome(string Version, string Executable, int ProcessId, int Commands) {
  public byte[] Encode() => Messages.Write(w => {
    w.Write(Version);
    w.Write(Executable);
    w.Write(ProcessId);
    w.Write(Commands);
  });

  public static Welcome Decode(byte[] payload) =>
      Messages.Read(payload, r => new Welcome(
          r.ReadString(), r.ReadString(), r.ReadInt32(), r.ReadInt32()));
}

/// The payload of Start: the command and everything of the client that it is carried out with
/// (protocol/lip-daemon.md, "Carrying out a command"). Language is the client language
/// (protocol/lip-tui.md, "Language") as a value of the configuration key language, or empty
/// when there is none.
public sealed record Start(
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyList<KeyValuePair<string, string>> Environment,
    int ProcessId,
    bool IsInputRedirected,
    bool IsOutputRedirected,
    bool IsErrorRedirected,
    ConsoleProfile Output,
    ConsoleProfile Error,
    string Language = "") {
  public byte[] Encode() => Messages.Write(w => {
    w.Write(Arguments.Count);
    foreach (string argument in Arguments) {
      w.Write(argument);
    }

    w.Write(WorkingDirectory);
    w.Write(Environment.Count);
    foreach ((string name, string value) in Environment) {
      w.Write(name);
      w.Write(value);
    }

    w.Write(ProcessId);
    w.Write(IsInputRedirected);
    w.Write(IsOutputRedirected);
    w.Write(IsErrorRedirected);
    WriteProfile(w, Output);
    WriteProfile(w, Error);
    w.Write(Language);
  });

  public static Start Decode(byte[] payload) => Messages.Read(payload, r => {
    string[] arguments = new string[Count(r)];
    for (int i = 0; i < arguments.Length; i++) {
      arguments[i] = r.ReadString();
    }

    string workingDirectory = r.ReadString();
    KeyValuePair<string, string>[] environment = new KeyValuePair<string, string>[Count(r)];
    for (int i = 0; i < environment.Length; i++) {
      environment[i] = new(r.ReadString(), r.ReadString());
    }

    return new Start(
        arguments, workingDirectory, environment, r.ReadInt32(), r.ReadBoolean(),
        r.ReadBoolean(), r.ReadBoolean(), ReadProfile(r), ReadProfile(r), r.ReadString());
  });

  private static int Count(BinaryReader reader) {
    int count = reader.ReadInt32();
    return count is >= 0 and <= Frames.MaxPayload
        ? count
        : throw new InvalidDataException($"{count} is not a count.");
  }

  private static void WriteProfile(BinaryWriter writer, ConsoleProfile profile) {
    writer.Write(profile.IsTerminal);
    writer.Write(profile.Ansi);
    writer.Write(profile.ColorSystem);
    writer.Write(profile.Unicode);
    writer.Write(profile.Links);
    writer.Write(profile.Interactive);
    writer.Write(profile.Width);
    writer.Write(profile.Height);
  }

  private static ConsoleProfile ReadProfile(BinaryReader reader) =>
      new(
          reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadInt32(), reader.ReadBoolean(),
          reader.ReadBoolean(), reader.ReadBoolean(), reader.ReadInt32(), reader.ReadInt32());
}

/// The payload of Key: a key that the user pressed, as Console.ReadKey reads it.
public static class KeyMessage {
  public static byte[] Encode(ConsoleKeyInfo key) => Messages.Write(w => {
    w.Write((ushort)key.KeyChar);
    w.Write((int)key.Key);
    w.Write((int)key.Modifiers);
  });

  public static ConsoleKeyInfo Decode(byte[] payload) => Messages.Read(payload, r => {
    char character = (char)r.ReadUInt16();
    ConsoleKey key = (ConsoleKey)r.ReadInt32();
    ConsoleModifiers modifiers = (ConsoleModifiers)r.ReadInt32();
    return new ConsoleKeyInfo(
        character, key,
        shift: modifiers.HasFlag(ConsoleModifiers.Shift),
        alt: modifiers.HasFlag(ConsoleModifiers.Alt),
        control: modifiers.HasFlag(ConsoleModifiers.Control));
  });
}

/// The payload of Resize: the columns and the rows of the terminal of the client.
public static class ResizeMessage {
  public static byte[] Encode(int width, int height) => Messages.Write(w => {
    w.Write(width);
    w.Write(height);
  });

  public static (int Width, int Height) Decode(byte[] payload) =>
      Messages.Read(payload, r => (r.ReadInt32(), r.ReadInt32()));
}

/// Writing and reading payloads.
public static class Messages {
  public const byte StandardOutput = 1;
  public const byte StandardError = 2;

  public static byte[] Write(Action<BinaryWriter> write) {
    using MemoryStream stream = new();
    using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true)) {
      write(writer);
    }

    return stream.ToArray();
  }

  /// Throws InvalidDataException when the payload is not of the message.
  public static T Read<T>(byte[] payload, Func<BinaryReader, T> read) {
    using BinaryReader reader = new(new MemoryStream(payload), Encoding.UTF8);
    try {
      return read(reader);
    }
    catch (EndOfStreamException ex) {
      throw new InvalidDataException("A message of lip ended early.", ex);
    }
  }

  public static byte[] Int32(int value) => Write(w => w.Write(value));

  public static int ReadInt32(byte[] payload) => Read(payload, r => r.ReadInt32());

  public static byte[] Boolean(bool value) => Write(w => w.Write(value));

  public static bool ReadBoolean(byte[] payload) => Read(payload, r => r.ReadBoolean());

  /// Output or Text: the stream, then the bytes.
  public static byte[] Output(byte stream, ReadOnlySpan<byte> bytes) {
    byte[] payload = new byte[1 + bytes.Length];
    payload[0] = stream;
    bytes.CopyTo(payload.AsSpan(1));
    return payload;
  }
}
