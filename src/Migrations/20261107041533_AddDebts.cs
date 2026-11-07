using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddDebts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Debt",
                columns: table => new
                {
                    DebtId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BalanceAsOf = table.Column<DateOnly>(type: "date", nullable: true),
                    Apr = table.Column<decimal>(type: "decimal(6,3)", nullable: false),
                    MinimumPayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DueDay = table.Column<int>(type: "int", nullable: true),
                    PromoApr = table.Column<decimal>(type: "decimal(6,3)", nullable: true),
                    PromoEndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsDeferredInterest = table.Column<bool>(type: "bit", nullable: false),
                    DetailsConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Debt", x => x.DebtId);
                    table.ForeignKey(
                        name: "FK_Debt_FinancialAccount_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccount",
                        principalColumn: "FinancialAccountId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DebtPlan",
                columns: table => new
                {
                    DebtPlanId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MonthlyAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Strategy = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CustomOrderJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BaselineJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtPlan", x => x.DebtPlanId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Debt_FinancialAccountId",
                table: "Debt",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Debt_UserId_FinancialAccountId",
                table: "Debt",
                columns: new[] { "UserId", "FinancialAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_DebtPlan_UserId",
                table: "DebtPlan",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Debt");

            migrationBuilder.DropTable(
                name: "DebtPlan");
        }
    }
}
