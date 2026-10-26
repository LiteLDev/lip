namespace Lip.Core.Git;

/// Thrown when a git remote misbehaves, reports an error, or sends malformed data.
public class GitProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);
