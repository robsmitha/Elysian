using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Models
{
    /// <summary>
    /// A linked bank that needs the person to sign in again. Identified by our id; the Plaid access token never
    /// leaves the server.
    /// </summary>
    public record ExpiredAccessItem(int InstitutionAccessItemId, string Message, InstitutionModel Institution);

    public static class AccessItemStatus
    {
        public const string Healthy = "healthy";
        /// <summary>The bank wants the person to sign in again (Plaid Link update mode fixes it)</summary>
        public const string LoginRequired = "login_required";
        /// <summary>Plaid or the bank is having trouble; usually passes on its own</summary>
        public const string Unavailable = "unavailable";
        public const string Error = "error";
    }

    public class InstitutionAccessItemModel
    {
        public int InstitutionAccessItemId { get; set; }
        /// <summary>Never null: filled from our own record when Plaid can't be read</summary>
        public ItemModel Item { get; set; } = new();
        public InstitutionModel Institution { get; set; }
        public List<AccountModel> Accounts { get; set; } = new List<AccountModel>();
        public ExpiredAccessItem? ExpiredAccessItem { get; set; }
        public string Status { get; set; } = AccessItemStatus.Healthy;
        /// <summary>A short message for the page when Status isn't healthy</summary>
        public string? StatusMessage { get; set; }
    }
}
