const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const DOTNET_CHANNEL = "10.0";
const DOTNET_MAJOR = 10;

function getDefaultDotnetRoot(options = {}) {
  const platform = options.platform ?? process.platform;
  const env = options.env ?? process.env;
  const homedir = options.homedir ?? os.homedir();
  const join = platform === "win32" ? path.win32.join : path.join;

  if (env.DOTNET_INSTALL_DIR) {
    return env.DOTNET_INSTALL_DIR;
  }

  if (platform === "win32") {
    const localAppData =
      env.LocalAppData ??
      env.LOCALAPPDATA ??
      join(homedir, "AppData", "Local");
    return join(localAppData, "Microsoft", "dotnet");
  }

  return join(homedir, ".dotnet");
}

function getSharedRuntimeDir(rootDir) {
  return path.join(rootDir, "shared", "Microsoft.NETCore.App");
}

function hasNetCoreAppMajor(rootDir, major = DOTNET_MAJOR) {
  const runtimeDir = getSharedRuntimeDir(rootDir);
  if (!fs.existsSync(runtimeDir)) {
    return false;
  }

  return fs
    .readdirSync(runtimeDir, { withFileTypes: true })
    .some(
      (entry) =>
        entry.isDirectory() &&
        entry.name.startsWith(`${major}.`) &&
        /^\d+\./.test(entry.name)
    );
}

function maybeAddDotnetRootEnv(targetEnv, major = DOTNET_MAJOR, options = {}) {
  const root =
    options.defaultRootOverride ??
    getDefaultDotnetRoot({
      platform: options.platform,
      env: options.env,
      homedir: options.homedir
    });

  if (hasNetCoreAppMajor(root, major)) {
    targetEnv.DOTNET_ROOT = root;
  }

  return targetEnv;
}

function hasMajorRuntimeInListRuntimesOutput(output, major = DOTNET_MAJOR) {
  if (!output) return false;

  return output
    .split(/\r?\n/)
    .some((line) => line.startsWith(`Microsoft.NETCore.App ${major}.`));
}

function isRuntimeInstalled(major = DOTNET_MAJOR, options = {}) {
  const env = options.env ?? process.env;

  // Respect explicit DOTNET_ROOT if provided by the user or the environment.
  if (env.DOTNET_ROOT && hasNetCoreAppMajor(env.DOTNET_ROOT, major)) {
    return true;
  }

  const defaultRoot = getDefaultDotnetRoot({
    platform: options.platform,
    env: options.env,
    homedir: options.homedir
  });

  if (hasNetCoreAppMajor(defaultRoot, major)) {
    return true;
  }

  // Best-effort: if `dotnet` is already available, ask it directly.
  const spawnSync = options.spawnSync ?? require("node:child_process").spawnSync;
  const result = spawnSync("dotnet", ["--list-runtimes"], {
    encoding: "utf8",
    windowsHide: true
  });

  if (result && result.status === 0) {
    return hasMajorRuntimeInListRuntimesOutput(result.stdout, major);
  }

  return false;
}

function getInstallCommandSpec(options = {}) {
  const platform = options.platform ?? process.platform;

  if (platform === "win32") {
    return {
      command: "powershell.exe",
      args: [
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        "& ([scriptblock]::Create((Invoke-WebRequest -UseBasicParsing https://dot.net/v1/dotnet-install.ps1).Content)) -Runtime dotnet -Channel 10.0 -NoPath"
      ]
    };
  }

  return {
    command: "bash",
    args: [
      "-c",
      "curl -fsSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --runtime dotnet --channel 10.0 --no-path"
    ]
  };
}

module.exports = {
  DOTNET_CHANNEL,
  DOTNET_MAJOR,
  getDefaultDotnetRoot,
  getInstallCommandSpec,
  getSharedRuntimeDir,
  hasMajorRuntimeInListRuntimesOutput,
  hasNetCoreAppMajor,
  isRuntimeInstalled,
  maybeAddDotnetRootEnv
};
