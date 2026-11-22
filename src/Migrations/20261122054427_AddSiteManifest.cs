using System;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteManifest",
                columns: table => new
                {
                    SiteManifestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteManifest", x => x.SiteManifestId);
                });

            migrationBuilder.CreateIndex(
                name: "AK_SiteManifest_Tenant",
                table: "SiteManifest",
                column: "TenantId",
                unique: true,
                filter: "[IsDeleted] = 0");

            // Lenient, like every other tenant table, until the hosts that don't declare a tenant are retired
            migrationBuilder.AddTenantRowLevelSecurity("SiteManifest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTenantRowLevelSecurity("SiteManifest");

            migrationBuilder.DropTable(
                name: "SiteManifest");
        }
    }
}
