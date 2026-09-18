using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Threadline.Comments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixProcessedImageContentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Processed images are stored re-encoded as PNG, but until now kept the type they were
            // uploaded as, so a PNG was served labelled image/jpeg or image/gif.
            // Kind 1 = Image, Status 1 = Ready.
            migrationBuilder.Sql(
                """
                UPDATE [Attachments]
                SET [ContentType] = 'image/png'
                WHERE [Kind] = 1 AND [Status] = 1 AND [StoragePath] LIKE '%.png' AND [ContentType] <> 'image/png'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the upload type was overwritten and is not recorded anywhere else. The
            // old value was wrong anyway, so there is nothing worth restoring.
        }
    }
}
