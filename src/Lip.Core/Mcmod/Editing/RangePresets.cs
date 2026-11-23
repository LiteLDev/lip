using Semver;

namespace Lip.Core.Mcmod.Editing;

/// The ranges that the editor offers for a dependency on a version that the user chose, each a
/// range (protocol/lip-package.md, "Terms").
public enum RangePreset {
  /// Every version from the one chosen up to the next minor version: its patch versions that
  /// are not lower than it, such as >=1.4.2 <1.5.0. What the editor recommends.
  Patch,

  /// Every version from the one chosen up to the next major version, as ^ means: ^1.4.2.
  Minor,

  /// The version chosen or any higher one: >=1.4.2.
  AtLeast,

  /// The version chosen only: 1.4.2.
  Exact,

  /// Any version: *.
  Any,
}

public static class RangePresets {
  /// The presets in the order the editor offers them, the recommended one first.
  public static IReadOnlyList<RangePreset> All { get; } = Enum.GetValues<RangePreset>();

  /// The range of a preset for a version.
  public static string Of(RangePreset preset, SemVersion version) {
    string text = version.WithoutMetadata().ToString();
    return preset switch {
      RangePreset.Patch => $">={text} <{version.Major}.{version.Minor + 1}.0",
      RangePreset.Minor => $"^{text}",
      RangePreset.AtLeast => $">={text}",
      RangePreset.Exact => text,
      _ => "*",
    };
  }

  /// The preset that gives a range for a version, or null when none does.
  public static RangePreset? Match(string range, SemVersion version) {
    foreach (RangePreset preset in All) {
      if (Of(preset, version) == range.Trim()) {
        return preset;
      }
    }

    return null;
  }

  /// The version that a range starts at, when it is one that a preset gives: what the editor
  /// takes as the version chosen for a dependency that was read.
  public static SemVersion? Start(string range) {
    string text = range.Trim();
    foreach (string prefix in (string[])[">=", "^", "~", "="]) {
      if (text.StartsWith(prefix, StringComparison.Ordinal)) {
        text = text[prefix.Length..];
        break;
      }
    }

    text = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    return SemVersion.TryParse(text, SemVersionStyles.Strict, out SemVersion? version)
        ? version
        : null;
  }
}
