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

        public const string ProductRead = "product.read";
        public const string ProductWrite = "product.write";
        public const string ProductDelete = "product.delete";

        public const string UserRead = "user.read";
        public const string UserWrite = "user.write";
        public const string UserDelete = "user.delete";

        private static readonly Dictionary<string, (int Level, string[] Policies)> roleHierarchy = new(StringComparer.OrdinalIgnoreCase)
        {
            { RoleNames.Authenticated, (1,[CodeRead, CodeWrite, CodeDelete]) },
            { RoleNames.MerchantAdmin, (100,[ProductRead, ProductWrite, ProductDelete, BudgetRead, BudgetWrite, BudgetDelete]) },
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