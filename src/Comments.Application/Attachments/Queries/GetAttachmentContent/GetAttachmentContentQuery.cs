using MediatR;

namespace Threadline.Comments.Application.Attachments.Queries.GetAttachmentContent;

/// <summary>Opens a stored attachment — the file itself or its thumbnail — for streaming to the browser.</summary>
public sealed record GetAttachmentContentQuery(Guid AttachmentId, bool Thumbnail) : IRequest<AttachmentContent>;
