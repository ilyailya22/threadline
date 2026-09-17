namespace Threadline.Comments.Domain.Common;

/// <summary>
/// Base class for aggregate roots and entities. Identity is the only thing that defines equality —
/// two <see cref="Entity"/> instances with the same <see cref="Id"/> are the same entity even if
/// their state differs (e.g. one is a stale copy loaded in another unit of work).
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Entity id must not be empty.");
        }

        Id = id;
    }

    /// <summary>EF Core materialisation constructor.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// Events raised by this entity during the current unit of work. They are collected and handed
    /// to the outbox by <c>AppDbContext.SaveChangesAsync</c>, so that persisting the entity and
    /// scheduling its side effects happen in one transaction.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    public bool Equals(Entity? other) =>
        other is not null && GetType() == other.GetType() && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}
