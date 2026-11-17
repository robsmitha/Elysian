using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elysian.Migrations
{
    /// <inheritdoc />
    public partial class AddRowLevelSecurity : Migration
    {
        /// <summary>
        /// The tenant tables as they are at this migration. Later tables add themselves in their own migration,
        /// so this list must never change.
        /// </summary>
        private static readonly string[] Tables =
        [
            "AssistantAction", "AssistantUsage", "BankTransaction", "BillTracking", "Budget", "BudgetAccessItem",
            "BudgetCategory", "BudgetExcludedTransaction", "BudgetMonth", "BudgetTarget", "CategoryRule", "Debt",
            "DebtPlan", "FinancialAccount", "FinancialCategory", "GoalContribution", "IncomePayment", "IncomeSource",
            "IncomeStream", "InstagramPost", "InstitutionAccessItem", "InstitutionAccessItemUser", "ManagedUser",
            "NotificationSettings", "OAuthState", "OAuthToken", "Photo", "Placement", "PlaidWebhookEvent", "Product",
            "ProductImage", "ProductSession", "SavingsGoal", "Story", "SyncLease", "SyncRun", "TenantInvite",
            "TenantMembership", "TenantModule", "TenantSetting", "TransactionCategory", "TransactionSplit", "User",
            "UserAuditLog",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every table starts lenient: a connection that hasn't declared a tenant still sees and writes
            // everything, so hosts on an Elysian from before this migration are unaffected.
            foreach (var sql in RowLevelSecurity.CreateSql(Tables))
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RowLevelSecurity.DropSql())
            {
                migrationBuilder.Sql(sql);
            }
        }
    }
}
