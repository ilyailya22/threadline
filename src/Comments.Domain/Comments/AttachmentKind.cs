namespace Threadline.Comments.Domain.Comments;

/// <summary>What the user attached. The assignment allows exactly these two kinds.</summary>
public enum AttachmentKind
{
    /// <summary>JPG, GIF or PNG, displayed at most 320×240 (see <see cref="Attachment"/>).</summary>
    Image = 1,

    /// <summary>Plain .txt, at most 100 KB.</summary>
    TextFile = 2,
}

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
