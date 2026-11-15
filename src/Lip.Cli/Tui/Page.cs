namespace Lip.Cli.Tui;

/// A key of the bottom bar with what it does, in one or two words: "↑↓ Move".
public readonly record struct Hint(string Key, string Text);

/// A page of the interface: what the content area shows for an entry of the sidebar. Every
/// member is called on the thread of the interface; work that takes time goes through
/// AppContext.RunOperation or AppContext.Background, which come back to that thread.
///
/// A page is created once, when the interface starts, and lives as long as it. Its text is in
/// the language of the interface when it is drawn: never keep the result of L.T in a field.
public abstract class Page(AppContext context) {
  /// The services of the interface.
  protected AppContext Context { get; } = context;

  /// The name of the page, at most three words: the entry of the sidebar and the title of the
  /// content area.
  public abstract string Title { get; }

  /// A mark after the entry of the sidebar, such as ● while the server runs; none when null.
  public virtual StyledLine? Badge => null;

  /// The keys that the page takes now, for the bottom bar, without Esc when Esc goes back to
  /// the sidebar: the shell adds that one.
  public virtual IReadOnlyList<Hint> Hints => [];

  /// Whether the page has unsaved changes: the user is then asked through ConfirmLeave before
  /// another page or another workspace is shown, and before the interface quits.
  public virtual bool CanLeave => true;

  /// Draws the page in area, the inside of the content box. focused: whether the content area,
  /// and not the sidebar or a dialog, takes the keys.
  public abstract void Draw(ScreenBuffer screen, Rect area, bool focused);

  /// Handles a key while the content area has the focus; false when the page does not take it.
  /// The shell then handles it: Esc and ← go back to the sidebar, Tab goes on to it, and
  /// Ctrl+C asks to quit.
  public virtual bool OnKey(ConsoleKeyInfo key) => false;

  /// Called once, when the interface starts, before anything is drawn.
  public virtual void OnStart() {
  }

  /// Called when the page becomes the one shown.
  public virtual void OnShown() {
  }

  /// Called when another page becomes the one shown.
  public virtual void OnHidden() {
  }

  /// Called four times a second, whether the page is shown or not.
  public virtual void OnTick() {
  }

  /// Called when another workspace was selected, or an operation changed the selected one: what
  /// the page read of the workspace is stale. Called for every page, shown or not.
  public virtual void OnWorkspaceChanged() {
  }

  /// Called after the page was shown through AppContext.Navigate with a request, one of
  /// PageRequest: another page asks this one to start something.
  public virtual void OnRequest(string request) {
  }

  /// Called instead of leaving the page when CanLeave is false: asks the user to save or
  /// discard the changes, and calls leave once they are saved or discarded. Not calling it
  /// keeps the page.
  public virtual void ConfirmLeave(Action leave) => leave();
}
