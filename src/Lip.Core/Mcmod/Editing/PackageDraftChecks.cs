using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Semver;

namespace Lip.Core.Mcmod.Editing;

/// What is wrong with a value of a draft (protocol/lip.toml.md); the interface says it in its
/// language.
public enum DraftProblemKind {
  /// The value is required and empty.
  Required,

  /// The package id is not a Go module path; Detail says why.
  NotModulePath,

  /// The version is not a SemVer version.
  NotVersion,

  /// The version starts with "v", which a version does not.
  LeadingV,

  /// The logo does not start with "https://".
  NotHttps,

  /// The name of a variant is not "" and does not match ^[a-z0-9_]+(/[a-z0-9_]+)?$.
  NotVariant,

  /// Another variant has the same name.
  DuplicateVariant,

  /// The side is not "server" or "client".
  NotSide,

  /// The platform is not a runtime identifier.
  NotPlatform,

  /// The platform is "server" or "client": the keys of lip before v0.40.
  OldPlatform,

  /// Another entry of the variant has the same side and platform.
  DuplicateEnvironment,

  /// The key of a dependency is not "<package id>" or "<package id>#<variant>"; Detail says
  /// why when it can.
  NotDependencyKey,

  /// Another dependency of the entry names the same package.
  DuplicateDependency,

  /// The range is not a range; Detail is Semver's reason.
  NotRange,

  /// The type of an asset is not "self", "zip" or "mcmod".
  NotAssetType,

  /// An asset of type "zip" or "mcmod" has no URL.
  UrlsRequired,

  /// A URL does not start with "https://".
  NotHttpsUrl,

  /// An asset of type "mcmod" has place.
  PlaceNotAllowed,

  /// The entry has another asset of type "mcmod".
  SecondMcmod,

  /// A src or a dest is not a path; Detail is the rule it breaks.
  NotPath,

  /// A glob is not valid; Detail says why.
  NotGlob,

  /// The dest must be "" or end with "/", for a glob or a directory src.
  DestMustBeDirectory,

  /// The dest must name a file, for a file src.
  DestMustBeFile,

  /// The variant has no entry.
  NoEntries,

  /// The package file has no variant.
  NoVariants,
}

/// A problem of a value: its kind, and what the interface adds to it, such as the reason of a
/// library, in English, or the value it names.
public sealed record DraftProblem(DraftProblemKind Kind, string? Detail = null);

/// The checks of the values of a draft that need no network, each as protocol/lip.toml.md and
/// lip.schema.toml state it: null when the value is valid. A value that holds a template
/// ({{ … }}) is checked once it is rendered, by Document.
public static partial class PackageDraftChecks {
  /// Whether a text holds a template, which only the rendered package file can be checked for.
  public static bool IsTemplate(string text) => text.Contains("{{", StringComparison.Ordinal);

  public static DraftProblem? Id(string id) {
    if (id.Length == 0) {
      return new DraftProblem(DraftProblemKind.Required);
    }

    if (IsTemplate(id)) {
      return null;
    }

    return Golang.Org.X.Mod.Module.CheckPath(id) is Exception ex
        ? new DraftProblem(DraftProblemKind.NotModulePath, (ex.InnerException ?? ex).Message)
        : null;
  }

  public static DraftProblem? Version(string version) {
    if (version.Length == 0) {
      return new DraftProblem(DraftProblemKind.Required);
    }

    if (IsTemplate(version)) {
      return null;
    }

    if (SemVersion.TryParse(version, SemVersionStyles.Strict, out _)) {
      return null;
    }

    return version.StartsWith('v') && SemVersion.TryParse(version[1..], SemVersionStyles.Strict, out _)
        ? new DraftProblem(DraftProblemKind.LeadingV, version[1..])
        : new DraftProblem(DraftProblemKind.NotVersion);
  }

  public static DraftProblem? Logo(string logo) =>
      logo.Length == 0 || IsTemplate(logo) || logo.StartsWith("https://", StringComparison.Ordinal)
          ? null
          : new DraftProblem(DraftProblemKind.NotHttps);

  /// The name of a variant, among the names of every variant of the draft.
  public static DraftProblem? VariantName(string name, IEnumerable<string> all) {
    if (name.Length > 0 && !Ref.IsValidVariant(name)) {
      return new DraftProblem(DraftProblemKind.NotVariant);
    }

    return all.Count(other => other == name) > 1
        ? new DraftProblem(DraftProblemKind.DuplicateVariant)
        : null;
  }

  public static DraftProblem? Side(string side) =>
      IsTemplate(side) || McmodSideExtensions.TryParse(side, out _)
          ? null
          : new DraftProblem(DraftProblemKind.NotSide);

  /// The platform of an entry, among the entries of its variant.
  public static DraftProblem? Platform(EntryDraft entry, IEnumerable<EntryDraft> variant) {
    if (entry.Platform.Length == 0) {
      return new DraftProblem(DraftProblemKind.Required);
    }

    if (IsTemplate(entry.Platform)) {
      return null;
    }

    if (McmodSideExtensions.TryParse(entry.Platform, out _)) {
      return new DraftProblem(DraftProblemKind.OldPlatform);
    }

    if (!PackageFileParser.IsPlatform(entry.Platform)) {
      return new DraftProblem(DraftProblemKind.NotPlatform);
    }

    return variant.Count(other => other.Side == entry.Side && other.Platform == entry.Platform) > 1
        ? new DraftProblem(DraftProblemKind.DuplicateEnvironment)
        : null;
  }

  /// The key of a dependency, among the dependencies of its entry.
  public static DraftProblem? DependencyKey(DependencyDraft dependency, IEnumerable<DependencyDraft> entry) {
    if (dependency.Package.Length == 0) {
      return new DraftProblem(DraftProblemKind.Required);
    }

    if (!Ref.TryParse(dependency.Package, out Ref? source, out string? error)
        || source.Kind != RefKind.Spec || source.GitRef is not null) {
      return new DraftProblem(DraftProblemKind.NotDependencyKey, error);
    }

    // Keys are compared as package ids are: ignoring ASCII case.
    return entry.Count(other => PackageNames.AreEqual(other.Package, dependency.Package)) > 1
        ? new DraftProblem(DraftProblemKind.DuplicateDependency)
        : null;
  }

  public static DraftProblem? Range(string range) {
    if (range.Length == 0) {
      return new DraftProblem(DraftProblemKind.Required);
    }

    if (IsTemplate(range)) {
      return null;
    }

    try {
      _ = VersionRange.Parse(range);
      return null;
    }
    catch (FormatException ex) {
      return new DraftProblem(DraftProblemKind.NotRange, ex.InnerException?.Message);
    }
  }

  public static DraftProblem? AssetType(string type) =>
      AssetDraft.Types.Contains(type) || IsTemplate(type)
          ? null
          : new DraftProblem(DraftProblemKind.NotAssetType);

  /// The URLs of an asset as a whole: whether it has one when it needs one.
  public static DraftProblem? Urls(AssetDraft asset) =>
      asset.Type is "zip" or "mcmod" && !asset.Urls.Any(url => url.Length > 0)
          ? new DraftProblem(DraftProblemKind.UrlsRequired)
          : null;

  public static DraftProblem? Url(string url) =>
      url.Length == 0 || IsTemplate(url) || url.StartsWith("https://", StringComparison.Ordinal)
          ? null
          : new DraftProblem(DraftProblemKind.NotHttpsUrl);

  /// The type of an asset among the assets of its entry: at most one is "mcmod".
  public static DraftProblem? Mcmod(AssetDraft asset, IEnumerable<AssetDraft> entry) =>
      asset.Type == "mcmod" && entry.TakeWhile(other => other != asset).Any(o => o.Type == "mcmod")
          ? new DraftProblem(DraftProblemKind.SecondMcmod)
          : null;

  /// Place in an asset of its type.
  public static DraftProblem? PlaceAllowed(AssetDraft asset) =>
      asset.Type == "mcmod" && asset.Place.Count > 0
          ? new DraftProblem(DraftProblemKind.PlaceNotAllowed)
          : null;

  /// The src of a member of place (protocol/lip.toml.md, "Places").
  public static DraftProblem? Src(PlaceDraft place) {
    if (IsTemplate(place.Src)) {
      return null;
    }

    if (PathRule(place.Src) is { } rule) {
      return new DraftProblem(DraftProblemKind.NotPath, rule);
    }

    if (PathGlob.HasMetacharacters(place.Src)) {
      try {
        _ = new PathGlob(place.Src);
      }
      catch (FormatException ex) {
        return new DraftProblem(DraftProblemKind.NotGlob, ex.Message);
      }
    }

    return null;
  }

  /// The dest of a member of place, which depends on the form of its src.
  public static DraftProblem? Dest(PlaceDraft place) {
    if (IsTemplate(place.Dest) || IsTemplate(place.Src)) {
      return null;
    }

    if (PathRule(place.Dest) is { } rule) {
      return new DraftProblem(DraftProblemKind.NotPath, rule);
    }

    bool directory = place.Dest.Length == 0 || place.Dest.EndsWith('/');
    bool directorySrc = PathGlob.HasMetacharacters(place.Src)
        || place.Src.Length == 0 || place.Src.EndsWith('/');
    return directorySrc && !directory ? new DraftProblem(DraftProblemKind.DestMustBeDirectory)
        : !directorySrc && directory ? new DraftProblem(DraftProblemKind.DestMustBeFile)
        : null;
  }

  /// A glob of preserve or purge (protocol/lip.toml.md, "Globs"). An empty one is left out.
  public static DraftProblem? Glob(string glob) {
    if (glob.Length == 0 || IsTemplate(glob)) {
      return null;
    }

    try {
      _ = new PathGlob(glob);
      return null;
    }
    catch (FormatException ex) {
      return new DraftProblem(DraftProblemKind.NotGlob, ex.Message);
    }
  }

  /// The rule of "Places" that a src or a dest breaks, or null.
  private static string? PathRule(string path) => path switch {
    _ when path.StartsWith('/') => "/…",
    _ when DriveLetterRegex().IsMatch(path) => "C:",
    _ when path.Contains("//", StringComparison.Ordinal) => "//",
    _ when path.Contains('\\') => "\\",
    _ when path.Split('/').Contains("..") => "..",
    _ => null,
  };

  /// Every problem of a draft that the checks above find, for a draft that is to be saved.
  public static bool HasProblems(PackageDraft draft) {
    if (Id(draft.Id) is not null || Version(draft.Version) is not null
        || Logo(draft.Logo) is not null) {
      return true;
    }

    if (draft.Variants.Count == 0) {
      return true;
    }

    List<string> names = [.. draft.Variants.Select(v => v.Name)];
    foreach (VariantDraft variant in draft.Variants) {
      if (VariantName(variant.Name, names) is not null || variant.Entries.Count == 0) {
        return true;
      }

      foreach (EntryDraft entry in variant.Entries) {
        if (Side(entry.Side) is not null || Platform(entry, variant.Entries) is not null
            || entry.Dependencies.Any(d =>
                DependencyKey(d, entry.Dependencies) is not null || Range(d.Range) is not null)
            || entry.Preserve.Concat(entry.Purge).Any(g => Glob(g) is not null)) {
          return true;
        }

        foreach (AssetDraft asset in entry.Assets) {
          if (AssetType(asset.Type) is not null || Urls(asset) is not null
              || asset.Urls.Any(u => Url(u) is not null) || Mcmod(asset, entry.Assets) is not null
              || PlaceAllowed(asset) is not null
              || asset.Place.Any(p => Src(p) is not null || Dest(p) is not null)) {
            return true;
          }
        }
      }
    }

    return false;
  }

  /// Why the package file as written is not valid, with its templates rendered
  /// (PackageFileParser), as lip says it; null when it is valid.
  public static string? Document(JsonObject written) {
    try {
      using JsonDocument json = JsonDocument.Parse(written.ToJsonString());
      using JsonDocument rendered = PackageTemplates.Render(json.RootElement);
      _ = PackageFileParser.Parse(rendered.RootElement);
      return null;
    }
    catch (PackageFormatException ex) {
      return ex.InnerException is { } inner && ex.Message.EndsWith(':')
          ? $"{ex.Message} {inner.Message}"
          : ex.Message;
    }
  }

  [GeneratedRegex(@"^[A-Za-z]:")]
  private static partial Regex DriveLetterRegex();
}
