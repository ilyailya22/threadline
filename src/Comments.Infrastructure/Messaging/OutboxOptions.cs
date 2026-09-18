namespace Threadline.Comments.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How many messages one pass publishes. Big enough to drain a burst, small enough to keep the transaction short.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>Idle delay between passes when there was nothing to do.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Attempts before a message is parked for a human to look at.</summary>
    public int MaxAttempts { get; set; } = 10;
}
