namespace Threadline.Comments.Domain.Common;

/// <summary>
/// Thrown when an operation would leave an aggregate in a state its invariants forbid.
/// Reaching this exception from an HTTP request means validation upstream was incomplete —
/// the API maps it to 422, never to 500.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DomainException()
    {
    }
}
