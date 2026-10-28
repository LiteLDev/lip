using Lip.Core.PublicApi;

namespace Lip.Daemon;

/// The JSON-RPC methods of lipd, exactly as schemas/lipd.v4.openrpc.json defines them: every
/// public method here is a method of the schema, with the schema's parameter names and result
/// shapes. Requests are forwarded to the client; a CancellationToken parameter is bound by
/// StreamJsonRpc to the $/cancelRequest of the request.
public class DaemonRpcTarget(ILipClient client) {
  private readonly ILipClient _client = client;

  public Task CacheClean() => _client.CacheClean();

  public Task ConfigDelete(string key) => _client.ConfigDelete(key);

  public Task<string> ConfigGet(string key) => _client.ConfigGet(key);

  public Task<IDictionary<string, string>> ConfigList() => _client.ConfigList();

  public Task ConfigSet(string key, string value) => _client.ConfigSet(key, value);

  public Task Init() => _client.Init();

  public Task Install(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? rid = null,
      CancellationToken cancellationToken = default) =>
      _client.Install(packages, dryRun, ignoreScripts, noDependencies, rid, cancellationToken);

  /// [explicitly installed, dependencies], each an array of "name@version". A value tuple would
  /// be serialized as an object with Item1 and Item2.
  public async Task<string[][]> List() {
    (IEnumerable<string> explicitInstalled, IEnumerable<string> implicitInstalled) =
        await _client.List();

    return [[.. explicitInstalled], [.. implicitInstalled]];
  }

  public Task Migrate() => _client.Migrate();

  public Task Uninstall(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      bool noDependencies,
      string? rid = null,
      CancellationToken cancellationToken = default) =>
      _client.Uninstall(packages, dryRun, ignoreScripts, noDependencies, rid, cancellationToken);

  public Task Update(
      IEnumerable<string> packages,
      bool dryRun,
      bool ignoreScripts,
      string? rid = null,
      CancellationToken cancellationToken = default) =>
      _client.Update(packages, dryRun, ignoreScripts, rid, cancellationToken);

  public Task<IEnumerable<string>> Versions(string package) => _client.Versions(package);

  public Task<string> View(string package) => _client.View(package);
}
