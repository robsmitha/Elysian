using Elysian.Domain.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elysian.Infrastructure.Context
{
    public partial class ElysianContext(DbContextOptions<ElysianContext> options) : DbContext(options)
    {
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ElysianContext).Assembly);
        }
    }
    public class ElysianContextFactory : IDesignTimeDbContextFactory<ElysianContext>
    {
        public ElysianContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ElysianContext>();
            optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=Elysian;Trusted_Connection=True;MultipleActiveResultSets=true");

            return new ElysianContext(optionsBuilder.Options);
        }
    }
}
