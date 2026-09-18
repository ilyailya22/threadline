using Threadline.Comments.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Threadline.Comments.Infrastructure.Persistence.Configurations;

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InboxMessages");

        builder.HasKey(m => new { m.MessageId, m.ConsumerType });

        builder.Property(m => m.ConsumerType).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(m => m.ProcessedAt).IsRequired();

        // Used by the retention job that trims records older than the broker's redelivery window.
        builder.HasIndex(m => m.ProcessedAt).HasDatabaseName("IX_InboxMessages_ProcessedAt");
    }
}
