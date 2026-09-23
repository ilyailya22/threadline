using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Threadline.Comments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentsProcessedOnUpload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attachments_Pending",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Attachments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "Attachments",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessedAt",
                table: "Attachments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Status",
                table: "Attachments",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Pending",
                table: "Attachments",
                column: "Status",
                filter: "[Status] = 0");
        }
    }
}
