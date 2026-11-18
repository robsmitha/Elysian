using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elysian.Tests.Platforms
{
    public class RowLevelSecurityCatalogTests
    {
        [Fact]
        public void Every_table_is_either_protected_or_deliberately_not()
        {
            using var host = new TestHost();
            var entities = host.Context.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToList();

            var tenantTables = entities.Where(e => e.IsMultiTenant()).Select(e => e.GetTableName()!).Distinct().Order().ToList();
            var otherTables = entities.Where(e => !e.IsMultiTenant()).Select(e => e.GetTableName()!).Distinct().Order().ToList();

            var unprotected = tenantTables.Except(RowLevelSecurity.ProtectedTables).ToList();
            Assert.True(unprotected.Count == 0,
                $"Add to RowLevelSecurity.ProtectedTables, and call AddTenantRowLevelSecurity in the migration that creates them: {string.Join(", ", unprotected)}");

            var stale = RowLevelSecurity.ProtectedTables.Except(tenantTables).ToList();
            Assert.True(stale.Count == 0, $"Listed as protected but not tenant tables: {string.Join(", ", stale)}");

            var undecided = otherTables.Except(RowLevelSecurity.UnprotectedTables).ToList();
            Assert.True(undecided.Count == 0,
                $"These tables have no tenant. Make them multi-tenant, or add them to RowLevelSecurity.UnprotectedTables on purpose: {string.Join(", ", undecided)}");
        }

        [Fact]
        public void The_policy_gives_every_table_a_filter_and_a_block_predicate_in_lenient_mode()
        {
            var policy = RowLevelSecurity.CreateSql(["Photo", "Story"]).Last();

            foreach (var table in new[] { "Photo", "Story" })
            {
                Assert.Contains($"ADD FILTER PREDICATE [rls].[fn_TenantLenient]([TenantId]) ON [dbo].[{table}]", policy);
                Assert.Contains($"ADD BLOCK PREDICATE [rls].[fn_TenantLenient]([TenantId]) ON [dbo].[{table}]", policy);
            }
            Assert.Contains("STATE = ON", policy);
            Assert.Contains("fn_TenantStrict", RowLevelSecurity.SetModeSql("Photo", strict: true));
            Assert.Throws<ArgumentException>(() => RowLevelSecurity.AddTableSql("Photo]; DROP TABLE [User"));
        }
    }

    /// <summary>
    /// Runs against a real SQL Server, because row-level security is the database's behavior and the in-memory
    /// provider has none. Set ELYSIAN_TEST_SQL to a server-level connection string (no database), e.g.
    /// "Server=localhost;Trusted_Connection=True;Encrypt=False". Each run creates and drops its own database.
    /// Without the variable these tests pass without doing anything.
    /// </summary>
    public sealed class RowLevelSecuritySqlTests : IDisposable
    {
        private static readonly string? Server = Environment.GetEnvironmentVariable("ELYSIAN_TEST_SQL");

        private static readonly ElysianTenantInfo TenantA = new() { Id = "tenant-a", Identifier = "a", Name = "A" };
        private static readonly ElysianTenantInfo TenantB = new() { Id = "tenant-b", Identifier = "b", Name = "B" };

        private readonly string database = $"Elysian_RlsTest_{Guid.NewGuid():N}";
        private readonly List<ServiceProvider> providers = [];

        private string ConnectionString => new SqlConnectionStringBuilder(Server) { InitialCatalog = database }.ConnectionString;

        public RowLevelSecuritySqlTests()
        {
            if (Server == null)
            {
                return;
            }

            // Design time: no tenant, with the bypass, exactly as dotnet ef database update runs
            using var migrator = Context(null, bypass: true);
            migrator.Database.Migrate();
        }

        public void Dispose()
        {
            providers.ForEach(p => p.Dispose());
            if (Server == null)
            {
                return;
            }

            SqlConnection.ClearAllPools();
            using var connection = new SqlConnection(Server);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END";
            command.ExecuteNonQuery();
        }

        [Fact]
        public void Every_tenant_table_has_both_predicates_after_the_migrations()
        {
            if (Server == null) return;

            var rows = Query(null, """
                SELECT OBJECT_NAME(p.target_object_id) + ':' + p.predicate_type_desc + ':' + ISNULL(p.operation_desc, 'ALL')
                FROM sys.security_predicates p JOIN sys.security_policies s ON s.object_id = p.object_id
                WHERE s.name = 'TenantPolicy' AND s.is_enabled = 1
                """);

            foreach (var table in RowLevelSecurity.ProtectedTables)
            {
                Assert.Contains($"{table}:FILTER:ALL", rows);
                Assert.Contains(rows, r => r.StartsWith($"{table}:BLOCK:"));
            }

            // And nothing with a TenantId column was missed
            var tenantTables = Query(null, "SELECT OBJECT_NAME(object_id) FROM sys.columns WHERE name = 'TenantId' AND OBJECTPROPERTY(object_id, 'IsUserTable') = 1");
            Assert.Empty(tenantTables.Except(RowLevelSecurity.ProtectedTables));
        }

        [Fact]
        public async Task A_connection_that_declared_a_tenant_sees_and_writes_only_that_tenants_rows()
        {
            if (Server == null) return;
            await SeedAsync();

            using var a = Context(TenantA);

            // IgnoreQueryFilters is the application bug this guards against: the database still says no
            var seen = await a.TenantSettings.IgnoreQueryFilters().Select(s => s.Section).ToListAsync();
            Assert.Equal(["a-settings"], seen);

            Assert.Equal(0, await a.Database.ExecuteSqlRawAsync("UPDATE [TenantSetting] SET [Json] = 'changed' WHERE [Section] = 'b-settings'"));
            Assert.Equal(0, await a.Database.ExecuteSqlRawAsync("DELETE FROM [TenantSetting] WHERE [Section] = 'b-settings'"));

            var blocked = await Assert.ThrowsAsync<SqlException>(() => a.Database.ExecuteSqlRawAsync(
                "INSERT INTO [TenantSetting] ([Section], [Json], [TenantId], [CreatedByUserId], [CreatedAt], [ModifiedByUserId], [ModifiedAt], [IsDeleted]) " +
                "VALUES ('planted', 'x', 'tenant-b', 'x', SYSDATETIMEOFFSET(), 'x', SYSDATETIMEOFFSET(), 0)"));
            Assert.Contains("block predicate", blocked.Message, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(["b-settings"], Query(TenantB, "SELECT [Section] FROM [TenantSetting]"));
        }

        [Fact]
        public async Task Ordinary_saves_through_the_context_still_work()
        {
            if (Server == null) return;

            using (var a = Context(TenantA))
            {
                var setting = new TenantSetting { Section = "Booking", Json = "{}", CreatedByUserId = "t", ModifiedByUserId = "t" };
                a.TenantSettings.Add(setting);
                await a.SaveChangesAsync();

                setting.Json = """{"TimeZone":"America/Denver"}""";
                await a.SaveChangesAsync();
            }

            using var again = Context(TenantA);
            Assert.Contains("Denver", (await again.TenantSettings.SingleAsync()).Json);
            again.TenantSettings.Remove(await again.TenantSettings.SingleAsync());
            await again.SaveChangesAsync();
            Assert.Empty(await again.TenantSettings.ToListAsync());
        }

        [Fact]
        public async Task Lenient_lets_an_undeclared_connection_see_everything_and_strict_shows_it_nothing()
        {
            if (Server == null) return;
            await SeedAsync();

            // A host on an older Elysian: it never declares a tenant
            Assert.Equal(2, RawCount("SELECT COUNT(*) FROM [TenantSetting]", declare: null));
            Execute("INSERT INTO [TenantSetting] ([Section], [Json], [TenantId], [CreatedByUserId], [CreatedAt], [ModifiedByUserId], [ModifiedAt], [IsDeleted]) " +
                "VALUES ('old-host', 'x', 'tenant-b', 'x', SYSDATETIMEOFFSET(), 'x', SYSDATETIMEOFFSET(), 0)");
            Assert.Equal(3, RawCount("SELECT COUNT(*) FROM [TenantSetting]", declare: null));

            Execute(RowLevelSecurity.SetModeSql("TenantSetting", strict: true));

            Assert.Equal(0, RawCount("SELECT COUNT(*) FROM [TenantSetting]", declare: null));
            Assert.Equal(1, RawCount("SELECT COUNT(*) FROM [TenantSetting]", declare: "tenant-a"));
            Assert.Equal(["a-settings", "b-settings", "old-host"], Query(null, "SELECT [Section] FROM [TenantSetting] ORDER BY [Section]", bypass: true));

            // Other tables are still lenient: modes are per table
            Assert.True(RawCount("SELECT COUNT(*) FROM [TenantModule]", declare: null) >= 2);
        }

        [Fact]
        public async Task A_pooled_connection_never_carries_the_last_tenant_into_the_next_request()
        {
            if (Server == null) return;
            await SeedAsync();

            for (var i = 0; i < 5; i++)
            {
                Assert.Equal(["a-settings"], Query(TenantA, "SELECT [Section] FROM [TenantSetting]"));
                Assert.Equal(["b-settings"], Query(TenantB, "SELECT [Section] FROM [TenantSetting]"));
                Assert.Equal(2, Query(null, "SELECT [Section] FROM [TenantSetting]", bypass: true).Count);
            }
        }

        [Fact]
        public async Task A_tenant_created_by_the_platform_is_complete_and_visible_only_to_itself()
        {
            if (Server == null) return;

            var options = new DbContextOptionsBuilder<PlatformContext>()
                .UseSqlServer(ConnectionString)
                .AddInterceptors(new TenantSessionContextInterceptor(bypass: true))
                .Options;

            Elysian.Application.Features.Platform.Commands.CreatedTenant created;
            await using (var platform = new PlatformContext(options))
            {
                created = await new Elysian.Application.Features.Platform.Commands.CreateTenantCommandHandler(platform,
                    new Elysian.Infrastructure.Identity.ClaimsPrincipalAccessor(), TimeProvider.System,
                    Microsoft.Extensions.Options.Options.Create(new Elysian.Infrastructure.Settings.AuthSettings()))
                    .Handle(new Elysian.Application.Features.Platform.Commands.CreateTenantCommand("friend", "Friend Photography", "photography", "cloud"), CancellationToken.None);
            }

            // The tenant store reads what the command wrote
            var stored = Query(null, "SELECT [Id] + '|' + [Platform] + '|' + [Runtime] + '|' + [Status] FROM [TenantInfo] WHERE [Identifier] = 'friend'").Single().Split('|');
            Assert.Equal(["photography", "cloud", "active"], stored[1..]);
            Assert.Equal(["friend"], Query(null, "SELECT [MerchantIdentifier] FROM [Merchant] WHERE [MerchantIdentifier] = 'friend'"));

            var friend = new ElysianTenantInfo { Id = stored[0], Identifier = "friend" };
            Assert.Equal(["photos"], Query(friend, "SELECT [ModuleKey] FROM [TenantModule]"));
            Assert.Equal(["owner"], Query(friend, "SELECT [Role] FROM [TenantInvite]"));
            Assert.Empty(Query(TenantA, "SELECT [Role] FROM [TenantInvite]"));
            Assert.Equal(19, created.OwnerInviteCode.Length);
        }

        private async Task SeedAsync()
        {
            foreach (var tenant in new[] { TenantA, TenantB })
            {
                using var context = Context(tenant);
                context.TenantSettings.Add(new TenantSetting { Section = $"{tenant.Identifier}-settings", Json = "{}", CreatedByUserId = "t", ModifiedByUserId = "t" });
                context.TenantModules.Add(new TenantModule { ModuleKey = "photos", Status = TenantModuleStatus.Active, CreatedByUserId = "t", ModifiedByUserId = "t" });
                await context.SaveChangesAsync();
            }
        }

        private ElysianContext Context(ElysianTenantInfo? tenant, bool bypass = false)
        {
            var services = new ServiceCollection();
            services.AddMultiTenant<ElysianTenantInfo>().WithInMemoryStore();
            var provider = services.BuildServiceProvider();
            providers.Add(provider);

            provider.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext =
                new MultiTenantContext<ElysianTenantInfo> { TenantInfo = tenant ?? new ElysianTenantInfo() };

            var options = new DbContextOptionsBuilder<ElysianContext>()
                .UseSqlServer(ConnectionString)
                .AddInterceptors(new TenantSessionContextInterceptor(bypass))
                .Options;

            return new ElysianContext(provider.GetRequiredService<IMultiTenantContextAccessor>(), options);
        }

        private List<string> Query(ElysianTenantInfo? tenant, string sql, bool bypass = false)
        {
            using var context = Context(tenant, bypass);
            return context.Database.SqlQueryRaw<string>(sql).ToList();
        }

        /// <summary>
        /// A plain connection with no interceptor, declaring a tenant by hand or not at all
        /// </summary>
        private int RawCount(string sql, string? declare)
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "EXEC sp_set_session_context N'TenantId', @tenant; EXEC sp_set_session_context N'TenantBypass', NULL; " + sql;
            command.Parameters.AddWithValue("@tenant", (object?)declare ?? DBNull.Value);
            return (int)command.ExecuteScalar()!;
        }

        private void Execute(string sql)
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
