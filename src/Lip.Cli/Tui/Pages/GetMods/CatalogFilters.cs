using Lip.Core.Catalog;

namespace Lip.Cli.Tui.Pages.GetMods;

/// The sections of the filters, in the order they are shown.
public enum FilterSection {
  GameVersion,
  Tags,
  License,
  Dependency,
}

/// Whether mods are filtered by a value: not at all, to those that have it, or to those that do
/// not.
public enum FilterState {
  Any,
  Included,
  Excluded,
}

/// A value that mods are filtered by: a game version, a tag, a license family or a dependency,
/// with the number of mods for a server that have it.
public sealed record FilterValue(FilterSection Section, string Key, string Label, int Count);

/// The filters of the Get mods page: what the mods of the catalogue can be filtered by, and
/// what the user chose of it. The catalogue is filtered to the mods that have a build for a
/// server, the only side lip manages (protocol/lip.md, "Environment").
public sealed class CatalogFilters {
  private const string Side = "server";

  private readonly Dictionary<FilterSection, HashSet<string>> _included = [];
  private readonly Dictionary<FilterSection, HashSet<string>> _excluded = [];

  public CatalogFilters() {
    foreach (FilterSection section in Enum.GetValues<FilterSection>()) {
      _included[section] = new HashSet<string>(StringComparer.Ordinal);
      _excluded[section] = new HashSet<string>(StringComparer.Ordinal);
    }
  }

  /// The values, section by section.
  public IReadOnlyList<FilterValue> Values { get; private set; } = [];

  /// The sections whose values the filter panel shows; it shows the heading only of the others.
  public HashSet<FilterSection> Expanded { get; } = [];

  /// How many times the values were read from a catalogue.
  public int Revision { get; private set; }

  /// How many values are chosen.
  public int Count => _included.Values.Sum(v => v.Count) + _excluded.Values.Sum(v => v.Count);

  /// How many values of a section are chosen.
  public int CountOf(FilterSection section) =>
      _included[section].Count + _excluded[section].Count;

  /// Whether mods can be filtered to those that do not have a value of the section.
  public static bool CanExclude(FilterSection section) =>
      section is FilterSection.Tags or FilterSection.Dependency;

  public FilterState StateOf(FilterValue value) =>
      _included[value.Section].Contains(value.Key) ? FilterState.Included
          : _excluded[value.Section].Contains(value.Key) ? FilterState.Excluded
          : FilterState.Any;

  /// The next state of a value: any, included, excluded where the section can be, and round.
  public void Cycle(FilterValue value) {
    switch (StateOf(value)) {
      case FilterState.Any:
        _included[value.Section].Add(value.Key);
        break;
      case FilterState.Included:
        _included[value.Section].Remove(value.Key);
        if (CanExclude(value.Section)) {
          _excluded[value.Section].Add(value.Key);
        }

        break;
      default:
        _excluded[value.Section].Remove(value.Key);
        break;
    }
  }

  /// Chooses nothing.
  public void Clear() {
    foreach (FilterSection section in Enum.GetValues<FilterSection>()) {
      _included[section].Clear();
      _excluded[section].Clear();
    }
  }

  /// What CatalogQuery.Select filters by.
  public CatalogFilter ToFilter() =>
      new(
          Side, _included[FilterSection.Tags], _excluded[FilterSection.Tags],
          _included[FilterSection.GameVersion]) {
        Licenses = _included[FilterSection.License],
        IncludeDependencies = _included[FilterSection.Dependency],
        ExcludeDependencies = _excluded[FilterSection.Dependency],
      };

  /// Reads the values from the catalogue: the game versions, newest first; the featured tags in
  /// the order of levimc.org/mods, then the other tags, the most used first; the license
  /// families; the dependencies, the most used first. Only what a mod for a server has. What
  /// was chosen and is no longer there is forgotten.
  public void Build(CatalogIndex index) {
    Dictionary<string, int> versions = new(StringComparer.Ordinal);
    Dictionary<string, int> tags = new(StringComparer.Ordinal);
    Dictionary<string, int> licenses = new(StringComparer.Ordinal);
    Dictionary<string, int> dependencies = new(StringComparer.Ordinal);
    Dictionary<string, string> dependencyNames = new(StringComparer.Ordinal);
    foreach (CatalogMod mod in index.Mods.Where(mod => mod.RunsOn(Side))) {
      foreach (string version in mod.GameVersions.Distinct(StringComparer.Ordinal)) {
        versions[version] = versions.GetValueOrDefault(version) + 1;
      }

      foreach (string tag in CatalogQuery.DisplayTags(mod)) {
        tags[tag] = tags.GetValueOrDefault(tag) + 1;
      }

      string family = CatalogQuery.LicenseFamily(mod);
      licenses[family] = licenses.GetValueOrDefault(family) + 1;
      HashSet<string> seen = new(StringComparer.Ordinal);
      foreach (string id in mod.Dependencies.Keys) {
        string dependency = CatalogQuery.DependencyKey(id);
        if (seen.Add(dependency)) {
          dependencies[dependency] = dependencies.GetValueOrDefault(dependency) + 1;
          dependencyNames.TryAdd(dependency, Cells.BareName(id));
        }
      }
    }

    Values = [
      .. versions
          .OrderByDescending(v => VersionKey(v.Key), StringComparer.Ordinal)
          .Select(v => new FilterValue(FilterSection.GameVersion, v.Key, v.Key, v.Value)),
      .. CatalogQuery.FeaturedTags
          .Where(tag => tags.ContainsKey(tag.Id))
          .Select(tag => new FilterValue(FilterSection.Tags, tag.Id, tag.Label, tags[tag.Id])),
      .. tags
          .Where(tag => !CatalogQuery.IsFeatured(tag.Key))
          .OrderByDescending(tag => tag.Value).ThenBy(tag => tag.Key, StringComparer.Ordinal)
          .Select(tag => new FilterValue(FilterSection.Tags, tag.Key, tag.Key, tag.Value)),
      .. CatalogQuery.LicenseFamilies
          .Where(family => licenses.ContainsKey(family.Id))
          .Select(family => new FilterValue(
              FilterSection.License, family.Id, family.Label, licenses[family.Id])),
      .. dependencies
          .OrderByDescending(d => d.Value).ThenBy(d => d.Key, StringComparer.Ordinal)
          .Select(d => new FilterValue(
              FilterSection.Dependency, d.Key, dependencyNames[d.Key], d.Value)),
    ];
    Revision++;
    foreach (FilterSection section in Enum.GetValues<FilterSection>()) {
      HashSet<string> keys = [
        .. Values.Where(value => value.Section == section).Select(value => value.Key),
      ];
      _included[section].IntersectWith(keys);
      _excluded[section].IntersectWith(keys);
    }
  }

  /// A version so that versions compare as their numbers do: each number padded.
  private static string VersionKey(string version) =>
      string.Join('.', version.Split('.').Select(part => part.PadLeft(6, '0')));
}
