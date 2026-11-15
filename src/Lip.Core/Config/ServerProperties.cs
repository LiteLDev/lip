using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lip.Core.Config;

/// What a value of server.properties may be, as the comment of its property says.
public enum PropertyKind {
  /// "true" or "false".
  Boolean,

  /// One of the choices.
  Choice,

  /// An integer, between Min and Max when they are given.
  Integer,

  /// Any text.
  Text,
}

/// The type of a property: its kind, its choices and its bounds.
public sealed record PropertyType(
    PropertyKind Kind, IReadOnlyList<string> Choices, long? Min = null, long? Max = null) {
  public static readonly PropertyType Text = new(PropertyKind.Text, []);

  /// Why value is not a value of this type, or null when it is one.
  public string? Check(string value) {
    switch (Kind) {
      case PropertyKind.Boolean when value is not ("true" or "false"):
        return "The value is \"true\" or \"false\".";
      case PropertyKind.Choice when !Choices.Contains(value):
        return "The value is one of " + string.Join(", ", Choices) + ".";
      case PropertyKind.Integer:
        if (!long.TryParse(
            value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number)) {
          return "The value is an integer.";
        }

        if (number < (Min ?? long.MinValue) || number > (Max ?? long.MaxValue)) {
          return Max is null
              ? string.Create(CultureInfo.InvariantCulture, $"The value is at least {Min}.")
              : string.Create(
                  CultureInfo.InvariantCulture, $"The value is an integer from {Min} to {Max}.");
        }

        return null;
      default:
        return value.Contains('\n') || value.Contains('\r') ? "The value is one line." : null;
    }
  }
}

/// A property of server.properties (protocol/lip-tui.md, "Configuration"): a line that does not
/// start with '#' and contains '='.
public sealed class ServerProperty {
  internal ServerProperty(
      int line, string key, string value, IReadOnlyList<string> comment, PropertyType type) {
    Line = line;
    Key = key;
    Value = value;
    Original = value;
    Comment = comment;
    Type = type;
  }

  internal int Line { get; }

  public string Key { get; }

  /// The value, as the user changed it.
  public string Value { get; internal set; }

  /// The value read.
  public string Original { get; }

  /// The lines of the comment that follows the property in the file, without their '#'.
  public IReadOnlyList<string> Comment { get; }

  /// The type that the "Allowed values:" line of the comment gives, Text without one.
  public PropertyType Type { get; }

  public bool Changed => Value != Original;
}

/// server.properties, the file of the server's properties, edited by replacing the values of the
/// properties the user changed and nothing else (protocol/lip-tui.md, "Configuration").
public sealed partial class ServerProperties {
  public const string FileName = "server.properties";

  // The lines with their line breaks: concatenated, they are the file.
  private readonly List<string> _lines;
  private readonly List<ServerProperty> _properties;

  private ServerProperties(List<string> lines, List<ServerProperty> properties) {
    _lines = lines;
    _properties = properties;
  }

  public IReadOnlyList<ServerProperty> Properties => _properties;

  public bool Changed => _properties.Any(p => p.Changed);

  public static ServerProperties Parse(string text) {
    List<string> lines = [];
    int start = 0;
    while (start < text.Length) {
      int end = text.IndexOf('\n', start);
      end = end < 0 ? text.Length : end + 1;
      lines.Add(text[start..end]);
      start = end;
    }

    List<ServerProperty> properties = [];
    for (int i = 0; i < lines.Count; i++) {
      string content = Content(lines[i]);
      int equals = content.IndexOf('=', StringComparison.Ordinal);
      if (content.StartsWith('#') || equals < 0) {
        continue;
      }

      List<string> comment = [];
      for (int j = i + 1; j < lines.Count; j++) {
        string next = Content(lines[j]).TrimStart();
        if (!next.StartsWith('#')) {
          break;
        }

        comment.Add(next[1..].Trim());
      }

      string key = content[..equals].TrimStart('﻿').Trim();
      properties.Add(new ServerProperty(
          i, key, content[(equals + 1)..], comment, TypeOf(comment)));
    }

    return new ServerProperties(lines, properties);
  }

  public ServerProperty? Find(string key) => _properties.FirstOrDefault(p => p.Key == key);

  /// Sets the value of the first property of the key; throws ArgumentException when there is
  /// none, or the value is not of its type.
  public void Set(string key, string value) =>
      Set(Find(key) ?? throw new ArgumentException($"There is no property {key}.", nameof(key)),
          value);

  /// Sets the value of a property of this file; throws ArgumentException when it is not of its
  /// type.
  public void Set(ServerProperty property, string value) {
    if (property.Type.Check(value) is { } error) {
      throw new ArgumentException(error, nameof(value));
    }

    property.Value = value;
  }

  /// Gives a property back the value read, whatever its type.
  public static void Reset(ServerProperty property) => property.Value = property.Original;

  /// The file with the values of the changed properties replaced, every other character kept.
  public string Serialize() {
    StringBuilder text = new();
    for (int i = 0; i < _lines.Count; i++) {
      ServerProperty? property = _properties.FirstOrDefault(p => p.Line == i && p.Changed);
      if (property is null) {
        text.Append(_lines[i]);
        continue;
      }

      string line = _lines[i];
      string content = Content(line);
      text.Append(content.AsSpan(0, content.IndexOf('=', StringComparison.Ordinal) + 1))
          .Append(property.Value)
          .Append(line.AsSpan(content.Length));
    }

    return text.ToString();
  }

  /// A line without its line break.
  private static string Content(string line) => line.TrimEnd('\n').TrimEnd('\r');

  /// The type that the "Allowed values:" line of a comment gives.
  internal static PropertyType TypeOf(IReadOnlyList<string> comment) {
    string? allowed = comment
        .Select(line => AllowedRegex().Match(line))
        .FirstOrDefault(match => match.Success)?.Groups[1].Value.Trim();
    if (allowed is null) {
      return PropertyType.Text;
    }

    List<string> quoted = [.. QuotedRegex().Matches(allowed).Select(m => m.Groups[1].Value)];
    string rest = QuotedRegex().Replace(allowed, "");
    if (quoted.Count == 0 && BareBooleanRegex().IsMatch(allowed)) {
      return new PropertyType(PropertyKind.Boolean, ["true", "false"]);
    }

    if (quoted.Count >= 2 && ListRestRegex().IsMatch(rest)) {
      return quoted.Count == 2 && quoted.Contains("true") && quoted.Contains("false")
          ? new PropertyType(PropertyKind.Boolean, ["true", "false"])
          : new PropertyType(PropertyKind.Choice, quoted);
    }

    if (quoted.Count > 0) {
      return PropertyType.Text;
    }

    if (RangeRegex().Match(allowed) is { Success: true } range) {
      return new PropertyType(
          PropertyKind.Integer, [], Number(range.Groups[1].Value), Number(range.Groups[2].Value));
    }

    if (IntegerRegex().IsMatch(allowed)) {
      return new PropertyType(
          PropertyKind.Integer, [],
          allowed.Contains("non-negative", StringComparison.OrdinalIgnoreCase) ? 0 : null);
    }

    return PropertyType.Text;
  }

  private static long Number(string text) => long.Parse(text, CultureInfo.InvariantCulture);

  [GeneratedRegex(@"^Allowed values:(.*)$", RegexOptions.IgnoreCase)]
  private static partial Regex AllowedRegex();

  [GeneratedRegex("\"([^\"]*)\"")]
  private static partial Regex QuotedRegex();

  // What is left of a list of quoted choices: "a", "b", or "c".
  [GeneratedRegex(@"^[\s,.]*(or[\s,.]*)?$")]
  private static partial Regex ListRestRegex();

  [GeneratedRegex(@"^true,?\s+(or\s+)?false\.?$")]
  private static partial Regex BareBooleanRegex();

  // "Integers in the range [1, 65535]", "0-65535".
  [GeneratedRegex(@"^(?:Integers in the range\s*)?\[?\s*(-?\d+)\s*(?:,|-)\s*(-?\d+)\s*\]?\.?$",
      RegexOptions.IgnoreCase)]
  private static partial Regex RangeRegex();

  // "Any positive integer", "Any non-negative integer.", "Positive integer equal to 5 or
  // greater."; not "A positive integer ..., or "unlimited"", which has a quoted choice.
  [GeneratedRegex(@"\binteger\b", RegexOptions.IgnoreCase)]
  private static partial Regex IntegerRegex();
}
