using Microsoft.EntityFrameworkCore.Migrations;
using System.Text;

namespace Elysian.Infrastructure.Context
{
    /// <summary>
    /// The database's own tenant check, behind the query filters the application adds. Every tenant table has a
    /// filter predicate (which rows a connection can see) and a block predicate (which rows it can write),
    /// both comparing the row's TenantId with the tenant the connection declared in SESSION_CONTEXT.
    ///
    /// A table is either lenient or strict. Lenient lets a connection that hasn't declared a tenant see and
    /// write everything, so hosts on an Elysian from before this existed keep working. Strict gives such a
    /// connection nothing. A connection that has declared a tenant is held to it in both modes.
    ///
    /// Adding a tenant table: add it to <see cref="ProtectedTables"/> (a test fails until you do) and call
    /// <see cref="RowLevelSecurityMigrations.AddTenantRowLevelSecurity"/> in its migration. Dropping one, or
    /// altering its TenantId column: call <see cref="RowLevelSecurityMigrations.DropTenantRowLevelSecurity"/> first.
    /// </summary>
    public static class RowLevelSecurity
    {
        public const string Schema = "rls";
        public const string Policy = "TenantPolicy";
        public const string LenientFunction = "fn_TenantLenient";
        public const string StrictFunction = "fn_TenantStrict";

        /// <summary>
        /// SESSION_CONTEXT key holding the tenant a connection acts for
        /// </summary>
        public const string TenantIdKey = "TenantId";

        /// <summary>
        /// SESSION_CONTEXT key set by platform work that spans tenants on purpose: migrations, finding a
        /// person's sites, redeeming an invite, queuing a job per tenant
        /// </summary>
        public const string BypassKey = "TenantBypass";

        /// <summary>
        /// Every table with a TenantId column
        /// </summary>
        public static readonly IReadOnlyList<string> ProtectedTables =
        [
            "AssistantAction", "AssistantUsage", "BankTransaction", "BillTracking", "Budget", "BudgetAccessItem",
            "BudgetCategory", "BudgetExcludedTransaction", "BudgetMonth", "BudgetTarget", "CategoryRule", "Debt",
            "DebtPlan", "FinancialAccount", "FinancialCategory", "GoalContribution", "IncomePayment", "IncomeSource",
            "IncomeStream", "InstagramPost", "InstitutionAccessItem", "InstitutionAccessItemUser", "ManagedUser",
            "NotificationSettings", "OAuthState", "OAuthToken", "Photo", "Placement", "PlaidWebhookEvent", "Product",
            "ProductImage", "ProductSession", "SavingsGoal", "SiteContentDraft", "SiteContentVersion", "SiteManifest", "Story", "SyncLease", "SyncRun", "TenantContactEmail",
            "TenantDomain", "TenantEmailCount", "TenantInvite", "TenantMembership", "TenantModule", "TenantSetting", "TransactionCategory", "TransactionSplit", "User",
            "UserAuditLog",
        ];

        /// <summary>
        /// Tables that belong to the platform or are shared reference data, so have no tenant to check
        /// </summary>
        public static readonly IReadOnlyList<string> UnprotectedTables =
        [
            "EdgeClient", "Merchant", "MerchantType", "PriceType", "ProductType", "UnitType",
        ];

        public static string FunctionFor(bool strict) => strict ? StrictFunction : LenientFunction;

        /// <summary>
        /// Creates the schema, both predicate functions and the policy, with every table in
        /// <paramref name="tables"/> in lenient mode. Each statement is its own batch.
        /// </summary>
        public static IEnumerable<string> CreateSql(IEnumerable<string> tables)
        {
            yield return $"IF SCHEMA_ID(N'{Schema}') IS NULL EXEC(N'CREATE SCHEMA [{Schema}]');";

            yield return $"""
                CREATE FUNCTION [{Schema}].[{LenientFunction}](@TenantId nvarchar(64))
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN SELECT 1 AS [Allowed]
                WHERE CAST(SESSION_CONTEXT(N'{BypassKey}') AS bit) = 1
                   OR SESSION_CONTEXT(N'{TenantIdKey}') IS NULL
                   OR @TenantId = CAST(SESSION_CONTEXT(N'{TenantIdKey}') AS nvarchar(64));
                """;

            yield return $"""
                CREATE FUNCTION [{Schema}].[{StrictFunction}](@TenantId nvarchar(64))
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN SELECT 1 AS [Allowed]
                WHERE CAST(SESSION_CONTEXT(N'{BypassKey}') AS bit) = 1
                   OR @TenantId = CAST(SESSION_CONTEXT(N'{TenantIdKey}') AS nvarchar(64));
                """;

            var builder = new StringBuilder($"CREATE SECURITY POLICY [{Schema}].[{Policy}]");
            var separator = Environment.NewLine + "    ";
            foreach (var table in tables)
            {
                builder.Append(separator).Append(Predicates("ADD", table, strict: false));
                separator = "," + Environment.NewLine + "    ";
            }
            builder.AppendLine().Append("    WITH (STATE = ON, SCHEMABINDING = ON);");
            yield return builder.ToString();
        }

        public static IEnumerable<string> DropSql()
        {
            yield return $"DROP SECURITY POLICY IF EXISTS [{Schema}].[{Policy}];";
            yield return $"DROP FUNCTION IF EXISTS [{Schema}].[{LenientFunction}];";
            yield return $"DROP FUNCTION IF EXISTS [{Schema}].[{StrictFunction}];";
            yield return $"IF SCHEMA_ID(N'{Schema}') IS NOT NULL EXEC(N'DROP SCHEMA [{Schema}]');";
        }

        public static string AddTableSql(string table, bool strict = false) =>
            $"ALTER SECURITY POLICY [{Schema}].[{Policy}] {Predicates("ADD", table, strict)};";

        public static string DropTableSql(string table) =>
            $"ALTER SECURITY POLICY [{Schema}].[{Policy}] DROP FILTER PREDICATE ON [dbo].[{Name(table)}], DROP BLOCK PREDICATE ON [dbo].[{Name(table)}];";

        /// <summary>
        /// Switches a table between lenient and strict. Go strict only once no running host leaves the tenant undeclared.
        /// </summary>
        public static string SetModeSql(string table, bool strict) =>
            $"ALTER SECURITY POLICY [{Schema}].[{Policy}] {Predicates("ALTER", table, strict)};";

        private static string Predicates(string verb, string table, bool strict)
        {
            var function = $"[{Schema}].[{FunctionFor(strict)}]([TenantId])";
            return $"{verb} FILTER PREDICATE {function} ON [dbo].[{Name(table)}], {verb} BLOCK PREDICATE {function} ON [dbo].[{Name(table)}]";
        }

        /// <summary>
        /// Table names are compiled in, never user input; this only keeps a typo from becoming odd SQL
        /// </summary>
        private static string Name(string table) =>
            table.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && table.Length > 0
                ? table
                : throw new ArgumentException($"'{table}' isn't a table name.", nameof(table));
    }

    public static class RowLevelSecurityMigrations
    {
        /// <summary>
        /// For the migration that creates a tenant table
        /// </summary>
        public static void AddTenantRowLevelSecurity(this MigrationBuilder migrationBuilder, string table, bool strict = false) =>
            migrationBuilder.Sql(RowLevelSecurity.AddTableSql(table, strict));

        /// <summary>
        /// Before dropping a tenant table or altering its TenantId column, which the policy otherwise blocks
        /// </summary>
        public static void DropTenantRowLevelSecurity(this MigrationBuilder migrationBuilder, string table) =>
            migrationBuilder.Sql(RowLevelSecurity.DropTableSql(table));

        public static void SetTenantRowLevelSecurityMode(this MigrationBuilder migrationBuilder, string table, bool strict) =>
            migrationBuilder.Sql(RowLevelSecurity.SetModeSql(table, strict));
    }
}
