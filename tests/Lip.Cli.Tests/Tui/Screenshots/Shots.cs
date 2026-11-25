using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Lip.Cli.Tests.Tui.Screenshots;
using Lip.Cli.Tui.I18n;
using Lip.Core.Infrastructure;
using Xunit.v3;

[assembly: AssemblyFixture(typeof(ShotRun))]

namespace Lip.Cli.Tests.Tui.Screenshots;

/// What the scenarios of the screenshots do in a run of the tests.
public enum ShotMode {
  /// They are skipped.
  Off,

  /// They write the screenshots.
  Write,

  /// They fail where a screenshot is not what the file of it holds.
  Check,
}

/// A scenario of the screenshots of the documentation: a theory that is given each language
/// in turn (AllLanguages), in the category DocsScreenshots, and skipped unless the environment
/// variable LIP_SCREENSHOTS is "write" or "check". How it ended is told to the run.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ShotTheoryAttribute
    : TheoryAttribute, ITraitAttribute, IBeforeAfterTestAttribute {
  public const string Category = "DocsScreenshots";

  public ShotTheoryAttribute(
      [CallerFilePath] string? sourceFilePath = null,
      [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber) {
    Skip = ShotRun.SkipReason;
  }

  public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
      [new KeyValuePair<string, string>("Category", Category)];

  public void Before(MethodInfo methodUnderTest, IXunitTest test) {
  }

  public void After(MethodInfo methodUnderTest, IXunitTest test) =>
      ShotRun.Current?.Ended(
          test.UniqueID, TestContext.Current.TestState?.Result == TestResult.Passed);
}

/// A test of the screenshots that is no scenario: in the same category, and skipped as the
/// scenarios are.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ShotFactAttribute : FactAttribute, ITraitAttribute {
  public ShotFactAttribute(
      [CallerFilePath] string? sourceFilePath = null,
      [CallerLineNumber] int sourceLineNumber = -1) : base(sourceFilePath, sourceLineNumber) {
    Skip = ShotRun.SkipReason;
  }

  public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
      [new KeyValuePair<string, string>("Category", ShotTheoryAttribute.Category)];
}

/// The languages of the interface, as the data of a scenario.
public sealed class AllLanguages : TheoryData<Language> {
  public AllLanguages() {
    foreach (Language language in Languages.All) {
      Add(language);
    }
  }
}

/// One run of the scenarios: where the screenshots go, and which were made. When every
/// scenario ran and passed, the files that no scenario made are deleted (write) or fail the
/// run (check, ShotLeftovers).
public sealed class ShotRun : IDisposable {
  public const string ModeVariable = "LIP_SCREENSHOTS";
  public const string DirectoryVariable = "LIP_SCREENSHOTS_DIR";

  private static readonly int _scenarioCount = typeof(ShotRun).Assembly.GetTypes()
      .SelectMany(type => type.GetMethods())
      .Count(method => method.GetCustomAttribute<ShotTheoryAttribute>() is not null);

  private readonly ConcurrentDictionary<string, string> _made =
      new(StringComparer.OrdinalIgnoreCase);
  // The scenarios that ended, each in a language, and whether they passed.
  private readonly ConcurrentDictionary<string, bool> _ended = new();
  private readonly TaskCompletionSource _allEnded =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

  public ShotRun() {
    Current = this;
  }

  /// The run of the tests that run now.
  public static ShotRun? Current { get; private set; }

  /// What the environment asks for.
  public static ShotMode Mode =>
      Environment.GetEnvironmentVariable(ModeVariable)?.ToLowerInvariant() switch {
        "write" => ShotMode.Write,
        "check" => ShotMode.Check,
        _ => ShotMode.Off,
      };

  /// Why a test of the screenshots is skipped, or null when it is not.
  public static string? SkipReason =>
      Mode == ShotMode.Off
          ? $"Screenshots are made with {ModeVariable}=write, and compared with"
              + $" {ModeVariable}=check."
          : null;

  /// The directory of the screenshots: that of LIP_SCREENSHOTS_DIR, or
  /// content/lip/public/screenshots of a checkout of LiteLDev/liteldev.github.io (the
  /// documentation) named liteldev.github.io next to the repository that the tests were built from.
  public static string OutputDirectory {
    get {
      if (Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } given) {
        return Path.GetFullPath(given);
      }

      for (DirectoryInfo? directory = new(System.AppContext.BaseDirectory); directory is not null;
          directory = directory.Parent) {
        if (File.Exists(Path.Join(directory.FullName, "lip.slnx"))) {
          string docs = Path.Join(directory.Parent?.FullName ?? directory.FullName,
              "liteldev.github.io", "content", "lip");
          if (Directory.Exists(docs)) {
            return Path.Join(docs, "public", "screenshots");
          }

          throw new InvalidOperationException(
              $"The documentation was not found at {docs}: set {DirectoryVariable} to"
                  + " content/lip/public/screenshots of a checkout of"
                  + " LiteLDev/liteldev.github.io.");
        }
      }

      throw new InvalidOperationException(
          $"The repository was not found above the tests: set {DirectoryVariable}.");
    }
  }

  /// The file of a screenshot.
  public static string FileOf(Language language, string name) =>
      Path.Join(OutputDirectory, Languages.Code(language), name + ".svg");

  /// Completes once every scenario of the tests has ended in every language.
  public Task AllEnded => _allEnded.Task;

  /// Whether every scenario of the tests ran in every language, and none failed or was
  /// skipped: the screenshots that were made are then all there are.
  public bool AllPassed =>
      _ended.Count == _scenarioCount * Languages.All.Count && _ended.Values.All(passed => passed);

  /// Starts the interface for a scenario in the world given.
  public Shot Start(World world) => new(this, world);

  /// A scenario, in a language, ended.
  internal void Ended(string test, bool passed) {
    _ended[test] = passed;
    if (_ended.Count == _scenarioCount * Languages.All.Count) {
      _allEnded.TrySetResult();
    }
  }

  /// The screenshots of the directory that no scenario of this run made: the pictures in the
  /// folders of the languages, and nothing else that the directory may hold.
  public List<string> Leftovers() => [
    .. Languages.All.Select(language => Path.Join(OutputDirectory, Languages.Code(language)))
        .Where(Directory.Exists)
        .SelectMany(directory => Directory.EnumerateFiles(directory, "*.svg"))
        .Where(file => !_made.ContainsKey(file)).Order(StringComparer.Ordinal),
  ];

  /// Writes a screenshot, or compares it with its file.
  internal void Made(Language language, string name, string svg) {
    string file = FileOf(language, name);
    if (!_made.TryAdd(file, name)) {
      Assert.Fail($"Two screenshots are named {name}.");
    }

    if (Mode == ShotMode.Write) {
      Directory.CreateDirectory(Path.GetDirectoryName(file)!);
      // Written only when it changed, so that a run that changes nothing touches nothing.
      if (!File.Exists(file) || Normalize(File.ReadAllText(file)) != svg) {
        File.WriteAllText(file, svg, new UTF8Encoding(false));
      }
    } else if (Mode == ShotMode.Check) {
      Assert.True(
          File.Exists(file), $"{file} is missing: make it with {ModeVariable}=write.");
      Assert.True(
          Normalize(File.ReadAllText(file)) == svg,
          $"{file} is not what the interface shows now: make it again with"
              + $" {ModeVariable}=write, and look at what changed.");
    }
  }

  public void Dispose() {
    // A file that no scenario makes any more is deleted, once all of them made theirs.
    if (Mode == ShotMode.Write && AllPassed) {
      Leftovers().ForEach(File.Delete);
    }

    Current = null;
  }

  /// A file as it was written, whatever git made of the ends of its lines.
  private static string Normalize(string text) => text.Replace("\r\n", "\n");
}

/// A scenario that runs: the interface, driven through Tui, and the screenshots of it.
public sealed class Shot : IDisposable {
  private static readonly Regex _name = new("^[a-z0-9]+(-[a-z0-9]+)*$");
  // The time since the server started, which no two runs agree on.
  private static readonly Regex _uptime = new(@"· \d\d:\d\d:\d\d");
  // A path of the file system of the tests where it is not that of Windows: "/D/servers".
  private static readonly Regex _path = new(@"(?<![\w./\\-])/([A-Z])((?:/[^\s/│]*)*)");

  private readonly ShotRun _run;
  private readonly List<(Regex Pattern, string Replacement)> _scrubs = [];

  internal Shot(ShotRun run, World world) {
    _run = run;
    World = world;
    Tui = TuiHarness.Start(world.Options());
  }

  /// The interface. Press its keys and wait for its screen as a test does.
  public TuiHarness Tui { get; }

  public World World { get; }

  public Language Language => World.Language;

  /// The title of the window of the screenshots.
  public string Title { get; set; } = "lip";

  /// A text in the language of the scenario, for the labels of annotations.
  public string T(string en, string zhHans, string zhHant) =>
      Language switch {
        Language.ZhHans => zhHans,
        Language.ZhHant => zhHant,
        _ => en,
      };

  /// From now on, what matches the pattern in a row of a frame is replaced before the frame
  /// is drawn, for what differs from run to run, such as a spinner (Frame.Replace). The
  /// replacement may name the groups of the pattern ("$1").
  public Shot Scrub(string pattern, string replacement) {
    _scrubs.Add((new Regex(pattern), replacement));
    return this;
  }

  /// Takes a screenshot of what the interface shows once it has handled the keys pressed so
  /// far, with the annotations above it, as the file name.svg of the language. Wait for
  /// what runs in the background (Tui.WaitFor) before the call.
  public async Task Capture(string name, params Note[] notes) {
    Assert.True(_name.IsMatch(name), $"'{name}' is not a name like home-01-empty.");
    await Tui.Settle();
    (string[] rows, string text) = await Tui.Invoke(
        () => (Tui.App.ScreenAnsi.ToArray(), Tui.App.ScreenText));
    Frame frame = Frame.Parse(rows);
    Assert.Equal(text, frame.Text);

    // What no world of a scenario decides: the time, and the directories of this user.
    frame = frame.Replace(_uptime, _ => "· 00:12:34");
    frame = frame.Replace(
        new Regex(Regex.Escape(LipDirectories.Cache)),
        _ => World.PathOf(@"C:\Users\Steve\AppData\Local\lip\Cache"));
    foreach ((Regex pattern, string replacement) in _scrubs) {
      frame = frame.Replace(pattern, match => match.Result(replacement));
    }

    if (!OperatingSystem.IsWindows()) {
      frame = frame.Replace(
          _path, match => $"{match.Groups[1].Value}:{match.Groups[2].Value.Replace('/', '\\')}");
    }

    Tui.Snapshot(name);
    AssertNothingOfThisMachine(frame.Text);
    string svg = SvgRenderer.Render(frame, notes, new SvgOptions { Title = Title });
    _run.Made(Language, name, svg);
  }

  public void Dispose() => Tui.Dispose();

  /// A screenshot shows the world of the scenario, not the machine it was made on: neither
  /// the home directory of who runs the tests nor the directory of temporary files.
  private static void AssertNothingOfThisMachine(string text) {
    string[] directories = [
      Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
      Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ];
    foreach (string directory in directories.Where(directory => directory.Length >= 4)) {
      Assert.False(
          text.Contains(directory, StringComparison.OrdinalIgnoreCase),
          $"The frame shows '{directory}' of this machine:{Environment.NewLine}{text}");
    }
  }
}

/// In a check, the screenshots that no scenario makes any more fail the run.
public sealed class ShotLeftovers(ShotRun run) {
  [ShotFact]
  public async Task NoScreenshotIsLeftOver() {
    Assert.SkipUnless(ShotRun.Mode == ShotMode.Check, "A run that writes deletes them itself.");
    try {
      await run.AllEnded.WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
    }
    catch (TimeoutException) {
      Assert.Skip("Not every scenario ran, so nobody knows which screenshots are left over.");
    }

    Assert.SkipUnless(
        run.AllPassed, "A scenario failed or was skipped, and made no screenshots.");
    List<string> left = run.Leftovers();
    Assert.True(
        left.Count == 0,
        "No scenario makes these screenshots any more; delete them, or run with"
            + $" {ShotRun.ModeVariable}=write:{Environment.NewLine}"
            + string.Join(Environment.NewLine, left));
  }
}
