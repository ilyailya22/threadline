namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// Commits the current unit of work. Implemented by the EF Core <c>DbContext</c>, which also turns
/// the domain events collected on tracked entities into outbox rows inside the same transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
