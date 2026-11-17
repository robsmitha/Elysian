using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformsAndModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantModule",
                columns: table => new
                {
                    TenantModuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModuleKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BillingReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantModule", x => x.TenantModuleId);
                });

            migrationBuilder.CreateIndex(
                name: "AK_TenantModule_ModuleKey",
                table: "TenantModule",
                columns: new[] { "ModuleKey", "TenantId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // TenantInfo belongs to the tenant store (TenantContext), which has no migrations of its own.
            // The columns are nullable, so hosts on an older Elysian keep reading the table unchanged.
            migrationBuilder.Sql(@"
ALTER TABLE [TenantInfo] ADD
    [Platform] nvarchar(64) NULL,
    [Runtime] nvarchar(64) NULL,
    [Status] nvarchar(64) NULL;
");

            // The two sites that exist today, with the modules each one already uses
            migrationBuilder.Sql(@"
UPDATE [TenantInfo] SET [Platform] = 'photography', [Runtime] = 'custom', [Status] = 'active' WHERE [Identifier] = 'aspensmitha';
UPDATE [TenantInfo] SET [Platform] = 'personal', [Runtime] = 'custom', [Status] = 'active' WHERE [Identifier] = 'robsmitha';

INSERT INTO [TenantModule] ([ModuleKey], [Status], [BillingReference], [TenantId], [CreatedByUserId], [CreatedAt], [ModifiedByUserId], [ModifiedAt], [IsDeleted])
SELECT m.[ModuleKey], 1, NULL, t.[Id], 'system', SYSDATETIMEOFFSET(), 'system', SYSDATETIMEOFFSET(), 0
FROM [TenantInfo] t
JOIN (VALUES
    ('aspensmitha', 'photos'),
    ('aspensmitha', 'booking'),
    ('aspensmitha', 'instagram'),
    ('robsmitha', 'finance'),
    ('robsmitha', 'assistant'),
    ('robsmitha', 'congress'),
    ('robsmitha', 'code')
) m([Identifier], [ModuleKey]) ON m.[Identifier] = t.[Identifier];
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE [TenantInfo] DROP COLUMN [Platform], [Runtime], [Status];
");

            migrationBuilder.DropTable(
                name: "TenantModule");
        }
    }
}
