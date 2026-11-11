using System.Globalization;
using System.Text;
using Flurl.Http;

namespace Lip.Core.Infrastructure;

/// What lip did during this run, in order: network requests, hook commands and their output,
/// messages shown to the user. It is kept in memory and written to the failure report when a
/// command fails (protocol/lip.md), so that the report shows how lip got to the failure.
public static class RunTrace {
  // Enough for the output of large hook commands; older lines are dropped first.
  private const int MaxChars = 8 * 1024 * 1024;

  private static readonly object _gate = new();
  private static readonly LinkedList<string> _lines = new();
  private static int _chars;
  private static int _dropped;

  public static void Write(string category, string message) {
    string line = string.Create(
        CultureInfo.InvariantCulture,
        $"{DateTimeOffset.UtcNow:HH:mm:ss.fff} [{category}] {message}");

    lock (_gate) {
      _lines.AddLast(line);
      _chars += line.Length;
      while (_chars > MaxChars && _lines.First is { } first) {
        _chars -= first.Value.Length;
        _lines.RemoveFirst();
        _dropped++;
      }
    }
  }

  /// Traces every HTTP request made through Flurl: its method, URL, status and duration, or the
  /// error it failed with.
  public static void TraceHttp() {
    FlurlHttp.Clients.WithDefaults(builder => builder
        .BeforeCall(call => Write("http", $"{call.Request.Verb} {call.Request.Url}"))
        .OnError(call => Write("http", $"{call.Request.Verb} {call.Request.Url} failed:"
            + $" {call.Exception?.GetType().Name}: {call.Exception?.Message}"))
        .AfterCall(call => {
          if (call.Exception is null) {
            Write("http", $"{call.Request.Verb} {call.Request.Url} -> {call.Response?.StatusCode}"
                + $" in {call.Duration?.TotalMilliseconds:0} ms");
          }
        }));
  }

  /// Every line written so far, oldest first.
  public static string Dump() {
    lock (_gate) {
      StringBuilder builder = new();
      if (_dropped > 0) {
        builder.Append(CultureInfo.InvariantCulture, $"({_dropped} older lines dropped)\n");
      }
      foreach (string line in _lines) {
        builder.Append(line).Append('\n');
      }
      return builder.ToString();
    }
  }
}
