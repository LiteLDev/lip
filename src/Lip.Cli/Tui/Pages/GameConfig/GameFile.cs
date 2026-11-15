using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Config;

namespace Lip.Cli.Tui.Pages.GameConfig;

/// A configuration file of the server as the page edits it (protocol/lip-tui.md,
/// "Configuration"): read once, changed in memory, and written only when the user saves.
internal abstract class GameFile(string name, string? source) {
  /// The path of the file, relative to the workspace.
  public string Name { get; } = name;

  /// The text of the file as it was last read or written; null when it did not exist. Saving
  /// compares it with the file, which the server or the user may have changed meanwhile.
  public string? Source { get; private set; } = source;

  /// Why lip cannot read the file, in the words of what failed; null when it can. Such a file
  /// is shown and never written.
  public string? Problem { get; protected init; }

  /// The text of a file that lip cannot read as what it is, when it has its text.
  public string? Raw { get; protected init; }

  /// Whether the file does not exist and is not created by saving.
  public virtual bool Missing => false;

  /// Whether the user may change the file.
  public bool Editable => Problem is null && !Missing;

  /// Whether the file has changes that are not saved.
  public abstract bool Dirty { get; }

  /// The text of the file with the changes.
  public abstract string Serialize();

  /// The file on the disk now holds text.
  public virtual void MarkSaved(string text) => Source = text;
}

/// server.properties: saving replaces the values of the changed properties and nothing else.
/// It is not created when it does not exist.
internal sealed class PropertiesFile : GameFile {
  /// text is null for a file that does not exist; problem says why it could not be read.
  public PropertiesFile(string? text, string? problem)
      : base(ServerProperties.FileName, text) {
    Problem = problem;
    Properties = text is null || problem is not null ? null : ServerProperties.Parse(text);
  }

  /// The properties, or null when the file does not exist or cannot be read.
  public ServerProperties? Properties { get; private set; }

  public override bool Missing => Properties is null && Problem is null;

  public override bool Dirty => Properties?.Changed == true;

  public override string Serialize() => Properties!.Serialize();

  public override void MarkSaved(string text) {
    base.MarkSaved(text);
    Properties = ServerProperties.Parse(text);
  }
}

/// allowlist.json or permissions.json: an array of players, read as [] when the file does not
/// exist.
internal sealed class PlayerListFile : GameFile {
  private string _saved = "";
  private bool? _dirty;

  /// text is null for a file that does not exist; problem says why it could not be read.
  public PlayerListFile(string name, string? text, string? problem) : base(name, text) {
    Problem = problem;
    Players = [];
    if (problem is not null) {
      return;
    }

    try {
      Players = PlayerLists.Parse(text);
      _saved = JsonConfigFile.Serialize(Players);
    }
    catch (JsonException ex) {
      Problem = ex.Message;
      Raw = text;
    }
  }

  /// The players; change them only through Change.
  public JsonArray Players { get; }

  public override bool Dirty => _dirty ??= JsonConfigFile.Serialize(Players) != _saved;

  /// Changes the players.
  public void Change(Action<JsonArray> change) {
    change(Players);
    _dirty = null;
  }

  public override string Serialize() => JsonConfigFile.Serialize(Players);

  public override void MarkSaved(string text) {
    base.MarkSaved(text);
    _saved = text;
    _dirty = null;
  }
}
