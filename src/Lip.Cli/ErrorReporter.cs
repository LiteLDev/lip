using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Flurl.Http;
using Lip.Core.Infrastructure;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Lock;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Lip.Cli;

/// Tells the user why a command failed, in plain words with what to do about it, and writes
/// everything known about the failure to a failure report below .lip/logs (protocol/cli.md §2).
public static class ErrorReporter {
  private const string IssuesUrl = "https://github.com/LiteLDev/lip/issues";
  private const int KeptReports = 20;

  private static readonly IAnsiConsole _console = AnsiConsole.Create(new() {
    Out = new AnsiConsoleOutput(Console.Error)
  });

  /// Reports the failure and returns the exit code.
  public static int Report(Exception exception, string[] args, bool cancelled) {
    // A usage error is fully described by its message; there is nothing to analyze.
    if (exception is CommandAppException or UsageException) {
      if (exception is CommandAppException { Pretty: { } pretty }) {
        _console.Write(pretty);
      } else {
        _console.MarkupLine($"[bold red]error:[/] {Markup.Escape(exception.Message)}");
      }
      _console.MarkupLine("Run [blue]lip --help[/] to see how to use lip.");
      return 1;
    }

    IReadOnlyList<Exception> failures = Unwrap(exception);

    if (cancelled && failures.All(f => Chain(f).Any(e => e is OperationCanceledException))) {
      _console.MarkupLine("[bold red]Cancelled.[/] Any changes to the workspace were undone.");
      return 1;
    }

    string summary = Redact(Summarize(failures));
    List<string> hints = [.. failures.SelectMany(Chain).SelectMany(Hints).Distinct()];
    bool bug = failures.Any(f => Chain(f).Any(IsBug));
    if (bug) {
      hints.Add($"This looks like a bug in lip. Please report it at {IssuesUrl} and attach the"
          + " full report.");
    }

    // Written without the console's line wrapping, which would break URLs and paths apart.
    _console.Markup("[bold red]error:[/] ");
    Console.Error.WriteLine(summary);
    foreach (string hint in hints) {
      _console.Markup("[yellow]hint:[/] ");
      Console.Error.WriteLine(hint);
    }

    try {
      string path = WriteReport(exception, args, summary, hints);
      _console.WriteLine();
      _console.MarkupLine($"Full report: [blue]{Markup.Escape(path)}[/]");
    }
    catch (Exception reportException) {
      // The report is lost; show its most important part instead.
      Console.Error.WriteLine();
      Console.Error.WriteLine($"Could not write a failure report ({reportException.Message}). Details:");
      Console.Error.WriteLine(Redact(exception.ToString()));
    }

    return 1;
  }

  /// The failures of the command: the exceptions inside aggregate and reflection wrappers.
  private static List<Exception> Unwrap(Exception exception) {
    return exception switch {
      AggregateException aggregate => [.. aggregate.Flatten().InnerExceptions.SelectMany(Unwrap)],
      TargetInvocationException { InnerException: { } inner } => Unwrap(inner),
      _ => [exception],
    };
  }

  /// An exception and its inner exceptions, outermost first.
  private static IEnumerable<Exception> Chain(Exception exception) {
    for (Exception? current = exception; current is not null; current = current.InnerException) {
      if (current is AggregateException aggregate) {
        yield return aggregate;
        foreach (Exception inner in aggregate.Flatten().InnerExceptions.SelectMany(Chain)) {
          yield return inner;
        }
        yield break;
      }
      yield return current;
    }
  }

  /// The message of each failure, followed by the messages of its causes that add something.
  private static string Summarize(IReadOnlyList<Exception> failures) {
    StringBuilder builder = new();
    foreach (Exception failure in failures) {
      if (failures.Count > 1) {
        builder.Append(builder.Length == 0 ? "" : "\n").Append("• ");
      }

      string shown = "";
      foreach (Exception cause in Chain(failure)) {
        string message = OwnMessage(cause);
        if (message.Length == 0 || shown.Contains(message, StringComparison.Ordinal)) {
          continue;
        }

        builder.Append(shown.Length == 0 ? message : $"\n  caused by: {message}");
        shown += message;
      }
    }
    return builder.ToString();
  }

  /// The message of an exception without the messages of its inner exceptions, which
  /// AggregateException appends in parentheses.
  private static string OwnMessage(Exception exception) {
    string message = exception.Message;
    if (exception is AggregateException aggregate) {
      foreach (Exception inner in aggregate.InnerExceptions) {
        message = message.Replace($" ({inner.Message})", "", StringComparison.Ordinal);
      }
    }
    return message.Trim();
  }

  /// What the user can do about an exception.
  private static IEnumerable<string> Hints(Exception exception) {
    switch (exception) {
      // GitHub answers 401 for a repository that does not exist or is private.
      case HttpRequestException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.Unauthorized }:
        yield return "The repository or file does not exist, or is private. Check the package"
            + " name and version for typos, and that the repository is public.";
        break;
      case HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests }:
        yield return "The server refused the request, possibly because too many requests were"
            + " sent. Wait a while and try again, or set a GitHub proxy with"
            + " 'lip config set github_proxy <URL>'.";
        break;
      case HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError }:
        yield return "The server had a problem. Try again later.";
        break;
      case HttpRequestException or FlurlHttpException or SocketException or TimeoutException:
        yield return NetworkHint;
        break;
      case TaskCanceledException { InnerException: TimeoutException }:
        yield return NetworkHint;
        break;
      case UnauthorizedAccessException:
        yield return "lip is not allowed to access a file. Check that it is not read-only and"
            + " that you may write to the workspace.";
        break;
      case IOException io when IsSharingViolation(io):
        yield return "A file is in use by another program. Stop the server and other programs"
            + " that use the workspace, then try again.";
        break;
      case IOException io when IsDiskFull(io):
        yield return "The disk is full. Free some space, then try again.";
        break;
      case CommandFailedException:
        yield return "A hook command of a package failed. To install or uninstall without"
            + " running hook commands, pass --ignore-scripts (the package may then need manual"
            + " setup).";
        break;
      case CannotAskException:
        yield return "Run lip in an interactive terminal.";
        break;
      case DependencyResolutionException:
        yield return "If the reasons above are about versions that do not fit together, request"
            + " other versions, or install with --no-dependencies to choose them yourself.";
        break;
      case WorkspaceBusyException:
        break;
      case PlatformNotSupportedException:
        yield return "Use --rid to choose the runtime to install packages for.";
        break;
    }
  }

  private const string NetworkHint = "lip could not reach the server. Check your network"
      + " connection. If GitHub is slow or blocked where you are, set a GitHub proxy with"
      + " 'lip config set github_proxy <URL>'.";

  private static bool IsSharingViolation(IOException exception) =>
      OperatingSystem.IsWindows() && (exception.HResult & 0xFFFF) is 32 or 33;

  // ERROR_DISK_FULL and ERROR_HANDLE_DISK_FULL on Windows, ENOSPC elsewhere.
  private static bool IsDiskFull(IOException exception) =>
      OperatingSystem.IsWindows()
          ? (exception.HResult & 0xFFFF) is 0x70 or 0x27
          : exception.HResult == 28;

  /// Exceptions that only a mistake in lip itself throws.
  private static bool IsBug(Exception exception) =>
      exception is NullReferenceException
          or IndexOutOfRangeException
          or InvalidCastException
          or KeyNotFoundException
          or NotImplementedException
          or ArgumentNullException
          or ArgumentOutOfRangeException
          or ObjectDisposedException
          or DivideByZeroException;

  /// Writes the failure report and returns its path relative to the current directory.
  private static string WriteReport(
      Exception exception,
      string[] args,
      string summary,
      IReadOnlyList<string> hints) {
    string lipDirectory = Path.GetFullPath(".lip");
    bool created = !Directory.Exists(lipDirectory);
    string logs = Directory.CreateDirectory(Path.Join(lipDirectory, "logs")).FullName;
    if (created && OperatingSystem.IsWindows()) {
      File.SetAttributes(lipDirectory, File.GetAttributes(lipDirectory) | FileAttributes.Hidden);
    }

    DateTimeOffset now = DateTimeOffset.UtcNow;
    string name = string.Create(
        CultureInfo.InvariantCulture,
        $"{now:yyyyMMdd'T'HHmmss'Z'}-{Environment.ProcessId}.log");

    using (StreamWriter writer = new(
        new FileStream(Path.Join(logs, name), FileMode.CreateNew, FileAccess.Write),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))) {
      writer.NewLine = "\n";
      writer.Write(Redact(Render(exception, args, summary, hints, now)));
    }

    Prune(logs);
    return Path.Join(".lip", "logs", name);
  }

  private static string Render(
      Exception exception,
      string[] args,
      string summary,
      IReadOnlyList<string> hints,
      DateTimeOffset now) {
    StringBuilder report = new();
    void Section(string title) => report.Append('\n').Append("== ").Append(title).Append(" ==\n");
    void Field(string name, string? value) =>
        report.Append(CultureInfo.InvariantCulture, $"{name + ":",-20}{value}\n");

    report.Append("lip failure report\n");
    Section("Environment");
    Field("Time (UTC)", now.ToString("O", CultureInfo.InvariantCulture));
    Field("lip version", Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    Field("Command", "lip " + string.Join(' ', args.Select(Quote)));
    Field("Working directory", Environment.CurrentDirectory);
    Field("OS", RuntimeInformation.OSDescription);
    Field("RID lip runs on", RuntimeInformation.RuntimeIdentifier);
    Field("Architecture", $"OS {RuntimeInformation.OSArchitecture}, process {RuntimeInformation.ProcessArchitecture}");
    Field(".NET", RuntimeInformation.FrameworkDescription);
    Field("Config directory", LipDirectories.Config);
    Field("Cache directory", LipDirectories.Cache);
    Field("Terminal", $"stdin {(Console.IsInputRedirected ? "redirected" : "terminal")},"
        + $" stdout {(Console.IsOutputRedirected ? "redirected" : "terminal")},"
        + $" stderr {(Console.IsErrorRedirected ? "redirected" : "terminal")}");
    foreach (string variable in (string[])["XDG_CONFIG_HOME", "XDG_CACHE_HOME", "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY"]) {
      if (Environment.GetEnvironmentVariable(variable) is { } value) {
        Field(variable, value);
      }
    }

    Section("Error");
    report.Append(summary).Append('\n');
    foreach (string hint in hints) {
      report.Append("hint: ").Append(hint).Append('\n');
    }

    Section("Exception");
    report.Append(exception).Append('\n');
    foreach (Exception cause in Chain(exception).Where(e => e.Data.Count > 0)) {
      report.Append(CultureInfo.InvariantCulture, $"Data of {cause.GetType().FullName}:\n");
      foreach (System.Collections.DictionaryEntry entry in cause.Data) {
        report.Append(CultureInfo.InvariantCulture, $"  {entry.Key} = {entry.Value}\n");
      }
    }

    Section("Workspace (.lip)");
    try {
      foreach (string entry in Directory.EnumerateFileSystemEntries(".lip").Order(StringComparer.Ordinal)) {
        string entryName = Path.GetFileName(entry);
        if (entryName == "logs") {
          continue;
        }
        report.Append(Directory.Exists(entry)
            ? $"{entryName}/\n"
            : $"{entryName} ({new FileInfo(entry).Length} bytes)\n");
      }
      foreach (string file in Directory.EnumerateFiles(".lip").Order(StringComparer.Ordinal)) {
        string fileName = Path.GetFileName(file);
        if (fileName.StartsWith("workspace.", StringComparison.Ordinal) || fileName == "journal") {
          report.Append(CultureInfo.InvariantCulture, $"\n--- {fileName} ---\n")
              .Append(File.ReadAllText(file)).Append('\n');
        }
      }
    }
    catch (Exception ex) {
      report.Append(CultureInfo.InvariantCulture, $"(cannot read: {ex.Message})\n");
    }

    Section("Trace");
    report.Append(RunTrace.Dump());
    return report.ToString();
  }

  // Credentials in URLs, such as a proxy's, are not written to the report.
  /// Masks credentials in the URLs of a text: the user info (https://user:token@host/) and the
  /// values of query parameters (?token=...), except the harmless git 'service' parameter. A
  /// github_proxy set by the user may carry either.
  private static string Redact(string text) =>
      Regex.Replace(text, @"\b[A-Za-z][A-Za-z0-9+.-]*://[^\s'""<>]+", url => {
        string redacted = Regex.Replace(url.Value, @"^([^:]+://)[^/?#@]+@", "$1***@");
        return Regex.Replace(redacted, @"([?&][^=&#]+)=([^&#]*)", parameter =>
            parameter.Groups[1].Value is "?service" or "&service" || parameter.Groups[2].Length == 0
                ? parameter.Value
                : $"{parameter.Groups[1].Value}=***");
      });

  private static string Quote(string arg) =>
      arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'')
          ? arg
          : $"\"{arg.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

  // Report names start with their time, so they sort from oldest to newest.
  private static void Prune(string logs) {
    try {
      foreach (string old in Directory.GetFiles(logs, "*.log")
          .Order(StringComparer.Ordinal)
          .SkipLast(KeptReports)) {
        File.Delete(old);
      }
    }
    catch (IOException) {
    }
    catch (UnauthorizedAccessException) {
    }
  }
}
