namespace Lip.Core.Mcmod;

/// One package of the state file, as listed to the user. Variant, Source and Commit are what
/// the state file records of it (protocol/state.json.md): the variant of its source, its
/// source with a local path as a file: URI, and, for a remote repository, the commit its
/// archive was at.
public record InstalledPackage(string Identity, string Version, bool Explicit) {
  private readonly string _source = "";

  public string Variant { get; init; } = "";

  public string Source {
    get => _source;
    init {
      _source = value;
      Origin = Ref.TryParse(value, allowFile: true, out Ref? source, out _) ? source : null;
    }
  }

  public string? Commit { get; init; }

  /// The recorded source, or null when what the state file records is no source.
  public Ref? Origin { get; private init; }

  /// Whether the package is the game (protocol/lip-setup.md, "Terms").
  public bool IsGame => BedrockServer.IsRepository(Location);

  /// Whether the package is the loader.
  public bool IsLoader => GameSetup.IsLoaderRepository(Location);

  /// The package argument (protocol/lip.md, "Package arguments") with which lip update
  /// updates the package as the interface means it. A remote repository without a ref or at
  /// a version is named without the ref, which moves the package to the highest version tag
  /// that fits the workspace (protocol/lip-update.md). Any other source is named as it is
  /// recorded: a package of a branch, a tag or a commit stays there, a URL is downloaded again
  /// and a local path is read again. The identity when the source cannot be read.
  public string UpdateArgument =>
      Origin switch {
        { Kind: RefKind.Spec, GitRef: null or { Kind: GitRefKind.Version } } source =>
            WithVariant(source),
        { } source => PackageArguments.Of(source),
        null => Identity,
      };

  /// The repository of the package as a package argument without a ref, to which '@' and a
  /// version, a branch, a tag or a commit can be added: the location of its source with the
  /// variant, or the identity when the source cannot be read. Null when the source is no
  /// remote repository: a URL and a local path have no other versions to ask for.
  public string? Repository =>
      Origin switch {
        null => Identity,
        { Kind: RefKind.Spec } source => WithVariant(source),
        _ => null,
      };

  /// What tells the source from the repository that the identity names: the local path, the
  /// URL, or the branch, tag or commit that the package is held at (branch:, tag: or rev: with
  /// a name). Null for a remote repository without a ref or at a version.
  public string? SourceNote =>
      Origin switch {
        { Kind: RefKind.File } source => source.LocalPath,
        { Kind: RefKind.Url } source => source.Target,
        { GitRef: { Kind: not GitRefKind.Version } gitRef } => gitRef.ToString(),
        _ => null,
      };

  /// The packages as the interface lists them: the mods that the user installed, then the
  /// dependency packages, then the game, then the loader; within each group by identity,
  /// ignoring case. The order does not depend on that of the state file, which an operation
  /// changes, so a package stays where it is while others come and go.
  public static IReadOnlyList<InstalledPackage> Listed(IEnumerable<InstalledPackage> packages) =>
      [
        .. packages
            .OrderBy(p => p.IsGame ? 2 : p.IsLoader ? 3 : p.Explicit ? 0 : 1)
            .ThenBy(p => p.Identity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Identity, StringComparer.Ordinal),
      ];

  private string Location => Identity.Split('#')[0];

  /// The location of a remote repository source with its variant, without its ref.
  private static string WithVariant(Ref source) =>
      source.Target + (source.Variant == "" ? "" : $"#{source.Variant}");
}
