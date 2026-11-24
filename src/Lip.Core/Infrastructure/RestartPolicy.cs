namespace Lip.Core.Infrastructure;

/// How lip run restarts a server that failed (protocol/lip-run.md, "Restarting"): after Delay, at
/// most Limit times within Window, by the clock of Time.
public sealed record RestartPolicy(int Limit, TimeSpan Window, TimeSpan Delay, TimeProvider Time) {
  /// The policy of protocol/lip-run.md, "Restarting": 5 seconds, at most 5 times within 10
  /// minutes.
  public static RestartPolicy Default { get; } =
      new(5, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5), TimeProvider.System);
}

/// What lip run tells about the servers it runs, besides its messages, for the interface and the
/// managed servers (protocol/lip-run.md, "Managed servers").
public sealed record ServerRunOptions {
  /// How a server that failed is restarted.
  public RestartPolicy Restart { get; init; } = RestartPolicy.Default;

  /// Called each time a server has started.
  public Action? Started { get; init; }

  /// Called with the exit code of the server when lip waits to restart it.
  public Action<int>? Restarting { get; init; }

  /// The user accepts the license of Rosetta 2 (--accept-rosetta-license), so that preparing the
  /// system installs it without asking (protocol/lip-workspace.md, "System dependencies").
  public bool AcceptRosettaLicense { get; init; }
}
