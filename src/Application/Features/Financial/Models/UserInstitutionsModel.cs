
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Models
{
    public record AccessItem(ItemModel Item, InstitutionModel Institution, List<AccountModel> Accounts);
    public class UserInstitutionsModel
    {
        public List<InstitutionAccessItemModel> AccessItems { get; set; }
    }
}
