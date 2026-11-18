using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "AK_User_ExternalUserId",
                table: "User");

            migrationBuilder.CreateTable(
                name: "TenantInvite",
                columns: table => new
                {
                    TenantInviteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RedeemedBySubject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantInvite", x => x.TenantInviteId);
                });

            migrationBuilder.CreateTable(
                name: "TenantMembership",
                columns: table => new
                {
                    TenantMembershipId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMembership", x => x.TenantMembershipId);
                });

            migrationBuilder.CreateIndex(
                name: "AK_User_ExternalUserId",
                table: "User",
                columns: new[] { "ExternalUserId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_TenantInvite_CodeHash",
                table: "TenantInvite",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_TenantMembership_Email",
                table: "TenantMembership",
                columns: new[] { "Email", "TenantId" },
                unique: true,
                filter: "[Email] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "AK_TenantMembership_Subject",
                table: "TenantMembership",
                columns: new[] { "Subject", "TenantId" },
                unique: true,
                filter: "[Subject] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantInvite");

            migrationBuilder.DropTable(
                name: "TenantMembership");

            migrationBuilder.DropIndex(
                name: "AK_User_ExternalUserId",
                table: "User");

            migrationBuilder.CreateIndex(
                name: "AK_User_ExternalUserId",
                table: "User",
                column: "ExternalUserId",
                unique: true);
        }
    }
}
