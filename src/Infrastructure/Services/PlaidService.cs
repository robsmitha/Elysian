using AutoMapper;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;

namespace Elysian.Infrastructure.Services
{
    public class PlaidService(IHttpClientFactory httpClientFactory, IOptions<PlaidSettings> plaidSettings, IMapper mapper,
        IAccessTokenService accessTokenService, ICategoryService categoryService, ILogger<IFinancialService> logger,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor) 
        : IFinancialService
    {
        private readonly PlaidSettings _plaidSettings = plaidSettings.Value;

        public async Task<InstitutionAccessTokenModel> SetAccessTokenAsync(string userId, string accessToken)
        {
            var item = await GetItemAsync(accessToken);
            if (item.HasError)
            {
                logger.LogError($"Item [{item.ItemId}] returned error code: {item.ErrorCode}");
                throw new FinancialServiceException($"Error code: {item.ErrorCode} ({item.ItemId})");
            }
            return await accessTokenService.SetAccessTokenAsync(userId, accessToken, item.ItemId, item.InstitutionId);
        }

        // Plaid's page size limit for /transactions/get, and a ceiling so a runaway date range can't loop forever
        internal const int TransactionPageSize = 500;
        internal const int MaxTransactionsPerItem = 10_000;

        public async Task<List<TransactionModel>> GetTransactionsAsync(string accessToken, DateTime startDate, DateTime endDate)
        {
            // /transactions/get returns one page at a time (100 by default), so read pages until total_transactions
            var pages = new List<TransactionResponse.Transaction>();
            List<Account>? plaidAccounts = null;
            int total;
            do
            {
                var page = await PostAsync<TransactionResponse>("transactions/get", new
                {
                    client_id = _plaidSettings.ClientId,
                    secret = _plaidSettings.Secret,
                    access_token = accessToken,
                    start_date = startDate.ToString("yyyy-MM-dd"),
                    end_date = endDate.ToString("yyyy-MM-dd"),
                    options = new { count = TransactionPageSize, offset = pages.Count }
                });

                plaidAccounts ??= page?.accounts;
                var batch = page?.transactions ?? [];
                pages.AddRange(batch);
                total = page?.total_transactions ?? 0;

                if (batch.Count == 0)
                {
                    break;
                }
            } while (pages.Count < total && pages.Count < MaxTransactionsPerItem);

            if (plaidAccounts?.Any() != true)
            {
                // A bank with no shared accounts has no transactions to show
                return [];
            }

            // A transaction can shift pages if Plaid adds one mid-read, so keep each id once
            var accounts = mapper.Map<List<AccountModel>>(plaidAccounts);
            var transactions = mapper.Map<List<TransactionModel>>(pages.DistinctBy(t => t.transaction_id).ToList());
         
            var results = transactions.Join(
                accounts,
                t => t.account_id,
                a => a.account_id,
                (t, a) =>
                {
                    t.Account = a;
                    return t;
                }).ToList();

            return results;
        }

        public async Task<LinkTokenModel> CreateLinkTokenAsync(string userId, string accessToken = null)
        {
            var response = string.IsNullOrEmpty(accessToken)
                ? await PostAsync<CreateLinkTokenResponse>("link/token/create", new
                {
                    client_id = _plaidSettings.ClientId,
                    secret = _plaidSettings.Secret,
                    user = new
                    {
                        client_user_id = userId
                    },
                    client_name = multiTenantContextAccessor.MultiTenantContext.TenantInfo.Name,
                    products = new[] { "transactions" },
                    country_codes = new[] { "US" },
                    language = "en"
                })
                : await PostAsync<CreateLinkTokenResponse>("link/token/create", new
                {
                    client_id = _plaidSettings.ClientId,
                    secret = _plaidSettings.Secret,
                    user = new
                    {
                        client_user_id = userId
                    },
                    client_name = multiTenantContextAccessor.MultiTenantContext.TenantInfo.Name,
                    access_token = accessToken,
                    country_codes = new[] { "US" },
                    language = "en"
                });

            return mapper.Map<LinkTokenModel>(response);
        }

        public async Task<ItemPublicTokenExchangeModel> ItemPublicTokenExchangeAsync(string publicToken)
        {
            var response = await PostAsync<ItemPublicTokenExchangeResponse>("item/public_token/exchange", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                public_token = publicToken
            });

            return mapper.Map<ItemPublicTokenExchangeModel>(response);
        }

        public async Task<string> RefreshTransactionsAsync(string accessToken)
        {
            var response = await PostAsync<RequestIdResponse>("transactions/refresh", new
            {
                client_id = _plaidSettings.ClientId,
                access_token = accessToken,
                secret = _plaidSettings.Secret
            });
            return response?.request_id;
        }

        public async Task<ItemModel> GetItemAsync(string accessToken)
        {
            var response = await PostAsync<ItemResponse>("item/get", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                access_token = accessToken
            });

            return mapper.Map<ItemModel>(response);
        }

        public async Task<string> RemoveItemAsync(string accessToken)
        {
            var response = await PostAsync<RequestIdResponse>("item/remove", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                access_token = accessToken
            });

            return response?.request_id;
        }

        public async Task<InstitutionModel> GetInstitutionAsync(string institutionId)
        {
            var response = await PostAsync<InstitutionResponse>("institutions/get_by_id", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                institution_id = institutionId,
                country_codes = new [] { "US" },
                // Adds the bank's logo and brand color for display
                options = new { include_optional_metadata = true }
            });
            return mapper.Map<InstitutionModel>(response.institution);
        }
        public async Task<List<InstitutionModel>> GetInstitutionsAsync(int count, int offset)
        {
            var response = await PostAsync<InstitutionResponse>("institutions/get", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                count = count,
                offset = offset,
                country_codes = new[] { "US" }
            });
            return mapper.Map<List<InstitutionModel>>(response.institution);
        }
        public async Task<List<AccountModel>> GetAccountsAsync(string accessToken)
        {
            var response = await PostAsync<GetAccountsResponse>("accounts/get", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                access_token = accessToken
            });
            var accounts = mapper.Map<List<AccountModel>>(response.accounts);
            return accounts;
        }

        // Pin Plaid's API version so a change to Plaid's account default can't change response shapes under us
        internal const string PlaidVersion = "2020-09-14";

        private async Task<T> PostAsync<T>(string endpoint, object data)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Plaid-Version", PlaidVersion);

            var client = httpClientFactory.CreateClient("PlaidClient");
            using var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Error? error = null;
                try
                {
                    error = JsonConvert.DeserializeObject<Error>(result);
                }
                catch (JsonException)
                {
                    // Not Plaid's JSON error (a gateway page, for example); fall back to the status code below
                }
                logger.LogWarning("Plaid {Endpoint} failed: {Status} {ErrorType}/{ErrorCode} ({RequestId})",
                    endpoint, (int)response.StatusCode, error?.error_type, error?.error_code, error?.request_id);
                throw new FinancialServiceException(error, $"Plaid {endpoint} returned {(int)response.StatusCode}.");
            }

            return JsonConvert.DeserializeObject<T>(result)!;
        }

        private sealed class RequestIdResponse
        {
            public string? request_id { get; set; }
        }
    }
}
