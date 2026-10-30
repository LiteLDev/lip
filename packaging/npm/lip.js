#!/usr/bin/env node

const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const process = require("node:process");
const { maybeAddDotnetRootEnv, DOTNET_MAJOR } = require("./dotnet-bootstrap");

const target = `${process.platform}:${process.arch}`;
const supportedTargets = new Set([
  "linux:x64",
  "linux:arm64",
  "darwin:x64",
  "darwin:arm64",
  "win32:x64",
  "win32:arm64"
]);

if (!supportedTargets.has(target)) {
  console.error(`Unsupported platform: ${target}`);
  process.exit(1);
}

const binaryPath = path.join(
  __dirname,
  `${process.platform}-${process.arch}`,
  process.platform === "win32" ? "lip.exe" : "lip"
);

if (!fs.existsSync(binaryPath)) {
  console.error(`Missing bundled binary for ${target}: ${binaryPath}`);
  process.exit(1);
}

const childEnv = { ...process.env };
maybeAddDotnetRootEnv(childEnv, DOTNET_MAJOR);

const child = spawn(binaryPath, process.argv.slice(2), {
  stdio: "inherit",
  env: childEnv
});
child.on("error", (error) => {
  console.error(error.message);
  process.exit(1);
});
child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }
  process.exit(code ?? 1);
});
