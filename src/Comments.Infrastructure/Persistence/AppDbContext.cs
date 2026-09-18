using System.Text.Json;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Comments.Events;
using Threadline.Comments.Domain.Common;
using Threadline.Comments.Domain.Users;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Persistence;

/// <summary>
/// The write-side database context and the implementation of <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IDateTimeProvider clock)
    : DbContext(options), IUnitOfWork
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public DbSet<User> Users => Set<User>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>
    /// Saves the unit of work, converting any domain events raised during it into outbox rows
    /// first — in the same transaction, which is the whole point of the pattern.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CollectDomainEventsIntoOutbox();

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    private void CollectDomainEventsIntoOutbox()
    {
        var entities = ChangeTracker
            .Entries<Entity>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToArray();

        if (entities.Length == 0)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                var message = ToOutboxMessage(domainEvent, now);

                if (message is not null)
                {
                    OutboxMessages.Add(message);
                }
            }

            entity.ClearDomainEvents();
        }
    }

    private static OutboxMessage? ToOutboxMessage(IDomainEvent domainEvent, DateTimeOffset now) =>
        domainEvent switch
        {
            CommentCreatedDomainEvent e => new OutboxMessage
            {
                Id = e.EventId,
                Type = nameof(CommentCreatedIntegrationEvent),
                OccurredAt = now,
                Payload = JsonSerializer.Serialize(
                    new CommentCreatedIntegrationEvent
                    {
                        EventId = e.EventId,
                        CommentId = e.CommentId,
                        RootId = e.RootId,
                        ParentId = e.ParentId,
                        AuthorId = e.UserId,
                        Depth = e.Depth,
                        AttachmentIds = e.AttachmentIds.ToArray(),
                        CreatedAt = e.CreatedAt,
                    },
                    PayloadJson),
            },
            _ => null,
        };
}
