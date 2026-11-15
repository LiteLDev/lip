using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Cli.Tui.I18n;
using Lip.Cli.Tui.Widgets;
using Lip.Core.Config;

namespace Lip.Cli.Tui.Pages.GameConfig;

/// The rows of the sections of the Game config page, and what Enter and Space do on them.
public sealed partial class GameConfigPage {
  /// The row "Add player…", before the players.
  private const int AddRow = -1;

  private const string IgnoresLimitMember = "ignoresPlayerLimit";

  /// The keys that act on the selected row.
  private IReadOnlyList<Hint> EntryHints {
    get {
      if (!CurrentList.HasCurrent) {
        return [];
      }

      if (ReadOnly) {
        // Enter shows the actions of the row, none of which can be chosen now.
        return CurrentList.Current == AddRow ? [] : [CommonText.HintActions];
      }

      if (CurrentProperty is { } property) {
        Hint enter = property.Changed ? CommonText.HintActions : CommonText.HintEdit;
        return property.Type.Kind == PropertyKind.Boolean
            ? [enter, CommonText.HintToggle]
            : [enter];
      }

      return [CurrentList.Current == AddRow ? CommonText.HintSelect : CommonText.HintActions];
    }
  }

  /// The columns of the value of a property in its row.
  private int ValueWidth => Math.Max(1, _entryWidth - _nameWidth - 3);

  /// The selected property, when the section is that of the properties and it has one.
  private ServerProperty? CurrentProperty =>
      CurrentFile is PropertiesFile { Properties: { } properties }
      && CurrentList.HasCurrent && CurrentList.Current < properties.Properties.Count
          ? properties.Properties[CurrentList.Current]
          : null;

  /// Starts what changes a file unless the server runs: then it says why not, and nothing
  /// changes. A dialog that was open when the server started is not kept from finishing: what
  /// the user gave it is kept with the other changes, unsaved until the server has stopped.
  private void IfEditable(Action change) {
    if (ReadOnly) {
      Context.Toast(CommonText.StopServerFirst, ToastKind.Warning);
    } else {
      change();
    }
  }

  /// An action of the menu of a row. While the server runs it cannot be chosen, and says why.
  private MenuItem Item(string label, Action run) =>
      new(
          label, () => IfEditable(run), Enabled: !ReadOnly,
          Reason: ReadOnly ? CommonText.StopServerFirst : null);

  /// Carries out Enter, or Space, on the selected row; false when the key does nothing there.
  /// While the server runs Enter shows the actions of the row, disabled, and whatever would
  /// change something at once says why it does not.
  private bool Act(bool space) {
    if (!CurrentList.HasCurrent) {
      return false;
    }

    switch (CurrentFile) {
      case PropertiesFile { Properties: { } properties } when CurrentProperty is { } property:
        if (space) {
          if (property.Type.Kind != PropertyKind.Boolean) {
            return false;
          }

          IfEditable(() => Toggle(properties, property));
        } else if (property.Changed || ReadOnly) {
          Context.ShowMenu(
              property.Key,
              [
                Item(EditLabel(property), () => Edit(properties, property)),
                .. property.Changed
                    ? [Item(GameConfigText.Reset, () => ServerProperties.Reset(property))]
                    : (MenuItem[])[],
              ]);
        } else {
          Edit(properties, property);
        }

        return true;
      case PlayerListFile players when !space:
        if (CurrentList.Current == AddRow) {
          IfEditable(() => AddPlayer(players));
        } else if (CurrentList.Current < players.Players.Count) {
          ShowPlayerMenu(players, players.Players[CurrentList.Current]);
        }

        return true;
      default:
        return false;
    }
  }

  #region Properties

  private static string EditLabel(ServerProperty property) => property.Type.Kind switch {
    PropertyKind.Boolean => GameConfigText.Toggle,
    PropertyKind.Choice => GameConfigText.Choose,
    _ => GameConfigText.Edit,
  };

  /// Switches a boolean; a value that is true but for its case or the spaces around it counts
  /// as true.
  private static void Toggle(ServerProperties properties, ServerProperty property) =>
      properties.Set(
          property,
          property.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
              ? "false"
              : "true");

  /// Changes a property with the editor of its type: a boolean is switched, an enumeration is
  /// chosen from its values, and anything else is typed.
  private void Edit(ServerProperties properties, ServerProperty property) {
    PropertyType type = property.Type;
    switch (type.Kind) {
      case PropertyKind.Boolean:
        Toggle(properties, property);
        break;
      case PropertyKind.Choice:
        Context.Show(new ChoiceDialog(
            property.Key,
            [.. type.Choices.Select(choice => new ChoiceItem(choice))],
            chosen => properties.Set(property, type.Choices[chosen]),
            selected: Math.Max(0, type.Choices.ToList().IndexOf(property.Value))));
        break;
      default:
        string label = type is { Kind: PropertyKind.Integer, Min: { } min, Max: { } max }
            ? GameConfigText.ValueBetween(min, max)
            : GameConfigText.Value;
        Context.Show(new PromptDialog(
            property.Key,
            [new PromptField(label, property.Value)],
            values => {
              // The prompt gives the text without the spaces around it: a value that only
              // lost those is not a change.
              if (values[0] != property.Value.Trim()) {
                properties.Set(property, values[0]);
              }
            },
            validate: values => Invalid(type, values[0])));
        break;
    }
  }

  /// Why the value is not one of the type, in the language of the interface, or null.
  private static string? Invalid(PropertyType type, string value) {
    if (type.Check(value) is null) {
      return null;
    }

    if (type.Kind != PropertyKind.Integer) {
      return GameConfigText.NotOneLine;
    }

    return type switch {
      { Min: { } min, Max: { } max } => GameConfigText.NotBetween(min, max),
      { Min: { } min } => GameConfigText.NotAtLeast(min),
      _ => GameConfigText.NotAnInteger,
    };
  }

  private StyledLine RenderProperty(int index) {
    if (_files?[PropertiesSection] is not PropertiesFile { Properties: { } properties }
        || index >= properties.Properties.Count) {
      return new StyledLine();
    }

    ServerProperty property = properties.Properties[index];
    StyledLine line = new(property.Changed ? ChangedMark + " " : "  ", Theme.Accent);
    line.Add(Pad(property.Key, _nameWidth) + " ");
    (string text, Style style) = property switch {
      { Type.Kind: PropertyKind.Boolean, Value: "true" } => ("[x]", Theme.Good),
      { Type.Kind: PropertyKind.Boolean, Value: "false" } => ("[ ]", default),
      { Value.Length: 0 } => (GameConfigText.EmptyValue, Theme.Muted),
      _ => (property.Value, default(Style)),
    };
    return line.Add(
        TextWidth.Fit(text, ValueWidth),
        property.Changed ? Theme.Accent : style);
  }

  #endregion

  #region Players

  private static bool IgnoresLimit(JsonNode? player) =>
      player is JsonObject o && o[IgnoresLimitMember]?.GetValueKind() == JsonValueKind.True;

  /// What a player is called in its list: the name in the allowlist, the xuid in the
  /// permissions.
  private static string PlayerLabel(PlayerListFile file, JsonNode? player) =>
      player is not JsonObject ? player?.ToJsonString() ?? "null"
          : PlayerLists.Text(player, IsAllowlist(file) ? "name" : "xuid");

  private static bool IsAllowlist(PlayerListFile file) =>
      file.Name == PlayerLists.AllowlistFileName;

  private void AddPlayer(PlayerListFile file) {
    if (IsAllowlist(file)) {
      Context.Show(new PromptDialog(
          GameConfigText.AddPlayerTitle,
          [
            new PromptField(GameConfigText.PlayerName, "", GameConfigText.PlayerNameExample),
            new PromptField(GameConfigText.XuidOptional, "", GameConfigText.XuidExample),
          ],
          values => {
            file.Change(players => PlayerLists.Allow(players, values[0], values[1]));
            PlayersChanged(file, file.Players.Count - 1);
          },
          validate: values =>
              values[0].Length == 0 ? CommonText.Required
                  : file.Players.Any(p => PlayerLists.Text(p, "name").Equals(
                      values[0], StringComparison.OrdinalIgnoreCase))
                      ? GameConfigText.AlreadyListed
                  : !values[1].All(char.IsAsciiDigit) ? GameConfigText.NotAXuid
                  : null));
      return;
    }

    Context.Show(new PromptDialog(
        GameConfigText.AddPlayerTitle,
        [new PromptField(GameConfigText.Xuid, "", GameConfigText.XuidExample)],
        values => ChoosePermission(
            values[0],
            PlayerLists.Text(
                file.Players.FirstOrDefault(p => PlayerLists.Text(p, "xuid") == values[0]),
                "permission"),
            permission => {
              file.Change(
                  players => PlayerLists.SetPermission(players, values[0], permission));
              PlayersChanged(
                  file,
                  file.Players.ToList().FindIndex(
                      p => PlayerLists.Text(p, "xuid") == values[0]));
            }),
        validate: values =>
            values[0].Length == 0 ? CommonText.Required
                : !values[0].All(char.IsAsciiDigit) ? GameConfigText.NotAXuid
                : null));
  }

  /// The action menu of a player.
  private void ShowPlayerMenu(PlayerListFile file, JsonNode? player) {
    List<MenuItem> items = [];
    if (player is JsonObject entry && IsAllowlist(file)) {
      items.Add(Item(
          IgnoresLimit(entry) ? GameConfigText.ApplyLimit : GameConfigText.IgnoreLimit,
          () => file.Change(_ => entry[IgnoresLimitMember] = !IgnoresLimit(entry))));
    } else if (player is JsonObject listed) {
      items.Add(Item(
          GameConfigText.ChangePermission,
          () => ChoosePermission(
              PlayerLabel(file, listed), PlayerLists.Text(listed, "permission"),
              permission => file.Change(_ => listed["permission"] = permission))));
    }

    items.Add(Item(GameConfigText.Remove, () => {
      file.Change(players => players.Remove(player));
      PlayersChanged(file, null);
    }));
    Context.ShowMenu(PlayerLabel(file, player), [.. items]);
  }

  /// Asks for a permission, starting at the current one; at "member", which is what the
  /// server gives by default, when there is none, and never at "operator" unasked.
  private void ChoosePermission(string title, string current, Action<string> chosen) {
    IReadOnlyList<string> permissions = PlayerLists.Permissions;
    Context.Show(new ChoiceDialog(
        title.Length > 0 ? title : GameConfigText.Permission,
        [
          .. permissions.Select(permission => new ChoiceItem(permission, permission switch {
            "operator" => GameConfigText.OperatorDetail,
            "member" => GameConfigText.MemberDetail,
            _ => GameConfigText.VisitorDetail,
          })),
        ],
        index => chosen(permissions[index]),
        selected: permissions.Contains(current)
            ? permissions.ToList().IndexOf(current)
            : permissions.ToList().IndexOf("member")));
  }

  /// The rows of a list follow its players; the player at index, when given, is selected.
  private void PlayersChanged(PlayerListFile file, int? index) {
    RefreshEntries();
    if (index is { } player && _files is { } files) {
      _lists[Array.IndexOf(files, file)].Select(row => row == player);
    }
  }

  private StyledLine RenderPlayer(int section, int index) {
    if (index == AddRow) {
      return new StyledLine(
          "+ " + GameConfigText.AddPlayer, ReadOnly ? Theme.Muted : Theme.Accent);
    }

    if (_files?[section] is not PlayerListFile file || index >= file.Players.Count) {
      return new StyledLine();
    }

    JsonNode? player = file.Players[index];
    if (player is not JsonObject) {
      return new StyledLine("  " + PlayerLabel(file, player), Theme.Muted);
    }

    StyledLine line = new("  " + Pad(PlayerLabel(file, player), _nameWidth) + " ");
    if (!IsAllowlist(file)) {
      return line.Add(PlayerLists.Text(player, "permission"));
    }

    line.Add(PlayerLists.Text(player, "xuid"), Theme.Muted);
    return IgnoresLimit(player)
        ? line.Add("  " + GameConfigText.IgnoresLimit, Theme.Tag)
        : line;
  }

  #endregion

  /// The columns of the names of the rows of a file, in a list of width columns.
  private static int NameWidth(GameFile file, int width) {
    IEnumerable<string> names = file switch {
      PropertiesFile { Properties: { } properties } => properties.Properties.Select(p => p.Key),
      PlayerListFile players =>
          players.Players.Where(p => p is JsonObject).Select(p => PlayerLabel(players, p)),
      _ => [],
    };
    // The values keep at least 14 columns, which most of them fit in, and stay near the names.
    return Math.Clamp(
        names.Select(TextWidth.Of).DefaultIfEmpty(0).Max(), 1,
        Math.Clamp(width - 3 - 14, 1, 40));
  }

  /// What the details area says of the selected row: the name and the value of a property
  /// that the list cut, its value before the change, and its comment in the file; one sentence
  /// about a list of players on its first row.
  private List<StyledLine> Details(GameFile file, int width) {
    List<StyledLine> lines = [];
    void Add(string text, Style style) =>
        lines.AddRange(TextWidth.Wrap(text, width).Select(line => new StyledLine(line, style)));

    if (CurrentProperty is { } property) {
      if (TextWidth.Of(property.Key) > _nameWidth) {
        Add(property.Key, Theme.Strong);
      }

      if (TextWidth.Of(property.Value) > ValueWidth) {
        Add(property.Value, default);
      }

      if (property.Changed) {
        Add(
            GameConfigText.Original(
                property.Original.Length == 0 ? GameConfigText.EmptyValue : property.Original),
            Theme.Accent);
      }

      foreach (string comment in property.Comment.Where(comment => comment.Length > 0)) {
        Add(comment, Theme.Muted);
      }
    } else if (file is PlayerListFile players && CurrentList.HasCurrent
        && CurrentList.Current == AddRow) {
      if (ReadOnly) {
        Add(CommonText.StopServerFirst, Theme.Warning);
      }

      Add(
          IsAllowlist(players) ? GameConfigText.AllowlistDetail : GameConfigText.PermissionsDetail,
          Theme.Muted);
    }

    return lines;
  }
}
