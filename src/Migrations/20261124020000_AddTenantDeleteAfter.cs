using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantDeleteAfter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TenantInfo belongs to the tenant store (TenantContext), which has no migrations of its own.
            // When a site marked as being deleted is erased; until then it can be brought back.
            // The tenant store reads every column, so this must be applied before a host runs this version.
            migrationBuilder.Sql(@"
ALTER TABLE [TenantInfo] ADD [DeleteAfter] datetimeoffset NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE [TenantInfo] DROP COLUMN [DeleteAfter];
");
        }
    }
}
