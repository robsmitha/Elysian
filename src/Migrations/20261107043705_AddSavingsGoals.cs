using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddSavingsGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavingsGoal",
                columns: table => new
                {
                    SavingsGoalId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CycleStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Cycle = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CycleEnd = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FundingMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LinkedAccountId = table.Column<int>(type: "int", nullable: true),
                    StartingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    PastCyclesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsGoal", x => x.SavingsGoalId);
                    table.ForeignKey(
                        name: "FK_SavingsGoal_FinancialAccount_LinkedAccountId",
                        column: x => x.LinkedAccountId,
                        principalTable: "FinancialAccount",
                        principalColumn: "FinancialAccountId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GoalContribution",
                columns: table => new
                {
                    GoalContributionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SavingsGoalId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BankTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalContribution", x => x.GoalContributionId);
                    table.ForeignKey(
                        name: "FK_GoalContribution_SavingsGoal_SavingsGoalId",
                        column: x => x.SavingsGoalId,
                        principalTable: "SavingsGoal",
                        principalColumn: "SavingsGoalId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoalContribution_BankTransactionId",
                table: "GoalContribution",
                column: "BankTransactionId",
                unique: true,
                filter: "[BankTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GoalContribution_SavingsGoalId",
                table: "GoalContribution",
                column: "SavingsGoalId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsGoal_LinkedAccountId",
                table: "SavingsGoal",
                column: "LinkedAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsGoal_UserId",
                table: "SavingsGoal",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalContribution");

            migrationBuilder.DropTable(
                name: "SavingsGoal");
        }
    }
}
