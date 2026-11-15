using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;

namespace Lip.Cli.Tui.Pages.Home;

/// What the user answers to the GitHub question (protocol/lip-tui.md, "GitHub reachability").
public enum GithubAnswer {
  Proxy,
  Direct,
  Later,
}

/// Asks how lip reaches GitHub once the reachability check failed: why it failed, the three
/// answers, and one sentence about the selected one. ↑ and ↓ move, Enter answers, and Esc
/// answers "later", which changes nothing and is the answer selected at first.
public sealed class GithubDialog : Dialog {
  private readonly string _failure;
  private readonly Action<GithubAnswer> _answered;
  private readonly ListView<GithubAnswer> _answers;

  /// failure is why the check failed, as the check says it; answered gets the answer once the
  /// dialog is closed.
  public GithubDialog(string failure, Action<GithubAnswer> answered) {
    _failure = failure;
    _answered = answered;
    _answers = new ListView<GithubAnswer>(answer => new StyledLine(Label(answer))) {
      Wrap = true,
      Items = [GithubAnswer.Proxy, GithubAnswer.Direct, GithubAnswer.Later],
    };
    // The question comes unasked, perhaps while the user presses Enter for something else:
    // the answer that changes nothing is the selected one.
    _answers.Selected = _answers.Items.Count - 1;
  }

  public override IReadOnlyList<Hint> Hints =>
      [CommonText.HintMove, CommonText.HintSelect, CommonText.HintCancel];

  public override void Draw(ScreenBuffer screen) {
    int width = Ui.DialogWidth(screen, 64);
    List<string> failure = TextWidth.Wrap(_failure, width - 4);
    List<string> detail = TextWidth.Wrap(Detail(_answers.Current), width - 4);
    // Two rows for the detail, so that the dialog keeps its size while the selection moves.
    int detailRows = Math.Max(2, detail.Count);
    int answers = _answers.Items.Count;
    Rect inner = Ui.DialogFrame(
        screen, HomeText.GithubTitle, width, failure.Count + answers + detailRows + 6);
    // On a small screen the failure is cut, never the answers.
    int room = Math.Max(0, inner.Height - answers - detailRows - 4);
    int y = inner.Y;
    foreach (string line in failure.Take(room)) {
      screen.Write(inner.X, y++, line, Theme.Warning, inner.Right);
    }

    y++;
    screen.WriteFit(inner.X, y, inner.Width, HomeText.GithubQuestion, Theme.Strong);
    y += 2;
    _answers.Draw(screen, new Rect(inner.X, y, inner.Width, answers), true);
    y += answers;
    screen.Write(inner.X, y++, new string('─', inner.Width), Theme.Border);
    for (int i = 0; i < detail.Count && y < inner.Bottom; i++) {
      screen.Write(inner.X, y++, detail[i], Theme.Muted, inner.Right);
    }
  }

  public override void OnKey(ConsoleKeyInfo key) {
    switch (key.Key) {
      case ConsoleKey.Escape:
        Close();
        _answered(GithubAnswer.Later);
        return;
      case ConsoleKey.Enter:
        Close();
        _answered(_answers.Current);
        return;
    }

    _answers.OnKey(key);
  }

  private static string Label(GithubAnswer answer) =>
      answer switch {
        GithubAnswer.Proxy => HomeText.GithubProxy,
        GithubAnswer.Direct => CommonText.ConnectDirectly,
        _ => CommonText.Later,
      };

  private static string Detail(GithubAnswer answer) =>
      answer switch {
        GithubAnswer.Proxy => HomeText.GithubProxyDetail,
        GithubAnswer.Direct => HomeText.GithubDirectDetail,
        _ => HomeText.GithubLaterDetail,
      };
}
