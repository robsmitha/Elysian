using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaidSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ItemId",
                table: "InstitutionAccessItem",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "AccessToken",
                table: "InstitutionAccessItem",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "AccessTokenCiphertext",
                table: "InstitutionAccessItem",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsentExpiresAt",
                table: "InstitutionAccessItem",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "InstitutionAccessItem",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstitutionColor",
                table: "InstitutionAccessItem",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstitutionLogo",
                table: "InstitutionAccessItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstitutionName",
                table: "InstitutionAccessItem",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncRequestedAt",
                table: "InstitutionAccessItem",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncedAt",
                table: "InstitutionAccessItem",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NewAccountsAvailable",
                table: "InstitutionAccessItem",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedAt",
                table: "InstitutionAccessItem",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "InstitutionAccessItem",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Healthy");

            migrationBuilder.AddColumn<string>(
                name: "TransactionsCursor",
                table: "InstitutionAccessItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "InstitutionAccessItem",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinancialAccount",
                columns: table => new
                {
                    FinancialAccountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionAccessItemId = table.Column<int>(type: "int", nullable: false),
                    PlaidAccountId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OfficialName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Mask = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Subtype = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AvailableBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IsoCurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BalanceAsOf = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BudgetRole = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialAccount", x => x.FinancialAccountId);
                    table.ForeignKey(
                        name: "FK_FinancialAccount_InstitutionAccessItem_InstitutionAccessItemId",
                        column: x => x.InstitutionAccessItemId,
                        principalTable: "InstitutionAccessItem",
                        principalColumn: "InstitutionAccessItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaidWebhookEvent",
                columns: table => new
                {
                    PlaidWebhookEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BodySha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WebhookType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WebhookCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaidWebhookEvent", x => x.PlaidWebhookEventId);
                });

            migrationBuilder.CreateTable(
                name: "SyncRun",
                columns: table => new
                {
                    SyncRunId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionAccessItemId = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Modified = table.Column<int>(type: "int", nullable: false),
                    Removed = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRun", x => x.SyncRunId);
                    table.ForeignKey(
                        name: "FK_SyncRun_InstitutionAccessItem_InstitutionAccessItemId",
                        column: x => x.InstitutionAccessItemId,
                        principalTable: "InstitutionAccessItem",
                        principalColumn: "InstitutionAccessItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BankTransaction",
                columns: table => new
                {
                    BankTransactionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: false),
                    PlaidTransactionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PendingTransactionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    AuthorizedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BudgetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MerchantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MerchantEntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PfcPrimary = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PfcDetailed = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PfcConfidence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PaymentChannel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TransactionType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsoCurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Pending = table.Column<bool>(type: "bit", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransaction", x => x.BankTransactionId);
                    table.ForeignKey(
                        name: "FK_BankTransaction_FinancialAccount_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccount",
                        principalColumn: "FinancialAccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionAccessItem_ItemId",
                table: "InstitutionAccessItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_FinancialAccountId_BudgetDate",
                table: "BankTransaction",
                columns: new[] { "FinancialAccountId", "BudgetDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransaction_PlaidTransactionId",
                table: "BankTransaction",
                column: "PlaidTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAccount_InstitutionAccessItemId",
                table: "FinancialAccount",
                column: "InstitutionAccessItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAccount_PlaidAccountId",
                table: "FinancialAccount",
                column: "PlaidAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaidWebhookEvent_BodySha256",
                table: "PlaidWebhookEvent",
                column: "BodySha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncRun_InstitutionAccessItemId_StartedAt",
                table: "SyncRun",
                columns: new[] { "InstitutionAccessItemId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankTransaction");

            migrationBuilder.DropTable(
                name: "PlaidWebhookEvent");

            migrationBuilder.DropTable(
                name: "SyncRun");

            migrationBuilder.DropTable(
                name: "FinancialAccount");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionAccessItem_ItemId",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "AccessTokenCiphertext",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "ConsentExpiresAt",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "InstitutionColor",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "InstitutionLogo",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "InstitutionName",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "LastSyncRequestedAt",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "NewAccountsAvailable",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "TransactionsCursor",
                table: "InstitutionAccessItem");

            migrationBuilder.DropColumn(
                name: "WebhookUrl",
                table: "InstitutionAccessItem");

            migrationBuilder.AlterColumn<string>(
                name: "ItemId",
                table: "InstitutionAccessItem",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "AccessToken",
                table: "InstitutionAccessItem",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
