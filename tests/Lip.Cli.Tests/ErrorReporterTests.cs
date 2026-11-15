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
  public void Hints_RepositoryNotFound_SuggestsCheckingTheName() {
    // Arrange
    HttpRequestException notFound = new("404", null, HttpStatusCode.NotFound);

    // Act
    IReadOnlyList<string> hints = ErrorReporter.Hints(
        new ArchiveUnavailableException("Could not download.", notFound));

    // Assert
    Assert.Contains("Check the package name", Assert.Single(hints));
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
