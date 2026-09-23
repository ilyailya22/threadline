using Threadline.Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Threadline.Comments.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Attachments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.CommentId).IsRequired();
        builder.Property(a => a.Kind).HasConversion<byte>().IsRequired();

        builder.Property(a => a.ContentType).HasMaxLength(127).IsUnicode(false).IsRequired();
        builder.Property(a => a.OriginalFileName).HasMaxLength(Attachment.MaxFileNameLength).IsRequired();
        builder.Property(a => a.StoragePath).HasMaxLength(512).IsUnicode(false).IsRequired();
        builder.Property(a => a.ThumbnailPath).HasMaxLength(512).IsUnicode(false);

        builder.HasIndex(a => a.CommentId).HasDatabaseName("IX_Attachments_CommentId");
    }
}
