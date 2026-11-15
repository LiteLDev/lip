using Flurl;
using Lip.Core.Git;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Resolution;

namespace Lip.Core.PublicApi;

/// What the commands of one flow share of the network: a dry run and the command that carries
/// out its plan, with what was read before them, such as the versions that the user chose
/// from (protocol/lip-tui.md, "Commands"). The caller makes one, and gives it to each of them.
///
/// A command with a session reads of the network only what no command of the session read
/// before it: the version tags and the refs of the remote repositories, the files of their
/// commits, their package files, and the builds list of the server. A download that a dry run
/// kept in memory, since a dry run changes no file of the cache, is stored in the cache and
/// used by the command that comes after it, instead of being downloaded again; and a URL that
/// lip update downloads again is downloaded again once in the session. Everything else is of
/// each command: it takes the workspace lock, recovers, reads the state file and the
/// configuration, and solves, as it does without a session.
///
/// A session is for commands that come one after another. It holds what its dry runs
/// downloaded until a command stores it: let go of the session with the flow.
public sealed class OperationSession {
  private readonly Lock _gate = new();
  private Network? _network;

  /// What the session shares for the value of github_proxy: the same while the value is the
  /// same, and started over when it is another.
  internal Network NetworkFor(string? githubProxyValue, Url? githubProxy) {
    lock (_gate) {
      if (_network is null || _network.GithubProxyValue != githubProxyValue) {
        _network = new Network(githubProxyValue, githubProxy);
      }

      return _network;
    }
  }

  /// The client of the remote repositories, and what was read through it.
  internal sealed class Network(string? githubProxyValue, Url? githubProxy) {
    public string? GithubProxyValue { get; } = githubProxyValue;

    public GitRemoteClient Git { get; } = new(githubProxy);

    public ArchiveMemory Archives { get; } = new();

    public ResolverCache Resolver { get; } = new();
  }
}
