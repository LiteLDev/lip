using Lip.Cli.Tui.Widgets;
using Lip.Core.Catalog;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Editing;
using Lip.Core.PublicApi;
using Semver;

namespace Lip.Cli.Tui.Pages.Tools;

/// The checks of the dependencies online, and adding and changing them.
public sealed partial class ManifestEditor {
  /// How long a dependency rests after a change before it is checked online, in milliseconds.
  private const long Debounce = 500;

  /// The check of a dependency online: when it is due, whether it runs, and what it found;
  /// Generation counts the changes, so that only the check of the last one counts.
  private sealed class DependencyCheck {
    public long Due { get; set; }
    public bool Running { get; set; }
    public RemoteCheck? Result { get; set; }
    public int Generation { get; set; }
  }

  private readonly Dictionary<DependencyDraft, DependencyCheck> _checks =
      new(ReferenceEqualityComparer.Instance);

  // What was found out about each package, by its id in lower case: one lookup each, which
  // the checks, the dialogs and the prediction share. One that failed is made again.
  private readonly Dictionary<string, Task<PackageLookup>> _lookups = new(StringComparer.Ordinal);

  // Abandons what is read online once the editor is closed.
  private readonly CancellationTokenSource _cancel = new();

  /// Every dependency of the draft.
  private IEnumerable<DependencyDraft> Dependencies() =>
      _draft.Variants.SelectMany(v => v.Entries).SelectMany(e => e.Dependencies);

  /// A dependency changed: it is checked online once it rested, or at once.
  private void Schedule(DependencyDraft dependency, bool now = false) {
    if (!_checks.TryGetValue(dependency, out DependencyCheck? check)) {
      check = new DependencyCheck();
      _checks[dependency] = check;
    }

    check.Generation++;
    check.Result = null;
    check.Due = Environment.TickCount64 + (now ? 0 : Debounce);
    Changed();
  }

  /// Forgets the checks of the dependencies that the draft does not have any more.
  private void Forget() {
    HashSet<DependencyDraft> kept = new(Dependencies(), ReferenceEqualityComparer.Instance);
    foreach (DependencyDraft gone in _checks.Keys.Where(d => !kept.Contains(d)).ToList()) {
      _checks.Remove(gone);
    }
  }

  /// Starts the checks that are due.
  public void OnTick() {
    long now = Environment.TickCount64;
    foreach (DependencyDraft dependency in Dependencies()) {
      if (_checks.TryGetValue(dependency, out DependencyCheck? check) && check.Result is null
          && !check.Running && check.Due <= now
          && PackageDraftChecks.DependencyKey(dependency, []) is null) {
        Start(dependency, check);
      }
    }
  }

  private void Start(DependencyDraft dependency, DependencyCheck check) {
    check.Running = true;
    int generation = check.Generation;
    Task<PackageLookup> lookup = Lookup(dependency.Id);
    _context.Background(
        () => lookup,
        found => {
          check.Running = false;
          // A check of an older value: the next tick checks the value as it is now.
          if (generation == check.Generation) {
            check.Result = DependencyChecks.Check(dependency, found);
          }
        },
        ex => {
          check.Running = false;
          if (generation == check.Generation) {
            check.Result = new RemoteCheck(RemoteStatus.Unknown, RemoteReason.CannotCheck, ex.Message);
          }
        });
  }

  /// The status of a dependency: what is wrong with its key or its range, otherwise what the
  /// check online found, or that it is being checked.
  private Status DependencyStatus(DependencyDraft dependency, EntryDraft entry) {
    if (PackageDraftChecks.DependencyKey(dependency, entry.Dependencies) is { } key) {
      return Local(key);
    }

    if (PackageDraftChecks.Range(dependency.Range) is { } range) {
      return Local(range);
    }

    if (!_checks.TryGetValue(dependency, out DependencyCheck? check) || check.Result is null) {
      return new Status(Mark.Checking, null, Remote: true);
    }

    RemoteCheck result = check.Result;
    Mark mark = result.Status switch {
      RemoteStatus.Valid => Mark.Valid,
      RemoteStatus.Invalid => Mark.Invalid,
      _ => Mark.Unknown,
    };
    return new Status(mark, ManifestEditorText.Remote(result), Remote: true);
  }

  /// What is found out about a package online; for any thread but the first call of each
  /// package, which is on the thread of the interface.
  internal Task<PackageLookup> Lookup(string id) {
    string key = PackageNames.Fold(id);
    lock (_lookups) {
      if (_lookups.TryGetValue(key, out Task<PackageLookup>? known)
          && (!known.IsCompleted || known is { IsCompletedSuccessfully: true, Result.Error: null })) {
        return known;
      }

      LipClient client = _context.Client;
      ModCatalogClient catalog = _context.Catalog;
      IServerSetup setup = _context.ServerSetup;
      CancellationToken token = _cancel.Token;
      Task<PackageLookup> lookup = Task.Run(() => ReadLookup(id, client, catalog, setup, token));
      _lookups[key] = lookup;
      return lookup;
    }
  }

  /// What was found out about a package so far, or null.
  private PackageLookup? Known(string id) {
    lock (_lookups) {
      return _lookups.TryGetValue(PackageNames.Fold(id), out Task<PackageLookup>? known)
          && known.IsCompletedSuccessfully
              ? known.Result
              : null;
    }
  }

  /// Finds out about a package: the server by the builds list, a package of the catalogue by
  /// its file there, any other by the version tags of its repository. Never fails but by
  /// cancellation: what could not be found out is said in the result.
  private static async Task<PackageLookup> ReadLookup(
      string id, LipClient client, ModCatalogClient catalog, IServerSetup setup,
      CancellationToken token) {
    try {
      if (BedrockServer.IsRepository(id)) {
        return PackageLookup.Of(
            (await setup.GameVersions(client).WaitAsync(token)).Select(g => g.Version));
      }

      CatalogIndex? index = null;
      try {
        index = await catalog.GetIndex(token);
      }
      catch (CatalogUnavailableException) {
        // The repository tells what the catalogue cannot.
      }

      CatalogMod? mod = index?.Find(id);
      if (mod is not null || GameSetup.IsLoaderRepository(id)) {
        try {
          CatalogDetail detail = await catalog.GetDetail(mod?.Id ?? id, token);
          if (mod is not null || detail.Versions.Count > 0) {
            return PackageLookup.Of(detail);
          }
        }
        catch (CatalogUnavailableException ex) when (mod is not null) {
          return PackageLookup.Failed(ex.Message);
        }
        catch (CatalogUnavailableException) {
          // LeviLamina is not in the catalogue: its tags tell.
        }
      }

      return PackageLookup.Of(await client.PackageVersions(id).WaitAsync(token));
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) {
      throw;
    }
    catch (Exception ex) {
      return PackageLookup.Failed(ex.Message);
    }
  }

  #region Adding and changing

  private void AddDependency(EntryDraft entry) =>
      _context.ShowMenu(
          ManifestEditorText.Dependencies,
          new MenuItem(ManifestEditorText.FromMarketplace, () => _context.Show(
              new MarketplaceDialog(_context.Catalog, mod => Choose(entry, mod.Id, null)))),
          new MenuItem(ManifestEditorText.TypeId, () => TypeId(entry, null)));

  private void DependencyMenu(EntryDraft entry, DependencyDraft dependency) =>
      _context.ShowMenu(
          dependency.Package,
          new MenuItem(
              ManifestEditorText.ChooseVersion, () => Choose(entry, dependency.Package, dependency)),
          new MenuItem(ManifestEditorText.ChooseRange, () => ChooseRange(
              entry, dependency.Package, RangePresets.Start(dependency.Range),
              Known(dependency.Id), dependency)),
          new MenuItem(ManifestEditorText.ChangeId, () => TypeId(entry, dependency)),
          new MenuItem(ManifestEditorText.Remove, () => {
            entry.Dependencies.Remove(dependency);
            Forget();
            Build(EntryKey(entry) + ".deps");
          }));

  /// Asks for the key of a dependency: a new one, or another for one that is there.
  private void TypeId(EntryDraft entry, DependencyDraft? dependency) =>
      _context.Show(new PromptDialog(
          ManifestEditorText.PackageIdTitle,
          [new PromptField(
              ManifestEditorText.PackageIdLabel, dependency?.Package ?? "", "github.com/owner/repo")],
          values => {
            if (dependency is null) {
              Choose(entry, values[0], null);
            } else {
              dependency.Package = values[0];
              Schedule(dependency, now: true);
              Build();
            }
          },
          validate: values => {
            DependencyDraft typed = new() { Package = values[0], Range = "*" };
            IEnumerable<DependencyDraft> others = entry.Dependencies.Where(d => d != dependency);
            return PackageDraftChecks.DependencyKey(typed, [.. others, typed]) is { } problem
                ? ManifestEditorText.Problem(problem)
                : null;
          }));

  /// Finds out about the package of a dependency, then asks for its version and its range.
  private void Choose(EntryDraft entry, string key, DependencyDraft? dependency) {
    string id = key.Split('#')[0];
    Task<PackageLookup> lookup = Lookup(id);
    _context.RunOperation(
        ManifestEditorText.Looking(id),
        token => lookup.WaitAsync(token),
        found => _context.Show(new DependencyVersionDialog(
            key, found, dependency is null ? null : RangePresets.Start(dependency.Range),
            (chosen, version) => ChooseRange(entry, chosen, version, found, dependency))),
        cancellable: true);
  }

  /// Asks for the range of a dependency, with the version chosen, when there is one.
  private void ChooseRange(
      EntryDraft entry, string key, SemVersion? version, PackageLookup? lookup,
      DependencyDraft? dependency) =>
      _context.Show(new RangeDialog(
          key, version, dependency?.Range,
          lookup?.VersionsOf(DependencyChecks.Variant(key)),
          range => Apply(entry, key, range, dependency)));

  /// Sets the key and the range of a dependency, or adds it; a dependency of the entry on the
  /// same package gets the range instead.
  private void Apply(EntryDraft entry, string key, string range, DependencyDraft? dependency) {
    dependency ??= entry.Dependencies.FirstOrDefault(d => PackageNames.AreEqual(d.Package, key));
    if (dependency is null) {
      dependency = new DependencyDraft();
      entry.Dependencies.Add(dependency);
    }

    dependency.Package = key;
    dependency.Range = range;
    Schedule(dependency, now: true);
    Build($"{EntryKey(entry)}.d{entry.Dependencies.IndexOf(dependency)}");
  }

  #endregion
}
