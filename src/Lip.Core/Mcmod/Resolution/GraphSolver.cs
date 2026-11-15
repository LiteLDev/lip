using Lip.Core.Infrastructure;
using Lip.Core.Mcmod.Install;
using Semver;

namespace Lip.Core.Mcmod.Resolution;

/// Finds the package set S of protocol/lip-workspace.md, "Valid states" and returns it in the
/// installation order of Section 5.
///
/// The search is a depth-first backtracking search over the open requirements (the requests,
/// then the dependencies of each member in the order members are added). For each open
/// requirement the candidates are tried in their sequence order, so earlier candidates win
/// (Section 3). To avoid exponential blowup it uses conflict-directed backjumping and learns
/// candidates that can never be part of S.
///
/// Candidates are produced lazily (IResolver.ResolveLazy): a remote repository with hundreds of
/// tags only opens the tags the search actually reaches. Whether a member already satisfies a
/// requirement is decided without enumerating every candidate: the loaded candidates are checked
/// first, then each member whose version is in range and whose identity is that of a loaded
/// candidate is looked up directly: the requirement's remote repository is resolved for exactly
/// that version.
///
/// Two members clash when they have the same identity, or are both mods of the same mod name
/// (Section 3 rule 3). A member resolved from a remote repository with a ref stands in for the
/// versions of that repository (Section 3), but its candidates are not candidates of a source
/// without the ref. When a requirement without a ref is decided before one with a ref for the
/// same repository, its choice clashes with the package of the ref; once every candidate has
/// failed so, the requirement is deferred to the end of the agenda, where that package may
/// satisfy it.
public class GraphSolver(IResolver resolver) {
  /// Pinned candidates (installed packages to keep) are members of S as they are; their
  /// dependencies must be satisfied too. Each preferred candidate (an installed package that may
  /// change version) is tried first for a requirement it satisfies, before the requirement's own
  /// candidates. Returns S in installation order (protocol/lip-workspace.md, "Operations"), with
  /// which member satisfies which dependency and request. Throws DependencyResolutionException.
  public async Task<SolvedGraph> SolveGraph(
      IReadOnlyList<PackageRequest> requests,
      IReadOnlyList<Candidate> pinned,
      LipEnvironment environment,
      IReadOnlyList<Candidate>? preferred = null) {
    Search search = new(resolver, environment, preferred ?? []);
    return await search.Run(requests, pinned);
  }

  /// How a package satisfies a request (protocol/lip-workspace.md, "Valid states" rule 1): the
  /// candidate of the request with its identity, version and commit, or else the package itself
  /// when it stands in for a candidate; null when it does neither.
  public async Task<Candidate?> RequestCandidate(
      PackageRequest request, Candidate package, LipEnvironment environment) {
    Search search = new(resolver, environment, []);
    return await search.RequestCandidate(request, package);
  }

  /// Orders a package set for installation (protocol/lip-workspace.md, "Operations"): every package
  /// comes after the packages it depends on; packages in a dependency cycle are ordered by
  /// lowercase identity (ordinal); remaining ties are broken by lowercase identity as well.
  /// Dependencies outside the set are ignored. Reverse the result to get an uninstall order.
  public static IReadOnlyList<Candidate> InstallOrder(
      IReadOnlyCollection<Candidate> set,
      Func<Candidate, IEnumerable<Candidate>> dependenciesOf) {
    List<Candidate> nodes = [];
    Dictionary<PackageKey, int> indexOf = [];
    foreach (Candidate candidate in set
        .OrderBy(c => PackageNames.Fold(c.Package.Identity), StringComparer.Ordinal)
        .ThenBy(c => c.Package.Version, SemVersion.SortOrderComparer)
        .ThenBy(c => c.Archive.Commit ?? "", StringComparer.Ordinal)) {
      if (indexOf.TryAdd(PackageKey.Of(candidate), nodes.Count)) {
        nodes.Add(candidate);
      }
    }

    return [
      .. InstallOrder(nodes.Count, i => dependenciesOf(nodes[i])
          .Select(d => indexOf.TryGetValue(PackageKey.Of(d), out int j) ? j : -1))
          .Select(i => nodes[i]),
    ];
  }

  /// InstallOrder for count packages given in ascending order of their lowercase identities:
  /// the indices of the packages in installation order, given the indices of the packages each
  /// depends on. Indices out of range are ignored.
  public static IReadOnlyList<int> InstallOrder(
      int count, Func<int, IEnumerable<int>> dependenciesOf) {
    // Edges point from a depender to its dependees.
    List<int>[] edges = new List<int>[count];
    for (int i = 0; i < count; i++) {
      edges[i] = [
        .. dependenciesOf(i)
            .Where(j => j >= 0 && j < count && j != i)
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

    for (int i = 0; i < count; i++) {
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
    for (int i = 0; i < count; i++) {
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

    List<int> order = new(count);
    while (ready.TryDequeue(out int component, out _)) {
      order.AddRange(members[component]);
      foreach (int depender in dependers[component]) {
        if (--pendingDependencies[depender] == 0) {
          ready.Enqueue(depender, rank[depender]);
        }
      }
    }

    return order;
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

  /// The "same package" identity of a candidate (protocol/lip-workspace.md, "Operations"): its
  /// identity, version and commit.
  private readonly record struct PackageKey(string Identity, SemVersion Version, string Commit) {
    public static PackageKey Of(Candidate candidate) {
      return new(
          PackageNames.Fold(candidate.Package.Identity),
          candidate.Package.Version,
          candidate.Archive.Commit?.ToLowerInvariant() ?? "");
    }
  }

  /// Identifies a candidate sequence by what determines it: the source and the range.
  private readonly record struct ResolveKey(string Ref, object Range) {
    public static ResolveKey Of(Ref source, IVersionConstraint? range) {
      return new(source.ToString(), RangeKey(range));
    }

    /// Ranges are keyed by text for the known range types and by value equality for others.
    private static object RangeKey(IVersionConstraint? range) {
      return range switch {
        null => "",
        VersionRange r => $"range:{r.Text}",
        ExactVersionConstraint r => $"exact:{r.Version}",
        _ => range,
      };
    }
  }

  /// An open obligation: a request (Owner null), or a dependency of a member. OwnerLevel is the
  /// decision level that added the owner, or -1 for requests and pinned owners. OwnerInstalled:
  /// whether the owner is an installed package kept as it is.
  private sealed record Requirement(
      ResolveKey Key,
      Candidate? Owner,
      string Description,
      int OwnerLevel,
      Ref Ref,
      IVersionConstraint? Range,
      bool Deferred = false,
      bool OwnerInstalled = false);

  /// How a candidate clashes with a member, for the hints of a failure.
  private enum Clash {
    None,

    /// With a package of the same identity that a request chose.
    Requested,

    /// With an installed package of the same identity.
    Installed,

    /// With a package of the same identity that another package needs.
    Needed,

    /// With a mod of the same mod name.
    ModName,

    /// With another variant of the server.
    Server,
  }

  /// Why candidates of a requirement cannot be chosen. Text: a clause that follows "but", or a
  /// sentence of its own (Sentence). Primary: whether it is a cause, rather than a consequence of
  /// other failures. Other: the name of the package clashed with, or of the one that needs it
  /// (Needed).
  private sealed record Obstacle(
      string Text,
      bool Primary,
      bool Sentence = false,
      Clash Clash = Clash.None,
      string? Other = null);

  /// The failures of one requirement target for one requirer (a package, or the user's
  /// requests), merged over the versions of both, as the user is told them.
  private sealed class Conflict(string requirer, bool installed, string target, Ref targetRef) {
    /// The name of the package that needs the target, or "" for a request.
    public string Requirer => requirer;

    public bool Installed => installed;

    public string Target => target;

    public Ref TargetRef => targetRef;

    public List<string> RequirerVersions { get; } = [];

    public List<string> Ranges { get; } = [];

    public List<Obstacle> Obstacles { get; } = [];

    public List<string> Diagnostics { get; } = [];

    public bool Unavailable { get; set; }

    /// The newest version of the target that a requested package of its identity blocked.
    public SemVersion? NewestBlocked { get; set; }

    public bool HasPrimary => Unavailable || Obstacles.Any(o => o.Primary);
  }

  /// The candidate sequence of one Resolve call, produced on demand and shared by every
  /// requirement that resolves the same ref, with the diagnostics of the paths that produced no
  /// candidate.
  private sealed class LazySequence(Func<LazySequence, IAsyncEnumerable<Candidate>> start) {
    private IAsyncEnumerator<Candidate>? _enumerator;

    public List<Candidate> Items { get; } = [];

    public List<ResolveDiagnostic> Diagnostics { get; } = [];

    public bool Complete { get; private set; }

    public void AddDiagnostic(ResolveDiagnostic diagnostic) => Diagnostics.Add(diagnostic);

    public void AddDiagnostic(string message) => Diagnostics.Add(new(message, null));

    /// Whether the sequence has an item at index, producing items up to it if needed.
    public async ValueTask<bool> Has(int index) {
      while (Items.Count <= index && !Complete) {
        _enumerator ??= start(this).GetAsyncEnumerator();
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

  /// The candidates that satisfy a requirement, in preference order and without duplicates.
  /// Loaded on demand.
  private sealed class Choices(LazySequence sequence) {
    private readonly HashSet<PackageKey> _keys = [];
    private int _position;

    public LazySequence Sequence => sequence;

    public List<Candidate> Candidates { get; } = [];

    public List<PackageKey> CandidateKeys { get; } = [];

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
      while (await sequence.Has(_position)) {
        Candidate candidate = sequence.Items[_position++];
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

  /// A member of S, with the requirement it was chosen for (null for a pinned candidate) and,
  /// when it is not the newest candidate of that requirement, why.
  private sealed record Member(
      Candidate Candidate,
      PackageKey Key,
      int Level,
      Requirement? ChosenFor = null,
      string? Note = null);

  private sealed class Search(
      IResolver resolver, LipEnvironment environment, IReadOnlyList<Candidate> preferred) {
    private const int MaxRecordedFailures = 100;

    // The search can take very many steps; tracing them all would push the lines that explain
    // the failure (network errors and the like) out of the bounded run trace.
    private const int MaxTracedSteps = 500;
    private int _tracedSteps;

    private void TraceStep(string message) {
      if (++_tracedSteps <= MaxTracedSteps) {
        RunTrace.Write("resolve", message);
      }
    }
    private const int LevelsPerYield = 128;
    private const int MaxReportedFailures = 10;
    private const int MaxListedReasons = 5;
    private const int MaxListedVersions = 4;
    private const int MaxHints = 4;

    private readonly Dictionary<ResolveKey, LazySequence> _resolved = [];
    private readonly Dictionary<ResolveKey, Choices> _choices = [];
    /// Members by case-folded identity.
    private readonly Dictionary<string, Member> _members = new(StringComparer.Ordinal);
    private readonly List<Requirement> _agenda = [];

    /// Candidates proven to fail whatever else is chosen, with the reason.
    private readonly Dictionary<PackageKey, Obstacle> _dead = [];

    /// Decision levels whose member blocked a candidate of a pinned spec ref by its identity.
    private readonly HashSet<int> _pinnedClashes = [];

    /// The failures, by requirer and target, in the order they first arose.
    private readonly Dictionary<(string Requirer, string Target), Conflict> _conflicts = [];
    private readonly List<(string Requirer, string Target)> _conflictOrder = [];

    /// The last cause of a failure recorded, as one line, and how many have been recorded: what
    /// made the subtree of a candidate fail.
    private string? _lastCause;
    private int _causeCount;

    /// The exceptions behind the failures (e.g. network failures), kept for the failure report.
    private readonly List<Exception> _causes = [];

    public async Task<SolvedGraph> Run(
        IReadOnlyList<PackageRequest> requests,
        IReadOnlyList<Candidate> pinned) {
      RunTrace.Write("resolve", $"Solving {requests.Count} requests with {pinned.Count}"
          + $" installed packages kept, for ({environment.Side.ToSpecString()},"
          + $" {environment.Platform})");
      foreach (PackageRequest request in requests) {
        _agenda.Add(RequestRequirement(request));
      }

      foreach (Candidate candidate in pinned) {
        PackageKey key = PackageKey.Of(candidate);
        RunTrace.Write("resolve", $"Keeping the installed package {ShowSource(candidate)}");
        if (_members.TryGetValue(key.Identity, out Member? other)) {
          if (other.Key == key) {
            continue;
          }

          throw Fail($"Installed packages {ShowSource(other.Candidate)} and {ShowSource(candidate)}"
              + " are versions of the same package, which the state file .lip/state.json must"
              + " not record. Uninstall the explicitly installed packages that need them, then"
              + " install the one you want again.");
        }

        if (IsServer(candidate) && _members.Values.FirstOrDefault(m => IsServer(m.Candidate))
            is Member server) {
          throw Fail($"Installed packages {ShowSource(server.Candidate)} and"
              + $" {ShowSource(candidate)} are both the server {BedrockServer.Repository}, which a"
              + " workspace holds once. Uninstall the explicitly installed packages that need one"
              + " of them.");
        }

        if (await ModName(candidate) is string modName) {
          foreach (Member member in _members.Values) {
            if (await ModName(member.Candidate) is string otherName
                && PackageNames.AreEqual(modName, otherName)) {
              throw Fail($"Installed packages {ShowSource(member.Candidate)} and "
                  + $"{ShowSource(candidate)} are mods of the same mod name '{modName}', so they"
                  + " would share one mod directory. Uninstall the explicitly installed packages"
                  + " that need one of them.");
            }
          }
        }

        _members.Add(key.Identity, new Member(candidate, key, -1));
        AddEdges(candidate, -1, installed: true);
      }

      HashSet<int>? nogood = await Solve(0, 0);
      if (_tracedSteps > MaxTracedSteps) {
        RunTrace.Write("resolve", $"{_tracedSteps - MaxTracedSteps} more search steps not traced");
      }
      if (nogood is not null) {
        throw Failure();
      }

      // On success the agenda holds exactly the requests and the dependencies of every member,
      // each satisfied by a member.
      Dictionary<PackageKey, List<Candidate>> dependencies =
          _members.Values.ToDictionary(m => m.Key, _ => new List<Candidate>());
      List<Candidate?> requestSatisfiers = [];
      foreach (Requirement requirement in _agenda.Where(r => !r.Deferred)) {
        Candidate? satisfier = await SatisfyingMember(requirement, await GetChoices(requirement))
            ?? await MemberAmongCandidates(await GetChoices(requirement));
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

      RunTrace.Write("resolve", "Solved; installation order: "
          + string.Join(", ", order.Select(ShowSource)));
      List<string?> notes = [
        .. order.Select(c => _members.TryGetValue(PackageKey.Of(c).Identity, out Member? m)
            ? m.Note
            : null),
      ];
      foreach (string note in notes.OfType<string>()) {
        RunTrace.Write("resolve", note);
      }

      return new SolvedGraph(
          order,
          [.. order.Select(c => (IReadOnlyList<int>)[
            .. dependencies[PackageKey.Of(c)].Select(d => indexOf[PackageKey.Of(d)]).Distinct(),
          ])],
          [.. requestSatisfiers.Select(s => s is null ? -1 : indexOf[PackageKey.Of(s)])],
          notes);
    }

    public async Task<Candidate?> RequestCandidate(PackageRequest request, Candidate package) {
      Requirement requirement = RequestRequirement(request);
      return await CandidateWithKey(requirement, PackageKey.Of(package))
          ?? (await IsPinnedSatisfier(requirement, await GetChoices(requirement), package)
              ? package
              : null);
    }

    private static Requirement RequestRequirement(PackageRequest request) => new(
        ResolveKey.Of(request.Ref, request.Range),
        null,
        $"You requested {ShowRequirement(request.Ref, request.Range)}",
        -1,
        request.Ref,
        request.Range);

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
          if (!requirement.Deferred || _agenda.Skip(index + 1).All(r => r.Deferred)) {
            break;
          }

          // A deferred requirement waits until every other open requirement is decided.
          _agenda.Add(requirement);
        }

        index++;
      }

      // The nogood is the owner (the requirement exists because of it) plus the blame: the
      // levels responsible for each candidate being rejected.
      HashSet<int> blame = [];
      List<(Candidate Candidate, Obstacle Obstacle)> blocked = [];
      int failedDeeper = 0;
      // Why the newest candidate (the first of the sequence) was not chosen, once it failed.
      string? newestFailed = null;
      _pinnedClashes.Remove(level);
      // A preferred candidate is tried first (as index -1), and not again in its place.
      Candidate? first = choices.Unsatisfiable
          ? null
          : await PreferredCandidate(requirement, choices);
      PackageKey? firstKey = first is null ? null : PackageKey.Of(first);
      for (int i = first is null ? 0 : -1;
          !choices.Unsatisfiable && (i < 0 || await choices.Has(i));
          i++) {
        Candidate candidate = i < 0 ? first! : choices.Candidates[i];
        PackageKey key = i < 0 ? firstKey!.Value : choices.CandidateKeys[i];
        if (i >= 0 && key == firstKey) {
          continue;
        }

        if (_members.TryGetValue(key.Identity, out Member? existing)
            && existing.Key == key) {
          // A member the shortcut of SatisfyingMember missed (see the class remarks). Nothing is
          // decided here, so no nogood of the rest contains this level.
          HashSet<int>? rest = await Solve(index + 1, level + 1);
          if (rest is null) {
            return null;
          }

          blame.UnionWith(rest);
          if (existing.Level >= 0) {
            blame.Add(existing.Level);
          }

          failedDeeper++;
          break;
        }

        bool newest = i == 0
            || (i < 0 && await choices.Has(0) && choices.CandidateKeys[0] == key);
        if (await Blocker(candidate, key) is (int blockerLevel, Obstacle obstacle)) {
          if (blockerLevel >= 0) {
            blame.Add(blockerLevel);
            if (IsPinned(candidate) && _members.TryGetValue(key.Identity, out Member? clash)
                && clash.Level == blockerLevel) {
              _pinnedClashes.Add(blockerLevel);
            }
          }

          TraceStep($"{requirement.Description}: rejected {Show(candidate)}: {obstacle.Text}");
          blocked.Add((candidate, obstacle));
          if (newest) {
            newestFailed = obstacle.Sentence
                ? obstacle.Text
                : $"{PackageDisplay.NameAndVersion(candidate)} cannot be installed, as"
                    + $" {obstacle.Text}";
          }

          continue;
        }

        TraceStep($"{requirement.Description}: trying {ShowSource(candidate)}");
        int agendaCount = _agenda.Count;
        int causesBefore = _causeCount;
        string? note = i > 0 ? Note(candidate, choices, newestFailed) : null;
        _members.Add(key.Identity, new Member(candidate, key, level, requirement, note));
        AddEdges(candidate, level, installed: false);

        HashSet<int>? result = await Solve(index + 1, level + 1);
        if (result is null) {
          return null;
        }

        TraceStep($"{requirement.Description}: {Show(candidate)} did not work out; backtracking");
        _members.Remove(key.Identity);
        _agenda.RemoveRange(agendaCount, _agenda.Count - agendaCount);
        if (newest) {
          newestFailed = _causeCount > causesBefore && _lastCause is string cause
              ? cause
              : $"{PackageDisplay.NameAndVersion(candidate)} needs packages that cannot be"
                  + " installed with the others";
        }

        if (!result.Contains(level)) {
          // This choice played no part in the failure; trying others cannot help.
          return result;
        }

        result.Remove(level);
        if (result.Count == 0) {
          _dead[key] = new Obstacle(
              $"{PackageDisplay.NameAndVersion(candidate)} needs packages that cannot be"
                  + " installed with the others",
              Primary: false);
        }

        blame.UnionWith(result);
        failedDeeper++;
      }

      if (!requirement.Deferred && _pinnedClashes.Contains(level)
          && requirement.Ref is { Kind: RefKind.Spec, GitRef: null }) {
        // A pinned package clashed with every choice made here; it may stand in for this
        // requirement once it is a member (see the class remarks). The deferred copy belongs to
        // this level, so its failure leads back here.
        int agendaCount = _agenda.Count;
        _agenda.Add(requirement with { OwnerLevel = level, Deferred = true });
        HashSet<int>? result = await Solve(index + 1, level + 1);
        if (result is null) {
          return null;
        }

        _agenda.RemoveRange(agendaCount, _agenda.Count - agendaCount);
        if (!result.Contains(level)) {
          return result;
        }

        result.Remove(level);
        blame.UnionWith(result);
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
    private async Task<(int Level, Obstacle Obstacle)?> Blocker(
        Candidate candidate, PackageKey key) {
      if (_dead.TryGetValue(key, out Obstacle? dead)) {
        return (-1, dead);
      }

      if (_members.TryGetValue(key.Identity, out Member? same)) {
        (Clash clash, string? other, string why) = Why(same);
        string member = PackageDisplay.NameAndVersion(same.Candidate);
        // Another package of the same version can only differ in where it comes from.
        if (same.Key.Version == key.Version) {
          member += $" from {same.Candidate.Archive.Key}";
        }

        return (same.Level, new Obstacle($"{member} {why}", Primary: true, Clash: clash,
            Other: other));
      }

      // A workspace holds one package of the server, whatever its variant
      // (protocol/lip-workspace.md, "Valid states", rule 3).
      if (IsServer(candidate) && _members.Values.FirstOrDefault(m => IsServer(m.Candidate))
          is Member server) {
        string why = Why(server).Text;
        return (server.Level, new Obstacle(
            $"{PackageDisplay.NameAndVersion(candidate)} and"
                + $" {PackageDisplay.NameAndVersion(server.Candidate)}, which {why}, are both the"
                + $" server {BedrockServer.Repository}, which a workspace holds once",
            Primary: true,
            Clash: Clash.Server,
            Other: PackageDisplay.Name(server.Candidate)));
      }

      string? modName;
      try {
        modName = await candidate.GetModManifest() is ModManifest manifest ? manifest.Name : null;
      }
      catch (McmodInstallException ex) {
        // A missing or invalid mod manifest fails whatever the other members are.
        Obstacle obstacle = new(ex.Message, Primary: true, Sentence: true);
        _dead[key] = obstacle;
        AddCause(ex);
        return (-1, obstacle);
      }

      if (modName is null) {
        return null;
      }

      (int Level, Obstacle Obstacle)? best = null;
      foreach (Member member in _members.Values) {
        if ((best is null || member.Level < best.Value.Level)
            && await ModName(member.Candidate) is string other
            && PackageNames.AreEqual(modName, other)) {
          string why = Why(member).Text;
          best = (member.Level, new Obstacle(
              $"{PackageDisplay.NameAndVersion(candidate)} is a mod of the same mod name"
                  + $" '{modName}' as {PackageDisplay.NameAndVersion(member.Candidate)}, which"
                  + $" {why}",
              Primary: true,
              Clash: Clash.ModName,
              Other: PackageDisplay.Name(member.Candidate)));
        }
      }

      return best;
    }

    /// Why a member is a member, as a clause ("was requested", "is installed" or "is needed by"
    /// a package), with the kind of clash it makes for another package of its identity and the
    /// package that needs it.
    private static (Clash Clash, string? Other, string Text) Why(Member member) {
      if (member.ChosenFor is not Requirement chosenFor) {
        return (Clash.Installed, PackageDisplay.NameAndVersion(member.Candidate), "is installed");
      }

      if (chosenFor.Owner is not Candidate owner) {
        return (Clash.Requested, PackageDisplay.NameAndVersion(member.Candidate), "was requested");
      }

      return (Clash.Needed, PackageDisplay.Name(owner),
          $"is needed by {PackageDisplay.NameAndVersion(owner)}");
    }

    /// Why a chosen candidate is not the newest candidate of its requirement, or null when it is,
    /// or the newest is not a newer version.
    private static string? Note(Candidate chosen, Choices choices, string? newestFailed) {
      if (newestFailed is null
          || choices.Candidates.Count == 0
          || choices.Candidates[0].Package.Version.ComparePrecedenceTo(chosen.Package.Version)
              <= 0) {
        return null;
      }

      return $"{PackageDisplay.NameAndVersion(chosen)} is not the newest version:"
          + $" {newestFailed}.";
    }

    /// Returns the member that satisfies a requirement (protocol/lip-workspace.md, "Valid states":
    /// a member that is one of the requirement's candidates, or stands in for one), or null. See
    /// the class remarks for how this avoids loading every candidate.
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

      if (requirement.Ref is { Kind: RefKind.Spec, GitRef: null }) {
        foreach (Member member in _members.Values) {
          if (await IsPinnedSatisfier(requirement, choices, member.Candidate)) {
            return member.Candidate;
          }
        }
      }

      if (choices.Complete) {
        return null;
      }

      // Only remote repositories can have long candidate sequences; load the others completely.
      if (requirement.Ref.Kind != RefKind.Spec) {
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

    /// The first preferred candidate that satisfies a requirement: one of its candidates, or one
    /// that stands in for one. Like SatisfyingMember, only preferred candidates named like a
    /// loaded candidate are looked up.
    private async Task<Candidate?> PreferredCandidate(Requirement requirement, Choices choices) {
      if (preferred.Count == 0) {
        return null;
      }

      foreach (Candidate candidate in preferred) {
        if (await IsPinnedSatisfier(requirement, choices, candidate)) {
          return candidate;
        }
      }

      if (!await choices.Has(0)) {
        return null;
      }

      HashSet<string> identities = [.. choices.CandidateKeys.Select(k => k.Identity)];
      foreach (Candidate candidate in preferred) {
        PackageKey key = PackageKey.Of(candidate);
        if (identities.Contains(key.Identity)
            && await CandidateWithKey(requirement, key) is not null) {
          return candidate;
        }
      }

      return null;
    }

    /// The member that is one of the loaded-on-demand candidates, walking all of them.
    private async Task<Candidate?> MemberAmongCandidates(Choices choices) {
      for (int i = 0; await choices.Has(i); i++) {
        if (_members.TryGetValue(choices.CandidateKeys[i].Identity, out Member? member)
            && member.Key == choices.CandidateKeys[i]) {
          return member.Candidate;
        }
      }

      return null;
    }

    /// Whether a candidate is a package of the server (protocol/lip-package.md, "The server").
    private static bool IsServer(Candidate candidate) => BedrockServer.IsServer(candidate.Source);

    /// The mod name of a candidate, or null for a file package and for a mod whose mod manifest
    /// cannot be read (which installing it reports).
    private static async Task<string?> ModName(Candidate candidate) {
      try {
        return (await candidate.GetModManifest())?.Name;
      }
      catch (McmodInstallException) {
        return null;
      }
    }

    private static bool IsPinned(Candidate candidate) {
      return candidate.Source is { Kind: RefKind.Spec, GitRef: not null };
    }

    /// Whether a candidate resolved from a remote repository with a ref stands in for a
    /// requirement without a ref (protocol/lip-workspace.md, "Valid states"): same repository
    /// (ignoring ASCII case) and variant, a version in the requirement's range, and a candidate of
    /// the requirement to stand in for, of which only the first is loaded. A package of a ref thus
    /// stands in for the tag versions of its repository, while a requirement with a ref still needs
    /// exactly its own candidates.
    private static async ValueTask<bool> IsPinnedSatisfier(
        Requirement requirement, Choices choices, Candidate candidate) {
      if (candidate.Source is not { Kind: RefKind.Spec, GitRef: not null } pinned
          || requirement.Ref is not { Kind: RefKind.Spec, GitRef: null } wanted) {
        return false;
      }

      if (requirement.Range is not null
          && !requirement.Range.IsSatisfiedBy(candidate.Package.Version)) {
        return false;
      }

      return PackageNames.AreEqual(wanted.Target, pinned.Target)
          && wanted.Variant == candidate.Package.Variant
          && await choices.Has(0);
    }

    /// Whether a member is one of the candidates of a requirement, without enumerating the
    /// candidates of other versions.
    private async Task<bool> IsCandidate(Requirement requirement, Member member) =>
        await CandidateWithKey(requirement, member.Key) is not null;

    /// The candidate of a requirement with the identity, version and commit of key, or null,
    /// without enumerating the candidates of other versions.
    private async Task<Candidate?> CandidateWithKey(Requirement requirement, PackageKey key) {
      SemVersion version = key.Version;
      if (requirement.Range is not null && !requirement.Range.IsSatisfiedBy(version)) {
        return null;
      }

      LazySequence sequence = Resolve(
          requirement.Ref,
          requirement.Ref.Kind == RefKind.Spec
              ? new ExactVersionConstraint(version)
              : requirement.Range);
      for (int i = 0; await sequence.Has(i); i++) {
        if (key == PackageKey.Of(sequence.Items[i])) {
          return sequence.Items[i];
        }
      }

      return null;
    }

    private void AddEdges(Candidate owner, int level, bool installed) {
      foreach (PackageDependency dependency in owner.Package.Entry.Dependencies) {
        _agenda.Add(new Requirement(
            ResolveKey.Of(dependency.Source, dependency.Range),
            owner,
            $"{Show(owner)}{(installed ? " (installed)" : "")} requires"
                + $" {ShowRequirement(dependency.Source, dependency.Range)}",
            level,
            dependency.Source,
            dependency.Range,
            OwnerInstalled: installed));
      }
    }

    private Task<Choices> GetChoices(Requirement requirement) {
      if (!_choices.TryGetValue(requirement.Key, out Choices? choices)) {
        choices = new Choices(Resolve(requirement.Ref, requirement.Range));
        _choices[requirement.Key] = choices;
      }

      return Task.FromResult(choices);
    }

    private LazySequence Resolve(Ref reference, IVersionConstraint? range) {
      ResolveKey key = ResolveKey.Of(reference, range);
      if (!_resolved.TryGetValue(key, out LazySequence? sequence)) {
        // The resolver of lip also hands over the exceptions behind its diagnostics.
        sequence = new LazySequence(s => resolver is ICausalResolver withCauses
            ? withCauses.ResolveLazyWithCauses(
                reference, range, environment, s.AddDiagnostic)
            : resolver.ResolveLazy(reference, range, environment, s.AddDiagnostic));
        _resolved[key] = sequence;
      }

      return sequence;
    }

    /// Records why a requirement could not be satisfied: in its conflict (by requirer and
    /// target), and as the last cause when it has one.
    private void RecordFailure(
        Requirement requirement,
        Choices choices,
        List<(Candidate Candidate, Obstacle Obstacle)> blocked,
        int failedDeeper) {
      Conflict? conflict = ConflictOf(requirement);
      if (choices.Candidates.Count == 0) {
        List<ResolveDiagnostic> diagnostics = choices.Sequence.Diagnostics;
        foreach (ResolveDiagnostic diagnostic in diagnostics) {
          if (conflict is not null && !conflict.Diagnostics.Contains(diagnostic.Message)) {
            conflict.Diagnostics.Add(diagnostic.Message);
          }

          if (diagnostic.Cause is Exception cause) {
            AddCause(cause);
          }
        }

        if (conflict is not null) {
          conflict.Unavailable = true;
        }

        NoteCause($"{Subject(requirement)}, but no version of it is available");
        TraceStep($"{requirement.Description}, but no version of it is available");
        return;
      }

      List<Obstacle> obstacles = [.. blocked.Select(b => b.Obstacle)];
      if (blocked.Count == 0 && failedDeeper == 0) {
        // Skipped: the candidates were already found unsatisfiable for another owner.
        obstacles.Add(new Obstacle("none of its versions can be installed", Primary: false));
      } else if (failedDeeper > 0) {
        obstacles.Add(new Obstacle(
            "each version of it that fits needs packages that cannot be installed with the"
                + " others",
            Primary: false));
      }

      if (conflict is not null) {
        foreach ((Candidate candidate, Obstacle obstacle) in blocked) {
          if (obstacle.Clash == Clash.Requested
              && (conflict.NewestBlocked is not SemVersion newest
                  || candidate.Package.Version.ComparePrecedenceTo(newest) > 0)) {
            conflict.NewestBlocked = candidate.Package.Version;
          }
        }

        foreach (Obstacle obstacle in obstacles) {
          if (!conflict.Obstacles.Any(o => o.Text == obstacle.Text)) {
            conflict.Obstacles.Add(obstacle);
          }
        }
      }

      List<string> causes = [
        .. obstacles.Where(o => o.Primary).Select(o => o.Text).Distinct().Take(MaxListedReasons),
      ];
      if (causes.Count > 0) {
        NoteCause($"{Subject(requirement)}, but {string.Join(", and ", causes)}");
      }

      TraceStep($"{requirement.Description}, but no version of it fits: "
          + string.Join("; ", obstacles.Select(o => o.Text).Distinct().Take(MaxListedReasons)));
    }

    /// The conflict of a requirement, with the version of its owner and its range added; null
    /// once too many conflicts are recorded.
    private Conflict? ConflictOf(Requirement requirement) {
      string requirer = requirement.Owner is Candidate owner ? PackageDisplay.Name(owner) : "";
      string target = PackageDisplay.Name(requirement.Ref);
      // Installed and other versions of a package are told apart.
      (string, string) key = (requirement.OwnerInstalled ? $"{requirer} (installed)" : requirer,
          target);
      if (!_conflicts.TryGetValue(key, out Conflict? conflict)) {
        if (_conflicts.Count >= MaxRecordedFailures) {
          // The most recent failures are closest to the requests that finally failed, and the
          // missing packages are usually the root cause.
          int oldest = _conflictOrder.FindIndex(k => !_conflicts[k].Unavailable);
          if (oldest < 0) {
            return null;
          }

          _conflicts.Remove(_conflictOrder[oldest]);
          _conflictOrder.RemoveAt(oldest);
        }

        conflict = new Conflict(requirer, requirement.OwnerInstalled, target, requirement.Ref);
        _conflicts.Add(key, conflict);
        _conflictOrder.Add(key);
      }

      if (requirement.Owner is Candidate dependent) {
        AddDistinct(conflict.RequirerVersions, dependent.Package.Version.ToString());
      }

      if (requirement.Range is not null || requirement.Owner is not null) {
        AddDistinct(conflict.Ranges, RangeText(requirement.Range));
      }

      return conflict;
    }

    /// A requirement for one version of its owner, such as "X 1.0.0 needs Y >=2.0.0".
    private static string Subject(Requirement requirement) {
      string target = PackageDisplay.Name(requirement.Ref);
      return requirement.Owner is Candidate owner
          ? $"{PackageDisplay.NameAndVersion(owner)}"
              + $"{(requirement.OwnerInstalled ? " (installed)" : "")} needs {target}"
              + $" {RangeText(requirement.Range)}"
          : $"You requested {target}"
              + (requirement.Range is null ? "" : $" {RangeText(requirement.Range)}");
    }

    private static string RangeText(IVersionConstraint? range) =>
        range is null ? "(any version)" : $"{range}";

    private static void AddDistinct(List<string> list, string item) {
      if (!list.Contains(item)) {
        list.Add(item);
      }
    }

    /// Notes a cause of a failure, as one line.
    private void NoteCause(string line) {
      _lastCause = line;
      _causeCount++;
    }

    /// Keeps an exception behind a failure, once.
    private void AddCause(Exception cause) {
      if (_causes.Count < MaxReportedFailures && !_causes.Contains(cause)) {
        _causes.Add(cause);
      }
    }

    /// The exception telling why no package set was found: the conflicts with a cause (or, when
    /// none has one, every conflict), those of packages that are not available first, as they
    /// are usually the root cause; with what the user can do about them.
    private DependencyResolutionException Failure() {
      List<Conflict> conflicts = [.. _conflictOrder.Select(k => _conflicts[k])];
      if (conflicts.Any(c => c.HasPrimary)) {
        conflicts = [.. conflicts.Where(c => c.HasPrimary)];
      }

      conflicts = [.. conflicts.Where(c => c.Unavailable), .. conflicts.Where(c => !c.Unavailable)];
      List<string> reasons = [.. conflicts.Select(Render)];
      if (reasons.Count == 0) {
        reasons.Add("Every combination of versions has two versions of one package, or two mods"
            + " of the same mod name.");
      }

      List<string> hints = [.. conflicts.SelectMany(Hints).Distinct().Take(MaxHints)];
      RunTrace.Write("resolve", $"No package set found; {reasons.Count} reasons recorded");
      // The exceptions behind the reasons (e.g. network failures) are kept as inner exceptions,
      // so the failure report and the hints see them.
      return new DependencyResolutionException(
          "Cannot find versions of the packages that work together:"
              + string.Concat(reasons.Take(MaxReportedFailures).Select(r => $"\n  - {r}"))
              + (reasons.Count > MaxReportedFailures
                  ? $"\n  ... and {reasons.Count - MaxReportedFailures} more reasons"
                  : ""),
          _causes.Count switch {
            0 => null,
            1 => _causes[0],
            _ => new AggregateException("The failures behind the reasons above.", _causes),
          },
          reasons,
          hints);
    }

    /// A conflict as one reason, such as "X 1.0.0 and 1.1.0 need Y 2.0.0, but Y 3.0.0 was
    /// requested."
    private static string Render(Conflict conflict) {
      string subject;
      if (conflict.Requirer.Length == 0) {
        subject = $"You requested {conflict.Target}"
            + (conflict.Ranges.Count == 0 ? "" : $" {string.Join(" or ", conflict.Ranges)}");
      } else {
        List<string> versions = conflict.RequirerVersions;
        string listed = versions.Count <= MaxListedVersions
            ? JoinAnd(versions)
            : $"{string.Join(", ", versions.Take(MaxListedVersions))} and"
                + $" {versions.Count - MaxListedVersions} more versions";
        subject = $"{conflict.Requirer} {listed}{(conflict.Installed ? " (installed)" : "")}"
            + $" {(versions.Count == 1 ? "needs" : "need")} {conflict.Target}"
            + $" {string.Join(" or ", conflict.Ranges)}";
      }

      if (conflict.Unavailable) {
        List<string> diagnostics = conflict.Diagnostics;
        return $"{subject}, but no version of it is available"
            + (diagnostics.Count == 0 ? "." : ":")
            + Bullets(diagnostics);
      }

      List<Obstacle> obstacles = [.. conflict.Obstacles.Where(o => o.Primary)];
      if (obstacles.Count == 0) {
        obstacles = [.. conflict.Obstacles.Take(1)];
      }

      if (obstacles.Count == 1 && !obstacles[0].Sentence) {
        return $"{subject}, but {obstacles[0].Text}.";
      }

      return $"{subject}, but no version of it can be installed:"
          + Bullets([.. obstacles.Select(o => o.Text)]);
    }

    private static string Bullets(List<string> items) =>
        string.Concat(items.Take(MaxListedReasons).Select(d => $"\n      - {d}"))
        + (items.Count > MaxListedReasons
            ? $"\n      ... and {items.Count - MaxListedReasons} more"
            : "");

    private static string JoinAnd(List<string> items) => items.Count <= 1
        ? string.Join("", items)
        : $"{string.Join(", ", items.SkipLast(1))} and {items[^1]}";

    /// What the user can do about a conflict.
    private static IEnumerable<string> Hints(Conflict conflict) {
      string requirer = conflict.Requirer;
      string target = conflict.Target;
      foreach (Obstacle obstacle in conflict.Obstacles.Where(o => o.Primary)) {
        switch (obstacle.Clash) {
          case Clash.Requested when requirer.Length > 0:
            string example = conflict.NewestBlocked is SemVersion version
                && conflict.TargetRef is { Kind: RefKind.Spec, GitRef: null }
                    ? $" (such as {target}@{version}, or {target} without a version to let lip"
                        + " choose one)"
                    : "";
            yield return $"To install {requirer}, request a version of {target} that it"
                + $" supports{example}. Otherwise, do not install {requirer}.";
            break;
          case Clash.Installed:
            yield return requirer.Length == 0
                ? $"{obstacle.Other} is installed. Request a version that fits it, or uninstall"
                    + " it first."
                : $"{obstacle.Other} is installed, but {requirer} needs another version of it."
                    + $" Install a version of {requirer} that works with it, or uninstall"
                    + $" {target} first.";
            break;
          case Clash.Needed:
            yield return requirer.Length == 0
                ? $"{obstacle.Other} needs another version of {target}. Request a version"
                    + $" that it supports, or do not install {obstacle.Other}."
                : $"{requirer} and {obstacle.Other} need different versions of {target}, so"
                    + " they cannot be installed together. Install only one of them, or"
                    + $" versions of them that work with the same version of {target}.";
            break;
          case Clash.Server:
            yield return $"A workspace holds one variant of the server only: install {target}"
                + $" or {obstacle.Other}, not both.";
            break;
          case Clash.ModName:
            yield return $"{target} and {obstacle.Other} are mods of the same mod name, so only"
                + " one of them can be installed.";
            break;
        }
      }
    }

    private static DependencyResolutionException Fail(string message) {
      return new DependencyResolutionException(message);
    }

    private static string Show(Candidate candidate) {
      return $"{candidate.Package.Identity}@{candidate.Package.Version}";
    }

    /// A candidate with where it comes from, to tell apart packages of one identity.
    private static string ShowSource(Candidate candidate) {
      return $"{Show(candidate)} (from {candidate.Archive.Key})";
    }

    /// A source with the versions a requirement accepts.
    private static string ShowRequirement(Ref source, IVersionConstraint? range) {
      return range is null ? $"{source} (any version)" : $"{source} {range}";
    }
  }
}
