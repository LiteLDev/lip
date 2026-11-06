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
