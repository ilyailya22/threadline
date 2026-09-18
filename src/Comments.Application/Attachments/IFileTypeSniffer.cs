namespace Threadline.Comments.Application.Attachments;

/// <summary>Detects the real type of an uploaded file from its content, not its name.</summary>
public interface IFileTypeSniffer
{
    /// <summary>
    /// Returns the type the bytes actually are, or <see langword="null"/> when they are none of the
    /// allowed types.
    /// </summary>
    SniffedFileType? Detect(ReadOnlySpan<byte> header);
}

