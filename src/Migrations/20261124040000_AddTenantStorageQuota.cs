using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantStorageQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TenantInfo belongs to the tenant store (TenantContext), which has no migrations of its own.
            // A site's own limit on uploaded photos, in gigabytes; null for the platform's default.
            // The tenant store reads every column, so this must be applied before a host runs this version.
            migrationBuilder.Sql(@"
ALTER TABLE [TenantInfo] ADD [StorageQuotaGb] int NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE [TenantInfo] DROP COLUMN [StorageQuotaGb];
");
        }
    }
}
