using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Deletes a tenant's rows with plain SQL, on the platform's connection, which is allowed across tenants.
    ///
    /// The tables are <see cref="RowLevelSecurity.ProtectedTables"/>: the list every table with a TenantId has
    /// to be on (a test fails otherwise), so a table added later is cleared here without anyone remembering to.
    /// They refer to one another, and the order that satisfies every foreign key isn't written down anywhere,
    /// so it is found: each pass deletes from every table that will let it, and a table that refuses because
    /// another still points at it waits for the next pass. A pass that removes nothing and still has tables
    /// left is a real problem, and is thrown.
    /// </summary>
    public class SqlTenantDataEraser(PlatformContext platform, ILogger<SqlTenantDataEraser> logger) : ITenantDataEraser
    {
        /// <summary>SQL Server: the DELETE conflicted with a REFERENCE constraint</summary>
        private const int ForeignKeyViolation = 547;

        /// <summary>
        /// The statements that follow the tenant's own tables, in order. The merchant record is found by the
        /// tenant's identifier, and products (a tenant table, gone by now) were what pointed at it.
        /// </summary>
        public static readonly IReadOnlyList<string> PlatformStatements =
        [
            "DELETE FROM [EdgeClient] WHERE [TenantIdentifier] = @identifier",
            "DELETE FROM [Merchant] WHERE [MerchantIdentifier] = @identifier",
            "DELETE FROM [TenantInfo] WHERE [Id] = @id AND [Identifier] = @identifier",
        ];

        public static string TenantStatement(string table) => $"DELETE FROM [{table}] WHERE [TenantId] = @id";

        public async Task<int> EraseAsync(string tenantId, string tenantIdentifier, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(tenantIdentifier))
            {
                throw new ArgumentException("A tenant's rows can only be erased by its id and identifier.");
            }

            var removed = 0;
            var remaining = RowLevelSecurity.ProtectedTables.ToList();
            while (remaining.Count > 0)
            {
                var waiting = new List<string>();
                SqlException? refusal = null;
                foreach (var table in remaining)
                {
                    try
                    {
                        removed += await platform.Database.ExecuteSqlRawAsync(TenantStatement(table), [new SqlParameter("@id", tenantId)], cancellationToken);
                    }
                    catch (SqlException ex) when (ex.Number == ForeignKeyViolation)
                    {
                        waiting.Add(table);
                        refusal = ex;
                    }
                }

                if (waiting.Count == remaining.Count)
                {
                    throw new InvalidOperationException(
                        $"The rows of {tenantIdentifier} in {string.Join(", ", waiting)} can't be removed: something outside the tenant still refers to them.", refusal);
                }
                remaining = waiting;
            }

            foreach (var statement in PlatformStatements)
            {
                removed += await platform.Database.ExecuteSqlRawAsync(statement,
                    [new SqlParameter("@id", tenantId), new SqlParameter("@identifier", tenantIdentifier)], cancellationToken);
            }

            logger.LogInformation("Erased {Rows} rows of {Tenant}", removed, tenantIdentifier);
            return removed;
        }
    }
}
