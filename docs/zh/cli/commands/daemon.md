# lip daemon

显示或停止 lip 的守护进程。

除 `lip daemon` 外，每个 `lip` 命令都由**守护进程**执行：它是你的用户在后台运行的一个 `lip` 进程。第一个命令会启动它，之后的命令复用它；它在一段时间无事可做后自行退出。你无需手动启动或停止它：命令的行为与 `lip` 自己执行时完全相同，在你的当前目录中、使用你的环境变量、读取你的输入并输出到你的终端，Ctrl+C 也照常有效。参见[内部机制](../../guides/internal_mechanism.md#守护进程)。

## lip daemon status

```shell
lip daemon status [--json]
```

显示守护进程是否在运行，若在运行，则显示其版本和进程 ID。它从不启动守护进程。使用 `--json` 时，输出 `{"running": false}`，或 `{"running": true, "version": "<版本>", "pid": <进程 ID>}`。

## lip daemon stop

```shell
lip daemon stop
```

请求守护进程退出，并等待它退出。如果守护进程正在执行命令（例如服务器正在运行的 `lip run`），或正在运行托管服务器（参见 [`lip run --managed`](run.md#托管服务器)），它会继续运行，`lip daemon stop` 失败；请先停止托管服务器，并在命令结束后重新运行。如果没有守护进程在运行，它什么也不做。

守护进程运行托管服务器期间不会自行退出；更新后的 `lip` 在其托管服务器停止之前，仍会用它执行 `lip run --managed`。
