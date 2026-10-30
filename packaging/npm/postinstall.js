#!/usr/bin/env node

const { spawnSync } = require("node:child_process");
const process = require("node:process");

const {
  DOTNET_CHANNEL,
  DOTNET_MAJOR,
  getDefaultDotnetRoot,
  getInstallCommandSpec,
  hasNetCoreAppMajor,
  isRuntimeInstalled
} = require("./dotnet-bootstrap");

const root = getDefaultDotnetRoot();

if (isRuntimeInstalled(DOTNET_MAJOR)) {
  process.exit(0);
}

const spec = getInstallCommandSpec();
console.log(
  `Installing .NET Runtime ${DOTNET_CHANNEL} via the official dotnet-install script...`
);

const result = spawnSync(spec.command, spec.args, {
  stdio: "inherit",
  env: process.env
});

if (result.error) {
  console.error(`Failed to launch .NET installer: ${result.error.message}`);
  process.exit(1);
}

if (result.status !== 0) {
  process.exit(result.status ?? 1);
}

if (!hasNetCoreAppMajor(root, DOTNET_MAJOR)) {
  console.error(
    `The installer finished but .NET Runtime ${DOTNET_MAJOR}.x was not detected under ${root}.`
  );
  process.exit(1);
}
