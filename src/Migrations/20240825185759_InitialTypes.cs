using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class InitialTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MerchantTypes
            migrationBuilder.InsertData(
                table: "MerchantType",
                columns: new[] { "MerchantTypeId", "Name", "Description", "CreatedByUserId", "CreatedAt", "ModifiedByUserId", "ModifiedAt", "IsDeleted" },
                values: new object[]
                {
                    1,
                    "Portfolio",
                    "Online portfolio with extended features",
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    false
                }
            );

            migrationBuilder.InsertData(
                table: "MerchantType",
                columns: new[] { "MerchantTypeId", "Name", "Description", "CreatedByUserId", "CreatedAt", "ModifiedByUserId", "ModifiedAt", "IsDeleted" },
                values: new object[]
                {
                    2,
                    "Store",
                    "Physical or online store",
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    false
                }
            );

            // Merchants
            migrationBuilder.InsertData(
                table: "Merchant",
                columns: new[]
                {
                    "MerchantId",
                    "MerchantIdentifier",
                    "Name",
                    "Description",
                    "WebsiteUrl",
                    "SelfBoardingApplication",
                    "IsBillable",
                    "CreatedByUserId",
                    "CreatedAt",
                    "ModifiedByUserId",
                    "ModifiedAt",
                    "IsDeleted",
                    "MerchantTypeId"
                },
                values: new object[]
                {
                    1,
                    "robsmitha",
                    "robsmitha.com",
                    "Merchant for robsmitha.com",
                    "https://robsmitha.com",
                    false,
                    false,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    false,
                    1
                }
            );

            migrationBuilder.InsertData(
                table: "Merchant",
                columns: new[]
                {
                    "MerchantId",
                    "MerchantIdentifier",
                    "Name",
                    "Description",
                    "WebsiteUrl",
                    "SelfBoardingApplication",
                    "IsBillable",
                    "CreatedByUserId",
                    "CreatedAt",
                    "ModifiedByUserId",
                    "ModifiedAt",
                    "IsDeleted",
                    "MerchantTypeId"
                },
                values: new object[]
                {
                    2,
                    "geekscloset",
                    "geekscloset.com",
                    "Merchant for geekscloset.com",
                    "https://geekscloset.com",
                    false,
                    false,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    false,
                    2
                }
            );

            // PriceType
            migrationBuilder.InsertData(
                table: "PriceType",
                columns: new[]
                {
                    "PriceTypeId",
                    "Name",
                    "Description",
                    "IsVariableCost",
                    "CreatedByUserId",
                    "CreatedAt",
                    "ModifiedByUserId",
                    "ModifiedAt",
                    "IsDeleted"
                },
                values: new object[]
                {
                    1,
                    "Fixed",
                    "Fixed pricing",
                    false,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    false
                }
            );

            migrationBuilder.InsertData(
                table: "PriceType",
                columns: new[]
                {
                    "PriceTypeId",
                    "Name",
                    "Description",
                    "IsVariableCost",
                    "CreatedByUserId",
                    "CreatedAt",
                    "ModifiedByUserId",
                    "ModifiedAt",
                    "IsDeleted"
                },
                values: new object[]
                {
                    2,
                    "Variable",
                    "Variable cost pricing",
                    true,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    false
                }
            );

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
                    1,
                    "Trackables",
                    "Products that are trackable and can be looked up through site functionality.",
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    Guid.Empty.ToString(),
                    DateTime.UtcNow,
                    false
                }
            );

            // UnitType
            migrationBuilder.InsertData(
                table: "UnitType",
                columns: new[]
                {
                    "UnitTypeId",
                    "Name",
                    "Description",
                    "PerUnit",
                    "CreatedByUserId",
                    "CreatedAt",
                    "ModifiedByUserId",
                    "ModifiedAt",
                    "IsDeleted"
                },
                values: new object[]
                {
                    1,
                    "Quantity",
                    "Unit is measured in quantities",
                    "Each",
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

        }
    }
}
