using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Persistence.Repositories;

public sealed class AttachmentRepository(AppDbContext context) : IAttachmentRepository
{
    public Task<Attachment?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
}
