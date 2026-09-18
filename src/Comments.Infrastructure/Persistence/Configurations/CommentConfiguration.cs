using Threadline.Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Threadline.Comments.Infrastructure.Persistence.Configurations;

public sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Comments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Path)
            .HasConversion(v => v.Value, v => CommentPath.FromStorage(v))
            .HasMaxLength(CommentPath.MaxLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(c => c.Depth).IsRequired();
        builder.Property(c => c.RootId).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        // Text: an owned type rather than two loose columns, so the invariant "html and plain text
        // always describe the same message" cannot be broken by assigning one of them.
        builder.OwnsOne(c => c.Body, body =>
        {
            body.Property(b => b.Html)
                .HasColumnName("TextHtml")
                .HasMaxLength(CommentBody.MaxHtmlLength)
                .IsRequired();

            body.Property(b => b.PlainText)
                .HasColumnName("TextPlain")
                .HasMaxLength(CommentBody.MaxPlainTextLength)
                .IsRequired();
        });

        builder.OwnsOne(c => c.Fingerprint, fingerprint =>
        {
            fingerprint.Property(f => f.IpHash)
                .HasColumnName("ClientIpHash")
                .HasMaxLength(ClientFingerprint.IpHashLength)
                .IsUnicode(false)
                .IsRequired();

            fingerprint.Property(f => f.UserAgent)
                .HasColumnName("ClientUserAgent")
                .HasMaxLength(ClientFingerprint.MaxUserAgentLength);

            fingerprint.Property(f => f.ClientId).HasColumnName("ClientId");
        });

        builder.HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Attachments)
            .WithOne()
            .HasForeignKey(a => a.CommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Attachments)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude(false);

        // Self-reference. Restrict, not Cascade: SQL Server refuses a cascade on a self-referencing
        // FK, and deleting a comment out from under its replies is not a thing this system does.
        builder.HasOne<Comment>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---------------------------------------------------------------- indexes
        //
        // These three indexes are the reason the read paths stay flat at a million rows.

        // 1. The default LIFO page and both date sorts of the top-level table. Filtered, so it
        //    contains only the ~4% of rows that are top-level, and covering, so the query never
        //    touches the base table.
        builder.HasIndex(c => c.CreatedAt)
            .HasDatabaseName("IX_Comments_TopLevel_CreatedAt")
            .HasFilter("[ParentId] IS NULL")
            .IncludeProperties(c => new { c.AuthorId, c.RootId });

        // 2. Whole-thread retrieval: one range scan that returns the subtree already in display
        //    order, with no recursive CTE and no query per level.
        builder.HasIndex(c => new { c.RootId, c.Path })
            .HasDatabaseName("IX_Comments_RootId_Path");

        // 3. Direct replies of one comment — what the GraphQL DataLoader batches on.
        builder.HasIndex(c => new { c.ParentId, c.CreatedAt })
            .HasDatabaseName("IX_Comments_ParentId_CreatedAt");

        builder.HasIndex(c => c.AuthorId).HasDatabaseName("IX_Comments_AuthorId");
    }
}
