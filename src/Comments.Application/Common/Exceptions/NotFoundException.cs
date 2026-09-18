namespace Threadline.Comments.Application.Common.Exceptions;

/// <summary>The referenced entity does not exist. Maps to 404.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string entity, object key)
        : base($"{entity} '{key}' was not found")
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException()
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
