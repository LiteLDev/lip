using Lip.Core.PublicApi;
using Spectre.Console.Cli;
using StreamJsonRpc;

namespace Lip.Daemon.Commands;

public class RunCommand : AsyncCommand<RunCommand.Settings> {
  public class Settings : CommandSettings {
  }

  public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) {
    using JsonRpc rpc = new(
        Console.OpenStandardOutput(),
        Console.OpenStandardInput());

    IClientContract clientProxy = rpc.Attach<IClientContract>();

    RpcUserInteraction userInteraction = new(clientProxy);

    using LipClient client = await LipClient.Create(userInteraction);

    rpc.AddLocalRpcTarget(client);

    rpc.StartListening();

    await rpc.Completion;

    return 0;
  }
}
