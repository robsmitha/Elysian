using Finbuckle.MultiTenant.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data;
using System.Data.Common;

namespace Elysian.Infrastructure.Context
{
    /// <summary>
    /// Tells SQL Server which tenant a connection acts for each time it opens, so the row-level security
    /// policy can hold it to that tenant. Both keys are set on every open, never left over from the pooled
    /// connection's last use.
    /// </summary>
    /// <param name="bypass">
    /// For platform work that spans tenants on purpose (see <see cref="RowLevelSecurity.BypassKey"/>).
    /// Never use it on a context that serves tenant requests.
    /// </param>
    public class TenantSessionContextInterceptor(bool bypass = false) : DbConnectionInterceptor
    {
        private const string CommandText =
            $"EXEC sp_set_session_context @key = N'{RowLevelSecurity.TenantIdKey}', @value = @tenantId; " +
            $"EXEC sp_set_session_context @key = N'{RowLevelSecurity.BypassKey}', @value = @bypass;";

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = Build(connection, eventData.Context);
            command?.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await using var command = Build(connection, eventData.Context);
            if (command != null)
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private DbCommand? Build(DbConnection connection, DbContext? context)
        {
            // SESSION_CONTEXT is SQL Server's; other providers (tests, tools) have nothing to set
            if (connection is not SqlConnection)
            {
                return null;
            }

            var tenantId = bypass ? null : (context as IMultiTenantDbContext)?.TenantInfo?.Id;

            var command = connection.CreateCommand();
            command.CommandText = CommandText;
            command.Parameters.Add(new SqlParameter("@tenantId", SqlDbType.NVarChar, 64)
            {
                Value = string.IsNullOrEmpty(tenantId) ? DBNull.Value : tenantId
            });
            command.Parameters.Add(new SqlParameter("@bypass", SqlDbType.Bit) { Value = bypass });

            return command;
        }
    }
}
