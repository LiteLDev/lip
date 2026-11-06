using System.Text.Json;
using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod;

/// A package with its selected entry (protocol/package.md §4): what a candidate installs. Variant
/// is the variant of the source it was resolved for (the variant it names, for a dependency of a
/// tooth.json whose "client" names "" of a package that has a package file).
public record Package(string Id, string Variant, SemVersion Version, PackageEntry Entry) {
  /// The package file of the package (for a package read from a tooth.json, the one
  /// protocol/compatibility.md §2 derives from it for the environment), which the workspace file
  /// records.
  public required JsonElement Manifest { get; init; }

  /// The identity (protocol/package.md §4): the package id, followed by '#' and the variant when
  /// that is not "". Compared ignoring ASCII case.
  public string Identity => GetIdentity(Id, Variant);

  public static string GetIdentity(string id, string variant) =>
      variant == "" ? id : $"{id}#{variant}";
}

public static partial class PackageNames {
  public static bool AreEqual(string a, string b) => Fold(a) == Fold(b);

  /// Lowercases ASCII letters only: package ids, identities and mod names are compared ASCII
  /// case-insensitively (protocol/package.md §1).
  public static string Fold(string name) => string.Create(name.Length, name, (span, s) => {
    for (int i = 0; i < s.Length; i++) {
      span[i] = char.IsAsciiLetterUpper(s[i]) ? (char)(s[i] | 0x20) : s[i];
    }
  });

  /// Whether a name is a mod name: ^[A-Za-z0-9][A-Za-z0-9._-]*$ (protocol/package.md §4).
  public static bool IsValid(string name) => ValidName().IsMatch(name);

  [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
  private static partial Regex ValidName();
}
