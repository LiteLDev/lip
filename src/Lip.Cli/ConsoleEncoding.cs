using System.Text;

namespace Lip.Cli;

/// The code page of the console of lip on Windows. lip writes UTF-8, and so do the servers it
/// runs, whose output it passes on as it is (protocol/lip-run.md, "Standard streams"); a console
/// shows the bytes written to it in its output code page, such as GBK on a Chinese Windows, in
/// which UTF-8 is garbled. So the console shows UTF-8 while lip runs, and what it showed before
/// once lip exits. What .NET writes to a file or a pipe is in the same encoding, which JSON needs
/// to be in UTF-8 too (protocol/lip.md, "Output"); without a console, it is UTF-8 already. The
/// input code page stays: a server reads its standard input in the ANSI code page of Windows,
/// which is the input code page of a console by default where it is not ASCII, such as GBK.
public static class ConsoleEncoding {
  public static void UseUtf8() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    Encoding previous = Console.OutputEncoding;
    if (previous.CodePage == Encoding.UTF8.CodePage) {
      return;
    }

    try {
      Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
    catch (IOException) {
      return;
    }

    AppDomain.CurrentDomain.ProcessExit += (_, _) => {
      try {
        Console.OutputEncoding = previous;
      }
      catch (IOException) {
      }
    };
  }
}
