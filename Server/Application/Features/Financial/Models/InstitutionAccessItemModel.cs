using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Models
{
    public record ExpiredAccessItem(string AccessToken, string Message, InstitutionModel Institution);
    public class InstitutionAccessItemModel
    {
        public int InstitutionAccessItemId { get; set; }
        public ItemModel Item { get; set; }
        public InstitutionModel Institution { get; set; }
        public List<AccountModel> Accounts { get; set; } = new List<AccountModel>();
        public ExpiredAccessItem ExpiredAccessItem { get; set; }
    }
}
