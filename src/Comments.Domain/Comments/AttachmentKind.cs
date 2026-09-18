namespace Threadline.Comments.Domain.Comments;

/// <summary>What the user attached. The assignment allows exactly these two kinds.</summary>
public enum AttachmentKind
{
    /// <summary>JPG, GIF or PNG, displayed at most 320×240 (see <see cref="Attachment"/>).</summary>
    Image = 1,

    /// <summary>Plain .txt, at most 100 KB.</summary>
    TextFile = 2,
}
