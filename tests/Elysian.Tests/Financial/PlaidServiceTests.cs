using System.Net;
using System.Text;
using AutoMapper;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elysian.Tests.Financial
{
    public class PlaidServiceTests
    {
        public static IMapper Mapper { get; } = new MapperConfiguration(cfg => cfg.AddMaps(typeof(BudgetModel).Assembly)).CreateMapper();

        /// <summary>Answers Plaid calls from a function, and records each request body and header.</summary>
        private sealed class FakePlaid(Func<string, JObject, HttpResponseMessage> respond) : HttpMessageHandler
        {
            public List<(string Path, JObject Body, string? Version)> Calls { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var version = request.Headers.TryGetValues("Plaid-Version", out var v) ? v.Single() : null;
                var path = request.RequestUri!.AbsolutePath.TrimStart('/');
                Calls.Add((path, body, version));
                return respond(path, body);
            }
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json") };

        private static PlaidService Service(FakePlaid fake)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("PlaidClient"))
                .Returns(() => new HttpClient(fake) { BaseAddress = new Uri("https://sandbox.plaid.com/") });
            return new PlaidService(factory.Object,
                Options.Create(new PlaidSettings { BaseUrl = "https://sandbox.plaid.com/", ClientId = "client", Secret = "secret" }),
                Mapper, Mock.Of<IAccessTokenService>(), Mock.Of<ICategoryService>(), NullLogger<IFinancialService>.Instance,
                Mock.Of<IMultiTenantContextAccessor<ElysianTenantInfo>>());
        }

        private static object Txn(int i) => new
        {
            transaction_id = $"txn-{i}",
            account_id = "acct-1",
            amount = 10.25,
            date = "2026-11-01",
            name = $"Store {i}",
            pending = false
        };

        [Fact]
        public async Task Transactions_are_read_page_by_page_until_the_total()
        {
            const int total = 1250;
            var fake = new FakePlaid((path, body) =>
            {
                var offset = body["options"]!["offset"]!.Value<int>();
                var count = body["options"]!["count"]!.Value<int>();
                return Json(new
                {
                    accounts = new[] { new { account_id = "acct-1", name = "Gold Card", type = "credit", balances = new { current = 100.5 } } },
                    transactions = Enumerable.Range(offset, Math.Min(count, total - offset)).Select(Txn),
                    total_transactions = total
                });
            });

            var transactions = await Service(fake).GetTransactionsAsync("access-sandbox-1", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));

            Assert.Equal(total, transactions.Count);
            Assert.Equal(total, transactions.Select(t => t.transaction_id).Distinct().Count());
            Assert.Equal([0, 500, 1000], fake.Calls.Select(c => c.Body["options"]!["offset"]!.Value<int>()));
            Assert.All(transactions, t => Assert.Equal("Gold Card", t.Account.name));
        }

        [Fact]
        public async Task A_transaction_that_shifts_pages_is_kept_once()
        {
            // Plaid adds a transaction between the two page reads, so txn-499 shows up on both pages
            var fake = new FakePlaid((path, body) =>
            {
                var offset = body["options"]!["offset"]!.Value<int>();
                var ids = offset == 0 ? Enumerable.Range(0, 500) : Enumerable.Range(499, 2);
                return Json(new
                {
                    accounts = new[] { new { account_id = "acct-1", name = "Gold Card", type = "credit" } },
                    transactions = ids.Select(Txn),
                    total_transactions = 501
                });
            });

            var transactions = await Service(fake).GetTransactionsAsync("access-sandbox-1", DateTime.Today, DateTime.Today);

            Assert.Equal(501, transactions.Count);
        }

        [Fact]
        public async Task A_bank_with_no_accounts_has_no_transactions()
        {
            var fake = new FakePlaid((_, _) => Json(new { accounts = Array.Empty<object>(), transactions = Array.Empty<object>(), total_transactions = 0 }));

            Assert.Empty(await Service(fake).GetTransactionsAsync("access-sandbox-1", DateTime.Today, DateTime.Today));
        }

        [Fact]
        public async Task Every_request_pins_the_Plaid_API_version()
        {
            var fake = new FakePlaid((_, _) => Json(new { institution = new { institution_id = "ins_1", name = "Bank" } }));

            await Service(fake).GetInstitutionAsync("ins_1");

            Assert.Equal("2020-09-14", fake.Calls.Single().Version);
        }

        [Fact]
        public async Task Institution_lookup_asks_for_logo_and_keeps_only_safe_values()
        {
            var fake = new FakePlaid((_, _) => Json(new
            {
                institution = new { institution_id = "ins_1", name = "Bank of America", logo = "iVBORw0KGgo=", primary_color = "javascript:alert(1)" }
            }));

            var institution = await Service(fake).GetInstitutionAsync("ins_1");

            Assert.True(fake.Calls.Single().Body["options"]!["include_optional_metadata"]!.Value<bool>());
            Assert.Equal("iVBORw0KGgo=", institution.logo);
            Assert.Null(institution.primary_color);
        }

        [Fact]
        public async Task A_Plaid_error_carries_its_code()
        {
            var fake = new FakePlaid((_, _) => Json(new
            {
                error_type = "ITEM_ERROR",
                error_code = "ITEM_LOGIN_REQUIRED",
                error_message = "the login details of this item have changed",
                display_message = (string?)null
            }, HttpStatusCode.BadRequest));

            var ex = await Assert.ThrowsAsync<FinancialServiceException>(() => Service(fake).GetAccountsAsync("access-sandbox-1"));

            Assert.Equal("ITEM_LOGIN_REQUIRED", ex.Error.error_code);
        }

        [Fact]
        public async Task An_error_that_is_not_JSON_still_becomes_a_financial_error()
        {
            var fake = new FakePlaid((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html>Bad gateway</html>", Encoding.UTF8, "text/html")
            });

            var ex = await Assert.ThrowsAsync<FinancialServiceException>(() => Service(fake).GetAccountsAsync("access-sandbox-1"));

            Assert.Contains("502", ex.Message);
            Assert.NotNull(ex.Error);
        }

        [Fact]
        public async Task Balances_are_mapped_onto_accounts()
        {
            var fake = new FakePlaid((_, _) => Json(new
            {
                accounts = new[] { new { account_id = "acct-1", name = "Blue Cash", type = "credit", balances = new { current = 4812.33, limit = 12000.0, iso_currency_code = "USD" } } }
            }));

            var account = (await Service(fake).GetAccountsAsync("access-sandbox-1")).Single();

            Assert.Equal(4812.33m, account.balances!.current);
            Assert.Equal(12000m, account.balances.limit);
        }
    }
}
