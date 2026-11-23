using Lip.Cli.Tui.I18n;
using Lip.Core.Mcmod.Editing;

namespace Lip.Cli.Tui.Pages.Tools;

/// The text of the lip.toml editor of the Tools page and of its dialogs.
public static class ManifestEditorText {
  // The action of the Tools page, and one sentence about it.
  public static string Edit => L.T("Edit lip.toml", "编辑 lip.toml", "編輯 lip.toml");
  public static string EditAbout =>
      L.T(
          "Edits the lip.toml of the workspace in a form that checks every value as you type.",
          "在表单中编辑工作区的 lip.toml，输入时自动检查每一项。",
          "在表單中編輯工作區的 lip.toml，輸入時自動檢查每一項。");

  // The titles.
  public static string CreateTitle => L.T("Create lip.toml", "创建 lip.toml", "建立 lip.toml");
  public static string EditTitle => L.T("Edit lip.toml", "编辑 lip.toml", "編輯 lip.toml");
  public static string CannotEdit =>
      L.T(
          "lip.toml cannot be edited here. Fix it by hand first:",
          "无法在这里编辑 lip.toml。请先手动修正：",
          "無法在這裡編輯 lip.toml。請先手動修正：");

  // The summary at the top.
  public static string AllValid =>
      L.T("Every value is valid.", "所有内容均有效。", "所有內容均有效。");
  public static string Problems(int count) =>
      L.T(
          count == 1 ? "1 value needs fixing." : $"{count} values need fixing.",
          $"有 {count} 项需要修正。",
          $"有 {count} 項需要修正。");
  public static string Checking(int count) =>
      L.T($"Checking {count} online…", $"正在联网检查 {count} 项…", $"正在連網檢查 {count} 項…");
  public static string Warnings(int count) =>
      L.T(
          count == 1 ? "1 value could not be confirmed." : $"{count} values could not be confirmed.",
          $"有 {count} 项未能确认。",
          $"有 {count} 項未能確認。");
  public static string FixFirst =>
      L.T("Fix the values marked ✗ first.", "请先修正标记为 ✗ 的内容。", "請先修正標記為 ✗ 的內容。");

  // The sections and the fields.
  public static string Package => L.T("Package", "包", "套件");
  public static string Id => L.T("Package id", "包 ID", "套件 ID");
  public static string IdHelp =>
      L.T(
          "A Go module path, usually the repository: github.com/owner/repo.",
          "Go 模块路径，通常是仓库地址：github.com/owner/repo。",
          "Go 模組路徑，通常是儲存庫位址：github.com/owner/repo。");
  public static string Version => L.T("Version", "版本", "版本");
  public static string VersionHelp =>
      L.T(
          "A SemVer version without a leading v, such as 1.0.0.",
          "SemVer 版本号，不带前缀 v，例如 1.0.0。",
          "SemVer 版本號，不帶前綴 v，例如 1.0.0。");
  public static string Information => L.T("Information", "信息", "資訊");
  public static string Name => L.T("Name", "名称", "名稱");
  public static string NameHelp =>
      L.T("Optional. The name people see.", "可选。展示给用户的名称。", "選填。顯示給使用者的名稱。");
  public static string Description => L.T("Description", "描述", "描述");
  public static string DescriptionHelp =>
      L.T("Optional. One sentence about the package.", "可选。一句话介绍这个包。", "選填。一句話介紹這個套件。");
  public static string Tags => L.T("Tags", "标签", "標籤");
  public static string TagsHelp =>
      L.T("Optional. Words that the package is found by.", "可选。便于搜索到这个包的词。", "選填。便於搜尋到這個套件的詞。");
  public static string Tag => L.T("Tag", "标签", "標籤");
  public static string Logo => L.T("Logo URL", "图标网址", "圖示網址");
  public static string LogoHelp =>
      L.T(
          "Optional. An https:// URL of the logo image.",
          "可选。图标图片的 https:// 网址。",
          "選填。圖示圖片的 https:// 網址。");

  public static string Variant(string name) =>
      name.Length == 0
          ? L.T("Default variant", "默认变体", "預設變體")
          : L.T($"Variant {name}", $"变体 {name}", $"變體 {name}");
  public static string VariantName => L.T("Variant name", "变体名", "變體名稱");
  public static string VariantHelp =>
      L.T(
          "Empty for the default variant; otherwise lowercase letters, digits and _, with at most one /.",
          "留空表示默认变体；否则只能用小写字母、数字和 _，最多一个 /。",
          "留空表示預設變體；否則只能用小寫字母、數字和 _，最多一個 /。");
  public static string DefaultVariantPlaceholder => L.T("(default)", "（默认）", "（預設）");
  public static string Entry(int number) => L.T($"Entry {number}", $"条目 {number}", $"條目 {number}");
  public static string EntryHelp =>
      L.T(
          "What is installed for one side and platform. Enter: add or remove entries.",
          "为某一端和平台安装的内容。Enter：添加或删除条目。",
          "為某一端和平台安裝的內容。Enter：新增或刪除條目。");
  public static string Side => L.T("Side", "端", "端");
  public static string SideHelp =>
      L.T("server for Bedrock Dedicated Server.", "server 表示 BDS 服务端。", "server 表示 BDS 伺服器端。");
  public static string Platform => L.T("Platform", "平台", "平台");
  public static string PlatformHelp =>
      L.T(
          "A .NET runtime identifier, such as win-x64 or linux-x64.",
          ".NET 运行时标识符，例如 win-x64 或 linux-x64。",
          ".NET 執行階段識別碼，例如 win-x64 或 linux-x64。");
  public static string Dependencies => L.T("Dependencies", "依赖", "相依套件");
  public static string AddDependency => L.T("+ Add…", "+ 添加…", "+ 新增…");
  public static string DependenciesHelp =>
      L.T(
          "Enter: find a package in the Marketplace, or type its id.",
          "Enter：从 Marketplace 查找包，或手动输入包 ID。",
          "Enter：從 Marketplace 尋找套件，或手動輸入套件 ID。");
  public static string RangeHelp =>
      L.T(
          "The versions it may be: >=1.2.0 <1.3.0, ^1.2.0, 1.2.0 or *. Enter: more.",
          "允许的版本范围：>=1.2.0 <1.3.0、^1.2.0、1.2.0 或 *。Enter：更多操作。",
          "允許的版本範圍：>=1.2.0 <1.3.0、^1.2.0、1.2.0 或 *。Enter：更多操作。");
  public static string Assets => L.T("Assets", "资源", "資源");
  public static string AddAsset => L.T("+ Add asset…", "+ 添加资源…", "+ 新增資源…");
  public static string AssetsHelp =>
      L.T(
          "self: this package's own files; zip: a ZIP to download; mcmod: a LeviLamina mod ZIP.",
          "self：本包自身的文件；zip：要下载的 ZIP；mcmod：LeviLamina 模组 ZIP。",
          "self：本套件自身的檔案；zip：要下載的 ZIP；mcmod：LeviLamina 模組 ZIP。");
  public static string Asset(int number) => L.T($"Asset {number}", $"资源 {number}", $"資源 {number}");
  public static string AssetHelp =>
      L.T("←→: type. Enter: URLs, places, remove.", "←→：类型。Enter：网址、放置、删除。", "←→：類型。Enter：網址、放置、刪除。");
  public static string Url => L.T("URL", "网址", "網址");
  public static string UrlHelp =>
      L.T(
          "An https:// URL; more URLs of the same file are tried in order.",
          "https:// 网址；同一文件的多个网址会按顺序尝试。",
          "https:// 網址；同一檔案的多個網址會依序嘗試。");
  public static string PlaceSrc => L.T("Place from", "放置来源", "放置來源");
  public static string PlaceDest => L.T("Place to", "放置目标", "放置目標");
  public static string PlaceHelp =>
      L.T(
          "/-separated paths: a file, a folder ending with /, or a glob; to is in the workspace.",
          "以 / 分隔的路径：文件、以 / 结尾的文件夹或通配符；目标位于工作区内。",
          "以 / 分隔的路徑：檔案、以 / 結尾的資料夾或萬用字元；目標位於工作區內。");
  public static string Preserve => L.T("Preserve", "保留", "保留");
  public static string PreserveHelp =>
      L.T(
          "Globs of files kept when the package is updated or uninstalled.",
          "更新或卸载时保留的文件（通配符）。",
          "更新或解除安裝時保留的檔案（萬用字元）。");
  public static string Purge => L.T("Purge", "清除", "清除");
  public static string PurgeHelp =>
      L.T(
          "Globs of files removed when the package is uninstalled.",
          "卸载时删除的文件（通配符）。",
          "解除安裝時刪除的檔案（萬用字元）。");
  public static string Glob => L.T("Glob", "通配符", "萬用字元");
  public static string Hook(string name) => L.T($"Hook {name}", $"钩子 {name}", $"掛鉤 {name}");
  public static string HookHelp =>
      L.T("Commands run at that step of installing.", "在安装的这一步运行的命令。", "在安裝的這一步執行的命令。");
  public static string Command => L.T("Command", "命令", "命令");
  public static string AddItem => L.T("+ Add", "+ 添加", "+ 新增");
  public static string ItemHelp =>
      L.T("Enter: remove. Empty rows are left out.", "Enter：删除。空行不会写入。", "Enter：刪除。空白列不會寫入。");
  public static string AddVariant => L.T("+ Add variant", "+ 添加变体", "+ 新增變體");
  public static string VariantActionsHelp =>
      L.T("Enter: add an entry or remove the variant.", "Enter：添加条目或删除变体。", "Enter：新增條目或刪除變體。");
  public static string Actions => L.T("Actions", "操作", "操作");

  // The menus.
  public static string FromMarketplace =>
      L.T("Find in Marketplace…", "在 Marketplace 中查找…", "在 Marketplace 中尋找…");
  public static string TypeId => L.T("Type a package id…", "输入包 ID…", "輸入套件 ID…");
  public static string ChooseVersion => L.T("Choose version…", "选择版本…", "選擇版本…");
  public static string ChooseRange => L.T("Version range…", "版本范围…", "版本範圍…");
  public static string ChangeId => L.T("Change package id…", "修改包 ID…", "修改套件 ID…");
  public static string Remove => L.T("Remove", "删除", "刪除");
  public static string AddEntry => L.T("Add entry", "添加条目", "新增條目");
  public static string RemoveEntry => L.T("Remove entry", "删除条目", "刪除條目");
  public static string RemoveVariant => L.T("Remove variant", "删除变体", "刪除變體");
  public static string AddUrl => L.T("Add URL", "添加网址", "新增網址");
  public static string AddPlace => L.T("Add place", "添加放置", "新增放置");
  public static string RemoveAsset => L.T("Remove asset", "删除资源", "刪除資源");
  public static string AssetType => L.T("Asset type", "资源类型", "資源類型");

  // The dependency dialogs.
  public static string MarketplaceTitle =>
      L.T("Add from Marketplace", "从 Marketplace 添加", "從 Marketplace 新增");
  public static string Search => L.T("Search", "搜索", "搜尋");
  public static string SearchPlaceholder => L.T("name, id or tag", "名称、ID 或标签", "名稱、ID 或標籤");
  public static string NoMatch => L.T("No mod matches.", "没有匹配的模组。", "沒有符合的模組。");
  public static string Looking(string id) =>
      L.T($"Looking up {id}…", $"正在查询 {id}…", $"正在查詢 {id}…");
  public static string VersionTitle(string id) =>
      L.T($"Version of {id}", $"{id} 的版本", $"{id} 的版本");
  public static string NoVersions =>
      L.T("No versions were found.", "没有找到任何版本。", "沒有找到任何版本。");
  public static string AnyVersionRow =>
      L.T("Without a version: type a range", "不选版本：直接输入范围", "不選版本：直接輸入範圍");
  public static string RangeTitle(string id) =>
      L.T($"Range of {id}", $"{id} 的版本范围", $"{id} 的版本範圍");
  public static string Preset(RangePreset preset) => preset switch {
    RangePreset.Patch => L.T(
        "Patch updates (recommended)", "补丁更新（推荐）", "修補更新（建議）"),
    RangePreset.Minor => L.T("Minor updates", "次版本更新", "次版本更新"),
    RangePreset.AtLeast => L.T("This version or newer", "此版本或更新", "此版本或更新"),
    RangePreset.Exact => L.T("Exactly this version", "仅此版本", "僅此版本"),
    _ => L.T("Any version", "任意版本", "任意版本"),
  };
  public static string PresetAbout(RangePreset preset) => preset switch {
    RangePreset.Patch => L.T(
        "Every patch version not lower than the chosen one.",
        "不低于所选版本的所有补丁版本。",
        "不低於所選版本的所有修補版本。"),
    RangePreset.Minor => L.T(
        "Every version up to the next major version.",
        "直到下一个主版本之前的所有版本。",
        "直到下一個主版本之前的所有版本。"),
    RangePreset.AtLeast => L.T(
        "The chosen version and every newer one.", "所选版本及所有更新的版本。", "所選版本及所有更新的版本。"),
    RangePreset.Exact => L.T("Only the chosen version.", "只允许所选版本。", "只允許所選版本。"),
    _ => L.T("Whatever version is newest.", "任意版本，总是取最新。", "任意版本，總是取最新。"),
  };
  public static string Custom => L.T("Custom", "自定义", "自訂");
  public static string CustomAbout =>
      L.T(
          "Type a range: >=1.2.0 <2.0.0, ^1.2.0, ~1.2.0, 1.* or alternatives with ||.",
          "输入范围：>=1.2.0 <2.0.0、^1.2.0、~1.2.0、1.* 或用 || 连接多个范围。",
          "輸入範圍：>=1.2.0 <2.0.0、^1.2.0、~1.2.0、1.* 或以 || 連接多個範圍。");
  public static string Matches(int count, string? newest) =>
      newest is null
          ? L.T("No known version is in this range.", "没有已知版本在此范围内。", "沒有已知版本在此範圍內。")
          : L.T(
              $"{count} known versions in range; newest {newest}.",
              $"范围内有 {count} 个已知版本；最新为 {newest}。",
              $"範圍內有 {count} 個已知版本；最新為 {newest}。");
  public static string PackageIdTitle => L.T("Dependency", "依赖", "相依套件");
  public static string PackageIdLabel =>
      L.T(
          "Package id, with #variant for a variant other than the default one",
          "包 ID；非默认变体请加上 #变体名",
          "套件 ID；非預設變體請加上 #變體名稱");
  public static string Add => L.T("Add", "添加", "新增");

  // The save dialog.
  public static string SaveTitle => L.T("Save lip.toml", "保存 lip.toml", "儲存 lip.toml");
  public static string Changes => L.T("Changes", "更改", "變更");
  public static string NoChanges =>
      L.T("Nothing changed: lip.toml is written as it is.", "没有更改：lip.toml 将保持原样写入。", "沒有變更：lip.toml 將維持原樣寫入。");
  public static string NewFile =>
      L.T("A new lip.toml is written:", "将写入新的 lip.toml：", "將寫入新的 lip.toml：");
  public static string KeepsText =>
      L.T(
          "Comments and other keys of lip.toml are kept.",
          "lip.toml 中的注释和其他键会保留。",
          "lip.toml 中的註解和其他鍵會保留。");
  public static string Rewrites =>
      L.T(
          "lip.toml is rewritten: its other keys are kept, but its comments and layout are not.",
          "lip.toml 将被重写：其他键会保留，但注释和格式不会保留。",
          "lip.toml 將被重寫：其他鍵會保留，但註解和格式不會保留。");
  public static string Prediction =>
      L.T("Expected installation", "预计安装情况", "預計安裝情況");
  public static string PredictionAbout =>
      L.T(
          "As the Marketplace tells, for a server with these versions. lip install decides.",
          "依据 Marketplace 的信息，针对使用以下版本的服务器。以 lip install 的实际结果为准。",
          "依據 Marketplace 的資訊，針對使用以下版本的伺服器。以 lip install 的實際結果為準。");
  public static string Bds => L.T("BDS", "BDS", "BDS");
  public static string LeviLamina => L.T("LeviLamina", "LeviLamina", "LeviLamina");
  public static string NoLoader => L.T("None", "不安装", "不安裝");
  public static string VariantChoice => L.T("Variant", "变体", "變體");
  public static string DefaultVariant => L.T("default", "默认", "預設");
  public static string Prerelease => L.T("prerelease", "预发布", "預發行");
  public static string LoadingVersions => L.T("Loading…", "加载中…", "載入中…");
  public static string NoGameVersions =>
      L.T("BDS versions could not be read.", "无法读取 BDS 版本。", "無法讀取 BDS 版本。");
  public static string RemoteWarnings =>
      L.T(
          "Some dependencies could not be confirmed online.",
          "部分依赖未能在线确认。",
          "部分相依套件未能線上確認。");
  public static string ChangedOnDisk =>
      L.T(
          "lip.toml changed on disk since it was opened. Overwrite it?",
          "打开之后，磁盘上的 lip.toml 已被修改。要覆盖吗？",
          "開啟之後，磁碟上的 lip.toml 已被修改。要覆寫嗎？");
  public static string Saved(string file) =>
      L.T($"Saved {file}.", $"已保存 {file}。", $"已儲存 {file}。");

  // The hints.
  public static Hint HintSave => new("Ctrl+S", CommonText.Save);
  public static Hint HintMore => new("Enter", L.T("More", "更多", "更多"));
  public static Hint HintAdd => new("Enter", L.T("Add", "添加", "新增"));
  public static Hint HintClose => new("Esc", L.T("Close", "关闭", "關閉"));

  /// What is wrong with a value.
  public static string Problem(DraftProblem problem) {
    string text = problem.Kind switch {
      DraftProblemKind.Required => L.T("Required.", "必填。", "必填。"),
      DraftProblemKind.NotModulePath => L.T(
          "Not a Go module path, such as github.com/owner/repo.",
          "不是 Go 模块路径，例如 github.com/owner/repo。",
          "不是 Go 模組路徑，例如 github.com/owner/repo。"),
      DraftProblemKind.NotVersion => L.T(
          "Not a SemVer version, such as 1.0.0.", "不是 SemVer 版本号，例如 1.0.0。", "不是 SemVer 版本號，例如 1.0.0。"),
      DraftProblemKind.LeadingV => L.T(
          $"Write it without the v: {problem.Detail}.",
          $"请去掉前缀 v：{problem.Detail}。",
          $"請去掉前綴 v：{problem.Detail}。"),
      DraftProblemKind.NotHttps or DraftProblemKind.NotHttpsUrl => L.T(
          "Must start with https://.", "必须以 https:// 开头。", "必須以 https:// 開頭。"),
      DraftProblemKind.NotVariant => L.T(
          "Use lowercase letters, digits and _, with at most one /.",
          "只能用小写字母、数字和 _，最多一个 /。",
          "只能用小寫字母、數字和 _，最多一個 /。"),
      DraftProblemKind.DuplicateVariant => L.T(
          "Another variant has this name.", "已有同名的变体。", "已有同名的變體。"),
      DraftProblemKind.NotSide => L.T(
          "Must be server or client.", "必须是 server 或 client。", "必須是 server 或 client。"),
      DraftProblemKind.NotPlatform => L.T(
          "Not a runtime identifier, such as win-x64.",
          "不是运行时标识符，例如 win-x64。",
          "不是執行階段識別碼，例如 win-x64。"),
      DraftProblemKind.OldPlatform => L.T(
          "server and client are sides; the platform is win-x64 or the like.",
          "server 和 client 是端；平台应为 win-x64 之类。",
          "server 和 client 是端；平台應為 win-x64 之類。"),
      DraftProblemKind.DuplicateEnvironment => L.T(
          "Another entry of this variant has the same side and platform.",
          "此变体中已有相同端和平台的条目。",
          "此變體中已有相同端和平台的條目。"),
      DraftProblemKind.NotDependencyKey => L.T(
          "Not a package id, or a package id with #variant.",
          "不是包 ID，也不是带 #变体 的包 ID。",
          "不是套件 ID，也不是帶 #變體 的套件 ID。"),
      DraftProblemKind.DuplicateDependency => L.T(
          "This package is a dependency already.", "已依赖这个包。", "已相依這個套件。"),
      DraftProblemKind.NotRange => L.T(
          "Not a version range.", "不是有效的版本范围。", "不是有效的版本範圍。"),
      DraftProblemKind.NotAssetType => L.T(
          "Must be self, zip or mcmod.", "必须是 self、zip 或 mcmod。", "必須是 self、zip 或 mcmod。"),
      DraftProblemKind.UrlsRequired => L.T(
          "Needs a URL: Enter, then Add URL.", "需要网址：按 Enter，选择“添加网址”。", "需要網址：按 Enter，選擇「新增網址」。"),
      DraftProblemKind.PlaceNotAllowed => L.T(
          "An mcmod asset has no places.", "mcmod 资源不能设置放置。", "mcmod 資源不能設定放置。"),
      DraftProblemKind.SecondMcmod => L.T(
          "An entry has at most one mcmod asset.", "每个条目最多一个 mcmod 资源。", "每個條目最多一個 mcmod 資源。"),
      DraftProblemKind.NotPath => L.T(
          $"Not a relative /-separated path ({problem.Detail}).",
          $"不是以 / 分隔的相对路径（{problem.Detail}）。",
          $"不是以 / 分隔的相對路徑（{problem.Detail}）。"),
      DraftProblemKind.NotGlob => L.T("Not a glob.", "不是有效的通配符。", "不是有效的萬用字元。"),
      DraftProblemKind.DestMustBeDirectory => L.T(
          "Must be empty or end with /, as the source is a folder or a glob.",
          "来源是文件夹或通配符，目标必须为空或以 / 结尾。",
          "來源是資料夾或萬用字元，目標必須為空或以 / 結尾。"),
      DraftProblemKind.DestMustBeFile => L.T(
          "Must name a file, as the source is a file.",
          "来源是文件，目标必须是文件路径。",
          "來源是檔案，目標必須是檔案路徑。"),
      DraftProblemKind.NoEntries => L.T(
          "A variant needs an entry.", "变体至少需要一个条目。", "變體至少需要一個條目。"),
      _ => L.T("A package needs a variant.", "包至少需要一个变体。", "套件至少需要一個變體。"),
    };
    // What a library says, in English, follows for the kinds that it explains.
    return problem.Detail is { Length: > 0 } detail
        && problem.Kind is DraftProblemKind.NotModulePath or DraftProblemKind.NotDependencyKey
            or DraftProblemKind.NotRange or DraftProblemKind.NotGlob
        ? $"{text} {detail}"
        : text;
  }

  /// What a check of a dependency online found.
  public static string Remote(RemoteCheck check) => check.Reason switch {
    RemoteReason.NewestInRange => L.T(
        $"Found; newest in range: {check.Detail}.",
        $"已找到；范围内最新：{check.Detail}。",
        $"已找到；範圍內最新：{check.Detail}。"),
    RemoteReason.NoVersionInRange => check.Detail is null
        ? L.T("No version is in this range.", "没有版本在此范围内。", "沒有版本在此範圍內。")
        : L.T(
            $"No version is in this range; the newest is {check.Detail}.",
            $"没有版本在此范围内；最新版本为 {check.Detail}。",
            $"沒有版本在此範圍內；最新版本為 {check.Detail}。"),
    RemoteReason.NotFound => L.T(
        "No such package, or it has no versions.", "找不到这个包，或它没有任何版本。", "找不到這個套件，或它沒有任何版本。"),
    RemoteReason.NoVariant => L.T(
        $"The package has no variant {check.Detail}.",
        $"这个包没有变体 {check.Detail}。",
        $"這個套件沒有變體 {check.Detail}。"),
    _ => L.T(
        $"Could not check online: {check.Detail}",
        $"无法联网检查：{check.Detail}",
        $"無法連網檢查：{check.Detail}"),
  };

  /// What is expected of a package when it is installed.
  public static string Predicted(Prediction prediction) => prediction.Reason switch {
    PredictionReason.HasEntry => L.T(
        "Has an entry for server on win-x64.", "有适用于 server、win-x64 的条目。", "有適用於 server、win-x64 的條目。"),
    PredictionReason.NoEntry => L.T(
        "No entry for server on win-x64 in this variant.",
        "此变体中没有适用于 server、win-x64 的条目。",
        "此變體中沒有適用於 server、win-x64 的條目。"),
    PredictionReason.GameInRange => L.T(
        $"This BDS is in {prediction.Detail}.", $"此 BDS 版本在 {prediction.Detail} 范围内。", $"此 BDS 版本在 {prediction.Detail} 範圍內。"),
    PredictionReason.GameOutOfRange => L.T(
        $"Needs BDS {prediction.Detail}.", $"需要 BDS {prediction.Detail}。", $"需要 BDS {prediction.Detail}。"),
    PredictionReason.LoaderInRange => L.T(
        $"This LeviLamina is in {prediction.Detail}.",
        $"此 LeviLamina 版本在 {prediction.Detail} 范围内。",
        $"此 LeviLamina 版本在 {prediction.Detail} 範圍內。"),
    PredictionReason.LoaderOutOfRange => L.T(
        $"Needs LeviLamina {prediction.Detail}.", $"需要 LeviLamina {prediction.Detail}。", $"需要 LeviLamina {prediction.Detail}。"),
    PredictionReason.NoLoader => L.T(
        "Needs LeviLamina, which is not chosen.", "需要 LeviLamina，但未选择。", "需要 LeviLamina，但未選擇。"),
    PredictionReason.LoaderNotForGame => L.T(
        "This LeviLamina is not for this BDS.", "此 LeviLamina 不适用于此 BDS。", "此 LeviLamina 不適用於此 BDS。"),
    PredictionReason.WouldInstall => L.T(
        $"Would install {prediction.Detail}.", $"将安装 {prediction.Detail}。", $"將安裝 {prediction.Detail}。"),
    PredictionReason.NoVersionInRange => L.T(
        "No version is in its range.", "没有版本在其范围内。", "沒有版本在其範圍內。"),
    PredictionReason.NoVersionFits => L.T(
        $"No version in its range fits these versions (newest {prediction.Detail}).",
        $"范围内没有适配这些版本的版本（最新 {prediction.Detail}）。",
        $"範圍內沒有適配這些版本的版本（最新 {prediction.Detail}）。"),
    PredictionReason.CompatibilityUnknown => L.T(
        $"Would install {prediction.Detail}; not in the Marketplace, so whether it fits is unknown.",
        $"将安装 {prediction.Detail}；它不在 Marketplace 中，无法判断是否适配。",
        $"將安裝 {prediction.Detail}；它不在 Marketplace 中，無法判斷是否適配。"),
    _ => L.T(
        $"Could not check: {prediction.Detail}", $"无法检查：{prediction.Detail}", $"無法檢查：{prediction.Detail}"),
  };
}
