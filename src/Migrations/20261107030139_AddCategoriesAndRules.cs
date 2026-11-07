using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoriesAndRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "FinancialCategory",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "FinancialCategory",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Flexible");

            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "FinancialCategory",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "FinancialCategory",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "FinancialCategory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SystemKey",
                table: "FinancialCategory",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "FinancialCategory",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "FinancialCategory",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Categories were one global list; each now belongs to a person (docs/budget-plan.md, Phase 2.1).
            migrationBuilder.Sql(@"-- A category used by budgets belongs to the person whose budgets use it most
SELECT x.FinancialCategoryId, x.UserId, x.TenantId,
       ROW_NUMBER() OVER (PARTITION BY x.FinancialCategoryId ORDER BY COUNT(*) DESC, x.UserId, x.TenantId) AS Rn
INTO #CategoryOwners
FROM (
    SELECT bc.FinancialCategoryId, b.UserId, b.TenantId FROM BudgetCategory bc JOIN Budget b ON b.BudgetId = bc.BudgetId
    UNION ALL
    SELECT tc.FinancialCategoryId, b.UserId, b.TenantId FROM TransactionCategory tc JOIN Budget b ON b.BudgetId = tc.BudgetId
) x
GROUP BY x.FinancialCategoryId, x.UserId, x.TenantId;

UPDATE c SET c.UserId = o.UserId, c.TenantId = o.TenantId
FROM FinancialCategory c JOIN #CategoryOwners o ON o.FinancialCategoryId = c.FinancialCategoryId AND o.Rn = 1;

-- Anyone else who used it gets their own copy, and their budget lines and tags point at the copy
CREATE TABLE #CategoryCopies (OldId int, UserId nvarchar(200), TenantId nvarchar(64), NewId int);
MERGE FinancialCategory AS target
USING (SELECT c.FinancialCategoryId, c.Name, c.CreatedByUserId, c.CreatedAt, c.ModifiedByUserId, c.ModifiedAt,
              o.UserId AS OwnerUserId, o.TenantId AS OwnerTenantId
       FROM FinancialCategory c JOIN #CategoryOwners o ON o.FinancialCategoryId = c.FinancialCategoryId AND o.Rn > 1) AS source
ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (Name, UserId, TenantId, [Group], IsArchived, SortOrder, IsDeleted, CreatedByUserId, CreatedAt, ModifiedByUserId, ModifiedAt)
    VALUES (source.Name, source.OwnerUserId, source.OwnerTenantId, 'Flexible', 0, 0, 0, source.CreatedByUserId, source.CreatedAt, source.ModifiedByUserId, source.ModifiedAt)
OUTPUT source.FinancialCategoryId, source.OwnerUserId, source.OwnerTenantId, inserted.FinancialCategoryId INTO #CategoryCopies;

UPDATE bc SET bc.FinancialCategoryId = m.NewId
FROM BudgetCategory bc JOIN Budget b ON b.BudgetId = bc.BudgetId
JOIN #CategoryCopies m ON m.OldId = bc.FinancialCategoryId AND m.UserId = b.UserId AND m.TenantId = b.TenantId;

UPDATE tc SET tc.FinancialCategoryId = m.NewId
FROM TransactionCategory tc JOIN Budget b ON b.BudgetId = tc.BudgetId
JOIN #CategoryCopies m ON m.OldId = tc.FinancialCategoryId AND m.UserId = b.UserId AND m.TenantId = b.TenantId;

-- Categories no budget used: a copy for everyone who has linked a bank, then the unowned originals go
INSERT INTO FinancialCategory (Name, UserId, TenantId, [Group], IsArchived, SortOrder, IsDeleted, CreatedByUserId, CreatedAt, ModifiedByUserId, ModifiedAt)
SELECT c.Name, u.ExternalUserId, u.TenantId, 'Flexible', 0, 0, 0, c.CreatedByUserId, c.CreatedAt, c.ModifiedByUserId, c.ModifiedAt
FROM FinancialCategory c
CROSS JOIN (SELECT DISTINCT us.ExternalUserId, us.TenantId
            FROM [User] us JOIN InstitutionAccessItemUser iu ON iu.UserId = us.UserId) u
WHERE c.UserId = '';

DELETE FROM FinancialCategory WHERE UserId = '';

DROP TABLE #CategoryOwners;
DROP TABLE #CategoryCopies;
");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "BankTransaction",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategorySource",
                table: "BankTransaction",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClassifiedAt",
                table: "BankTransaction",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MerchantKey",
                table: "BankTransaction",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "BankTransaction",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Treatment",
                table: "BankTransaction",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Spending");

            migrationBuilder.AddColumn<string>(
                name: "TreatmentSource",
                table: "BankTransaction",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Auto");

            migrationBuilder.CreateTable(
                name: "CategoryRule",
                columns: table => new
                {
                    CategoryRuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Match = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SetCategoryId = table.Column<int>(type: "int", nullable: true),
                    SetTreatment = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryRule", x => x.CategoryRuleId);
                    table.ForeignKey(
                        name: "FK_CategoryRule_FinancialCategory_SetCategoryId",
                        column: x => x.SetCategoryId,
                        principalTable: "FinancialCategory",
                        principalColumn: "FinancialCategoryId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TransactionSplit",
                columns: table => new
                {
                    TransactionSplitId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionSplit", x => x.TransactionSplitId);
                    table.ForeignKey(
                        name: "FK_TransactionSplit_BankTransaction_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalTable: "BankTransaction",
                        principalColumn: "BankTransactionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TransactionSplit_FinancialCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "FinancialCategory",
                        principalColumn: "FinancialCategoryId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialCategory_UserId_SystemKey",
                table: "FinancialCategory",
                columns: new[] { "UserId", "SystemKey" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_CategoryId",
                table: "BankTransaction",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRule_SetCategoryId",
                table: "CategoryRule",
                column: "SetCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRule_UserId",
                table: "CategoryRule",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionSplit_BankTransactionId",
                table: "TransactionSplit",
                column: "BankTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionSplit_CategoryId",
                table: "TransactionSplit",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankTransaction_FinancialCategory_CategoryId",
                table: "BankTransaction",
                column: "CategoryId",
                principalTable: "FinancialCategory",
                principalColumn: "FinancialCategoryId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankTransaction_FinancialCategory_CategoryId",
                table: "BankTransaction");

            migrationBuilder.DropTable(
                name: "CategoryRule");

            migrationBuilder.DropTable(
                name: "TransactionSplit");

            migrationBuilder.DropIndex(
                name: "IX_FinancialCategory_UserId_SystemKey",
                table: "FinancialCategory");

            migrationBuilder.DropIndex(
                name: "IX_BankTransaction_CategoryId",
                table: "BankTransaction");

            migrationBuilder.DropColumn(
                name: "Group",
                table: "FinancialCategory");

            migrationBuilder.DropColumn(
                name: "Icon",
                table: "FinancialCategory");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "FinancialCategory");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "FinancialCategory");

            migrationBuilder.DropColumn(
                name: "SystemKey",
                table: "FinancialCategory");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FinancialCategory");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "FinancialCategory");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "BankTransaction");

            migrationBuilder.DropColumn(
                name: "CategorySource",
                table: "BankTransaction");

            migrationBuilder.DropColumn(
                name: "ClassifiedAt",
                table: "BankTransaction");

            migrationBuilder.DropColumn(
                name: "MerchantKey",
                table: "BankTransaction");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "BankTransaction");

            migrationBuilder.DropColumn(
                name: "Treatment",
                table: "BankTransaction");

            migrationBuilder.DropColumn(
                name: "TreatmentSource",
                table: "BankTransaction");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "FinancialCategory",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);
        }
    }
}
