using Threadline.Comments.Infrastructure.Messaging.Consumers;
using MassTransit;

namespace Threadline.Comments.Infrastructure.Messaging;

/// <summary>
/// The two sets of consumers, grouped by the tier that hosts them.
/// </summary>
/// <remarks>
/// Consumers are split by what they need, not by what they consume. Broadcasting needs the SignalR
/// hub connections, which live in the web tier. Everything else is background work that should
/// scale on queue depth, independently of web traffic, and belongs to the worker.
/// </remarks>
public static class ConsumerRegistration
{
    /// <summary>Pushes new comments to connected browsers. Web tier.</summary>
    public static IBusRegistrationConfigurator AddBroadcastConsumers(this IBusRegistrationConfigurator bus)
    {
        ArgumentNullException.ThrowIfNull(bus);

        bus.AddConsumer<CommentBroadcastConsumer>();

        return bus;
    }

    /// <summary>Keeps the search index current. Worker tier.</summary>
    public static IBusRegistrationConfigurator AddBackgroundConsumers(this IBusRegistrationConfigurator bus)
    {
        ArgumentNullException.ThrowIfNull(bus);

        // A batch closes at BatchSize events or after 100 ms, whichever comes first — so a quiet
        // system still indexes a new comment almost immediately, and a busy one amortises the
        // refresh wait across a whole batch.
        bus.AddConsumer<CommentIndexerConsumer>(consumer => consumer.Options<BatchOptions>(options => options
            .SetMessageLimit(CommentIndexerConsumer.BatchSize)
            .SetTimeLimit(TimeSpan.FromMilliseconds(100))
            .SetConcurrencyLimit(2)));

        return bus;
    }
}
