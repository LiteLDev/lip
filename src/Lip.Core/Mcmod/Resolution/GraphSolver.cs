using Semver;

namespace Lip.Core.Mcmod.Resolution;

/// Finds the package set S of spec §6 and returns it in the install order of spec §7.
///
/// The search is a depth-first backtracking search over the open requirements (the requests,
/// then the install edges of each member in the order members are added). For each open
/// requirement the candidates are tried in their sequence order, so earlier candidates win
/// (spec §6). To avoid exponential blowup it uses conflict-directed backjumping and learns
/// candidates that can never be part of S; a step limit bounds pathological inputs.
///
/// Candidates are produced lazily (IResolver.ResolveLazy): a spec ref with hundreds of tags only
/// opens the tags the search actually reaches. Whether a member already satisfies a requirement
/// is decided without enumerating every candidate: the loaded candidates are checked first, then
/// each member whose version is in range and whose identity is that of a loaded candidate is
/// looked up directly: the requirement's spec refs are resolved for exactly that version. This
/// equals the full enumeration except for a routing document without a version key at a tag
/// whose version differs from the routed package's, and for a package renamed between versions.
public class GraphSolver(IResolver resolver, int maxSteps = GraphSolver.DefaultMaxSteps)
    : IGraphSolver {
  /// The default maximum number of candidates the search examines before giving up.
  public const int DefaultMaxSteps = 1_000_000;

  public async Task<IReadOnlyList<Candidate>> Solve(
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyList<Candidate> pinned,
      LipEnvironment environment) {
    return (await SolveGraph(requests, pinned, environment)).Order;
  }

  public async Task<SolvedGraph> SolveGraph(
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

  private static bool SameIdentity(string a, string b) {
    return a.Length == b.Length && FoldCase(a) == FoldCase(b);
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
        ArchiveHandle? baseArchive,
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

    public static string BaseKey(IEnumerable<Ref> refs, ArchiveHandle? baseArchive) {
      return refs.Any(r => r.Kind == RefKind.Relative) ? baseArchive?.Key ?? "" : "";
    }

    /// Ranges are keyed by text for the known range types and by value equality for others.
    public static object RangeKey(IVersionConstraint? range) {
      return range switch {
        null => "",
        VersionRange r => $"range:{r.Text}",
        SemVersionRangeConstraint r => $"semver:{r.Range}",
        ExactVersionConstraint r => $"exact:{r.Version}",
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
      IReadOnlyList<Ref> Refs,
      ArchiveHandle? Base,
      IVersionConstraint? Range,
      bool Compat,
      string? Name) {
    public Choices? Loaded { get; set; }
  }

  /// The candidate sequence of one Resolve call, produced on demand and shared by every
  /// requirement that resolves the same ref.
  private sealed class LazySequence(Func<Action<string>, IAsyncEnumerable<Candidate>> start) {
    private IAsyncEnumerator<Candidate>? _enumerator;

    public List<Candidate> Items { get; } = [];

    public List<string> Diagnostics { get; } = [];

    public bool Complete { get; private set; }

    /// Whether the sequence has an item at index, producing items up to it if needed.
    public async ValueTask<bool> Has(int index) {
      while (Items.Count <= index && !Complete) {
        _enumerator ??= start(Diagnostics.Add).GetAsyncEnumerator();
        if (await _enumerator.MoveNextAsync()) {
          Items.Add(_enumerator.Current);
        } else {
          Complete = true;
          await _enumerator.DisposeAsync();
        }
      }

      return index < Items.Count;
    }
  }

  /// The candidates that satisfy a requirement, in preference order and without duplicates:
  /// the concatenated sequences of its refs, filtered by name. Loaded on demand.
  private sealed class Choices(IReadOnlyList<(Ref Ref, LazySequence Sequence)> sources, string? name) {
    private readonly HashSet<PackageKey> _keys = [];
    private readonly List<string> _filtered = [];
    private int _source;
    private int _position;

    public List<Candidate> Candidates { get; } = [];

    public List<PackageKey> CandidateKeys { get; } = [];

    public IEnumerable<string> Diagnostics =>
        sources.SelectMany(s => s.Sequence.Diagnostics).Concat(_filtered);

    /// Whether every candidate has been loaded.
    public bool Complete { get; private set; }

    /// Set once no candidate can be chosen whatever the other members are.
    public bool Unsatisfiable { get; set; }

    /// Whether there is a candidate at index, loading candidates up to it if needed.
    public async ValueTask<bool> Has(int index) {
      while (Candidates.Count <= index && !Complete) {
        await LoadNext();
      }

      return index < Candidates.Count;
    }

    private async ValueTask LoadNext() {
      while (_source < sources.Count) {
        (Ref reference, LazySequence sequence) = sources[_source];
        if (!await sequence.Has(_position)) {
          _source++;
          _position = 0;
          continue;
        }

        Candidate candidate = sequence.Items[_position++];
        if (name is not null && !SameIdentity(name, candidate.Package.Identity)) {
          _filtered.Add($"{reference}: {candidate.Package.Identity}@{candidate.Package.Version}"
              + $" is not named {name}");
          continue;
        }

        PackageKey key = PackageKey.Of(candidate);
        if (_keys.Add(key)) {
          Candidates.Add(candidate);
          CandidateKeys.Add(key);
          return;
        }
      }

      Complete = true;
    }
  }

  private sealed record Member(Candidate Candidate, PackageKey Key, int Level);

  private sealed class Search(IResolver resolver, LipEnvironment environment, int maxSteps) {
    private const int MaxRecordedFailures = 100;
    private const int MaxDiagnostics = 1000;
    private const int LevelsPerYield = 128;
    private const int MaxReportedFailures = 10;
    private const int MaxListedReasons = 5;

    private readonly Dictionary<ResolveKey, LazySequence> _resolved = [];
    private readonly Dictionary<ChoicesKey, Choices> _choices = [];
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

    public async Task<SolvedGraph> Run(
        IReadOnlyList<PackageRequest> requests,
        IReadOnlyList<Candidate> pinned) {
      for (int i = 0; i < requests.Count; i++) {
        PackageRequest request = requests[i];
        _agenda.Add(new Requirement(
            ChoicesKey.Of([request.Ref], request.Base, request.Range, false, null),
            null,
            $"request {request.Ref} {Show(request.Range)}",
            -1,
            [request.Ref],
            request.Base,
            request.Range,
            false,
            null));
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
      List<Candidate?> requestSatisfiers = [];
      foreach (Requirement requirement in _agenda) {
        Candidate? satisfier = await SatisfyingMember(requirement, await GetChoices(requirement));
        if (requirement.Owner is Candidate owner) {
          if (satisfier is not null) {
            dependencies[PackageKey.Of(owner)].Add(satisfier);
          }
        } else {
          requestSatisfiers.Add(satisfier);
        }
      }

      IReadOnlyList<Candidate> order = InstallOrder(
          [.. _members.Values.Select(m => m.Candidate)],
          c => dependencies[PackageKey.Of(c)]);
      Dictionary<PackageKey, int> indexOf = [];
      for (int i = 0; i < order.Count; i++) {
        indexOf[PackageKey.Of(order[i])] = i;
      }

      return new SolvedGraph(
          order,
          [.. order.Select(c => (IReadOnlyList<int>)[
            .. dependencies[PackageKey.Of(c)].Select(d => indexOf[PackageKey.Of(d)]).Distinct(),
          ])],
          [.. requestSatisfiers.Select(s => s is null ? -1 : indexOf[PackageKey.Of(s)])]);
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
        if (await SatisfyingMember(requirement, choices) is null) {
          break;
        }

        index++;
      }

      // The nogood is the owner (the requirement exists because of it) plus the blame: the
      // levels responsible for each candidate being rejected.
      HashSet<int> blame = [];
      List<string> blocked = [];
      int failedDeeper = 0;
      for (int i = 0; !choices.Unsatisfiable && await choices.Has(i); i++) {
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

    /// Returns the member that satisfies a requirement (spec §6: a member that is one of the
    /// requirement's candidates), or null. See the class remarks for how this avoids loading
    /// every candidate.
    private async Task<Candidate?> SatisfyingMember(Requirement requirement, Choices choices) {
      if (_members.Count == 0) {
        return null;
      }

      for (int i = 0; i < choices.Candidates.Count; i++) {
        if (_members.TryGetValue(choices.CandidateKeys[i].Identity, out Member? member)
            && member.Key == choices.CandidateKeys[i]) {
          return member.Candidate;
        }
      }

      if (requirement.Refs.Any(r => r.Kind == RefKind.Spec && r.GitRef is null)) {
        foreach (Member member in _members.Values) {
          if (IsPinnedSatisfier(requirement, member.Candidate)) {
            return member.Candidate;
          }
        }
      }

      if (choices.Complete) {
        return null;
      }

      // Only spec refs can have long candidate sequences; load the others completely.
      if (requirement.Refs.All(r => r.Kind != RefKind.Spec)) {
        for (int i = choices.Candidates.Count; await choices.Has(i); i++) {
          if (_members.TryGetValue(choices.CandidateKeys[i].Identity, out Member? member)
              && member.Key == choices.CandidateKeys[i]) {
            return member.Candidate;
          }
        }

        return null;
      }

      // Only members named like a candidate are looked up, so that members of unrelated packages
      // never make the requirement's repository open the tag of their version.
      if (!await choices.Has(0)) {
        return null;
      }

      HashSet<string> identities = [.. choices.CandidateKeys.Select(k => k.Identity)];
      foreach (Member member in _members.Values.ToList()) {
        if (identities.Contains(member.Key.Identity) && await IsCandidate(requirement, member)) {
          return member.Candidate;
        }
      }

      return null;
    }

    /// Whether a candidate resolved from a spec ref pinned by a git ref satisfies a requirement
    /// through one of its unpinned spec refs (spec §6): same module path (ignoring ASCII case, as
    /// the hosts do) and variant, and a version in the requirement's range. A pinned build thus
    /// stands in for the tag versions of its repository, while a pinned requirement still needs
    /// exactly its pin.
    private static bool IsPinnedSatisfier(Requirement requirement, Candidate candidate) {
      if (candidate.Chain.Count == 0
          || candidate.Chain[0].Ref is not { Kind: RefKind.Spec, GitRef: not null } pinned) {
        return false;
      }

      if (requirement.Range is not null
          && !requirement.Range.IsSatisfiedBy(candidate.Package.Version)) {
        return false;
      }

      if (requirement.Name is not null
          && !SameIdentity(requirement.Name, candidate.Package.Identity)) {
        return false;
      }

      return requirement.Refs.Any(r => r.Kind == RefKind.Spec && r.GitRef is null
          && string.Equals(r.Target, pinned.Target, StringComparison.OrdinalIgnoreCase)
          && (r.Variant == pinned.Variant
              || (requirement.Compat && r.Variant == "client" && pinned.Variant == "")));
    }

    /// Whether a member is one of the candidates of a requirement, without enumerating the
    /// candidates of other versions.
    private async Task<bool> IsCandidate(Requirement requirement, Member member) {
      SemVersion version = member.Candidate.Package.Version;
      if (requirement.Range is not null && !requirement.Range.IsSatisfiedBy(version)) {
        return false;
      }

      if (requirement.Name is not null
          && !SameIdentity(requirement.Name, member.Candidate.Package.Identity)) {
        return false;
      }

      foreach (Ref reference in requirement.Refs) {
        LazySequence sequence = Resolve(
            reference,
            requirement.Base,
            reference.Kind == RefKind.Spec
                ? new ExactVersionConstraint(version)
                : requirement.Range,
            requirement.Compat);
        for (int i = 0; await sequence.Has(i); i++) {
          if (PackageKey.Of(sequence.Items[i]) == member.Key) {
            return true;
          }
        }
      }

      return false;
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
            edge.Refs,
            owner.Archive,
            edge.Version,
            edge.ToothClientCompat,
            edge.Name));
      }
    }

    private static ChoicesKey EdgeKey(Candidate owner, InstallEdge edge) {
      return ChoicesKey.Of(edge.Refs, owner.Archive, edge.Version, edge.ToothClientCompat,
          edge.Name);
    }

    private Task<Choices> GetChoices(Requirement requirement) {
      if (!_choices.TryGetValue(requirement.Key, out Choices? choices)) {
        choices = new Choices(
            [
              .. requirement.Refs.Select(r => (r, Resolve(
                  r, requirement.Base, requirement.Range, requirement.Compat))),
            ],
            requirement.Name);
        _choices[requirement.Key] = choices;
      }

      requirement.Loaded = choices;
      return Task.FromResult(choices);
    }

    private LazySequence Resolve(
        Ref reference,
        ArchiveHandle? baseArchive,
        IVersionConstraint? range,
        bool compat) {
      ResolveKey key = new(
          reference.ToString(),
          ChoicesKey.BaseKey([reference], baseArchive),
          ChoicesKey.RangeKey(range),
          compat);
      if (!_resolved.TryGetValue(key, out LazySequence? sequence)) {
        sequence = new LazySequence(sink => resolver.ResolveLazy(
            reference, baseArchive, range, environment, compat, sink));
        _resolved[key] = sequence;
      }

      return sequence;
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
    private void AddFailure(string line, IEnumerable<string> diagnostics) {
      if (!_reported.Add(line)) {
        return;
      }

      if (_failures.Count == MaxRecordedFailures) {
        _failures.RemoveAt(0);
      }

      _failures.Add(line);
      AddDiagnostics(line, diagnostics);
    }

    private void AddDiagnostics(string line, IEnumerable<string> diagnostics) {
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
