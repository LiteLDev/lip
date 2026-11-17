using System.Net;
using System.Text.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Install;
using Lip.Core.Sources;
using Moq;
using Semver;

namespace Lip.Cli.Tests;

/// What lip tells the user to do about a failure (protocol/lip.md, EXIT STATUS).
public class ErrorReporterTests {
  [Fact]
  public void Hints_DependencyResolutionWithHints_AreThoseHints() {
    // Arrange
    DependencyResolutionException exception = new(
        "Cannot find versions of the packages that work together:\n  - reason",
        reasons: ["reason"],
        hints: ["Do not install X."]);

    // Act
    IReadOnlyList<string> hints = ErrorReporter.Hints(exception);

    // Assert
    Assert.Equal(["Do not install X."], hints);
  }

  [Fact]
  public void Hints_DependencyResolutionWithoutHints_SuggestsOtherVersions() {
    // Act
    IReadOnlyList<string> hints = ErrorReporter.Hints(new DependencyResolutionException("x"));

    // Assert
    Assert.Equal(
        ["Request other versions of the packages named above, or install fewer of them together."],
        hints);
  }

  [Fact]
  public void Hints_AssetNotFound_SaysThePackageFileIsMissingAndWhereToReportIt() {
    // Arrange: the release of a published mod has no file its package file names.
    const string Url = "https://github.com/CoralFans-Dev/CoralFans/releases/download/v1.0.0/x.zip";
    HttpRequestException notFound = new(
        $"Could not download {Url}: the server answered HTTP 404 (Not Found).",
        null,
        HttpStatusCode.NotFound);
    AssetUnavailableException exception = new(
        "Cannot install github.com/CoralFans-Dev/CoralFans 1.0.0: lip could not obtain asset 0.",
        new ArchiveUnavailableException($"Could not download {Url}.", notFound),
        Candidate("github.com/CoralFans-Dev/CoralFans", "1.0.0"));

    // Act
    IReadOnlyList<string> hints = ErrorReporter.Hints(exception);

    // Assert
    string hint = Assert.Single(hints);
    Assert.StartsWith("A file that github.com/CoralFans-Dev/CoralFans 1.0.0 publishes is missing",
        hint);
    Assert.Contains("https://github.com/CoralFans-Dev/CoralFans/issues", hint);
    Assert.Contains("'github.com/CoralFans-Dev/CoralFans@<version>'", hint);
  }

  [Fact]
  public void Hints_CertificateNotVerified_SaysSoInsteadOfTheNetwork() {
    // Arrange: as without CA certificates, in a minimal Docker image.
    HttpRequestException tls = new(
        "The SSL connection could not be established, see inner exception.",
        new System.Security.Authentication.AuthenticationException(
            "The remote certificate is invalid because of errors in the certificate chain:"
            + " PartialChain"));

    // Act
    IReadOnlyList<string> hints = ErrorReporter.Hints(
        new ArchiveUnavailableException("Could not download.", tls));

    // Assert
    string hint = Assert.Single(hints);
    Assert.StartsWith("lip could not verify the certificate of the server.", hint);
    Assert.Equal(OperatingSystem.IsLinux(), hint.Contains("ca-certificates"));
  }

  [Fact]
  public void Hints_RepositoryNotFound_SuggestsCheckingTheName() {
    // Arrange
    HttpRequestException notFound = new("404", null, HttpStatusCode.NotFound);

    // Act
    IReadOnlyList<string> hints = ErrorReporter.Hints(
        new ArchiveUnavailableException("Could not download.", notFound));

    // Assert
    Assert.Contains("Check the package name", Assert.Single(hints));
  }

  /// A failure report holds the journal of the workspace, which says what an operation was
  /// doing, and names the other entries of .lip without their content: the protocol defines
  /// no file there whose name starts with "workspace." (protocol/lip-workspace.md, "The .lip
  /// directory").
  [Fact]
  public void Describe_TheReport_HoldsTheJournal_AndNoOtherFileOfTheLipDirectory() {
    // Arrange
    string workspace = Directory.CreateTempSubdirectory("lip-report-test-").FullName;
    try {
      Directory.CreateDirectory(Path.Join(workspace, ".lip"));
      File.WriteAllText(Path.Join(workspace, ".lip", "journal"), "JOURNAL-MARK");
      File.WriteAllText(Path.Join(workspace, ".lip", "workspace.json"), "LEFTOVER-MARK");
      File.WriteAllText(Path.Join(workspace, ".lip", "state.json"), "STATE-MARK");

      // Act
      Failure failure = ErrorReporter.Describe(
          new InvalidOperationException("It broke."), ["install", "x"], cancelled: false,
          workspace);

      // Assert
      string report = File.ReadAllText(Path.Join(workspace, failure.ReportPath));
      Assert.Contains("--- journal ---", report);
      Assert.Contains("JOURNAL-MARK", report);
      Assert.Contains("workspace.json (13 bytes)", report);
      Assert.DoesNotContain("--- workspace.json ---", report);
      Assert.DoesNotContain("LEFTOVER-MARK", report);
      Assert.DoesNotContain("STATE-MARK", report);
    }
    finally {
      Directory.Delete(workspace, recursive: true);
    }
  }

  /// A failure is described the same way whoever writes its report: the message of the
  /// failure, then those of its causes, the hints, and the path that the writer gives. A
  /// command that the user cancelled has no report.
  [Fact]
  public void Describe_WithAWriterOfTheReport_SummarizesTheCauses_AndWritesNothingItself() {
    // Arrange
    InvalidOperationException failure = new(
        "Cannot migrate tooth.json.",
        new FormatException("The version is missing.", new IOException("Disk error.")));
    List<(string Summary, IReadOnlyList<string> Hints)> written = [];

    // Act
    Failure described = ErrorReporter.Describe(failure, cancelled: false, (summary, hints) => {
      written.Add((summary, hints));
      return "report.log";
    });
    Failure cancelled = ErrorReporter.Describe(
        new OperationCanceledException(), cancelled: true, (_, _) => {
          written.Add(("", []));
          return "never.log";
        });

    // Assert
    Assert.Equal(
        "Cannot migrate tooth.json.\n  caused by: The version is missing."
            + "\n  caused by: Disk error.",
        described.Summary);
    Assert.Equal(
        ("report.log", null, false),
        (described.ReportPath, described.ReportError, described.Cancelled));
    (string summary, IReadOnlyList<string> hints) = Assert.Single(written);
    Assert.Equal((described.Summary, described.Hints), (summary, hints));
    Assert.True(cancelled.Cancelled);
    Assert.Null(cancelled.ReportPath);
  }

  /// The interface reports the failure of a command below the workspace that is selected in
  /// it, not below the directory that lip was started in.
  [Fact]
  public void Describe_WithAWorkspace_WritesTheReportBelowIt() {
    // Arrange
    string workspace = Directory.CreateTempSubdirectory("lip-report-test-").FullName;
    try {
      // Act
      Failure failure = ErrorReporter.Describe(
          new InvalidOperationException("It broke."), ["(interface)", "Install"],
          cancelled: false, workspace);

      // Assert
      Assert.Contains("It broke.", failure.Summary);
      Assert.Null(failure.ReportError);
      Assert.StartsWith(Path.Join(".lip", "logs") + Path.DirectorySeparatorChar, failure.ReportPath);
      string report = File.ReadAllText(Path.Join(workspace, failure.ReportPath));
      Assert.Contains("Install", report);
      Assert.Contains(workspace, report);
      Assert.False(Directory.Exists(Path.Join(Environment.CurrentDirectory, ".lip", "logs"))
          && Directory.GetFiles(Path.Join(Environment.CurrentDirectory, ".lip", "logs"))
              .Any(f => Path.GetFileName(f) == Path.GetFileName(failure.ReportPath)));
    }
    finally {
      Directory.Delete(workspace, recursive: true);
    }
  }

  private static Candidate Candidate(string id, string version) {
    PackageEntry entry = new() {
      Side = McmodSide.Server,
      Platform = "win-x64",
      Dependencies = [],
      Assets = [],
      Json = JsonDocument.Parse("{}").RootElement.Clone(),
    };
    Package package = new(id, "", SemVersion.Parse(version), entry) {
      Manifest = JsonDocument.Parse("{}").RootElement.Clone(),
    };
    Ref source = Ref.Parse(id, allowFile: false);
    return Lip.Core.Mcmod.Candidate.Create(
        package,
        new ArchiveHandle($"{id}@{version}", Mock.Of<ISource>(), null),
        source,
        Mock.Of<IArchiveProvider>());
  }
}
