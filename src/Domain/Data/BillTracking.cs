using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Elysian.Domain.Seedwork;
using Finbuckle.MultiTenant;

namespace Elysian.Domain.Data
{
    /// <summary>
    /// A bill a user saved from the Congress feed. Congress, type and number identify the bill for good;
    /// the snapshot fields are a copy of Congress.gov data so the saved list renders without calling the API,
    /// and are refreshed as the bill moves.
    /// </summary>
    public class BillTracking : AuditableEntity
    {
        public int BillTrackingId { get; set; }

        /// <summary>
        /// The signed in user's external id (the client principal's NameIdentifier)
        /// </summary>
        public string UserId { get; set; }

        public int Congress { get; set; }

        /// <summary>
        /// Upper case, as the bill list API returns it, e.g. "HR" or "SJRES"
        /// </summary>
        public string BillType { get; set; }

        public int BillNumber { get; set; }

        #region Snapshot

        /// <summary>
        /// Congress.gov's display title. Usually fixed, but can change when a bill is amended or gains a short title.
        /// </summary>
        public string Title { get; set; }
        public DateTimeOffset? IntroducedDate { get; set; }
        public string? OriginChamber { get; set; }

        /// <summary>
        /// Assigned by CRS, often days after introduction, so empty at first for new bills
        /// </summary>
        public string? PolicyArea { get; set; }

        public string? SponsorBioguideId { get; set; }
        public string? SponsorName { get; set; }
        public string? SponsorParty { get; set; }
        public string? SponsorState { get; set; }

        public string? LatestActionText { get; set; }
        public DateTimeOffset? LatestActionDate { get; set; }

        /// <summary>
        /// Public or private law number, e.g. "119-21", once the bill is enacted
        /// </summary>
        public string? LawNumber { get; set; }

        /// <summary>
        /// Congress.gov's updateDate, so a refresh can skip bills that haven't changed
        /// </summary>
        public DateTimeOffset? BillUpdateDate { get; set; }
        public DateTimeOffset SnapshotRefreshedAt { get; set; }

        #endregion

        /// <summary>
        /// Set when a refresh finds a new latest action, cleared when the user opens the bill
        /// </summary>
        public bool HasUnseenActivity { get; set; }
        public DateTimeOffset? LastViewedAt { get; set; }

        public string? Notes { get; set; }

        public class Configuration : AuditableEntityConfiguration<BillTracking>
        {
            public override void Configure(EntityTypeBuilder<BillTracking> builder)
            {
                base.Configure(builder);

                builder.IsMultiTenant();

                builder.HasKey(k => k.BillTrackingId);
                builder.Property(e => e.UserId).HasMaxLength(128).IsRequired();
                builder.Property(e => e.Congress).IsRequired();
                builder.Property(e => e.BillType).HasMaxLength(10).IsRequired();
                builder.Property(e => e.BillNumber).IsRequired();

                builder.Property(e => e.Title).IsRequired();
                builder.Property(e => e.OriginChamber).HasMaxLength(10);
                builder.Property(e => e.PolicyArea).HasMaxLength(256);
                builder.Property(e => e.SponsorBioguideId).HasMaxLength(16);
                builder.Property(e => e.SponsorName).HasMaxLength(256);
                builder.Property(e => e.SponsorParty).HasMaxLength(8);
                builder.Property(e => e.SponsorState).HasMaxLength(8);
                builder.Property(e => e.LawNumber).HasMaxLength(16);
                builder.Property(e => e.Notes).HasMaxLength(4000);

                builder.HasIndex(["UserId", "Congress", "BillType", "BillNumber", "TenantId"])
                    .IsUnique()
                    .HasDatabaseName("AK_BillTracking_UserId_Bill");

                builder.ToTable("BillTracking");
            }
        }
    }
}
