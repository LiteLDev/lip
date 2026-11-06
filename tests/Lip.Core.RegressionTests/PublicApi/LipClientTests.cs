using Lip.Core.Infrastructure;
using Lip.Core.PublicApi;
using Moq;

namespace Lip.Core.RegressionTests.PublicApi;

public class LipClientTests {
  public record WorkflowBaseStep();
  public record WorkflowInstallStep(List<string> Packages) : WorkflowBaseStep;
  public record WorkflowUninstallStep(List<string> Packages) : WorkflowBaseStep;
  public record WorkflowUpdateStep(List<string> Packages) : WorkflowBaseStep;

  private class TempWorkdir : IDisposable {
    private readonly string _originalDir = Directory.GetCurrentDirectory();
    private readonly string _tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());

    public TempWorkdir() {
      Directory.CreateDirectory(_tempDir);

      Directory.SetCurrentDirectory(_tempDir);
    }

    public void Dispose() {
      Directory.SetCurrentDirectory(_originalDir);

      try {
        Directory.Delete(_tempDir, recursive: true);
      }
      catch {
        // Ignore any exceptions during cleanup
      }
    }
  }

  public static TheoryData<List<string>> Install_OnWinX64_DoesNotThrowData {
    get {
      TheoryData<List<string>> data = new();
      data.Add(new List<string> { "github.com/LiteLDev/MoreDimensions@0.13.0" });
      data.Add(new List<string> { "github.com/LiteLDev/LegacyScriptEngine@0.17.13" });
      data.Add(new List<string> { "github.com/LiteLDev/LeviLamina@1.9.9" });
      data.Add(new List<string> { "github.com/LiteLDev/LeviLamina@1.9.9", "github.com/LiteLDev/LegacyScriptEngine@0.17.13" });
      data.Add(new List<string> { "github.com/LiteLDev/LegacyScriptEngine@0.17.13", "github.com/LiteLDev/MoreDimensions@0.13.0" });
      return data;
    }
  }

  [WinX64Theory]
  [MemberData(nameof(Install_OnWinX64_DoesNotThrowData))]
  public async Task Install_OnWinX64_DoesNotThrow(List<string> packages) {
    IUserInteraction userInteraction = CreateMockUserInteraction();

    using TempWorkdir _ = new();

    LipClient lipClient = await LipClient.Create(userInteraction);

    await lipClient.Install(packages, dryRun: false, ignoreScripts: false, noDependencies: false);
  }

  public static TheoryData<(List<WorkflowBaseStep> Steps, List<string> FinalExplicitPackages)> ComplexWorkflow_OnWinX64_DoesNotThrowData =>
  [
    (
      [
        new WorkflowInstallStep(["github.com/LiteLDev/LeviLamina@1.9.7", "github.com/LiteLDev/LegacyScriptEngine@0.17.5"]),
        new WorkflowUninstallStep(["github.com/LiteLDev/LegacyScriptEngine"]),
        new WorkflowInstallStep(["github.com/LiteLDev/MoreDimensions@0.13.0"]),
        new WorkflowInstallStep(["github.com/LiteLDev/LegacyScriptEngine@0.17.5"]),
      ],
      [
        "github.com/LiteLDev/LeviLamina@1.9.7",
        "github.com/LiteLDev/MoreDimensions@0.13.0",
        "github.com/LiteLDev/LegacyScriptEngine@0.17.5"
      ]
    ),
    (
      [
        new WorkflowInstallStep(["github.com/LiteLDev/LeviLamina@1.9.7"]),
        new WorkflowInstallStep(["github.com/LiteLDev/LegacyMoney@0.17.0"]),
        new WorkflowInstallStep(["github.com/LiteLDev/LegacyScriptEngine@0.17.5"]),
        new WorkflowUninstallStep(["github.com/LiteLDev/LegacyMoney"]),
      ],
      [
        "github.com/LiteLDev/LeviLamina@1.9.7",
        "github.com/LiteLDev/LegacyScriptEngine@0.17.5"
      ]
    ),
    (
      [
        new WorkflowInstallStep(["github.com/LiteLDev/LeviLamina@1.9.7"]),
        new WorkflowInstallStep(["github.com/LiteLDev/LegacyScriptEngine@0.17.5"]),
        new WorkflowInstallStep(["github.com/LiteLDev/LegacyMoney@0.17.0"]),
        new WorkflowUninstallStep(["github.com/LiteLDev/LegacyMoney"]),
      ],
      [
        "github.com/LiteLDev/LeviLamina@1.9.7",
        "github.com/LiteLDev/LegacyScriptEngine@0.17.5"
      ]
    )
  ];

  [WinX64Theory]
  [MemberData(nameof(ComplexWorkflow_OnWinX64_DoesNotThrowData))]
  public async Task ComplexWorkflow_OnWinX64_DoesNotThrow((List<WorkflowBaseStep> Steps, List<string> FinalExplicitPackages) testCase) {
    IUserInteraction userInteraction = CreateMockUserInteraction();

    using TempWorkdir _ = new();

    LipClient lipClient = await LipClient.Create(userInteraction);

    foreach (var step in testCase.Steps) {
      switch (step) {
        case WorkflowInstallStep installStep:
          await lipClient.Install(installStep.Packages, dryRun: false, ignoreScripts: false, noDependencies: false);
          break;

        case WorkflowUninstallStep uninstallStep:
          await lipClient.Uninstall(uninstallStep.Packages, dryRun: false, ignoreScripts: false, noDependencies: false);
          break;

        case WorkflowUpdateStep updateStep:
          await lipClient.Update(updateStep.Packages, dryRun: false, ignoreScripts: false);
          break;

        default:
          throw new NotSupportedException("Unknown workflow step type");
      }
    }

    (IEnumerable<string>? explicitPackages, var _) = await lipClient.List();

    Assert.NotNull(explicitPackages);
    Assert.Equal(testCase.FinalExplicitPackages.Order(), explicitPackages.Order());
  }

  private static IUserInteraction CreateMockUserInteraction() {
    Mock<IUserInteraction> mock = new();

    mock.Setup(u => u.PrintInfo(It.IsAny<string>())).Returns(Task.CompletedTask);
    mock.Setup(u => u.PrintWarning(It.IsAny<string>())).Returns(Task.CompletedTask);
    mock.Setup(u => u.PrintError(It.IsAny<string>())).Returns(Task.CompletedTask);
    mock.Setup(u => u.RunWithProgress(It.IsAny<string>(), It.IsAny<Func<IProgress<double>, Task>>()))
        .Returns((string _, Func<IProgress<double>, Task> action) =>
            action(new Progress<double>()));

    return mock.Object;
  }
}
