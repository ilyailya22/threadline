namespace Threadline.Comments.Domain.Comments;

/// <summary>Lifecycle of the stored file.</summary>
public enum AttachmentStatus
{
    /// <summary>Uploaded and validated, waiting for the worker to post-process it.</summary>
    Pending = 0,

    /// <summary>Post-processing finished; the file is servable.</summary>
    Ready = 1,

    /// <summary>Post-processing failed permanently; the comment renders without the file.</summary>
    Failed = 2,
}
