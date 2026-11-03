using Lip.Core.Infrastructure;

namespace Lip.Cli;

public class RpcUserInteraction(IClientContract clientContract) : IUserInteraction {
  private readonly IClientContract _clientContract = clientContract;

  public async Task PrintError(string message) {
    await _clientContract.PrintError(message);
  }

  public async Task PrintInfo(string message) {
    await _clientContract.PrintInfo(message);
  }

  public async Task PrintSuccess(string message) {
    await _clientContract.PrintSuccess(message);
  }

  public async Task PrintWarning(string message) {
    await _clientContract.PrintWarning(message);
  }

  public async Task RunWithProgress(string message, Func<IProgress<double>, Task> action) {
    string progressId = Guid.NewGuid().ToString();

    Progress<double> progress = new(p => {
      _clientContract.ReportProgress(progressId, message, p);
    });

    await action(progress);
  }
}
