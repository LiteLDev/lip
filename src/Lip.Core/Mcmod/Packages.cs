using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod;

/// A package with its selected entry (protocol/package.md §4): what a candidate installs. Variant
/// is the variant of the source it was resolved for (the variant it names, for a dependency of
/// format 3 whose "client" names "" of a package of format 4).
public record Package(string Tooth, string Variant, SemVersion Version, ToothEntry Entry) {
  /// The identity (protocol/package.md §4): the tooth path, followed by '#' and the variant when
  /// that is not "". Compared ignoring ASCII case.
  public string Identity => GetIdentity(Tooth, Variant);

  /// Whether the package is a mod (protocol/package.md §4). Its mod name is in the mod manifest
  /// of its asset of type "mcmod" (Candidate.GetModManifest).
  public bool IsMod => Entry.IsMod;

  public static string GetIdentity(string tooth, string variant) =>
      variant == "" ? tooth : $"{tooth}#{variant}";
}

public static partial class PackageNames {
  public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

  public static bool AreEqual(string a, string b) => Fold(a) == Fold(b);

  /// Lowercases ASCII letters only: tooth paths, identities and mod names are compared ASCII
  /// case-insensitively (protocol/package.md §1).
  public static string Fold(string name) => string.Create(name.Length, name, (span, s) => {
    for (int i = 0; i < s.Length; i++) {
      span[i] = char.IsAsciiLetterUpper(s[i]) ? (char)(s[i] | 0x20) : s[i];
    }
  });

  /// Whether a name is an id: ^[A-Za-z0-9][A-Za-z0-9._-]*$ (protocol/package.md §1).
  public static bool IsValid(string name) => ValidName().IsMatch(name);

  [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
  private static partial Regex ValidName();
}
