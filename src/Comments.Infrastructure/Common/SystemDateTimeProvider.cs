using Threadline.Comments.Application.Common.Abstractions;

namespace Threadline.Comments.Infrastructure.Common;

/// <summary>The real clock. Every other implementation in the solution is a test double.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
