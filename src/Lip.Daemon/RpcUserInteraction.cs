using Lip.Core.Infrastructure;
using StreamJsonRpc;

namespace Lip.Daemon;

/// Sends the output of an operation to the client as JSON-RPC notifications (PrintInfo,
/// PrintSuccess, PrintWarning, PrintError, ReportProgress; see schemas/lipd.v4.openrpc.json).
/// They are notifications, not requests: the client does not answer them, and an operation never
/// waits for, or fails on, a client that does not handle them.
public class RpcUserInteraction(JsonRpc rpc) : IUserInteraction {
  private readonly JsonRpc _rpc = rpc;

  public Task PrintError(string message) => _rpc.NotifyAsync("PrintError", message);

  public Task PrintInfo(string message) => _rpc.NotifyAsync("PrintInfo", message);

  public Task PrintSuccess(string message) => _rpc.NotifyAsync("PrintSuccess", message);

  public Task PrintWarning(string message) => _rpc.NotifyAsync("PrintWarning", message);

  public async Task RunWithProgress(string message, Func<IProgress<double>, Task> action) {
    string progressId = Guid.NewGuid().ToString();

    Progress<double> progress = new(p => {
      // Progress is best effort: a lost report must not fail the operation.
      _ = _rpc.NotifyAsync("ReportProgress", progressId, message, p)
          .ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    });

    await action(progress);
  }
}
