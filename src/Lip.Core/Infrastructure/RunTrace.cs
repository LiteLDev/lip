using System.Globalization;
using System.Text;
using Flurl.Http;

namespace Lip.Core.Infrastructure;

/// What lip did for one invocation, in order: network requests, hook commands and their output,
/// messages shown to the user. It is kept in memory and written to the failure report when a
/// command fails (protocol/lip.md), so that the report shows how lip got to the failure.
public sealed class TraceLog {
  // Enough for the output of large hook commands; older lines are dropped first.
  private const int MaxChars = 8 * 1024 * 1024;

  private readonly object _gate = new();
  private readonly LinkedList<string> _lines = new();
  private int _chars;
  private int _dropped;

  public void Write(string category, string message) {
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

  /// Every line written so far, oldest first.
  public string Dump() {
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

/// The trace of the current invocation (Invocation.Current).
public static class RunTrace {
  public static void Write(string category, string message) =>
      Invocation.Current.Trace.Write(category, message);

  /// Traces every HTTP request made through Flurl: its method, URL, status and duration, or the
  /// error it failed with; and sends it through the proxy that the environment variables of the
  /// invocation name (InvocationProxy). Before each, checks that .NET can load OpenSSL
  /// (OpenSsl.EnsureLoadable).
  public static void TraceHttp() {
    FlurlHttp.Clients.WithDefaults(builder => builder
        .ConfigureInnerHandler(handler => {
          handler.Proxy = InvocationProxy.Instance;
          handler.UseProxy = true;
        })
        .BeforeCall(call => {
          Write("http", $"{call.Request.Verb} {call.Request.Url}");
          OpenSsl.EnsureLoadable();
        })
        .OnError(call => Write("http", $"{call.Request.Verb} {call.Request.Url} failed:"
            + $" {call.Exception?.GetType().Name}: {call.Exception?.Message}"))
        .AfterCall(call => {
          if (call.Exception is null) {
            Write("http", $"{call.Request.Verb} {call.Request.Url} -> {call.Response?.StatusCode}"
                + $" in {call.Duration?.TotalMilliseconds:0} ms");
          }
        }));
  }

  /// Every line written so far for the current invocation, oldest first.
  public static string Dump() => Invocation.Current.Trace.Dump();
}
