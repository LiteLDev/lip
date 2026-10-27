# lip 包规范（manifest.json / .mcmod）

本规范只在 LeviLamina `manifest.json` 规范之上**增加**键，不改变、不删除其中任何键的含义。
LeviLamina 忽略本规范新增的键。"必须 / 禁止 / 可以"分别对应 RFC 2119 的 MUST / MUST NOT / MAY。
凡本规范判定为"失败"的情形，lip 必须中止当前操作，并且不修改工作区：操作开始修改工作区之后才发现的失败，会撤销该操作的修改（§7"原子性"；命令所做的事除外）。

## 1. 术语

- **归档**：一棵文件树，有三种形式：
  - **ZIP 归档**：一个 ZIP 文件，以 `.mcmod` 为扩展名时称 mcmod。
  - **git 归档**：某个 commit 的树。
  - **目录归档**：另一个归档内的一个目录（以 `/` 结尾的 relative ref，见 §3）。
- **文档**：归档根部的 `manifest.json`。必须是 UTF-8 编码的严格 JSON（RFC 8259，不允许注释），顶层必须是对象。
- **路由文档**：含 `artifacts` 键的文档。
- **具体文档**：不含 `artifacts` 键的文档。根部是具体文档的归档称为**具体包**。
- **身份**：具体包的 `name`。比较名称时不区分 ASCII 大小写。
- **工作区**：lip 操作的根目录。
- **环境**：一对值 `(platform, rid)`，见 §5.1。

## 2. 归档

1. ZIP 归档中的条目路径以 `/` 分隔，禁止以 `/` 开头，禁止含 `..` 段或 `\`，路径按 UTF-8 解释。只允许普通文件和目录两类条目。
2. git 归档只包含普通文件（mode `100644` / `100755`）和树。符号链接与子模块不属于归档。
3. 判断一个文件是否为 ZIP 归档，只看内容，与扩展名无关。mcmod 文件应当使用 `.mcmod` 扩展名。
4. 归档根部有 `tooth.json` 时，按 §9 处理。没有 `tooth.json` 也没有 `manifest.json` 时，归档无效。

## 3. 引用（ref）

```
ref     = target [ "#" variant ]                    ; relative 与 url target
        / spec [ "#" variant ] [ "@" git-ref ]      ; spec target
variant = 匹配 ^[a-z0-9_]+(/[a-z0-9_]+)?$
target  = relative / url / spec
git-ref = "branch:" name / "tag:" name / "rev:" commit
commit  = 40 位十六进制数字（不区分大小写）
```

relative 与 url ref 在第一个 `#` 处切分。spec ref 先在第一个 `@` 处切分，其后的全部内容为 git-ref，其前的部分再在第一个 `#` 处切分。不带 `#` 的 ref，其 variant 为 `""`。带 `#` 但后面为空的 ref 无效。

| target 形式 | 判定 | 指向 |
|---|---|---|
| **relative** | 以 `./` 开头 | 当前文档所在归档内的路径，见下文 |
| **url** | 以 `https://` 开头 | 一个绝对 URL（RFC 3986）。lip 跟随重定向；响应体必须是 ZIP 归档 |
| **spec** | 其余全部 | 必须是合法的 Go 模块路径（`golang.org/x/mod/module.CheckPath`）。指向 git 仓库 `https://<spec>.git` |

**relative 的规则：**
- `./` 之后是用 `/` 分隔的非空路径，禁止出现空段、`.` 段、`..` 段和 `\`。
- 以 `/` 结尾时指向一个目录，该目录作为目录归档。
- 否则指向一个文件，该文件必须是 ZIP 归档。

**spec 的规则：**
- 只有名为 `v<SemVer 2.0.0>` 的 tag 才是该包的版本，其余 tag 一律忽略。
- 某个版本的归档，是该 tag 所指 commit 的 git 归档。

**git-ref 的规则**（把 spec ref **固定**到一个 commit，而不是其各个版本）：
- `branch:<name>` 指 `refs/heads/<name>` 所指的 commit；`tag:<name>` 指 `refs/tags/<name>` 所指的 commit（附注 tag 解析到其 commit），tag 名不限；`rev:<commit>` 指该 commit 本身。`<name>` 必须是合法的 git ref 名称（`git check-ref-format`）：非空，不含控制字符、空格、`~`、`^`、`:`、`?`、`*`、`[`、`\`、`..` 或 `@{`，不为 `@`，不以 `/` 开头或结尾，不以 `.` 结尾，不含 `//`，且以 `/` 分隔的各段都不以 `.` 开头、不以 `.lock` 结尾。
- 缩写的 commit id 无效：解析它需要仓库的历史。
- spec ref 中的 `@` 后面不是合法的 git-ref 时，ref 无效；尤其是 `@` 之后永远不写版本号（版本由 `range` 约束，见 §5.2）。
- 只有 spec ref 可以带 git-ref：含 `@branch:`、`@tag:` 或 `@rev:` 的 relative 或 url ref 无效。

文档中禁止出现其他 scheme，包括 `http:`、`file:` 等。

## 4. 文档

本规范和 LeviLamina 都未定义的键一律忽略。键的值类型与定义不符时，文档无效，处理即失败。
格式标记见 §4.4。

### 4.1 路由文档

| 键 | 类型 | 必需 | 定义 |
|---|---|---|---|
| `artifacts` | Artifact[] | 是 | 非空 |
| `name` | string | 否 | 只用于模板 |
| `version` | string | 否 | SemVer 2.0.0，只用于模板 |
| `refs` | ref[] | 否 | 上游位置，仅供参考，不参与解析 |

路由文档中的其他键不参与解析。路由文档必须带格式标记（§4.4）。

**Artifact：**

| 键 | 类型 | 必需 | 定义 |
|---|---|---|---|
| `variant` | string | 否 | 缺省为 `""`；非空时必须匹配 §3 的 variant 正则 |
| `platform` | `"server"` \| `"client"` \| 二者组成的非空数组 | 否 | 缺省表示不限 |
| `rid` | string \| 非空 string 数组 | 否 | .NET RID；缺省表示不限 |
| `refs` | ref[] | 是 | 非空，按顺序为备选 |

<span v-pre>**模板**：`refs` 中的字符串可以包含 `{{name}}`、`{{version}}`、`{{platform}}`、`{{rid}}`，替换规则如下：</span>
- <span v-pre>`{{name}}`、`{{version}}` 取本文档同名键的值；本文档缺少该键时，该 ref 不产出候选（见 §5.2），不中止整个操作。</span>
- <span v-pre>`{{platform}}`、`{{rid}}` 取当前环境的值。</span>
- 先替换模板，再按 §3 解析 ref。
- <span v-pre>出现其他任何 `{{`…`}}` 时，文档无效。</span>

**有效性**：设 A、B 是两个 `variant` 相同的条目。若下面两个条件同时成立，文档无效：
1. A 与 B 写出的选择器键集合相同（`platform` 和 `rid` 各自写了或都没写）；
2. 对每个写出的键，A 与 B 的取值集合都有交集。

### 4.2 具体文档

| 键 | 类型 | 必需 | 定义 |
|---|---|---|---|
| `name` | string | 是 | 匹配 `^[A-Za-z0-9][A-Za-z0-9._-]*$` |
| `version` | string | 是 | SemVer 2.0.0 |
| `type` | string | 是 | `"files"` 表示**文件包**；其他任何值表示 **mod**，语义按 LeviLamina 的定义 |
| `platform` | `"server"` \| `"client"` \| `"universal"` | 否 | 缺省为 `"universal"` |
| `rid` | string | 否 | .NET RID；缺省表示不限 |
| `refs` | ref[] | 否 | 上游位置，仅供参考，不参与解析 |
| `dependencies` | Dep[] | 否 | LeviLamina 语义；其中含 `refs` 的条目也由 lip 安装 |
| `optionalDependencies` | Dep[] | 否 | LeviLamina 语义；lip 不安装 |
| `conflicts` | Dep[] | 否 | LeviLamina 语义；lip 按 §6 检查，忽略其中的 `refs` |
| `installDependencies` | InstallDep[] | 否 | 只由 lip 安装；LeviLamina 不读取 |
| `placements` | Placement[] | 否 | 只允许出现在 `type` 为 `"files"` 的文档中 |
| `preserveFiles` | glob[] | 否 | 见 §7 |
| `removeFiles` | glob[] | 否 | 见 §7 |
| `scripts` | Scripts | 否 | 见 §7 |

`entry` 等其余 LeviLamina 键，本规范不使用。

**子结构：**
- **Dep** = `{ "name": string, "version"?: range, "refs"?: ref[] }`，其中 `refs` 若出现必须非空。
- **InstallDep** = `{ "name"?: string, "version"?: range, "refs": ref[] }`，其中 `refs` 必须非空。
- **range**：语法与匹配语义同 LeviLamina 的 `ll::data::VersionRequirement`，但禁止不带运算符的完整版本号（例如 `1.2.3`）。缺省表示接受任意版本。
- **Placement** = `{ "src": string, "dest": string }`。
  - `src` 是归档内的路径，`dest` 是工作区内的路径。两者都禁止以 `/` 开头，禁止含 `..` 段、`\` 或盘符。
  - 值为 `""` 或以 `/` 结尾时为**目录形式**，否则为**文件形式**。`src` 与 `dest` 的形式必须相同。
- **Scripts** = `{ "preInstall"?, "postInstall"?, "preUninstall"?, "postUninstall"? }`，每个值都是 `string[]`。
- **glob**：一种路径模式。对 mod，它相对于 mod 目录 `<modsDir>/<name>/`（见 §7）；对文件包，它相对于工作区根。
  - `*` 匹配不含 `/` 的任意字符序列。
  - 单独构成一段的 `**` 匹配零个或多个段。
  - 以 `/` 结尾的模式匹配目录；位于被匹配目录之下的文件，也视为被该模式匹配。
  - 没有其他元字符。

### 4.3 来自 tag 的文档

当文档位于某个 spec 版本的 git 归档根部，并且含有 `version` 键时，`version` 必须等于该 tag 的版本。
固定的 spec ref（§3）的归档，只有当其 git-ref 为 `tag:v<SemVer 2.0.0>` 时才有 tag 版本；位于 branch、rev 或其他 tag 时没有 tag 版本，使用文档自身的 `version`。

### 4.4 格式标记

本格式的文档带有两个格式标记：`"formatVersion": 4` 与 `"formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f"`。
任一标记键出现但取值不同，或两个标记键只出现其中一个时，文档无效。

不带格式标记的文档是普通的 LeviLamina 清单，只作为自成一体的具体文档接受：
- 含 `artifacts` 键时，文档无效；
- 忽略其 `dependencies` 条目中的 `refs` 和 `installDependencies`，因此它不产生任何安装边（§6）。

## 5. 解析

### 5.1 环境

环境 `(platform, rid)` 属于工作区，按下列顺序确定：

| 分量 | 确定顺序 |
|---|---|
| `platform` | 1. 用户显式指定<br>2. 锁中记录的值<br>3. 工作区根部存在 `bedrock_server.exe` 时为 `"server"`<br>4. 存在 `Minecraft.Windows.exe` 时为 `"client"`<br>5. 以上都不满足时失败 |
| `rid` | 1. 用户显式指定<br>2. 锁中记录的值<br>3. lip 自身运行时的 RID |

### 5.2 Resolve(ref, base, range) → 候选序列

`base` 是 ref 所在文档的归档。`range` 是调用方给出的版本约束，缺省表示任意版本。
Resolve 产出一个**有序的具体包候选序列**。下文中"无候选"指这条路径不产出任何候选，**不是**整个操作失败。lip 应当报告无候选的路径及其原因。

1. **按 target 取归档。**
   - relative：在 `base` 内按 §3 取得。
   - url：下载取得。
   - spec：取所有满足 `range` 的 tag 版本，按 SemVer 优先级从高到低排列；优先级相同时，tag 名字典序大者在前。对每个版本的归档依次执行第 2–4 步，把各自产出的候选按此顺序拼接。
   - 固定的 spec：把 git-ref 解析为一个 commit（§3），每次操作只解析一次，使同一个 branch 在整个操作中指向同一个 commit；对该 commit 的 git 归档执行第 2–4 步。仓库中没有该 branch 或 tag、或没有该 commit 时，无候选。`range` 不用于选择归档，但在第 4 步中仍然适用。
   - 取不到归档，或归档无效（§2）时，无候选。
2. **读取文档。** 文档无效时，无候选。
3. **若是路由文档：**
   - 先选出 `variant` 等于 ref 的 variant 的条目，再筛选匹配环境的条目。条目匹配环境，是指它写出的每个选择器键的取值集合都包含环境的对应值。
   - 在匹配的条目中，选出写出选择器键数量最多的那一个。没有匹配条目，或数量最多的条目不唯一时，无候选。
   - 对该条目 `refs` 中的每个 r，按顺序执行 Resolve(r, 本归档, range)，把各自的候选按此顺序拼接。
4. **若是具体文档：** 下列条件全部满足时，产出唯一候选"本包"；任一不满足时，无候选。
   - ref 的 variant 为 `""`；
   - `platform` 等于环境的 `platform`，或为 `"universal"`；
   - `rid` 缺省，或等于环境的 `rid`；
   - `version` 满足 `range`。

**额外的无候选条件：**
- 链超过 8 跳。
- 同一条链中，同一个 `(归档键, variant)` 出现两次。归档键的定义如下：

| 归档来源 | 归档键 |
|---|---|
| url | 去掉 fragment 后的 URL |
| spec | `spec@version` |
| 固定的 spec | `spec@rev:<commit>`，commit 为 git-ref 解析到的小写 commit id |
| relative | `base 的归档键` + `!` + 路径 |
| 本地文件（仅请求，见 §6） | 其绝对 `file:` URI（RFC 8089） |

身份始终取最终具体包的 `name`，与链经过的路由文档无关。

## 6. 依赖图

一次操作的**请求**是用户给出的若干 `(ref, range)` 对。请求不位于任何归档内，因此没有 `base`：请求中的 relative ref 无候选。除 §3 的 target 外，请求的 target 还可以是**本地文件**：本地文件系统上的一个 ZIP 归档，以其绝对路径指定，后面可跟 `#` variant。它按 url target 的方式解析，只是其归档从该文件读取而不是下载。本地目录不是归档，不能作为请求。本地文件永远不出现在文档中。
lip 必须为请求求出一个具体包集合 S，满足下列全部条件；求不出时失败。

1. 每个请求都被 S 中的某个包满足：该包是对这个请求执行 Resolve 所得的候选之一（或代替其中之一，见下文）。
2. 对 S 中每个包的每个**安装边**，S 中都有一个包满足它。安装边是：
   - 含 `refs` 的 `dependencies` 条目；
   - 所有 `installDependencies` 条目。

   不带格式标记的文档没有安装边（§4.4）。

   满足一条安装边，意味着同时满足：
   - 该包是 Resolve(r, 条目所在包的归档, 条目的 `version`) 所得的候选之一，其中 r 是该条目 `refs` 中的某个 ref，或代替其中之一（见下文）；
   - 条目写了 `name` 时，该包的身份等于该 `name`。

   **固定的包代替版本**：解析链以固定的 spec ref `s#v@g` 开头的包，可以代替未固定的 spec ref `s#v`（spec 相同，比较时不区分 ASCII 大小写；variant 相同）的候选，前提是其版本满足该处的 `range`。固定的 ref 只由其自身的候选满足：来自 `s@branch:a` 的包不满足 `s@branch:b`，来自 tag 版本的包也不满足 `s@branch:a`。因此依赖的 `range` 约束的是固定 commit 处声明的 `version`：版本不在范围内的固定包不满足该依赖。
3. S 中任意两个包身份不同。
4. 对 S 中每个包的每个 `conflicts` 条目，S 中不存在身份等于该条目 `name`、且版本满足该条目 `version` 的包。

满足条件的 S 不止一个时，lip 应当优先选择在候选序列中位置更靠前的包。不带 `refs` 的 `dependencies` 条目与 `optionalDependencies` 条目都不参与依赖图。

## 7. 安装与卸载

**安装顺序**：被依赖者先于依赖者。依赖关系成环时，环内按身份的 ASCII 小写形式升序安装。

安装一个具体包 P 的步骤：

1. 依次执行 `scripts.preInstall` 中的每条命令（见下文"命令"）。
2. 写入文件：
   - **mod**：把 P 的整个归档写入 `<modsDir>/<name>/`。服务端与客户端的 `modsDir` 均为 `mods`。
   - **文件包**：按 `placements` 的顺序逐条执行。
     - 目录形式：把 `src` 目录下的全部文件按相对路径写入 `dest`。
     - 文件形式：把 `src` 文件写入 `dest`。
     - 归档根部的 `manifest.json` 永不写入。
     - 若某个 `src` 在归档中不存在，失败。
3. 依次执行 `scripts.postInstall` 中的每条命令。

**写入规则：**
- 待写文件已归其他已安装包所有时，失败。
- 待写文件已存在、且匹配 P 的 `preserveFiles` 时，保留原文件，不写入。
- 其他情况直接覆盖。
- P 写入的文件归 P 所有。

卸载 P 的步骤：
1. 执行 `scripts.preUninstall`。
2. 删除 P 所有、且不匹配 `preserveFiles` 的文件。
3. 删除匹配 `removeFiles`、且不归其他包所有的路径。目录模式会递归删除。`mods` 与 `plugins` 本身永不匹配任何模式，因此不会经由它们删除其下的任何内容。
4. 删除因第 2、3 步而变空的目录，但永不删除 `mods` 与 `plugins` 本身。
5. 执行 `scripts.postUninstall`。

**更新**：等于先卸载旧版本，再安装新版本。

**服务端目录布局**：服务端工作区的 `plugins` 必须是指向 `mods` 的链接，以便仍在 `plugins` 中查找的软件继续工作（lip 在 Windows 上创建目录联接，在其他平台上创建相对符号链接）。经由 `plugins/` 的路径与经由 `mods/` 的对应路径是同一文件：比较所有权、在锁中记录文件时，一律使用 `mods/` 形式。在会修改工作区的操作（安装、卸载、更新）之前，lip 必须按下列规则建立该布局：

- `mods` 不存在时，创建它。
- `plugins` 不存在时，创建指向 `mods` 的链接。
- `plugins` 已是指向 `mods` 的链接时，不做任何事。
- `plugins` 是指向其他位置的链接时，失败。
- `plugins` 是普通目录时，把其中的条目移入 `mods`，再删除 `plugins` 并创建链接。若任一条目在 `mods` 中已有同名项，则在移动任何条目之前失败。

建立布局失败时，工作区不得有任何改动。建立布局是操作的一部分：操作的后续步骤失败时，布局的改动也一并撤销。

**命令**：每条命令由平台 shell 执行（Windows 上为 `cmd /c`，其他平台为 `sh -c`），工作目录为工作区根。任一命令以非零码退出时失败。

**原子性**：一次操作（安装、卸载或更新，连同它安装、替换或移除的所有依赖）要么整体成功，要么整体失败。

1. **先规划**：在修改工作区之前，lip 必须完成所有无需修改工作区、却可能失败的工作：解析、求解、获取该操作要安装的每个归档、确定每次安装与卸载要写入、保留和删除的文件，并检查本节的所有失败条件（包括服务端目录布局）。每一步都基于之前各步完成后的工作区来规划（例如，更新时新版本安装到卸载旧版本之后的工作区中）。规划阶段的失败不改动工作区与锁。
2. **再执行**：执行某一步时失败（文件无法写入或删除、命令以非零码退出、用户取消操作），lip 必须按相反顺序撤销该操作对工作区的全部修改：删除它创建的文件，恢复它覆盖或删除的文件，恢复目录与服务端目录布局。锁不得改动。lip 在所有步骤成功之后才最后写入锁。
3. **命令不会被撤销**：lip 无法撤销命令所做的事。命令（`preInstall`、`postInstall`、`preUninstall` 或 `postUninstall`）失败时，与其他步骤失败一样使操作失败，lip 撤销自己的修改（包括在之前的命令运行前所做的修改）；已经运行的命令既不会重新运行，也不会被逆转。因此命令应当可以安全地重复运行，并且不应假定该操作写入的文件会保留。
4. 卸载第 2–4 步要删除的路径在规划阶段、任何命令运行之前确定：命令创建的文件不会被 `removeFiles` 删除。
5. **中断**：lip 在执行操作时被中断（例如进程被终止）时，下一次对该工作区的操作必须先处理被中断的操作：若其锁已写入，则完成它，否则撤销它。
6. **并发**：两个操作不得同时作用于同一个工作区。

## 8. 锁

对每个已安装的包，lip 必须在工作区锁中记录以下内容：

- 身份与 `version`；
- 环境；
- 解析链：每一跳的 ref（包括其 git-ref，如有），在该跳选中的 Artifact 下标，以及 spec 跳的归档所在的 commit；
- 最终归档的标识：ZIP 归档记录其 SHA-256（以及读取它的 URL 或本地文件），git 归档记录 commit id 与路径；
- 该包所有的文件列表（工作区相对路径，`plugins/` 下的文件记为 `mods/` 形式，见 §7）。

按锁重装时，lip 必须直接取用锁中记录的最终归档，并校验其标识，不得重新走解析链。尤其是，来自固定 spec ref 的包按记录的 commit 重装，无论其 branch 或 tag 现在指向哪里。

**更新**一个显式安装的包时，重新请求其解析链的第一个 ref：未固定的 spec ref 取最新版本，`branch:` 取当前的头，`tag:` 取该 tag 现在所指的 commit，`rev:` 取同一个 commit；url 重新请求（lip 可以从其下载缓存提供，因此 URL 背后的内容不应改变），本地文件重新读取。结果在身份、版本、归档键或归档标识上有任何不同时，替换该包。

## 9. tooth.json 兼容

归档根部存在 `tooth.json` 时，lip 忽略 `manifest.json`，按下列映射把 tooth.json 转换为一个具体包。此后该包与其他具体包一样参与解析、安装、卸载与锁定（§5–§8）。tooth.json 格式 1 与 2 先按 tooth.json 规范的定义迁移为格式 3（包括其中的模板替换）；下列映射定义在格式 3 上。

1. **身份与版本**：身份为 `tooth` 的值，版本为 `version` 的值。在 spec 版本的归档中（包括固定的 `tag:v<SemVer 2.0.0>` 的归档，见 §4.3），改用该 tag 的版本，且 §4.3 不适用；二者不同时 lip 应当给出警告。位于 branch、rev 或其他 tag 时，使用 `version` 的值。
2. **变体选择**（取代 §5.2 第 4 步）：ref 的 variant 即 v3 的 label，但 variant 为 `""`、环境 `platform` 为 `"client"`、并且存在 label 为 `client` 的变体时，表示 `client`。label 等于它、且 `platform` 为空、等于环境 `rid` 或作为 glob 匹配环境 `rid` 的各变体按顺序合并：其 `dependencies`、`assets`、`preserve_files`、`remove_files` 依次拼接，每个脚本钩子取自唯一具有它的变体。没有变体匹配时无候选；两个被合并的变体声明了相同的依赖键或相同的脚本钩子时为失败。结果是一个文件包（`"type": "files"`、`"platform": "universal"`、没有 `rid`）；其版本满足 `range` 时为唯一候选，否则无候选。
3. **依赖**：每个依赖 `"<path>[#label]": range` 成为一条安装边，其 refs 只有 `<path>[#label]`，没有 `name`，range 按 v3 的语义解释。该 ref 指向的归档采用 `manifest.json` 且 label 为 `client` 时，variant 视为 `""`。
4. **资源（assets）**：变体的第 i 个资源（从 0 计数）挂载在包归档的目录 `i` 下。`self` 资源即 tooth.json 所在的归档本身。`zip`、`tar`、`tgz` 资源是从其 `urls` 中第一个可以下载的地址下载的归档，格式按内容判定，§2.1 不适用。`uncompressed` 资源是按同样方式下载的单个文件，挂载为文件 `i`。
5. **放置（placements）**：安装时，资源 i 的每条放置对它匹配到的该资源中的每个文件生成一条文件形式的放置，按资源、再按文件在资源中的顺序、再按放置的顺序排列。`file` 放置取路径等于 `src` 的文件，放到 `dest`；没有这样的文件时，取匹配 glob `src`（tooth.json 的 glob 语法）的每个文件，放到 `dest/<文件名>`。`dir` 放置取目录 `src` 下的每个文件（`""` 与 `"."` 表示全部文件），放到 `dest/<相对 src 的路径>`。`dest` 中的 `\` 读作 `/`，`.` 与 `..` 段按路径语义处理。匹配不到文件的放置不放置任何文件；两条放置写同一路径时，后者生效。§7 中“归档根部的 `manifest.json` 永不写入”的规则不适用。
6. **preserve_files 与 remove_files** 分别成为 `preserveFiles` 与 `removeFiles`，其中 `\` 替换为 `/`。不含 `/` 的 `preserve_files` 模式前加 `**/`，因为 v3 也用它匹配单独的文件名。不以 `/` 结尾的 `remove_files` 模式另外产生在其后加 `/` 的模式，因为 v3 也删除匹配的目录。不是合法 glob（§4.2）的模式（例如以 `/` 开头）被丢弃；`*` 与 `**` 以外的 tooth.json glob 语法在 glob 中没有含义，按字面匹配。
7. **脚本**：`pre_install` 成为 `preInstall`；`install` 后接 `post_install` 成为 `postInstall`；`pre_uninstall` 后接 `uninstall` 成为 `preUninstall`；`post_uninstall` 成为 `postUninstall`。
8. **锁**：锁中记录 tooth.json（格式 3，带所安装的版本）以代替文档。lip 再次需要该包时，以链中最后一跳的 variant 和锁中的环境，按本映射重新转换该 tooth.json。

## 附录：示例（非规范性）

仓库 `github.com/LiteLDev/LegacyMoney` 在 tag `v0.22.0` 下的路由文档：

```json
{
  "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney",
  "version": "0.22.0",
  "artifacts": [
    { "platform": ["server", "client"], "rid": "win-x64",
      "refs": ["https://github.com/LiteLDev/LegacyMoney/releases/download/v{{version}}/{{name}}-{{platform}}-{{rid}}.mcmod"] }
  ]
}
```

`LegacyMoney-server-win-x64.mcmod` 根部的具体文档：

```json
{
  "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "LegacyMoney", "entry": "LegacyMoney.dll", "type": "native",
  "version": "0.22.0", "platform": "server", "rid": "win-x64",
  "refs": ["github.com/LiteLDev/LegacyMoney"],
  "dependencies": [
    { "name": "iListenAttentively", "version": "^0.17.0", "refs": ["github.com/MiracleForest/iListenAttentively"] }
  ],
  "installDependencies": [
    { "version": ">=26.51.0 <26.52.0", "refs": ["github.com/LiteLDev/LeviLamina"] }
  ]
}
```

一个文件包：

```json
{
  "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "name": "PeEditor", "type": "files", "version": "3.9.0", "rid": "win-x64",
  "placements": [ { "src": "PeEditor.exe", "dest": "PeEditor.exe" } ]
}
```

一个通过 variant 路由到子包的仓库：

```json
{
  "formatVersion": 4, "formatUuid": "0c6b977a-64ad-441f-9a29-c7f4b196882f",
  "artifacts": [
    { "variant": "quickjs", "refs": ["./quickjs/"] },
    { "variant": "lua", "refs": ["./lua/"] }
  ]
}
```
