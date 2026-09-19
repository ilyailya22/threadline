using System.Collections.Frozen;
using Threadline.Comments.Domain.Comments.Events;
using Threadline.Comments.Domain.Common;
using Threadline.Comments.Infrastructure.Messaging.Contracts;

namespace Threadline.Comments.Infrastructure.Messaging;

/// <summary>
/// The one place that knows which integration events exist and which domain event becomes which.
/// Adding an event means adding its contract here — the outbox and the publisher need no change.
/// </summary>
public static class IntegrationEvents
{
    /// <summary>Contracts that may travel through the outbox, by the name stored in the row.</summary>
    private static readonly FrozenDictionary<string, Type> Contracts =
        new[] { typeof(CommentCreatedIntegrationEvent) }.ToFrozenDictionary(type => type.Name, StringComparer.Ordinal);

    /// <summary>
    /// The published form of a domain event, or <see langword="null"/> for one that stays inside
    /// this service.
    /// </summary>
    /// <remarks>
    /// A separate type rather than the domain event itself: a domain event is an internal detail
    /// that may be refactored freely, while an integration event is a contract that other
    /// deployables — including an older worker during a rolling deploy — must keep reading.
    /// </remarks>
    public static object? From(IDomainEvent domainEvent) =>
        domainEvent switch
        {
            CommentCreatedDomainEvent e => new CommentCreatedIntegrationEvent
            {
                EventId = e.EventId,
                CommentId = e.CommentId,
                RootId = e.RootId,
                ParentId = e.ParentId,
                AuthorId = e.UserId,
                Depth = e.Depth,
                AttachmentIds = [.. e.AttachmentIds],
                CreatedAt = e.CreatedAt,
            },
            _ => null,
        };

    public static Type ContractFor(string name) =>
        Contracts.TryGetValue(name, out var type)
            ? type
            : throw new NotSupportedException($"Unknown integration event '{name}'.");
}
