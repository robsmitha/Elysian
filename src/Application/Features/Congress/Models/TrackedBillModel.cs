using CapitolSharp.Congress.Bills.BillDetails;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Congress.Models
{
    public record TrackedBillModel(
        int BillTrackingId,
        int Congress,
        string BillType,
        int BillNumber,
        string Title,
        DateTimeOffset? IntroducedDate,
        string? OriginChamber,
        string? PolicyArea,
        string? SponsorBioguideId,
        string? SponsorName,
        string? SponsorParty,
        string? SponsorState,
        string? LatestActionText,
        DateTimeOffset? LatestActionDate,
        string? LawNumber,
        bool HasUnseenActivity,
        DateTimeOffset? LastViewedAt,
        string? Notes,
        DateTimeOffset SavedAt);

    public static class TrackedBillMappings
    {
        public static TrackedBillModel ToModel(this BillTracking billTracking) => new(
            billTracking.BillTrackingId,
            billTracking.Congress,
            billTracking.BillType,
            billTracking.BillNumber,
            billTracking.Title,
            billTracking.IntroducedDate,
            billTracking.OriginChamber,
            billTracking.PolicyArea,
            billTracking.SponsorBioguideId,
            billTracking.SponsorName,
            billTracking.SponsorParty,
            billTracking.SponsorState,
            billTracking.LatestActionText,
            billTracking.LatestActionDate,
            billTracking.LawNumber,
            billTracking.HasUnseenActivity,
            billTracking.LastViewedAt,
            billTracking.Notes,
            billTracking.CreatedAt);

        /// <summary>
        /// Copies Congress.gov's current data onto the saved bill. A new latest action on a bill that
        /// already had one is flagged as unseen; the first snapshot never is.
        /// </summary>
        public static void ApplySnapshot(this BillTracking billTracking, Bill bill, DateTimeOffset refreshedAt)
        {
            var hadSnapshot = billTracking.LatestActionDate.HasValue || billTracking.LatestActionText != null;
            if (hadSnapshot && (billTracking.LatestActionDate != bill.LatestAction?.ActionDate
                || billTracking.LatestActionText != bill.LatestAction?.Text))
            {
                billTracking.HasUnseenActivity = true;
            }

            var sponsor = bill.Sponsors?.FirstOrDefault();

            billTracking.Title = string.IsNullOrWhiteSpace(bill.Title) ? $"{billTracking.BillType} {billTracking.BillNumber}" : bill.Title;
            billTracking.IntroducedDate = bill.IntroducedDate;
            billTracking.OriginChamber = bill.OriginChamber;
            billTracking.PolicyArea = bill.PolicyArea?.Name;
            billTracking.SponsorBioguideId = sponsor?.BioguideId;
            billTracking.SponsorName = sponsor?.FullName;
            billTracking.SponsorParty = sponsor?.Party;
            billTracking.SponsorState = sponsor?.State;
            billTracking.LatestActionText = bill.LatestAction?.Text;
            billTracking.LatestActionDate = bill.LatestAction?.ActionDate;
            billTracking.LawNumber = bill.Laws?.FirstOrDefault()?.Number;
            billTracking.BillUpdateDate = bill.UpdateDate;
            billTracking.SnapshotRefreshedAt = refreshedAt;
        }
    }
}
