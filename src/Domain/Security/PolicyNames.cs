using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Security
{
    public static class PolicyNames
    {
        public const string BudgetRead = "budget.read";
        public const string BudgetWrite = "budget.write";
        public const string BudgetDelete = "budget.delete";

        public const string CodeRead = "code.read";
        public const string CodeWrite = "code.write";
        public const string CodeDelete = "code.delete";

        public const string IncomeRead = "income.read";
        public const string IncomeWrite = "income.write";
        public const string IncomeDelete = "income.delete";

        public const string PhotoRead = "photo.read";
        public const string PhotoWrite = "photo.write";
        public const string PhotoDelete = "photo.delete";

        public const string ProductRead = "product.read";
        public const string ProductWrite = "product.write";
        public const string ProductDelete = "product.delete";

        public const string UserRead = "user.read";
        public const string UserWrite = "user.write";
        public const string UserDelete = "user.delete";

        public const string MemberRead = "member.read";
        public const string MemberWrite = "member.write";

        /// <summary>
        /// A site's own colours, wording and links: reading them, editing the draft, and making it live
        /// </summary>
        public const string SiteRead = "site.read";
        public const string SiteWrite = "site.write";
        public const string SitePublish = "site.publish";

        private static readonly Dictionary<string, (int Level, string[] Policies)> roleHierarchy = new(StringComparer.OrdinalIgnoreCase)
        {
            { RoleNames.Authenticated, (1,[CodeRead, CodeWrite, CodeDelete]) },
            { RoleNames.MerchantAdmin, (100,[ProductRead, ProductWrite, ProductDelete, PhotoRead, PhotoWrite, PhotoDelete, BudgetRead, BudgetWrite, BudgetDelete, IncomeRead, IncomeWrite, IncomeDelete, SitePublish]) },
            // Membership roles. An owner sits just above merchantAdmin, so it gets everything that role has
            // plus managing who else belongs to the site.
            // An editor can prepare a site's content; making it live is the owner's call
            { RoleNames.Editor, (50,[ProductRead, ProductWrite, PhotoRead, PhotoWrite, PhotoDelete, SiteRead, SiteWrite]) },
            { RoleNames.Owner, (110,[MemberRead, MemberWrite]) },
            { RoleNames.SystemAdmin, (int.MaxValue,[UserRead, UserWrite, UserDelete]) }
        };

        public static List<string> GetDefaultPolicies(List<string> roles)
        {
            var userLevel = roles
                .Where(roleHierarchy.ContainsKey)
                .Select(role => roleHierarchy[role].Level)
                .DefaultIfEmpty(0)
                .Max();

            var policies = roleHierarchy
                .Where(kv => kv.Value.Level <= userLevel)
                .SelectMany(kv => kv.Value.Policies)
                .Distinct()
                .ToList();

            return policies;
        }
    }
}