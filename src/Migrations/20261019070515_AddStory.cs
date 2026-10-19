using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddStory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StoryId",
                table: "Photo",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Story",
                columns: table => new
                {
                    StoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                    VenueName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    HeroPhotoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SeoTitle = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Story", x => x.StoryId);
                    table.ForeignKey(
                        name: "FK_Story_Photo_HeroPhotoId",
                        column: x => x.HeroPhotoId,
                        principalTable: "Photo",
                        principalColumn: "PhotoId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Photo_StoryId_SortOrder",
                table: "Photo",
                columns: new[] { "StoryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "AK_Story_Slug",
                table: "Story",
                columns: new[] { "Slug", "TenantId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Story_Category_Status",
                table: "Story",
                columns: new[] { "Category", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Story_HeroPhotoId",
                table: "Story",
                column: "HeroPhotoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Photo_Story_StoryId",
                table: "Photo",
                column: "StoryId",
                principalTable: "Story",
                principalColumn: "StoryId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Photo_Story_StoryId",
                table: "Photo");

            migrationBuilder.DropTable(
                name: "Story");

            migrationBuilder.DropIndex(
                name: "IX_Photo_StoryId_SortOrder",
                table: "Photo");

            migrationBuilder.DropColumn(
                name: "StoryId",
                table: "Photo");
        }
    }
}
