using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Common;
using Threadline.Comments.Domain.Users;
using Threadline.Comments.Infrastructure.Messaging;
using Threadline.Comments.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Persistence;

/// <summary>
/// The write-side database context and the implementation of <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IUnitOfWork
{
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

        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                if (IntegrationEvents.From(domainEvent) is { } integrationEvent)
                {
                    OutboxMessages.Add(OutboxMessage.For(domainEvent.EventId, integrationEvent, domainEvent.OccurredAt));
                }
            }

            entity.ClearDomainEvents();
        }
    }
}
