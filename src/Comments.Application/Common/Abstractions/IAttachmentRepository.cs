using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Common.Abstractions;

public interface IAttachmentRepository
{
    /// <summary>Reads an attachment for serving; not tracked, since nothing about it changes.</summary>
    Task<Attachment?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}
