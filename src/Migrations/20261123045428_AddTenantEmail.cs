using System;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantContactEmail",
                columns: table => new
                {
                    TenantContactEmailId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PendingEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CodeExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CodeSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantContactEmail", x => x.TenantContactEmailId);
                });

            migrationBuilder.CreateTable(
                name: "TenantEmailCount",
                columns: table => new
                {
                    TenantEmailCountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantEmailCount", x => x.TenantEmailCountId);
                });

            migrationBuilder.CreateIndex(
                name: "AK_TenantContactEmail_Tenant",
                table: "TenantContactEmail",
                column: "TenantId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "AK_TenantEmailCount_Day",
                table: "TenantEmailCount",
                columns: new[] { "TenantId", "Day" },
                unique: true);

            // Lenient, like every other tenant table, until the hosts that don't declare a tenant are retired
            migrationBuilder.AddTenantRowLevelSecurity("TenantContactEmail");
            migrationBuilder.AddTenantRowLevelSecurity("TenantEmailCount");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTenantRowLevelSecurity("TenantContactEmail");
            migrationBuilder.DropTenantRowLevelSecurity("TenantEmailCount");

            migrationBuilder.DropTable(
                name: "TenantContactEmail");

            migrationBuilder.DropTable(
                name: "TenantEmailCount");
        }
    }
}
