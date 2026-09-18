namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>Injectable clock — the reason every test in this solution is deterministic.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
