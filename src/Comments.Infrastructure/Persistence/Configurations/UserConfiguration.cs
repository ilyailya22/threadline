using Threadline.Comments.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Threadline.Comments.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.UserName)
            .HasConversion(v => v.Value, v => UserName.Create(v))
            .HasMaxLength(UserName.MaxLength)
            .IsUnicode(false) // latin letters and digits only — half the storage, faster compares
            .IsRequired();

        builder.Property(u => u.Email)
            .HasConversion(v => v.Value, v => EmailAddress.Create(v))
            .HasMaxLength(EmailAddress.MaxLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(u => u.HomePage)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => HomePageUrl.CreateOrNull(v))
            .HasMaxLength(HomePageUrl.MaxLength)
            .IsUnicode(false);

        builder.Property(u => u.IsRegistered).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsUnicode(false);
        builder.Property(u => u.GoogleSubject).HasMaxLength(128).IsUnicode(false);
        builder.Property(u => u.ConfirmationTokenHash).HasMaxLength(128).IsUnicode(false);
        builder.Property(u => u.AvatarPath).HasMaxLength(512).IsUnicode(false);
        builder.Property(u => u.ExternalAvatarUrl).HasMaxLength(512).IsUnicode(false);

        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.LastPostedAt).IsRequired();

        // The identity of a commenter is the (name, e-mail) pair they type. Unique, so two requests
        // racing to register the same visitor cannot create two rows — the loser retries and finds
        // the winner's row.
        builder.HasIndex(u => new { u.UserName, u.Email })
            .IsUnique()
            .HasDatabaseName("UX_Users_UserName_Email");

        builder.HasIndex(u => u.Email).HasDatabaseName("IX_Users_Email");

        // An address belongs to at most one account. Filtered, because guests share addresses
        // freely — two people called Ann and Anna may both have typed the same one — and only a
        // registered owner reserves it.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[IsRegistered] = 1")
            .HasDatabaseName("UX_Users_Email_Registered");

        // One Google identity, one account.
        builder.HasIndex(u => u.GoogleSubject)
            .IsUnique()
            .HasFilter("[GoogleSubject] IS NOT NULL")
            .HasDatabaseName("UX_Users_GoogleSubject");
    }
}
