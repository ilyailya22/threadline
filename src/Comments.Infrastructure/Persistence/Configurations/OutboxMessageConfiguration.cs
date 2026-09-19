using Threadline.Comments.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Threadline.Comments.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Type).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.OccurredAt).IsRequired();
        builder.Property(m => m.Error).HasMaxLength(OutboxMessage.MaxErrorLength);

        // The publisher's only query: "give me the next batch of unprocessed messages that are due".
        // Filtered on ProcessedAt IS NULL, so the index shrinks back to near-empty as the queue
        // drains and never grows with the history of everything ever published.
        builder.HasIndex(m => new { m.NextAttemptAt, m.OccurredAt })
            .HasDatabaseName("IX_OutboxMessages_Pending")
            .HasFilter("[ProcessedAt] IS NULL");
    }
}
