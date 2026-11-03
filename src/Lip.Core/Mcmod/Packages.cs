using System.Text.Json;
using System.Text.RegularExpressions;
using Lip.Core.Entities;
using Semver;

namespace Lip.Core.Mcmod;

/// An edge of the dependency graph that lip installs (spec §6): a dependencies entry with a
/// source, whose key the identity of the package satisfying it must equal; or a tooth.json
/// dependency (spec §10.2 rule 3), without a name, which any identity satisfies.
/// ToothClientCompat marks the latter: a "client" variant is treated as "" when the target
/// archive uses manifest.json.
public record InstallEdge(
    string? Name,
    IVersionConstraint? Version,
    IReadOnlyList<Ref> Refs,
    bool ToothClientCompat = false);

/// A package, i.e. a resolution candidate's payload.
public interface IPackage {
  /// How the lock reads the recorded manifest: "mcmod" or "tooth" (McmodPackage.Kind).
  public string Kind { get; }

  /// The package id; compared ignoring ASCII case.
  public string Identity { get; }

  public SemVersion Version { get; }

  /// What selected this package within its archive besides the archive itself: "" for a
  /// manifest.json package (an archive holds one package document), the v3 label for a
  /// tooth.json, whose variants are different packages in one archive (spec §10.2).
  public string Variant => "";

  public IReadOnlyList<InstallEdge> InstallEdges { get; }

  public IReadOnlyList<Dep> Conflicts { get; }

  /// The root document as JSON, recorded in the lock so that uninstall needs no network.
  public JsonElement Manifest { get; }
}

/// A package document, from a manifest.json or read from a tooth.json (spec §10).
public record McmodPackage(PackageDocument Document, JsonElement Manifest) : IPackage {
  public const string KindName = "mcmod";

  /// "mcmod" when Manifest is the manifest.json; "tooth" (ToothCompat.KindName) when it is the
  /// tooth.json the document was converted from, which the lock records instead, since the
  /// conversion is not expressible as manifest.json.
  public string Kind { get; init; } = KindName;

  public string Identity => Document.Id;

  public SemVersion Version => Document.Version;

  /// The v3 label a tooth.json was converted with (IPackage.Variant); "" otherwise.
  public string Variant { get; init; } = "";

  public IReadOnlyList<InstallEdge> InstallEdges => [
    .. Document.Dependencies
        .Where(d => d.Refs is not null)
        .Select(d => new InstallEdge(d.Id, d.Version, d.Refs!)),
    .. Document.InstallDependencies
        .Select(d => new InstallEdge(null, d.Version, d.Refs, d.ToothClientCompat)),
  ];

  public IReadOnlyList<Dep> Conflicts => Document.Conflicts;
}

/// The root document of an archive (spec §2.4).
public abstract record ArchiveDocument;

public record MetaArchiveDocument(MetaDocument Document) : ArchiveDocument;

public record PackageArchiveDocument(PackageDocument Document, JsonElement Manifest)
    : ArchiveDocument;

/// A tooth.json root; Manifest is the v3 form after migration of v1/v2.
public record ToothArchiveDocument(PackageManifest Manifest, JsonElement Json) : ArchiveDocument;

public static partial class PackageNames {
  public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

  public static bool AreEqual(string a, string b) => Fold(a) == Fold(b);

  /// Lowercases ASCII letters only: identities are compared ignoring ASCII case (spec §1).
  public static string Fold(string name) => string.Create(name.Length, name, (span, s) => {
    for (int i = 0; i < s.Length; i++) {
      span[i] = char.IsAsciiLetterUpper(s[i]) ? (char)(s[i] | 0x20) : s[i];
    }
  });

  /// A valid id (^[A-Za-z0-9][A-Za-z0-9._-]*$, spec §1) derived from free text:
  /// other characters become '-', leading '.', '_' and '-' are dropped.
  public static string FromText(string text, string fallback) {
    string name = ReplaceInvalidCharacters(text).TrimStart('.', '_', '-');
    return name == "" ? fallback : name;
  }

  /// Replaces every character other than ASCII letters, digits, '.', '_' and '-' with '-'.
  public static string ReplaceInvalidCharacters(string text) =>
      InvalidNameCharacters().Replace(text, "-");

  /// Whether a name matches ^[A-Za-z0-9][A-Za-z0-9._-]*$ (spec §1).
  public static bool IsValid(string name) => ValidName().IsMatch(name);

  [GeneratedRegex("[^A-Za-z0-9._-]")]
  private static partial Regex InvalidNameCharacters();

  [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
  private static partial Regex ValidName();
}
