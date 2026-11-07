using System.Text.RegularExpressions;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Categories
{
    /// <summary>A default category. Keys are stored on people's categories: never rename one.</summary>
    public record DefaultCategory(string Key, string Name, string Icon, CategoryGroup Group, params string[] Aliases);

    /// <summary>Where a Plaid category goes: a treatment, and for spending a default category key.</summary>
    public record PlaidMapping(Treatment Treatment, string? CategoryKey);

    /// <summary>
    /// The default categories, and the map from Plaid's personal finance categories (v1 and v2 codes) to them. Ported from
    /// thejourneyfi.com (src/services/bankFill/categoryMap.ts). A Plaid code can be overridden per person with a rule.
    /// </summary>
    public static class CategoryCatalog
    {
        public static readonly IReadOnlyList<DefaultCategory> Defaults =
        [
            new("housing", "Housing", "mdi-home-outline", CategoryGroup.Fixed, "rent", "mortgage", "housing / rent"),
            new("utilities", "Utilities", "mdi-lightbulb-outline", CategoryGroup.Fixed, "utility", "electric", "power"),
            new("phone_internet", "Phone & internet", "mdi-wifi", CategoryGroup.Fixed, "phone", "internet", "cell phone"),
            new("insurance", "Insurance", "mdi-shield-check-outline", CategoryGroup.Fixed, "renters insurance", "car insurance", "medical insurance"),
            new("subscriptions", "Subscriptions", "mdi-cellphone-play", CategoryGroup.Fixed, "streaming"),
            new("loans", "Loan payments", "mdi-bank-transfer-out", CategoryGroup.Fixed, "student loan", "car payment", "loan"),
            new("childcare", "Childcare", "mdi-baby-face-outline", CategoryGroup.Fixed, "daycare"),
            new("fitness", "Fitness", "mdi-dumbbell", CategoryGroup.Fixed, "gym"),
            new("groceries", "Groceries", "mdi-cart-outline", CategoryGroup.Flexible, "grocery", "food"),
            new("dining", "Dining out", "mdi-silverware-fork-knife", CategoryGroup.Flexible, "restaurants", "dining", "eating out"),
            new("transport", "Gas & transport", "mdi-gas-station-outline", CategoryGroup.Flexible, "gas", "car", "transportation", "fuel"),
            new("shopping", "Shopping", "mdi-shopping-outline", CategoryGroup.Flexible, "clothing"),
            new("home", "Home & household", "mdi-hammer-wrench", CategoryGroup.Flexible, "household", "home"),
            new("health", "Health", "mdi-hospital-box-outline", CategoryGroup.Flexible, "doctor", "medical", "pharmacy"),
            new("personal_care", "Personal care", "mdi-face-man-shimmer-outline", CategoryGroup.Flexible, "personal"),
            new("entertainment", "Entertainment", "mdi-movie-open-outline", CategoryGroup.Flexible, "hobbies", "fun"),
            new("pets", "Pets", "mdi-paw", CategoryGroup.Flexible, "pet"),
            new("fees", "Fees & interest", "mdi-receipt-text-outline", CategoryGroup.Flexible, "fees", "bank fees", "interest"),
            new("other", "Other", "mdi-pin-outline", CategoryGroup.Flexible, "misc", "miscellaneous", "flex"),
            new("travel", "Travel", "mdi-airplane", CategoryGroup.NonMonthly, "vacation"),
            new("gifts", "Gifts & donations", "mdi-gift-outline", CategoryGroup.NonMonthly, "gifts", "donations", "charity"),
            new("education", "Education", "mdi-book-open-variant", CategoryGroup.NonMonthly, "school", "tuition"),
        ];

        public static DefaultCategory? ByKey(string? key) => Defaults.FirstOrDefault(d => d.Key == key);

        /// <summary>The default an existing category name stands for (exact name or alias, any case), if any.</summary>
        public static DefaultCategory? ForName(string name)
        {
            var n = name.Trim().ToLowerInvariant();
            return Defaults.FirstOrDefault(d => d.Name.ToLowerInvariant() == n || d.Aliases.Contains(n));
        }

        // Longest prefix wins, so a detailed code can override its primary group
        private static readonly (string Prefix, PlaidMapping Mapping)[] PlaidPrefixes =
        [
            // Money in, money moving, and card payments: not spending
            ("INCOME", new(Treatment.Income, null)),
            ("TRANSFER_IN", new(Treatment.Transfer, null)),
            ("TRANSFER_OUT", new(Treatment.Transfer, null)),
            ("LOAN_DISBURSEMENTS", new(Treatment.Transfer, null)),
            ("LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", new(Treatment.CardPayment, null)),
            // Loan payments are spending you budget for; "other payment" is too vague to place, so it's left to review
            ("LOAN_PAYMENTS_MORTGAGE_PAYMENT", new(Treatment.Spending, "housing")),
            ("LOAN_PAYMENTS_CAR_PAYMENT", new(Treatment.Spending, "loans")),
            ("LOAN_PAYMENTS_STUDENT_LOAN_PAYMENT", new(Treatment.Spending, "loans")),
            ("LOAN_PAYMENTS_PERSONAL_LOAN_PAYMENT", new(Treatment.Spending, "loans")),
            ("LOAN_PAYMENTS_OTHER_PAYMENT", new(Treatment.Spending, null)),
            ("BANK_FEES", new(Treatment.Spending, "fees")),

            ("FOOD_AND_DRINK", new(Treatment.Spending, "dining")),
            ("FOOD_AND_DRINK_GROCERIES", new(Treatment.Spending, "groceries")),
            ("TRANSPORTATION", new(Treatment.Spending, "transport")),
            ("GENERAL_SERVICES_AUTOMOTIVE", new(Treatment.Spending, "transport")),
            ("TRAVEL", new(Treatment.Spending, "travel")),

            ("RENT_AND_UTILITIES", new(Treatment.Spending, "utilities")),
            ("RENT_AND_UTILITIES_RENT", new(Treatment.Spending, "housing")),
            ("RENT_AND_UTILITIES_TELEPHONE", new(Treatment.Spending, "phone_internet")),
            ("RENT_AND_UTILITIES_INTERNET_AND_CABLE", new(Treatment.Spending, "phone_internet")),
            ("HOME_IMPROVEMENT", new(Treatment.Spending, "home")),

            ("MEDICAL", new(Treatment.Spending, "health")),
            ("MEDICAL_VETERINARY_SERVICES", new(Treatment.Spending, "pets")),
            ("PERSONAL_CARE", new(Treatment.Spending, "personal_care")),
            ("PERSONAL_CARE_GYMS_AND_FITNESS_CENTERS", new(Treatment.Spending, "fitness")),

            ("ENTERTAINMENT", new(Treatment.Spending, "entertainment")),
            ("ENTERTAINMENT_TV_AND_MOVIES", new(Treatment.Spending, "subscriptions")),
            ("ENTERTAINMENT_MUSIC_AND_AUDIO", new(Treatment.Spending, "subscriptions")),

            ("GENERAL_MERCHANDISE", new(Treatment.Spending, "shopping")),
            ("GENERAL_MERCHANDISE_PET_SUPPLIES", new(Treatment.Spending, "pets")),
            ("GENERAL_MERCHANDISE_GIFTS_AND_NOVELTIES", new(Treatment.Spending, "gifts")),
            ("GENERAL_SERVICES", new(Treatment.Spending, "other")),
            ("GENERAL_SERVICES_CHILDCARE", new(Treatment.Spending, "childcare")),
            ("GENERAL_SERVICES_EDUCATION", new(Treatment.Spending, "education")),
            ("GENERAL_SERVICES_INSURANCE", new(Treatment.Spending, "insurance")),
            ("GOVERNMENT_AND_NON_PROFIT", new(Treatment.Spending, "fees")),
            ("GOVERNMENT_AND_NON_PROFIT_DONATIONS", new(Treatment.Spending, "gifts")),
        ];

        /// <summary>Plaid's detailed code → treatment and category key; null for unknown or "other" codes.</summary>
        public static PlaidMapping? ForPlaid(string? detailed)
        {
            if (string.IsNullOrWhiteSpace(detailed) || detailed.StartsWith("OTHER", StringComparison.Ordinal))
            {
                return null;
            }
            return PlaidPrefixes
                .Where(p => detailed.StartsWith(p.Prefix, StringComparison.Ordinal))
                .OrderByDescending(p => p.Prefix.Length)
                .Select(p => p.Mapping)
                .FirstOrDefault();
        }

        /// <summary>Plaid categories that can be one leg of a move between the person's own accounts.</summary>
        public static bool IsTransferLike(string? detailed) =>
            detailed == null
            || detailed.StartsWith("TRANSFER_", StringComparison.Ordinal)
            || detailed.StartsWith("LOAN_PAYMENTS", StringComparison.Ordinal)
            || detailed.StartsWith("OTHER", StringComparison.Ordinal);

        private static readonly Regex Digits = new(@"[\d#*]+", RegexOptions.Compiled);
        private static readonly Regex NotLetters = new(@"[^a-z& ]+", RegexOptions.Compiled);
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// A merchant's text, normalized so "TRADER JOE S #552" and "Trader Joe's #103" match (thejourneyfi.com
        /// src/planning/transactions/place.ts merchantKey).
        /// </summary>
        public static string MerchantKey(string? merchant)
        {
            var text = (merchant ?? "").ToLowerInvariant();
            var key = Spaces.Replace(NotLetters.Replace(Digits.Replace(text, " "), " "), " ").Trim();
            var result = string.IsNullOrEmpty(key) ? text.Trim() : key;
            return result.Length <= 200 ? result : result[..200];
        }
    }
}
