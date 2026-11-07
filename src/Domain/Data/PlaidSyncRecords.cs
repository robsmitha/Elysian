using Elysian.Domain.Constants;
using Finbuckle.MultiTenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elysian.Domain.Data
{
    /// <summary>One sync attempt for a bank: what started it, what changed, and how it ended</summary>
    public class SyncRun
    {
        public long SyncRunId { get; set; }
        public int InstitutionAccessItemId { get; set; }
        public SyncTrigger Trigger { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public int Added { get; set; }
        public int Modified { get; set; }
        public int Removed { get; set; }
        /// <summary>Null on success; otherwise the Plaid error code (or a short reason)</summary>
        public string? ErrorCode { get; set; }

        public class Configuration : IEntityTypeConfiguration<SyncRun>
        {
            public void Configure(EntityTypeBuilder<SyncRun> builder)
            {
                builder.HasKey(r => r.SyncRunId);
                builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(16);
                builder.Property(r => r.ErrorCode).HasMaxLength(100);
                builder.HasOne<InstitutionAccessItem>()
                    .WithMany()
                    .HasForeignKey(r => r.InstitutionAccessItemId)
                    .OnDelete(DeleteBehavior.Cascade);
                builder.HasIndex(r => new { r.InstitutionAccessItemId, r.StartedAt });
                builder.ToTable("SyncRun").IsMultiTenant();
            }
        }
    }

    /// <summary>Every Plaid webhook received, for auditing and so a redelivered one is handled once</summary>
    public class PlaidWebhookEvent
    {
        public long PlaidWebhookEventId { get; set; }
        /// <summary>SHA-256 of the raw body (hex); unique, so a repeat delivery is ignored</summary>
        public string BodySha256 { get; set; }
        public string WebhookType { get; set; }
        public string WebhookCode { get; set; }
        public string? ItemId { get; set; }
        public string PayloadJson { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public DateTimeOffset? ProcessedAt { get; set; }
        public string? Error { get; set; }

        public class Configuration : IEntityTypeConfiguration<PlaidWebhookEvent>
        {
            public void Configure(EntityTypeBuilder<PlaidWebhookEvent> builder)
            {
                builder.HasKey(e => e.PlaidWebhookEventId);
                builder.Property(e => e.BodySha256).IsRequired().HasMaxLength(64);
                builder.Property(e => e.WebhookType).IsRequired().HasMaxLength(50);
                builder.Property(e => e.WebhookCode).IsRequired().HasMaxLength(50);
                builder.Property(e => e.ItemId).HasMaxLength(200);
                builder.Property(e => e.PayloadJson).IsRequired();
                builder.Property(e => e.Error).HasMaxLength(500);
                builder.HasIndex(e => e.BodySha256).IsUnique();
                builder.ToTable("PlaidWebhookEvent").IsMultiTenant();
            }
        }
    }
}
