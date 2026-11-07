using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Responses.Plaid;
using Moq;
using Newtonsoft.Json;

namespace Elysian.Tests.Financial
{
    public class AccessItemLoaderTests
    {
        private const string SecretToken = "access-sandbox-0000-secret";

        private static readonly InstitutionAccessTokenModel Token = new()
        {
            InstitutionAccessItemId = 7,
            AccessToken = SecretToken,
            ItemId = "item-7",
            InstitutionId = "ins_bofa"
        };

        private static FinancialServiceException PlaidError(string type, string code, string? display = null) =>
            new(new Error { error_type = type, error_code = code, error_message = "technical detail", display_message = display });

        private static Mock<IFinancialService> Plaid()
        {
            var plaid = new Mock<IFinancialService>();
            plaid.Setup(p => p.GetInstitutionAsync("ins_bofa")).ReturnsAsync(new InstitutionModel { institution_id = "ins_bofa", name = "Bank of America" });
            plaid.Setup(p => p.GetAccountsAsync(SecretToken)).ReturnsAsync([new AccountModel { account_id = "a1", name = "Checking" }]);
            plaid.Setup(p => p.GetItemAsync(SecretToken)).ReturnsAsync(new ItemModel { ItemId = "item-7", LastSuccessfulUpdate = DateTime.UtcNow });
            return plaid;
        }

        [Fact]
        public async Task A_healthy_bank_has_its_accounts()
        {
            var model = await AccessItemLoader.LoadAsync(Plaid().Object, Token);

            Assert.Equal(AccessItemStatus.Healthy, model.Status);
            Assert.Single(model.Accounts);
            Assert.Null(model.ExpiredAccessItem);
        }

        [Fact]
        public async Task A_bank_that_needs_sign_in_is_marked_and_never_carries_the_token()
        {
            var plaid = Plaid();
            plaid.Setup(p => p.GetAccountsAsync(SecretToken)).ThrowsAsync(PlaidError("ITEM_ERROR", "ITEM_LOGIN_REQUIRED"));

            var model = await AccessItemLoader.LoadAsync(plaid.Object, Token);

            Assert.Equal(AccessItemStatus.LoginRequired, model.Status);
            Assert.Equal(7, model.ExpiredAccessItem!.InstitutionAccessItemId);
            Assert.Equal(AccessItemLoader.LoginRequiredMessage, model.StatusMessage);
            Assert.DoesNotContain(SecretToken, JsonConvert.SerializeObject(model));
        }

        [Fact]
        public async Task A_bank_that_is_down_is_unavailable_and_still_has_an_item()
        {
            var plaid = Plaid();
            plaid.Setup(p => p.GetAccountsAsync(SecretToken)).ThrowsAsync(PlaidError("INSTITUTION_ERROR", "INSTITUTION_DOWN"));

            var model = await AccessItemLoader.LoadAsync(plaid.Object, Token);

            Assert.Equal(AccessItemStatus.Unavailable, model.Status);
            Assert.NotNull(model.Item);
            Assert.Equal("item-7", model.Item.ItemId);
            Assert.Null(model.ExpiredAccessItem);
        }

        [Fact]
        public async Task A_failed_institution_lookup_still_shows_the_bank()
        {
            var plaid = Plaid();
            plaid.Setup(p => p.GetInstitutionAsync("ins_bofa")).ThrowsAsync(PlaidError("API_ERROR", "INTERNAL_SERVER_ERROR"));

            var model = await AccessItemLoader.LoadAsync(plaid.Object, Token);

            Assert.Equal("Your bank", model.Institution.name);
            Assert.Equal(AccessItemStatus.Healthy, model.Status);
        }

        [Fact]
        public async Task An_item_error_from_item_get_counts_too()
        {
            var plaid = Plaid();
            plaid.Setup(p => p.GetItemAsync(SecretToken)).ReturnsAsync(new ItemModel { ItemId = "item-7", ErrorCode = "PENDING_EXPIRATION" });

            var model = await AccessItemLoader.LoadAsync(plaid.Object, Token);

            Assert.Equal(AccessItemStatus.LoginRequired, model.Status);
        }
    }
}
