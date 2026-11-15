using System.Text.Json;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Lip.Core.Infrastructure;

/// The variables of Scriban templates rendered as Template.Render(model) renders them with a
/// dictionary of strings or JSON values as the model, without importing a .NET object by
/// reflection, which the trimmed lip cannot do.
internal sealed class ScribanModel {
  private readonly Action<ScriptObject> _import;

  private ScribanModel(Action<ScriptObject> import) {
    _import = import;
  }

  /// The members of a JSON object as the variables.
  public static ScribanModel From(JsonElement root) => new(model => model.Import(root));

  /// Variables with these string values.
  public static ScribanModel From(params (string Name, string? Value)[] variables) =>
      new(model => {
        foreach ((string name, string? value) in variables) {
          model[name] = value;
        }
      });

  /// Template.Render(model): each rendering has variables of its own, which the template may
  /// change without changing those of another.
  public string Render(Template template) {
    ScriptObject model = [];
    _import(model);
    TemplateContext context = template.LexerOptions.Lang == ScriptLang.Liquid
        ? new LiquidTemplateContext()
        : new TemplateContext();
    context.MemberRenamer = StandardMemberRenamer.Default;
    context.MemberFilter = null;
    context.PushGlobal(model);
    return template.Render(context);
  }
}
