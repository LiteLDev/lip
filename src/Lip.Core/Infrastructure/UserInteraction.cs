namespace Lip.Core.Infrastructure;

/// Thrown when lip needs an answer from the user but cannot ask for it.
public class CannotAskException(string message) : InvalidOperationException(message);

public interface IUserInteraction {
  Task PrintInfo(string message);
  Task PrintSuccess(string message);
  Task PrintWarning(string message);
  Task PrintError(string message);
  Task RunWithProgress(string message, Func<IProgress<double>, Task> action);

  /// Asks the user for a value and returns the answer. Throws CannotAskException when the user
  /// cannot be asked (e.g. standard input is not a terminal).
  Task<string> Ask(string question);
}

public static class UserInteractionExtensions {
  /// Asks until the answer is valid (validate returns null) and returns the trimmed answer.
  /// Throws CannotAskException when the user cannot be asked.
  public static async Task<string> AskValid(
      this IUserInteraction ui, string question, Func<string, string?> validate) {
    while (true) {
      // A user interaction that returns no answer cannot ask.
      string answer = (await ui.Ask(question))?.Trim()
          ?? throw new CannotAskException($"lip needs {question}, but cannot ask for it here.");
      if (validate(answer) is not string error) {
        return answer;
      }

      await ui.PrintWarning($"Invalid answer: {error}");
    }
  }
}
