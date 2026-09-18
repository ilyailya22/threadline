using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Attachments;

/// <summary>
/// Validates an uploaded file and puts it in object storage, returning the domain entity.
/// </summary>
/// <remarks>
/// Deliberately splits "accept" from "process". Acceptance is cheap and synchronous — sniff the
/// bytes, check the limits, stream to Blob Storage — so the user gets an answer immediately.
/// Downscaling the image to 320×240 and making a thumbnail is expensive and happens on a worker,
/// triggered by the comment's outbox event. That is what keeps a burst of uploads from consuming
/// the API's threads.
/// </remarks>
public interface IAttachmentIntakeService
{
    Task<Attachment> StageAsync(AttachmentUpload upload, CancellationToken cancellationToken = default);
}
