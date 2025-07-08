using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "AK_InstitutionAccessItem_InstitutionId_UserId",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "InstitutionAccessItem");

            migrationBuilder.AlterColumn<string>(
                name: "InstitutionId",
                table: "InstitutionAccessItem",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateTable(
                name: "IncomeSource",
                columns: table => new
                {
                    IncomeSourceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionAccessItemId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AmountDue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IncomeSourceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentFrequency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    ExpectedPaymentMemos = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeSource", x => x.IncomeSourceId);
                    table.ForeignKey(
                        name: "FK_IncomeSource_InstitutionAccessItem_InstitutionAccessItemId",
                        column: x => x.InstitutionAccessItemId,
                        principalTable: "InstitutionAccessItem",
                        principalColumn: "InstitutionAccessItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionAccessItemUser",
                columns: table => new
                {
                    InstitutionAccessItemUserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    InstitutionAccessItemId = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionAccessItemUser", x => x.InstitutionAccessItemUserId);
                    table.ForeignKey(
                        name: "FK_InstitutionAccessItemUser_InstitutionAccessItem_InstitutionAccessItemId",
                        column: x => x.InstitutionAccessItemId,
                        principalTable: "InstitutionAccessItem",
                        principalColumn: "InstitutionAccessItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstitutionAccessItemUser_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomePayment",
                columns: table => new
                {
                    IncomePaymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncomeSourceId = table.Column<int>(type: "int", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentMemo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsManualAdjustment = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomePayment", x => x.IncomePaymentId);
                    table.ForeignKey(
                        name: "FK_IncomePayment_IncomeSource_IncomeSourceId",
                        column: x => x.IncomeSourceId,
                        principalTable: "IncomeSource",
                        principalColumn: "IncomeSourceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncomePayment_IncomeSourceId",
                table: "IncomePayment",
                column: "IncomeSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomeSource_InstitutionAccessItemId",
                table: "IncomeSource",
                column: "InstitutionAccessItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionAccessItemUser_InstitutionAccessItemId",
                table: "InstitutionAccessItemUser",
                column: "InstitutionAccessItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionAccessItemUser_UserId",
                table: "InstitutionAccessItemUser",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncomePayment");

            migrationBuilder.DropTable(
                name: "InstitutionAccessItemUser");

            migrationBuilder.DropTable(
                name: "IncomeSource");

            migrationBuilder.AlterColumn<string>(
                name: "InstitutionId",
                table: "InstitutionAccessItem",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "InstitutionAccessItem",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "AK_InstitutionAccessItem_InstitutionId_UserId",
                table: "InstitutionAccessItem",
                columns: new[] { "InstitutionId", "UserId", "TenantId" },
                unique: true);
        }
    }
}
