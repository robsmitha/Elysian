using Elysian.Application.Features.MultiTenant;
using Elysian.Domain.Data;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Elysian.Infrastructure.Context
{
    public partial class ElysianContext : MultiTenantDbContext
    {
        public ElysianContext(IMultiTenantContextAccessor multiTenantContextAccessor) : base(multiTenantContextAccessor)
        {
        }

        public ElysianContext(IMultiTenantContextAccessor multiTenantContextAccessor, DbContextOptions<ElysianContext> options) 
            : base(multiTenantContextAccessor, options)
        {

        }

        public DbSet<Budget> Budgets { get; set; }
        public DbSet<BudgetAccessItem> BudgetAccessItems { get; set; }
        public DbSet<BudgetCategory> BudgetCategories { get; set; }
        public DbSet<BudgetExcludedTransaction> BudgetExcludedTransactions { get; set; }
        public DbSet<FinancialCategory> FinancialCategories { get; set; }
        public DbSet<InstitutionAccessItem> InstitutionAccessItems { get; set; }
        public DbSet<Merchant> Merchants { get; set; }
        public DbSet<MerchantType> MerchantTypes { get; set; }
        public DbSet<OAuthState> OAuthStates { get; set; }
        public DbSet<OAuthToken> OAuthTokens { get; set; }
        public DbSet<PriceType> PriceTypes { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<ProductType> ProductTypes { get; set; }
        public DbSet<TransactionCategory> TransactionCategories { get; set; }
        public DbSet<UnitType> UnitTypes { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ElysianContext).Assembly);

            base.OnModelCreating(modelBuilder);
        }
    }
    public class ElysianContextFactory : IDesignTimeDbContextFactory<ElysianContext>
    {
        public ElysianContext CreateDbContext(string[] args)
        {
            var services = new ServiceCollection();
            services.AddMultiTenant<ElysianTenantInfo>().WithInMemoryStore();
            var serviceProvider = services.BuildServiceProvider();

            var multiTenantContextSetter = serviceProvider.GetRequiredService<IMultiTenantContextSetter>();
            multiTenantContextSetter.MultiTenantContext = new MultiTenantContext<ElysianTenantInfo>
            {
                TenantInfo = new ElysianTenantInfo()
            };

            var builder = new DbContextOptionsBuilder<ElysianContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=Elysian;Trusted_Connection=True;MultipleActiveResultSets=true");
            return new (
                multiTenantContextAccessor: serviceProvider.GetRequiredService<IMultiTenantContextAccessor>(),
                options: builder.Options);
        }
    }
}
