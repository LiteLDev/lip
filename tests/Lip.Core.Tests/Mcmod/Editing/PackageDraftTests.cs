using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lip.Core.Catalog;
using Lip.Core.Json;
using Lip.Core.Mcmod;
using Lip.Core.Mcmod.Editing;
using Semver;

namespace Lip.Core.Tests.Mcmod.Editing;

/// The package file editor: drafts, the text it writes, the ranges it offers, and what it
/// checks and predicts.
public class PackageDraftTests {
  private const string Commented = """
      # The package of the tests.
      format = 4
      id = "github.com/owner/repo" # the repository
      version = "1.0.0"
      custom = "kept"

      # People read this.
      [info]
      name = "Repo"
      extra = 1

      [[variants.""]]
      side = "server"
      platform = "win-x64"
      note = "unknown key"

      # The dependencies.
      [variants."".deps]
      "github.com/LiteLDev/LeviLamina" = "1.0.0"

      [[variants."".assets]]
      type = "self"

      [[variants.lua]]
      side = "server"
      platform = "win-x64"
      """;

  private static JsonObject Written(string toml) {
    using JsonDocument document = TomlJson.Parse(toml);
    return JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
  }

  private static PackageFileText File(string toml) => new("lip.toml", toml, Written(toml));

  [Fact]
  public void New_IsWhatLipPkgInitWrites() {
    JsonObject json = PackageDraft.New("github.com/owner/repo").ToJson();

    Assert.True(JsonNode.DeepEquals(
        JsonNode.Parse("""
            { "format": 4, "id": "github.com/owner/repo", "version": "0.1.0",
              "variants": { "": [ { "side": "server", "platform": "win-x64" } ] } }
            """),
        JsonNode.Parse(json.ToJsonString())));
    Assert.Null(PackageDraftChecks.Document(json));
  }

  [Fact]
  public void Read_ThenToJson_IsTheSameValue() {
    JsonObject written = Written(Commented);

    JsonObject json = PackageDraft.Read(written).ToJson(written);

    Assert.True(JsonNode.DeepEquals(written, json), json.ToJsonString());
  }

  [Fact]
  public void Read_KeyOfAnotherType_Throws() {
    Assert.Throws<PackageDraftException>(
        () => PackageDraft.Read(Written("format = 4\nversion = 1\nid = \"a.b/c\"")));
  }

  [Theory]
  [InlineData("format = 5
id = \"a.b/c\"
version = \"1.0.0\"")]
  [InlineData("format = \"4\"
id = \"a.b/c\"
version = \"1.0.0\"")]
  public void Read_OtherFormat_Throws(string toml) {
    Assert.Throws<PackageDraftException>(() => PackageDraft.Read(Written(toml)));
  }

  [Fact]
  public void ToJson_EmptyTablesThatWereThere_AreKept() {
    JsonObject written = Written("""
        format = 4
        id = "a.b/c"
        version = "1.0.0"

        [info]

        [[variants.""]]
        side = "server"
        platform = "win-x64"
        hooks = {}
        """);

    Assert.True(JsonNode.DeepEquals(written, PackageDraft.Read(written).ToJson(written)));
  }

  [Fact]
  public void Update_ChangedVersion_KeepsCommentsAndUnknownKeys() {
    PackageFileText file = File(Commented);
    PackageDraft draft = PackageDraft.Read(file.Written!);
    draft.Version = "1.1.0";

    PackageFileUpdate update = PackageFileStore.Update(file, draft);

    Assert.True(update.KeepsText);
    Assert.True(update.Changed);
    Assert.Equal(Commented.Replace("version = \"1.0.0\"", "version = \"1.1.0\""), update.Text);
  }

  [Fact]
  public void Update_Unchanged_IsTheTextAsItIs() {
    PackageFileText file = File(Commented);

    PackageFileUpdate update = PackageFileStore.Update(file, PackageDraft.Read(file.Written!));

    Assert.False(update.Changed);
    Assert.Equal(Commented, update.Text);
  }

  [Fact]
  public void Update_AddedAndRemovedValues_KeepsTheRest() {
    PackageFileText file = File(Commented);
    PackageDraft draft = PackageDraft.Read(file.Written!);
    draft.Description = "Does things";
    draft.Name = "";
    EntryDraft entry = draft.Variants[0].Entries[0];
    entry.Dependencies.Add(new DependencyDraft {
      Package = "github.com/acme/alpha",
      Range = ">=1.2.0 <1.3.0",
    });
    entry.Assets.Add(new AssetDraft { Type = "zip", Urls = ["https://example.com/a.zip"] });
    entry.Preserve.Add("config/");
    draft.Variants[1].Entries.Add(new EntryDraft { Platform = "linux-x64" });
    draft.Variants.Add(new VariantDraft {
      Name = "client",
      Entries = [new EntryDraft { Side = "client" }],
    });

    PackageFileUpdate update = PackageFileStore.Update(file, draft);

    Assert.True(update.KeepsText, update.Text);
    Assert.Contains("# The package of the tests.", update.Text);
    Assert.Contains("# The dependencies.", update.Text);
    Assert.Contains("custom = \"kept\"", update.Text);
    Assert.Contains("note = \"unknown key\"", update.Text);
    Assert.DoesNotContain("name = \"Repo\"", update.Text);
    Assert.True(JsonNode.DeepEquals(draft.ToJson(file.Written), Written(update.Text)), update.Text);
    Assert.Null(PackageDraftChecks.Document(Written(update.Text)));
  }

  [Fact]
  public void Update_RemovedEntryAndVariant_KeepsTheRest() {
    PackageFileText file = File(Commented);
    PackageDraft draft = PackageDraft.Read(file.Written!);
    draft.Variants.RemoveAt(1);
    draft.Variants[0].Entries[0].Assets.Clear();
    draft.Variants[0].Entries[0].Dependencies.Clear();

    PackageFileUpdate update = PackageFileStore.Update(file, draft);

    Assert.True(update.KeepsText, update.Text);
    Assert.DoesNotContain("lua", update.Text);
    Assert.DoesNotContain("LeviLamina", update.Text);
    Assert.Contains("# The package of the tests.", update.Text);
    Assert.True(JsonNode.DeepEquals(draft.ToJson(file.Written), Written(update.Text)), update.Text);
  }

  [Fact]
  public void Update_NewFile_IsWrittenWhole() {
    PackageFileUpdate update = PackageFileStore.Update(
        new PackageFileText("lip.toml", null, null), PackageDraft.New("github.com/a/b"));

    Assert.Equal(TomlJson.Serialize(PackageDraft.New("github.com/a/b").ToJson()), update.Text);
    Assert.True(update.KeepsText);
  }

  [Fact]
  public void Store_WritesAtomically_AndSeesChanges() {
    MockFileSystem fs = new();
    string root = OperatingSystem.IsWindows() ? @"C:\ws" : "/ws";
    fs.AddDirectory(root);
    PackageFileText file = PackageFileStore.Read(fs, root);
    Assert.False(file.Exists);
    Assert.True(PackageFileStore.Unchanged(fs, file));

    PackageFileStore.Write(fs, file, "format = 4\n");

    Assert.Equal("format = 4\n", fs.File.ReadAllText(file.Path));
    Assert.False(fs.File.Exists(file.Path + ".tmp"));
    Assert.False(PackageFileStore.Unchanged(fs, file));
    Assert.True(PackageFileStore.Unchanged(fs, PackageFileStore.Read(fs, root)));
  }

  [Theory]
  [InlineData(RangePreset.Patch, "1.4.2", ">=1.4.2 <1.5.0")]
  [InlineData(RangePreset.Patch, "0.4.2-beta.1", ">=0.4.2-beta.1 <0.5.0")]
  [InlineData(RangePreset.Minor, "1.4.2", "^1.4.2")]
  [InlineData(RangePreset.AtLeast, "1.4.2", ">=1.4.2")]
  [InlineData(RangePreset.Exact, "1.4.2", "1.4.2")]
  [InlineData(RangePreset.Any, "1.4.2", "*")]
  public void RangePresets_AreRanges(RangePreset preset, string version, string range) {
    SemVersion parsed = SemVersion.Parse(version);

    Assert.Equal(range, RangePresets.Of(preset, parsed));
    Assert.True(VersionRange.TryParse(range, out VersionRange? parsedRange));
    Assert.True(parsedRange!.IsSatisfiedBy(parsed));
    Assert.Equal(preset, RangePresets.Match(range, parsed));
  }

  [Fact]
  public void RangePresets_Patch_HasThePatchVersionsNotLower() {
    VersionRange range = VersionRange.Parse(
        RangePresets.Of(RangePreset.Patch, SemVersion.Parse("1.4.2")));

    Assert.True(range.IsSatisfiedBy(SemVersion.Parse("1.4.9")));
    Assert.False(range.IsSatisfiedBy(SemVersion.Parse("1.4.1")));
    Assert.False(range.IsSatisfiedBy(SemVersion.Parse("1.5.0")));
    Assert.Equal(SemVersion.Parse("1.4.2"), RangePresets.Start(">=1.4.2 <1.5.0"));
  }

  [Theory]
  [InlineData("", DraftProblemKind.Required)]
  [InlineData("not a path", DraftProblemKind.NotModulePath)]
  public void Checks_Id(string id, DraftProblemKind kind) =>
      Assert.Equal(kind, PackageDraftChecks.Id(id)?.Kind);

  [Theory]
  [InlineData("v1.0.0", DraftProblemKind.LeadingV)]
  [InlineData("1.0", DraftProblemKind.NotVersion)]
  [InlineData("", DraftProblemKind.Required)]
  public void Checks_Version(string version, DraftProblemKind kind) =>
      Assert.Equal(kind, PackageDraftChecks.Version(version)?.Kind);

  [Fact]
  public void Checks_ValidValues_HaveNoProblem() {
    Assert.Null(PackageDraftChecks.Id("github.com/owner/repo"));
    Assert.Null(PackageDraftChecks.Version("1.0.0-beta.1"));
    Assert.Null(PackageDraftChecks.Version("{{ x }}"));
    Assert.Null(PackageDraftChecks.Range(">=1.0.0 <2.0.0"));
    Assert.Null(PackageDraftChecks.Glob("config/**/*.json"));
    Assert.Null(PackageDraftChecks.Dest(new PlaceDraft { Src = "bin/", Dest = "plugins/" }));
    Assert.False(PackageDraftChecks.HasProblems(PackageDraft.Read(Written(Commented))));
  }

  [Fact]
  public void Checks_InvalidValues_SayWhy() {
    Assert.Equal(DraftProblemKind.NotRange, PackageDraftChecks.Range(">>1")?.Kind);
    Assert.Equal(DraftProblemKind.NotHttps, PackageDraftChecks.Logo("http://x")?.Kind);
    Assert.Equal(DraftProblemKind.NotGlob, PackageDraftChecks.Glob("/abs")?.Kind);
    Assert.Equal(
        DraftProblemKind.DestMustBeFile,
        PackageDraftChecks.Dest(new PlaceDraft { Src = "a.txt", Dest = "dir/" })?.Kind);
    Assert.Equal(
        DraftProblemKind.NotPath,
        PackageDraftChecks.Src(new PlaceDraft { Src = "../x", Dest = "" })?.Kind);
    EntryDraft a = new();
    EntryDraft b = new();
    Assert.Equal(DraftProblemKind.DuplicateEnvironment, PackageDraftChecks.Platform(a, [a, b])?.Kind);
    DependencyDraft x = new() { Package = "github.com/a/b", Range = "*" };
    DependencyDraft y = new() { Package = "github.com/A/B", Range = "*" };
    Assert.Equal(
        DraftProblemKind.DuplicateDependency, PackageDraftChecks.DependencyKey(x, [x, y])?.Kind);
    Assert.Equal(
        DraftProblemKind.NotDependencyKey,
        PackageDraftChecks.DependencyKey(new DependencyDraft { Package = "a@1" }, [])?.Kind);
    Assert.Equal(DraftProblemKind.NotVariant, PackageDraftChecks.VariantName("Bad", ["Bad"])?.Kind);
    Assert.NotNull(PackageDraftChecks.Document(Written("format = 4")));
  }

  private static CatalogDetail Detail(params (string Version, string? Loader)[] versions) =>
      new("github.com/acme/alpha", null, null, null, null,
          [
            .. versions.Select(v => new CatalogVersion(v.Version, false, null,
                [
                  new CatalogBuild("", "server", "win-x64",
                      v.Loader is null ? new Dictionary<string, string>()
                          : new Dictionary<string, string> {
                            [GameSetup.LoaderRepository] = v.Loader,
                          }),
                ])),
          ],
          null);

  [Fact]
  public void DependencyChecks_FindTheNewestVersionInTheRange() {
    PackageLookup lookup = PackageLookup.Of(Detail(("1.4.3", null), ("1.4.2", null), ("1.5.0", null)));

    RemoteCheck ok = DependencyChecks.Check(
        new DependencyDraft { Package = "github.com/acme/alpha", Range = ">=1.4.2 <1.5.0" }, lookup);
    RemoteCheck none = DependencyChecks.Check(
        new DependencyDraft { Package = "github.com/acme/alpha", Range = "2.0.0" }, lookup);
    RemoteCheck variant = DependencyChecks.Check(
        new DependencyDraft { Package = "github.com/acme/alpha#lua", Range = "*" }, lookup);
    RemoteCheck failed = DependencyChecks.Check(
        new DependencyDraft { Package = "github.com/acme/alpha", Range = "*" },
        PackageLookup.Failed("offline"));

    Assert.Equal(new RemoteCheck(RemoteStatus.Valid, RemoteReason.NewestInRange, "1.4.3"), ok);
    Assert.Equal(new RemoteCheck(RemoteStatus.Invalid, RemoteReason.NoVersionInRange, "1.5.0"), none);
    Assert.Equal(new RemoteCheck(RemoteStatus.Invalid, RemoteReason.NoVariant, "lua"), variant);
    Assert.Equal(RemoteStatus.Unknown, failed.Status);
  }

  [Fact]
  public void InstallPrediction_UsesTheGameAndTheLoader() {
    SemVersion game = SemVersion.Parse("1.21.50");
    SemVersion loader = SemVersion.Parse("1.1.0");
    PackageLookup alpha = PackageLookup.Of(Detail(("1.4.3", "1.2.*"), ("1.4.2", "1.1.*")));

    Prediction fits = InstallPrediction.Dependency(
        new DependencyDraft { Package = "github.com/acme/alpha", Range = ">=1.4.2 <1.5.0" },
        game, loader, alpha);
    Prediction noLoader = InstallPrediction.Dependency(
        new DependencyDraft { Package = "github.com/acme/alpha", Range = ">=1.4.2 <1.5.0" },
        game, null, alpha);
    Prediction bds = InstallPrediction.Dependency(
        new DependencyDraft { Package = "github.com/LiteLDev/bds", Range = "1.21.40" },
        game, loader, PackageLookup.Of([]));
    Prediction levilamina = InstallPrediction.Dependency(
        new DependencyDraft { Package = GameSetup.LoaderRepository, Range = "1.*" },
        game, loader, PackageLookup.Of([]));
    Prediction unlisted = InstallPrediction.Dependency(
        new DependencyDraft { Package = "github.com/x/y", Range = "1.*" },
        game, loader, PackageLookup.Of([SemVersion.Parse("1.0.0")]));

    Assert.Equal(PredictionReason.WouldInstall, fits.Reason);
    Assert.Equal("1.4.2", fits.Detail);
    Assert.Equal(PredictionOutcome.Fails, noLoader.Outcome);
    Assert.Equal(PredictionReason.NoVersionFits, noLoader.Reason);
    Assert.Equal(PredictionReason.GameOutOfRange, bds.Reason);
    Assert.Equal(PredictionReason.LoaderInRange, levilamina.Reason);
    Assert.Equal(PredictionOutcome.Unknown, unlisted.Outcome);
    Assert.Equal(
        PredictionReason.HasEntry,
        InstallPrediction.Self(PackageDraft.New("github.com/a/b"), "").Reason);
    Assert.Equal(
        PredictionReason.NoEntry,
        InstallPrediction.Self(PackageDraft.New("github.com/a/b"), "lua").Reason);
  }
}
