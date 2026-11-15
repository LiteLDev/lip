using Spectre.Console;

namespace Lip.Cli;

/// The standard output of the invocation a command is carried out for, where its result goes
/// (protocol/lip.md, "Output"): Out for text written as it is, such as JSON, which Console would
/// wrap; Console for tables and trees.
public sealed record CommandOutput(TextWriter Out, IAnsiConsole Console);
