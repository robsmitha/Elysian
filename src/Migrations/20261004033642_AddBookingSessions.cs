using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastValidatedAt",
                table: "OAuthToken",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshToken",
                table: "OAuthToken",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductSession",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Collection = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Features = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PortfolioCategory = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CoverPhotoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsBookable = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSession", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_ProductSession_Photo_CoverPhotoId",
                        column: x => x.CoverPhotoId,
                        principalTable: "Photo",
                        principalColumn: "PhotoId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProductSession_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSession_CoverPhotoId",
                table: "ProductSession",
                column: "CoverPhotoId");

            // ProductType
            migrationBuilder.InsertData(
                table: "ProductType",
                columns: new[]
                {
                    "ProductTypeId",
                    "Name",
                    "Description",
                    "CreatedByUserId",
                    "CreatedAt",
                    "ModifiedByUserId",
                    "ModifiedAt",
                    "IsDeleted"
                },
                values: new object[]
                {
                    2,
                    "Session",
                    "Photo sessions clients can view and book online.",
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    false
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductSession");

            migrationBuilder.DeleteData(
                table: "ProductType",
                keyColumn: "ProductTypeId",
                keyValue: 2);

            migrationBuilder.DropColumn(
                name: "LastValidatedAt",
                table: "OAuthToken");

            migrationBuilder.DropColumn(
                name: "RefreshToken",
                table: "OAuthToken");
        }
    }
}
