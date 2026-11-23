using System;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDomainsAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantDomain",
                columns: table => new
                {
                    TenantDomainId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantDomain", x => x.TenantDomainId);
                });

            migrationBuilder.CreateIndex(
                name: "AK_TenantDomain_Host",
                table: "TenantDomain",
                column: "Host",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "AK_TenantDomain_Primary",
                table: "TenantDomain",
                column: "TenantId",
                unique: true,
                filter: "[IsPrimary] = 1 AND [IsDeleted] = 0");

            // Lenient, like every other tenant table, until the hosts that don't declare a tenant are retired
            migrationBuilder.AddTenantRowLevelSecurity("TenantDomain");

            // TenantInfo belongs to the tenant store (TenantContext), which has no migrations of its own.
            // The columns are nullable, so hosts on an older Elysian keep reading the table unchanged.
            migrationBuilder.Sql(@"
ALTER TABLE [TenantInfo] ADD
    [TemplateKey] nvarchar(64) NULL,
    [TemplateVersion] int NULL,
    [ContentVersion] int NULL;
");

            // The one site that is live on the platform today, at the address it already answers on
            migrationBuilder.Sql(@"
INSERT INTO [TenantDomain] ([TenantId], [Host], [Kind], [Status], [IsPrimary], [CreatedByUserId], [CreatedAt], [ModifiedByUserId], [ModifiedAt], [IsDeleted])
SELECT t.[Id], 'www.aspensmitha.com', 'custom', 'active', 1, 'system', SYSDATETIMEOFFSET(), 'system', SYSDATETIMEOFFSET(), 0
FROM [TenantInfo] t
WHERE t.[Identifier] = 'aspensmitha';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE [TenantInfo] DROP COLUMN [TemplateKey], [TemplateVersion], [ContentVersion];
");

            migrationBuilder.DropTenantRowLevelSecurity("TenantDomain");

            migrationBuilder.DropTable(
                name: "TenantDomain");
        }
    }
}
