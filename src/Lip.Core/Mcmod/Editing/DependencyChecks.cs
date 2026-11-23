using Lip.Core.Catalog;
using Semver;

namespace Lip.Core.Mcmod.Editing;

/// What the editor found out about a package on the network: its file of the catalogue
/// (protocol/lip-tui.md, "Catalogue") when the catalogue lists it, otherwise the versions of
/// its version tags, or of the server; or why it could not find out.
public sealed record PackageLookup(
    CatalogDetail? Detail, IReadOnlyList<SemVersion> Versions, string? Error = null) {
  public static PackageLookup Of(CatalogDetail detail) => new(detail, []);

  public static PackageLookup Of(IEnumerable<SemVersion> versions) => new(null, [.. versions]);

  public static PackageLookup Failed(string error) => new(null, [], error);

  /// The versions of the package that have the variant, from highest to lowest.
  public IReadOnlyList<SemVersion> VersionsOf(string variant) {
    if (Detail is null) {
      return [.. Versions.OrderByDescending(v => v, SemVersion.PrecedenceComparer)];
    }

    return [
      .. Detail.Versions.Where(v => HasVariant(v, variant))
          .Select(v => Parse(v.Version)).OfType<SemVersion>()
          .OrderByDescending(v => v, SemVersion.PrecedenceComparer),
    ];
  }

  /// Whether a version of the catalogue has an entry of the variant: a version whose variants
  /// the file does not list has the default one only.
  internal static bool HasVariant(CatalogVersion version, string variant) =>
      version.Builds.Count == 0 ? variant.Length == 0
          : version.Builds.Any(build => build.Variant == variant);

  internal static SemVersion? Parse(string version) =>
      SemVersion.TryParse(version, SemVersionStyles.Strict, out SemVersion? parsed) ? parsed : null;
}

/// How a check that reads the network came out.
public enum RemoteStatus {
  Valid,
  Invalid,

  /// The check could not be made, such as when the network cannot be reached.
  Unknown,
}

/// What a check of a dependency found.
public enum RemoteReason {
  /// The highest version in the range is Detail.
  NewestInRange,

  /// No version is in the range; Detail is the highest version, when there is one.
  NoVersionInRange,

  /// The package has no version, or no repository: it cannot be found.
  NotFound,

  /// The package has no variant Detail.
  NoVariant,

  /// The check could not be made; Detail says why.
  CannotCheck,
}

public sealed record RemoteCheck(RemoteStatus Status, RemoteReason Reason, string? Detail = null);

/// The checks of a dependency that read the network: whether its package exists, has its
/// variant, and has a version in its range.
public static class DependencyChecks {
  public static RemoteCheck Check(DependencyDraft dependency, PackageLookup lookup) {
    if (lookup.Error is { } error) {
      return new RemoteCheck(RemoteStatus.Unknown, RemoteReason.CannotCheck, error);
    }

    string variant = Variant(dependency.Package);
    if (lookup.Detail is { } detail && variant.Length > 0
        && !detail.Versions.Any(v => PackageLookup.HasVariant(v, variant))) {
      return new RemoteCheck(RemoteStatus.Invalid, RemoteReason.NoVariant, variant);
    }

    IReadOnlyList<SemVersion> versions = lookup.VersionsOf(variant);
    if (versions.Count == 0) {
      return new RemoteCheck(RemoteStatus.Invalid, RemoteReason.NotFound);
    }

    if (!VersionRange.TryParse(dependency.Range, out VersionRange? range)) {
      // The range is wrong itself: the local check says so.
      return new RemoteCheck(RemoteStatus.Valid, RemoteReason.NewestInRange, versions[0].ToString());
    }

    SemVersion? newest = versions.FirstOrDefault(range!.IsSatisfiedBy);
    return newest is null
        ? new RemoteCheck(RemoteStatus.Invalid, RemoteReason.NoVersionInRange, versions[0].ToString())
        : new RemoteCheck(RemoteStatus.Valid, RemoteReason.NewestInRange, newest.ToString());
  }

  /// The variant of the key of a dependency: what follows its '#', or "".
  public static string Variant(string key) {
    int hash = key.IndexOf('#');
    return hash < 0 ? "" : key[(hash + 1)..];
  }
}

/// How the installation of a package is expected to go.
public enum PredictionOutcome {
  Installs,
  Fails,

  /// What the catalogue says is not enough to tell.
  Unknown,
}

/// Why the installation of a package is expected to go as it does.
public enum PredictionReason {
  /// The package has an entry for the server on win-x64 in the variant.
  HasEntry,

  /// The package has no entry for the server on win-x64 in the variant.
  NoEntry,

  /// The game version chosen is in the range Detail.
  GameInRange,

  /// The game version chosen is not in the range Detail.
  GameOutOfRange,

  /// The LeviLamina version chosen is in the range Detail.
  LoaderInRange,

  /// The LeviLamina version chosen is not in the range Detail.
  LoaderOutOfRange,

  /// No LeviLamina is chosen, and the package depends on it.
  NoLoader,

  /// The LeviLamina version chosen is not for the game version chosen.
  LoaderNotForGame,

  /// The version Detail would be installed.
  WouldInstall,

  /// No version is in the range.
  NoVersionInRange,

  /// No version in the range fits the game and the LeviLamina chosen; Detail is the highest
  /// version in the range.
  NoVersionFits,

  /// The version Detail is in the range, but the catalogue does not list the package, so
  /// whether it fits is not known.
  CompatibilityUnknown,

  /// What the package is could not be found out; Detail says why.
  CannotCheck,
}

/// What is expected of a package when it is installed: Package names it.
public sealed record Prediction(
    string Package, PredictionOutcome Outcome, PredictionReason Reason, string? Detail = null);

/// What is expected when the package of a draft is installed in a workspace with a game version
/// and a LeviLamina version, as the catalogue tells: for the package itself, and for each of
/// its dependencies. Only the dependencies of the package are looked at, not theirs; lip install
/// decides.
public static class InstallPrediction {
  /// The side and the platform that the interface installs for (protocol/lip.md,
  /// "Environment").
  public const string Side = "server";

  /// The entry of a variant of the draft that would be installed: that for the server on
  /// win-x64, or null.
  public static EntryDraft? Entry(PackageDraft draft, string variant) =>
      draft.Variants.FirstOrDefault(v => v.Name == variant)?.Entries.FirstOrDefault(
          e => e.Side == Side && e.Platform == CatalogCompatibility.Platform);

  /// The package of the draft itself, in the variant.
  public static Prediction Self(PackageDraft draft, string variant) {
    string name = variant.Length == 0 ? draft.Id : $"{draft.Id}#{variant}";
    return Entry(draft, variant) is null
        ? new Prediction(name, PredictionOutcome.Fails, PredictionReason.NoEntry)
        : new Prediction(name, PredictionOutcome.Installs, PredictionReason.HasEntry);
  }

  /// A dependency, with what was found out about its package, and about LeviLamina.
  public static Prediction Dependency(
      DependencyDraft dependency,
      SemVersion game,
      SemVersion? loader,
      PackageLookup lookup,
      PackageLookup? loaderLookup = null) {
    string name = dependency.Package;
    if (!VersionRange.TryParse(dependency.Range, out VersionRange? range)) {
      return new Prediction(name, PredictionOutcome.Fails, PredictionReason.NoVersionInRange);
    }

    if (BedrockServer.IsRepository(dependency.Id)) {
      return range!.IsSatisfiedBy(game)
          ? new Prediction(name, PredictionOutcome.Installs, PredictionReason.GameInRange, range.Text)
          : new Prediction(
              name, PredictionOutcome.Fails, PredictionReason.GameOutOfRange, range.Text);
    }

    if (GameSetup.IsLoaderRepository(dependency.Id)) {
      if (loader is null) {
        return new Prediction(name, PredictionOutcome.Fails, PredictionReason.NoLoader);
      }

      if (!range!.IsSatisfiedBy(loader)) {
        return new Prediction(
            name, PredictionOutcome.Fails, PredictionReason.LoaderOutOfRange, range.Text);
      }

      return LoaderFits(loader, game, loaderLookup ?? lookup) == false
          ? new Prediction(name, PredictionOutcome.Fails, PredictionReason.LoaderNotForGame)
          : new Prediction(
              name, PredictionOutcome.Installs, PredictionReason.LoaderInRange, range.Text);
    }

    if (lookup.Error is { } error) {
      return new Prediction(name, PredictionOutcome.Unknown, PredictionReason.CannotCheck, error);
    }

    string variant = DependencyChecks.Variant(dependency.Package);
    List<SemVersion> inRange = [.. lookup.VersionsOf(variant).Where(range!.IsSatisfiedBy)];
    if (inRange.Count == 0) {
      return new Prediction(name, PredictionOutcome.Fails, PredictionReason.NoVersionInRange);
    }

    if (lookup.Detail is not { } detail) {
      return new Prediction(
          name, PredictionOutcome.Unknown, PredictionReason.CompatibilityUnknown,
          inRange[0].ToString());
    }

    CatalogWorkspace workspace = new(game.ToString(), loader?.ToString());
    foreach (SemVersion version in inRange) {
      CatalogVersion? listed = detail.Versions.FirstOrDefault(
          v => PackageLookup.Parse(v.Version) is { } parsed
              && SemVersion.PrecedenceComparer.Compare(parsed, version) == 0);
      if (listed is not null && CatalogCompatibility.IsCompatible(listed, variant, workspace)) {
        return new Prediction(
            name, PredictionOutcome.Installs, PredictionReason.WouldInstall, version.ToString());
      }
    }

    return new Prediction(
        name, PredictionOutcome.Fails, PredictionReason.NoVersionFits, inRange[0].ToString());
  }

  /// Whether a version of LeviLamina is for a game version, as its file of the catalogue tells;
  /// null when the file does not list it.
  private static bool? LoaderFits(SemVersion loader, SemVersion game, PackageLookup lookup) {
    CatalogVersion? listed = lookup.Detail?.Versions.FirstOrDefault(
        v => PackageLookup.Parse(v.Version) is { } parsed
            && SemVersion.PrecedenceComparer.Compare(parsed, loader) == 0);
    return listed is null
        ? null
        : CatalogCompatibility.IsCompatible(listed, "", new CatalogWorkspace(game.ToString(), null));
  }
}
