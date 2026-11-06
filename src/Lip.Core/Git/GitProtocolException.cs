namespace Lip.Core.Git;

/// Thrown when a git remote misbehaves, reports an error, or sends malformed data.
public class GitProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// Thrown when the content of a repository is something lip cannot handle (a file name that is
/// not valid, an object that is too large, an object of the wrong type). Unlike other protocol
/// failures, trying again or using another server does not help. The message is the reason, in
/// plain words, without a final period.
public sealed class UnusableRepositoryException(string reason) : GitProtocolException(reason);
