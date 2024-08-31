using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class FixUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "AK_ProductImage_StorageId",
                table: "ProductImage");

            migrationBuilder.DropIndex(
                name: "AK_Product_SerialNumber",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "AK_OAuthProvider_UserId",
                table: "OAuthToken");

            migrationBuilder.DropIndex(
                name: "AK_InstitutionAccessItem_InstitutionId_UserId",
                table: "InstitutionAccessItem");

            migrationBuilder.CreateIndex(
                name: "AK_ProductImage_StorageId",
                table: "ProductImage",
                columns: new[] { "StorageId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_Product_SerialNumber",
                table: "Product",
                columns: new[] { "SerialNumber", "TenantId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "AK_OAuthProvider_UserId",
                table: "OAuthToken",
                columns: new[] { "OAuthProvider", "UserId", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_InstitutionAccessItem_InstitutionId_UserId",
                table: "InstitutionAccessItem",
                columns: new[] { "InstitutionId", "UserId", "TenantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "AK_ProductImage_StorageId",
                table: "ProductImage");

            migrationBuilder.DropIndex(
                name: "AK_Product_SerialNumber",
                table: "Product");

            migrationBuilder.DropIndex(
                name: "AK_OAuthProvider_UserId",
                table: "OAuthToken");

            migrationBuilder.DropIndex(
                name: "AK_InstitutionAccessItem_InstitutionId_UserId",
                table: "InstitutionAccessItem");

            migrationBuilder.CreateIndex(
                name: "AK_ProductImage_StorageId",
                table: "ProductImage",
                column: "StorageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_Product_SerialNumber",
                table: "Product",
                column: "SerialNumber",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "AK_OAuthProvider_UserId",
                table: "OAuthToken",
                columns: new[] { "OAuthProvider", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "AK_InstitutionAccessItem_InstitutionId_UserId",
                table: "InstitutionAccessItem",
                columns: new[] { "InstitutionId", "UserId" },
                unique: true);
        }
    }
}
