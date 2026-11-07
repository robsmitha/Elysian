using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>One account at a linked bank, with its balances as of the last sync</summary>
    public class FinancialAccount : AuditableEntity
    {
        public int FinancialAccountId { get; set; }
        public int InstitutionAccessItemId { get; set; }
        public string PlaidAccountId { get; set; }
        public string Name { get; set; }
        public string? OfficialName { get; set; }
        public string? Mask { get; set; }
        public string Type { get; set; }
        public string? Subtype { get; set; }
        public decimal? CurrentBalance { get; set; }
        public decimal? AvailableBalance { get; set; }
        public decimal? CreditLimit { get; set; }
        public string? IsoCurrencyCode { get; set; }
        public DateTimeOffset? BalanceAsOf { get; set; }
        public AccountBudgetRole BudgetRole { get; set; }

        public InstitutionAccessItem InstitutionAccessItem { get; set; }
        public ICollection<BankTransaction> Transactions { get; set; }

        /// <summary>
        /// The role a newly seen account starts with: cards and checking count as spending (D2), savings is savings,
        /// investments and loans are ignored until Phase 4 uses them.
        /// </summary>
        public static AccountBudgetRole DefaultRole(string? type, string? subtype) => type switch
        {
            "credit" => AccountBudgetRole.Spending,
            "depository" when subtype is "savings" or "money market" or "cd" or "hsa" => AccountBudgetRole.Savings,
            "depository" => AccountBudgetRole.Spending,
            _ => AccountBudgetRole.Ignore
        };

        public class Configuration : AuditableEntityConfiguration<FinancialAccount>
        {
            public override void Configure(EntityTypeBuilder<FinancialAccount> builder)
            {
                base.Configure(builder);

                builder.HasKey(a => a.FinancialAccountId);
                builder.Property(a => a.PlaidAccountId).IsRequired().HasMaxLength(200);
                builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
                builder.Property(a => a.OfficialName).HasMaxLength(200);
                builder.Property(a => a.Mask).HasMaxLength(10);
                builder.Property(a => a.Type).IsRequired().HasMaxLength(40);
                builder.Property(a => a.Subtype).HasMaxLength(60);
                builder.Property(a => a.CurrentBalance).HasColumnType("decimal(18,2)");
                builder.Property(a => a.AvailableBalance).HasColumnType("decimal(18,2)");
                builder.Property(a => a.CreditLimit).HasColumnType("decimal(18,2)");
                builder.Property(a => a.IsoCurrencyCode).HasMaxLength(10);
                builder.Property(a => a.BudgetRole).HasConversion<string>().HasMaxLength(16).IsRequired();

                builder.HasOne(a => a.InstitutionAccessItem)
                    .WithMany(i => i.Accounts)
                    .HasForeignKey(a => a.InstitutionAccessItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                builder.HasIndex(a => a.PlaidAccountId);

                builder.ToTable("FinancialAccount").IsMultiTenant();
            }
        }
    }
}
