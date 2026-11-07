using Elysian.Domain.Constants;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// One bank connection (a Plaid "Item"). Its access token is stored encrypted and only read on the server.
    /// </summary>
    public class InstitutionAccessItem : AuditableEntity
    {
        public int InstitutionAccessItemId { get; set; }

        /// <summary>Legacy plaintext token. Emptied once the encrypted copy is written; dropped in a later migration.</summary>
        public string? AccessToken { get; set; }
        /// <summary>The access token, encrypted by IAccessTokenProtector</summary>
        public string? AccessTokenCiphertext { get; set; }

        public string ItemId { get; set; }
        public string InstitutionId { get; set; }

        // Display details cached from Plaid, so pages don't call Plaid to show a bank
        public string? InstitutionName { get; set; }
        public string? InstitutionLogo { get; set; }
        public string? InstitutionColor { get; set; }

        public ItemStatus Status { get; set; } = ItemStatus.Healthy;
        /// <summary>The last Plaid error code that changed the status</summary>
        public string? ErrorCode { get; set; }
        /// <summary>When the bank's consent ends (some OAuth banks), from Plaid</summary>
        public DateTimeOffset? ConsentExpiresAt { get; set; }
        /// <summary>Plaid offered new accounts at this bank (NEW_ACCOUNTS_AVAILABLE)</summary>
        public bool NewAccountsAvailable { get; set; }

        /// <summary>The /transactions/sync cursor: where the next sync picks up</summary>
        public string? TransactionsCursor { get; set; }
        /// <summary>The webhook URL Plaid has for this Item, so a changed setting gets pushed once</summary>
        public string? WebhookUrl { get; set; }
        public DateTimeOffset? LastSyncedAt { get; set; }
        /// <summary>The last time someone asked to sync it, for the "Sync now" rate limit</summary>
        public DateTimeOffset? LastSyncRequestedAt { get; set; }

        /// <summary>Removed with history kept: Plaid access was revoked, past transactions stay for old budgets</summary>
        public DateTimeOffset? RemovedAt { get; set; }

        public ICollection<InstitutionAccessItemUser> AccessUsers { get; set; }
        public ICollection<FinancialAccount> Accounts { get; set; }

        public class Configuration : AuditableEntityConfiguration<InstitutionAccessItem>
        {
            public override void Configure(EntityTypeBuilder<InstitutionAccessItem> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.InstitutionAccessItemId);
                builder.Property(e => e.AccessToken);
                builder.Property(e => e.AccessTokenCiphertext).HasMaxLength(1000);
                builder.Property(e => e.ItemId).IsRequired();
                builder.Property(e => e.InstitutionId).IsRequired();
                builder.Property(e => e.InstitutionName).HasMaxLength(200);
                builder.Property(e => e.InstitutionColor).HasMaxLength(7);
                builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
                builder.Property(e => e.ErrorCode).HasMaxLength(100);
                builder.Property(e => e.WebhookUrl).HasMaxLength(500);

                builder.HasIndex(e => e.ItemId);

                builder.ToTable("InstitutionAccessItem");
            }
        }
    }
}
