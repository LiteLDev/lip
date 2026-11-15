using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Lip.Core.Services;

namespace Lip.Cli.Tui;

/// The reachability of GitHub (protocol/lip-tui.md, "GitHub reachability"): when the interface
/// starts and github_proxy is not configured, lip checks in the background that it reaches
/// GitHub; when it does not, the interface asks the user for a proxy, to connect directly, or to
/// be asked again later.
public sealed partial class TuiApp {
  // Why GitHub was not reached, until the interface asks the user about it.
  private string? _githubUnreachable;

  /// Starts the check, which posts its result to the interface; the interface goes on meanwhile.
  private void CheckGithub() {
    if (_githubProbe is not { } probe) {
      return;
    }

    LipClient client = _client;
    _ = Task.Run(async () => {
      try {
        if (await client.GithubProxy() is not null) {
          return;
        }

        if (await probe.Check(null) is { } failure) {
          Post(() => _githubUnreachable = failure);
        }
      }
      catch (Exception ex) {
        // A failure of the check is no failure of the interface.
        RunTrace.Write("github", "The GitHub check did not run: " + ex);
      }
    });
  }

  /// Asks the user about GitHub once no dialog is open, so that the question interrupts nothing.
  private void AskAboutGithubWhenIdle() {
    if (_githubUnreachable is not { } failure || _dialogs.Count > 0) {
      return;
    }

    _githubUnreachable = null;
    AskAboutGithub(failure);
  }

  private void AskAboutGithub(string failure) {
    Push(new MessageDialog(
        this, "GitHub is not reachable",
        [
          new StyledLine("lip could not reach GitHub. " + failure, Theme.Warning),
          new StyledLine(),
          new StyledLine("lip downloads LeviLamina and most mods from GitHub. Where GitHub is"
              + " blocked or slow, a GitHub proxy helps: a URL prefix that lip puts before the"
              + " URLs of GitHub (the configuration key github_proxy)."),
          new StyledLine(),
          new StyledLine().Add("Set a proxy", Theme.Strong)
              .Add(": enter its URL; lip tests it before saving it."),
          new StyledLine().Add("Connect directly", Theme.Strong)
              .Add(": lip connects to GitHub directly and does not ask again."),
          new StyledLine().Add("Later", Theme.Strong)
              .Add(": nothing changes; lip asks again when it next starts."),
          new StyledLine(),
          new StyledLine("Either can be changed in 5 Settings.", Theme.Muted),
        ],
        ["Set a proxy", "Connect directly", "Later"],
        choice => {
          switch (choice) {
            case 0:
              PromptGithubProxy("");
              break;
            case 1:
              SaveGithubProxy("");
              break;
          }
        }));
  }

  /// Asks for a proxy, tests it, and saves it when it works, or when the user keeps it although
  /// it did not.
  private void PromptGithubProxy(string value) {
    Push(new PromptDialog(
        this, "GitHub proxy",
        [
          new PromptField(
              "The URL prefix of the proxy; lip tests it before saving it", value,
              "https://example.com/"),
        ],
        values => {
          string proxy = values[0];
          if (proxy.Length == 0) {
            return;
          }

          string? failure = null;
          Start(
              "Test the GitHub proxy",
              async token => {
                try {
                  failure = await _githubProbe!.Check(proxy, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                  // Such as a URI that the request cannot be sent to: the proxy did not work.
                  failure = ex.Message;
                }
              },
              _ => {
                if (failure is null) {
                  SaveGithubProxy(proxy);
                  return;
                }

                Push(new MessageDialog(
                    this, "The proxy did not reach GitHub",
                    [new StyledLine(failure, Theme.Warning)],
                    ["Enter another", "Save anyway", "Later"],
                    choice => {
                      switch (choice) {
                        case 0:
                          PromptGithubProxy(proxy);
                          break;
                        case 1:
                          SaveGithubProxy(proxy);
                          break;
                      }
                    }, border: Theme.Warning));
              });
        }));
  }

  /// Sets github_proxy, as lip config set does.
  private void SaveGithubProxy(string value) {
    LipClient client = _client;
    Start(
        $"Set {ConfigService.GithubProxyKey}",
        _ => client.ConfigSet(ConfigService.GithubProxyKey, value),
        done => {
          ShowResult(
              "GitHub", done,
              value.Length == 0
                  ? "lip connects to GitHub directly."
                  : "lip reaches GitHub through the proxy.");
          if (_tab == SettingsTab) {
            LoadConfig();
          }
        });
  }
}
