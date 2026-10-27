using Semver;

namespace Lip.Core.Mcmod.Resolution;

/// Finds the package set S of spec §6 and returns it in the install order of spec §7.
///
/// The search is a depth-first backtracking search over the open requirements (the requests,
/// then the install edges of each member in the order members are added). For each open
/// requirement the candidates are tried in their sequence order, so earlier candidates win
/// (spec §6). To avoid exponential blowup it uses conflict-directed backjumping and learns
/// candidates that can never be part of S; a step limit bounds pathological inputs.
public class GraphSolver(IResolver resolver, int maxSteps = GraphSolver.DefaultMaxSteps)
    : IGraphSolver {
  /// The default maximum number of candidates the search examines before giving up.
  public const int DefaultMaxSteps = 1_000_000;

  public async Task<IReadOnlyList<Candidate>> Solve(
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyList<Candidate> pinned,
      LipEnvironment environment) {
    Search search = new(resolver, environment, maxSteps);
    return await search.Run(requests, pinned);
  }

  /// Orders a package set for installation (spec §7): every package comes after the packages
  /// it depends on; packages in a dependency cycle are ordered by lowercase identity (ordinal);
  /// remaining ties are broken by lowercase identity as well. Dependencies outside the set are
  /// ignored. Reverse the result to get an uninstall order.
  public static IReadOnlyList<Candidate> InstallOrder(
      IReadOnlyCollection<Candidate> set,
      Func<Candidate, IEnumerable<Candidate>> dependenciesOf) {
    List<Candidate> nodes = [];
    Dictionary<PackageKey, int> indexOf = [];
    foreach (Candidate candidate in set
        .OrderBy(c => FoldCase(c.Package.Identity), StringComparer.Ordinal)
        .ThenBy(c => c.Package.Version, SemVersion.SortOrderComparer)
        .ThenBy(c => c.Archive.Key, StringComparer.Ordinal)) {
      if (indexOf.TryAdd(PackageKey.Of(candidate), nodes.Count)) {
        nodes.Add(candidate);
      }
    }

    // Edges point from a depender to its dependees.
    List<int>[] edges = new List<int>[nodes.Count];
    for (int i = 0; i < nodes.Count; i++) {
      edges[i] = [
        .. dependenciesOf(nodes[i])
            .Select(d => indexOf.TryGetValue(PackageKey.Of(d), out int j) ? j : -1)
            .Where(j => j >= 0 && j != i)
            .Distinct()
            .Order(),
      ];
    }

    int[] componentOf = StronglyConnectedComponents(edges, out int componentCount);

    // Nodes are sorted, so iterating them in order yields each component's members sorted by
    // lowercase identity, and the first member seen is the component's smallest one.
    List<int>[] members = new List<int>[componentCount];
    int[] rank = new int[componentCount];
    for (int c = 0; c < componentCount; c++) {
      members[c] = [];
    }

    for (int i = 0; i < nodes.Count; i++) {
      if (members[componentOf[i]].Count == 0) {
        rank[componentOf[i]] = i;
      }

      members[componentOf[i]].Add(i);
    }

    // Kahn's algorithm on the condensation, always emitting the ready component with the
    // smallest member first.
    int[] pendingDependencies = new int[componentCount];
    List<int>[] dependers = new List<int>[componentCount];
    for (int c = 0; c < componentCount; c++) {
      dependers[c] = [];
    }

    HashSet<(int, int)> componentEdges = [];
    for (int i = 0; i < nodes.Count; i++) {
      foreach (int j in edges[i]) {
        (int from, int to) = (componentOf[i], componentOf[j]);
        if (from != to && componentEdges.Add((from, to))) {
          pendingDependencies[from]++;
          dependers[to].Add(from);
        }
      }
    }

    PriorityQueue<int, int> ready = new();
    for (int c = 0; c < componentCount; c++) {
      if (pendingDependencies[c] == 0) {
        ready.Enqueue(c, rank[c]);
      }
    }

    List<Candidate> order = new(nodes.Count);
    while (ready.TryDequeue(out int component, out _)) {
      order.AddRange(members[component].Select(i => nodes[i]));
      foreach (int depender in dependers[component]) {
        if (--pendingDependencies[depender] == 0) {
          ready.Enqueue(depender, rank[depender]);
        }
      }
    }

    return order;
  }

  /// Whether two candidates are the same package: same identity (ignoring ASCII case), same
  /// version and same archive key.
  public static bool IsSamePackage(Candidate a, Candidate b) {
    return PackageKey.Of(a).Equals(PackageKey.Of(b));
  }

  /// Lowercases ASCII letters only, matching identity comparison (spec §1).
  private static string FoldCase(string identity) {
    return identity.Any(char.IsAsciiLetterUpper)
        ? string.Create(identity.Length, identity, (span, s) => {
          for (int i = 0; i < s.Length; i++) {
            span[i] = char.IsAsciiLetterUpper(s[i]) ? (char)(s[i] | 0x20) : s[i];
          }
        })
        : identity;
  }

  /// Tarjan's algorithm, iterative. Returns the component index of every node.
  private static int[] StronglyConnectedComponents(List<int>[] edges, out int count) {
    int n = edges.Length;
    int[] index = new int[n];
    int[] lowLink = new int[n];
    int[] component = new int[n];
    bool[] onStack = new bool[n];
    Array.Fill(index, -1);
    Stack<int> stack = new();
    Stack<(int Node, int Edge)> work = new();
    int nextIndex = 0;
    count = 0;

    for (int root = 0; root < n; root++) {
      if (index[root] != -1) {
        continue;
      }

      work.Push((root, 0));
      while (work.TryPop(out (int Node, int Edge) frame)) {
        int v = frame.Node;
        if (frame.Edge == 0) {
          index[v] = lowLink[v] = nextIndex++;
          stack.Push(v);
          onStack[v] = true;
        } else {
          int child = edges[v][frame.Edge - 1];
          lowLink[v] = Math.Min(lowLink[v], lowLink[child]);
        }

        bool descended = false;
        for (int e = frame.Edge; e < edges[v].Count; e++) {
          int w = edges[v][e];
          if (index[w] == -1) {
            work.Push((v, e + 1));
            work.Push((w, 0));
            descended = true;
            break;
          }

          if (onStack[w]) {
            lowLink[v] = Math.Min(lowLink[v], index[w]);
          }
        }

        if (descended || lowLink[v] != index[v]) {
          continue;
        }

        int member;
        do {
          member = stack.Pop();
          onStack[member] = false;
          component[member] = count;
        } while (member != v);

        count++;
      }
    }

    return component;
  }

  /// The "same package" identity of a candidate.
  private readonly record struct PackageKey(string Identity, SemVersion Version, string Archive) {
    public static PackageKey Of(Candidate candidate) {
      return new(
          FoldCase(candidate.Package.Identity),
          candidate.Package.Version,
          candidate.Archive.Key);
    }
  }

  private readonly record struct ResolveKey(string Ref, string Base, object Range, bool Compat);

  /// Identifies the candidate list of a requirement by what determines it. Base is "" when no
  /// ref is relative, since only relative refs depend on the base archive.
  private readonly record struct ChoicesKey(
      string Refs,
      string Base,
      object Range,
      bool Compat,
      string? Name) {
    public static ChoicesKey Of(
        IReadOnlyList<Ref> refs,
        ArchiveHandle baseArchive,
        IVersionConstraint? range,
        bool compat,
        string? name) {
      return new(
          string.Join("\n", refs),
          BaseKey(refs, baseArchive),
          RangeKey(range),
          compat,
          name is null ? null : FoldCase(name));
    }

    public static string BaseKey(IEnumerable<Ref> refs, ArchiveHandle baseArchive) {
      return refs.Any(r => r.Kind == RefKind.Relative) ? baseArchive.Key : "";
    }

    /// Ranges are keyed by text for the known range types and by value equality for others.
    public static object RangeKey(IVersionConstraint? range) {
      return range switch {
        null => "",
        VersionRange r => $"range:{r.Text}",
        SemVersionRangeConstraint r => $"semver:{r.Range}",
        _ => range,
      };
    }
  }

  /// An open obligation: a request (Owner null), or an install edge of a member. OwnerLevel is
  /// the decision level that added the owner, or -1 for requests and pinned owners.
  private sealed record Requirement(
      ChoicesKey Key,
      Candidate? Owner,
      string Description,
      int OwnerLevel,
      Func<Task<Choices>> Load) {
    public Choices? Loaded { get; set; }
  }

  /// The candidates that satisfy a requirement, in preference order and without duplicates.
  private sealed class Choices(
      IReadOnlyList<Candidate> candidates,
      IReadOnlyList<string> diagnostics) {
    public IReadOnlyList<Candidate> Candidates { get; } = candidates;

    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;

    public PackageKey[] CandidateKeys { get; } = [.. candidates.Select(PackageKey.Of)];

    public HashSet<PackageKey> Keys => field ??= [.. CandidateKeys];

    /// Distinct case-folded identities of the candidates.
    public string[] Identities => field ??= [.. CandidateKeys.Select(k => k.Identity).Distinct()];

    /// Set once no candidate can be chosen whatever the other members are.
    public bool Unsatisfiable { get; set; }
  }

  private sealed record Member(Candidate Candidate, PackageKey Key, int Level);

  private sealed class Search(IResolver resolver, LipEnvironment environment, int maxSteps) {
    private const int MaxRecordedFailures = 100;
    private const int MaxDiagnostics = 1000;
    private const int LevelsPerYield = 128;
    private const int MaxReportedFailures = 10;
    private const int MaxListedReasons = 5;

    private readonly Dictionary<ResolveKey, Task<ResolveResult>> _resolved = [];
    private readonly Dictionary<ChoicesKey, Task<Choices>> _choices = [];
    /// Members by case-folded identity.
    private readonly Dictionary<string, Member> _members = new(StringComparer.Ordinal);
    private readonly List<Requirement> _agenda = [];

    /// Candidates proven to fail whatever else is chosen, with the reason.
    private readonly Dictionary<PackageKey, string> _dead = [];

    private readonly List<string> _failures = [];
    private readonly List<string> _unresolvable = [];
    private readonly List<string> _diagnostics = [];
    private readonly HashSet<string> _reported = [];
    private int _steps;

    public async Task<IReadOnlyList<Candidate>> Run(
        IReadOnlyList<PackageRequest> requests,
        IReadOnlyList<Candidate> pinned) {
      for (int i = 0; i < requests.Count; i++) {
        PackageRequest request = requests[i];
        _agenda.Add(new Requirement(
            ChoicesKey.Of([request.Ref], request.Base, request.Range, false, null),
            null,
            $"request {request.Ref} {Show(request.Range)}",
            -1,
            () => LoadChoices([request.Ref], request.Base, request.Range, false, null)));
      }

      foreach (Candidate candidate in pinned) {
        PackageKey key = PackageKey.Of(candidate);
        if (_members.TryGetValue(key.Identity, out Member? other)) {
          if (other.Key == key) {
            continue;
          }

          throw Fail($"Installed packages {Show(other.Candidate)} and {Show(candidate)} have the "
              + "same identity.");
        }

        foreach (Member member in _members.Values) {
          if (Conflicts(member.Candidate.Package, candidate.Package)
              || Conflicts(candidate.Package, member.Candidate.Package)) {
            throw Fail($"Installed packages {Show(member.Candidate)} and {Show(candidate)} "
                + "conflict.");
          }
        }

        if (Conflicts(candidate.Package, candidate.Package)) {
          throw Fail($"Installed package {Show(candidate)} conflicts with itself.");
        }

        _members.Add(key.Identity, new Member(candidate, key, -1));
        AddEdges(candidate, -1, " (installed)");
      }

      HashSet<int>? nogood = await Solve(0, 0);
      if (nogood is not null) {
        // Missing dependencies are usually the root cause; among the other failures, the most
        // recent ones are closest to the requests that finally failed.
        List<string> lines = [.. _unresolvable, .. Enumerable.Reverse(_failures)];
        if (lines.Count == 0) {
          lines.Add("no combination of candidates is free of identity clashes and conflicts");
        }

        throw Fail("No package set satisfies the request:"
            + string.Concat(lines.Take(MaxReportedFailures).Select(l => $"\n  - {l}"))
            + (lines.Count > MaxReportedFailures
                ? $"\n  ... and {lines.Count - MaxReportedFailures} more (see diagnostics)"
                : ""));
      }

      // On success the agenda holds exactly the requests and the edges of every member, each
      // satisfied by a member.
      Dictionary<PackageKey, List<Candidate>> dependencies =
          _members.Values.ToDictionary(m => m.Key, _ => new List<Candidate>());
      foreach (Requirement requirement in _agenda) {
        if (requirement.Owner is Candidate owner
            && SatisfyingMember(requirement.Loaded!) is Candidate dependee) {
          dependencies[PackageKey.Of(owner)].Add(dependee);
        }
      }

      return InstallOrder(
          [.. _members.Values.Select(m => m.Candidate)],
          c => dependencies[PackageKey.Of(c)]);
    }

    /// Satisfies the open requirements from agenda position index on. Returns null on success
    /// (the members then form S), or the decision levels whose choices together caused the
    /// failure (a nogood; -1 is never included).
    private async Task<HashSet<int>?> Solve(int index, int level) {
      if (level % LevelsPerYield == LevelsPerYield - 1) {
        // Resolver results are usually cached and complete synchronously, so the recursion
        // would otherwise grow the thread stack by one frame per member.
        await Task.Yield();
      }

      Requirement requirement;
      Choices choices;
      while (true) {
        if (index == _agenda.Count) {
          return null;
        }

        requirement = _agenda[index];
        choices = await GetChoices(requirement);
        if (SatisfyingMember(choices) is null) {
          break;
        }

        index++;
      }

      // The nogood is the owner (the requirement exists because of it) plus the blame: the
      // levels responsible for each candidate being rejected.
      HashSet<int> blame = [];
      List<string> blocked = [];
      int failedDeeper = 0;
      for (int i = 0; i < choices.Candidates.Count && !choices.Unsatisfiable; i++) {
        Candidate candidate = choices.Candidates[i];
        if (++_steps > maxSteps) {
          throw Fail($"Gave up resolving dependencies after examining {maxSteps} candidates; "
              + $"last open requirement: {requirement.Description}.");
        }

        if (Blocker(candidate, choices.CandidateKeys[i]) is (int blockerLevel, string reason)) {
          if (blockerLevel >= 0) {
            blame.Add(blockerLevel);
          }

          blocked.Add(reason);
          continue;
        }

        int agendaCount = _agenda.Count;
        PackageKey key = choices.CandidateKeys[i];
        _members.Add(key.Identity, new Member(candidate, key, level));
        AddEdges(candidate, level, "");

        HashSet<int>? result = await Solve(index + 1, level + 1);
        if (result is null) {
          return null;
        }

        _members.Remove(key.Identity);
        _agenda.RemoveRange(agendaCount, _agenda.Count - agendaCount);

        if (!result.Contains(level)) {
          // This choice played no part in the failure; trying others cannot help.
          return result;
        }

        result.Remove(level);
        if (result.Count == 0) {
          _dead[key] = $"{Show(candidate)} cannot be installed";
        }

        blame.UnionWith(result);
        failedDeeper++;
      }

      if (blame.Count == 0) {
        // No member is to blame, so the same candidates fail for any owner.
        choices.Unsatisfiable = true;
      }

      RecordFailure(requirement, choices, blocked, failedDeeper);
      if (requirement.OwnerLevel >= 0) {
        blame.Add(requirement.OwnerLevel);
      }

      return blame;
    }

    /// Returns why a candidate cannot join the current members, with the decision level of the
    /// member responsible (-1 when no member or only fixed ones are), or null when it can.
    private (int Level, string Reason)? Blocker(Candidate candidate, PackageKey key) {
      if (_dead.TryGetValue(key, out string? dead)) {
        return (-1, dead);
      }

      if (_members.TryGetValue(key.Identity, out Member? same)) {
        return (same.Level, $"{Show(candidate)} has the same identity as {Show(same)}");
      }

      if (Conflicts(candidate.Package, candidate.Package)) {
        return (-1, $"{Show(candidate)} conflicts with itself");
      }

      (int Level, string Reason)? best = null;
      foreach (Member member in _members.Values) {
        if (best is not null && best.Value.Level <= member.Level) {
          continue;
        }

        if (Conflicts(member.Candidate.Package, candidate.Package)) {
          best = (member.Level, $"{Show(member)} conflicts with {Show(candidate)}");
        } else if (Conflicts(candidate.Package, member.Candidate.Package)) {
          best = (member.Level, $"{Show(candidate)} conflicts with {Show(member)}");
        }
      }

      return best;
    }

    private Candidate? SatisfyingMember(Choices choices) {
      foreach (string identity in choices.Identities) {
        if (_members.TryGetValue(identity, out Member? member)
            && choices.Keys.Contains(member.Key)) {
          return member.Candidate;
        }
      }

      return null;
    }

    private void AddEdges(Candidate owner, int level, string suffix) {
      IReadOnlyList<InstallEdge> edges = owner.Package.InstallEdges;
      foreach (InstallEdge edge in edges) {
        _agenda.Add(new Requirement(
            EdgeKey(owner, edge),
            owner,
            $"{Show(owner)}{suffix} requires {edge.Name ?? "a package"} {Show(edge.Version)} "
                + $"from {string.Join(", ", edge.Refs)}",
            level,
            () => LoadChoices(edge.Refs, owner.Archive, edge.Version, edge.ToothClientCompat,
                edge.Name)));
      }
    }

    private static ChoicesKey EdgeKey(Candidate owner, InstallEdge edge) {
      return ChoicesKey.Of(edge.Refs, owner.Archive, edge.Version, edge.ToothClientCompat,
          edge.Name);
    }

    private async Task<Choices> GetChoices(Requirement requirement) {
      if (!_choices.TryGetValue(requirement.Key, out Task<Choices>? choices)) {
        choices = requirement.Load();
        _choices[requirement.Key] = choices;
      }

      requirement.Loaded = await choices;
      return requirement.Loaded;
    }

    private async Task<Choices> LoadChoices(
        IReadOnlyList<Ref> refs,
        ArchiveHandle baseArchive,
        IVersionConstraint? range,
        bool compat,
        string? name) {
      List<Candidate> candidates = [];
      HashSet<PackageKey> seen = [];
      List<string> diagnostics = [];
      foreach (Ref reference in refs) {
        ResolveResult result = await Resolve(reference, baseArchive, range, compat);
        diagnostics.AddRange(result.Diagnostics);
        foreach (Candidate candidate in result.Candidates) {
          if (name is not null && !SameIdentity(name, candidate.Package.Identity)) {
            diagnostics.Add($"{reference}: {Show(candidate)} is not named {name}");
            continue;
          }

          if (seen.Add(PackageKey.Of(candidate))) {
            candidates.Add(candidate);
          }
        }
      }

      return new Choices(candidates, diagnostics);
    }

    private Task<ResolveResult> Resolve(
        Ref reference,
        ArchiveHandle baseArchive,
        IVersionConstraint? range,
        bool compat) {
      ResolveKey key = new(
          reference.ToString(),
          ChoicesKey.BaseKey([reference], baseArchive),
          ChoicesKey.RangeKey(range),
          compat);
      if (!_resolved.TryGetValue(key, out Task<ResolveResult>? result)) {
        result = resolver.Resolve(reference, baseArchive, range, environment, compat);
        _resolved[key] = result;
      }

      return result;
    }

    private void RecordFailure(
        Requirement requirement,
        Choices choices,
        List<string> blocked,
        int failedDeeper) {
      string line;
      if (choices.Candidates.Count > 0 && blocked.Count == 0 && failedDeeper == 0) {
        // Skipped: the candidates were already found unsatisfiable for another owner.
        AddFailure($"{requirement.Description}: no candidate can be installed", []);
        return;
      }

      if (choices.Candidates.Count == 0) {
        line = $"{requirement.Description}: no candidates";
        if (_unresolvable.Count < MaxRecordedFailures && _reported.Add(line)) {
          _unresolvable.Add(line);
          AddDiagnostics(line, choices.Diagnostics);
        }

        return;
      }

      List<string> reasons = [.. blocked.Take(MaxListedReasons)];
      if (blocked.Count > MaxListedReasons) {
        reasons.Add($"{blocked.Count - MaxListedReasons} more blocked");
      }

      if (failedDeeper > 0) {
        reasons.Add($"{failedDeeper} failed on their own requirements");
      }

      AddFailure(
          $"{requirement.Description}: no candidate fits ({string.Join("; ", reasons)})",
          choices.Diagnostics);
    }

    /// Keeps the most recent distinct failures.
    private void AddFailure(string line, IReadOnlyList<string> diagnostics) {
      if (!_reported.Add(line)) {
        return;
      }

      if (_failures.Count == MaxRecordedFailures) {
        _failures.RemoveAt(0);
      }

      _failures.Add(line);
      AddDiagnostics(line, diagnostics);
    }

    private void AddDiagnostics(string line, IReadOnlyList<string> diagnostics) {
      if (_diagnostics.Count < MaxDiagnostics) {
        _diagnostics.Add(line);
        _diagnostics.AddRange(diagnostics.Select(d => $"    {d}"));
      }
    }

    private DependencyResolutionException Fail(string message) {
      return new DependencyResolutionException(message, [.. _diagnostics]);
    }

    private static bool Conflicts(IPackage package, IPackage other) {
      return package.Conflicts.Any(d => SameIdentity(d.Name, other.Identity)
          && (d.Version is null || d.Version.IsSatisfiedBy(other.Version)));
    }

    private static bool SameIdentity(string a, string b) {
      return a.Length == b.Length && FoldCase(a) == FoldCase(b);
    }

    private static string Show(Candidate candidate) {
      return $"{candidate.Package.Identity}@{candidate.Package.Version}";
    }

    private static string Show(Member member) {
      return Show(member.Candidate) + (member.Level < 0 ? " (installed)" : "");
    }

    private static string Show(IVersionConstraint? range) {
      return range?.ToString() ?? "*";
    }
  }
}
