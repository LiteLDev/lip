using System.Globalization;

namespace Lip.Cli.Tui.I18n;

/// The languages of the interface (protocol/lip-tui.md, "Language").
public enum Language {
  En,
  ZhHans,
  ZhHant,
}

/// The interface language of one interface, which the user may change while it runs. Several
/// interfaces, each with its own, run at once in the daemon.
public sealed class LanguageScope(Language language = Language.En) {
  private volatile int _language = (int)language;

  public Language Language {
    get => (Language)_language;
    set => _language = (int)value;
  }
}

/// The text of the interface in its language. Every text of the interface is written as
/// L.T(en, zhHans, zhHant), so that the compiler finds a language left out. The language is
/// that of the interface whose thread, or whose operation, asks: never keep the result in a
/// field, since the user may change the language while the interface runs.
public static class L {
  // Flows from the thread of an interface to the tasks it starts.
  private static readonly AsyncLocal<LanguageScope?> s_scope = new();

  /// The language of the interface that the caller belongs to; English outside any.
  public static Language Current => s_scope.Value?.Language ?? Language.En;

  /// The text in the current language.
  public static string T(string en, string zhHans, string zhHant) =>
      Current switch {
        Language.ZhHans => zhHans,
        Language.ZhHant => zhHant,
        _ => en,
      };

  /// Makes scope the language of the caller and of what it starts from now on.
  public static void Bind(LanguageScope? scope) => s_scope.Value = scope;

  /// What get returns in the language, whatever the language of the caller is.
  public static T In<T>(Language language, Func<T> get) {
    LanguageScope? before = s_scope.Value;
    s_scope.Value = new LanguageScope(language);
    try {
      return get();
    }
    finally {
      s_scope.Value = before;
    }
  }
}

/// The names of the languages, and the language that a locale names (protocol/lip-tui.md,
/// "Language").
public static class Languages {
  public static readonly IReadOnlyList<Language> All =
      [Language.En, Language.ZhHans, Language.ZhHant];

  /// The value of the configuration key language for the language.
  public static string Code(Language language) =>
      language switch {
        Language.ZhHans => "zh-Hans",
        Language.ZhHant => "zh-Hant",
        _ => "en",
      };

  /// The name of the language in the language itself.
  public static string NativeName(Language language) =>
      language switch {
        Language.ZhHans => "简体中文",
        Language.ZhHant => "繁體中文",
        _ => "English",
      };

  /// The language of a value of the configuration key language, or null.
  public static Language? Parse(string? code) =>
      code switch {
        "en" => Language.En,
        "zh-Hans" => Language.ZhHans,
        "zh-Hant" => Language.ZhHant,
        _ => null,
      };

  /// The language that a locale names, such as zh-CN or zh_TW.UTF-8, or null when it names
  /// none.
  public static Language? OfLocale(string? locale) {
    if (string.IsNullOrEmpty(locale)) {
      return null;
    }

    int end = locale.IndexOfAny(['.', '@']);
    string[] subtags = (end < 0 ? locale : locale[..end]).Split('-', '_');
    if (subtags[0].Equals("en", StringComparison.OrdinalIgnoreCase)) {
      return Language.En;
    }

    if (!subtags[0].Equals("zh", StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    // The script, when the locale names it, says more than the region.
    string[] traditional = ["Hant", "TW", "HK", "MO"];
    bool Has(params string[] any) =>
        subtags.Skip(1).Any(s => any.Contains(s, StringComparer.OrdinalIgnoreCase));

    return !Has("Hans") && Has(traditional) ? Language.ZhHant : Language.ZhHans;
  }

  /// The client language of this process: on Windows that of the display language of the user,
  /// elsewhere that of the first of LC_ALL, LC_MESSAGES and LANG that names one.
  public static Language? OfThisProcess() {
    if (OperatingSystem.IsWindows()) {
      return OfLocale(CultureInfo.CurrentUICulture.Name);
    }

    foreach (string variable in (string[])["LC_ALL", "LC_MESSAGES", "LANG"]) {
      if (OfLocale(Environment.GetEnvironmentVariable(variable)) is { } language) {
        return language;
      }
    }

    return null;
  }
}
