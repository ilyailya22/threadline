using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// Turns a stored attachment into the shape the client renders, including the URLs it is served
/// from. An abstraction because those URLs are routes of the API, which this layer does not know.
/// </summary>
public interface IAttachmentDtoMapper
{
    AttachmentDto ToDto(Attachment attachment);
}
