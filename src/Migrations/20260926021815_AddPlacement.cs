using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Placement",
                columns: table => new
                {
                    PlacementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PhotoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Placement", x => x.PlacementId);
                    table.ForeignKey(
                        name: "FK_Placement_Photo_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photo",
                        principalColumn: "PhotoId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "AK_Placement_Key",
                table: "Placement",
                columns: new[] { "Key", "TenantId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Placement_PhotoId",
                table: "Placement",
                column: "PhotoId");

            // Each old slot filled several spots; give every spot its own row so they can differ from now on
            migrationBuilder.Sql($"""
                INSERT INTO [Placement] ([Key], [PhotoId], [TenantId], [CreatedByUserId], [CreatedAt], [ModifiedByUserId], [ModifiedAt], [IsDeleted])
                SELECT m.[Key], p.[PhotoId], p.[TenantId], p.[ModifiedByUserId], SYSDATETIMEOFFSET(), p.[ModifiedByUserId], SYSDATETIMEOFFSET(), 0
                FROM [Photo] p
                JOIN (VALUES {SlotToSpots}) m([Slot], [Key]) ON m.[Slot] = p.[Slot]
                WHERE p.[IsDeleted] = 0;
                """);

            migrationBuilder.DropIndex(
                name: "AK_Photo_Slot",
                table: "Photo");

            migrationBuilder.DropColumn(
                name: "Slot",
                table: "Photo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slot",
                table: "Photo",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Best effort: a slot goes back to whichever photo fills its first spot
            migrationBuilder.Sql($"""
                UPDATE p SET [Slot] = s.[Slot]
                FROM [Photo] p
                JOIN (
                    SELECT m.[Slot], pl.[PhotoId],
                        ROW_NUMBER() OVER (PARTITION BY m.[Slot], pl.[TenantId] ORDER BY m.[Key]) AS rn,
                        ROW_NUMBER() OVER (PARTITION BY pl.[PhotoId] ORDER BY m.[Key]) AS photoRn
                    FROM [Placement] pl
                    JOIN (VALUES {SlotToSpots}) m([Slot], [Key]) ON m.[Key] = pl.[Key]
                    WHERE pl.[IsDeleted] = 0
                ) s ON s.[PhotoId] = p.[PhotoId] AND s.rn = 1 AND s.photoRn = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "AK_Photo_Slot",
                table: "Photo",
                columns: new[] { "Slot", "TenantId" },
                unique: true,
                filter: "[Slot] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.DropTable(
                name: "Placement");
        }

        /// <summary>
        /// Old photo-named slots and the site spots each one filled (spot keys are defined by the site)
        /// </summary>
        private const string SlotToSpots = """
            ('maternityAnnouncement', 'home-hero-1'),
            ('maternityAnnouncement', 'home-specialty-maternity'),
            ('maternityAnnouncement', 'home-recent-bottom'),
            ('bridePortrait', 'home-hero-2'),
            ('bridePortrait', 'home-specialty-weddings'),
            ('bridePortrait', 'about-cta'),
            ('coupleJump', 'home-hero-3'),
            ('coupleJump', 'home-recent-large'),
            ('familyLittleBrother', 'home-hero-4'),
            ('familyLittleBrother', 'home-specialty-family'),
            ('familyLittleBrother', 'home-cta'),
            ('coupleCloseUp', 'home-hero-5'),
            ('coupleCloseUp', 'home-recent-top'),
            ('aspenPortrait', 'home-intro'),
            ('aspenPortrait', 'contact-portrait'),
            ('aspenFamily', 'about-story')
            """;
    }
}
