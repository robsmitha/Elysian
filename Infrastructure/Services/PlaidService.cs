using AutoMapper;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Responses.Plaid;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;

namespace Elysian.Infrastructure.Services
{
    public class PlaidService(IHttpClientFactory httpClientFactory, IOptions<PlaidSettings> plaidSettings, IMapper mapper,
        IAccessTokenService accessTokenService, ICategoryService categoryService, ILogger<IFinancialService> logger) : IFinancialService
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

        public async Task<List<TransactionModel>> GetTransactionsAsync(string accessToken, DateTime startDate, DateTime endDate)
        {
            // get api response from plaid
            var response = await PostAsync<TransactionResponse>("transactions/get", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                access_token = accessToken,
                start_date = startDate.ToString("yyyy-MM-dd"),
                end_date = endDate.ToString("yyyy-MM-dd")
            });

            if(response?.accounts.Any() != true)
            {
                throw new FinancialServiceException($"{nameof(response.accounts)} cannot be null or empty.");
            }

            if(response?.transactions == null)
            {
                throw new FinancialServiceException($"{nameof(response.transactions)} cannot be null.");
            }

            // join accounts and transactions
            var accounts = mapper.Map<List<AccountModel>>(response.accounts);
            var transactions = mapper.Map<List<TransactionModel>>(response.transactions);
         
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
                    client_name = "Expense Tracker",
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
                    client_name = "Expense Tracker",
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
            var response = await PostAsync<dynamic>("transactions/refresh", new
            {
                client_id = _plaidSettings.ClientId,
                access_token = accessToken,
                secret = _plaidSettings.Secret
            });
            return response.request_id as string;
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
            var response = await PostAsync<dynamic>("item/remove", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                access_token = accessToken
            });

            return response.request_id as string;
        }

        public async Task<InstitutionModel> GetInstitutionAsync(string institutionId)
        {
            var response = await PostAsync<InstitutionResponse>("institutions/get_by_id", new
            {
                client_id = _plaidSettings.ClientId,
                secret = _plaidSettings.Secret,
                institution_id = institutionId,
                country_codes = new [] { "US" }
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

        private async Task<T> PostAsync<T>(string endpoint, dynamic data)
        {
            try
            {
                var content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

                var client = httpClientFactory.CreateClient("PlaidClient");
                var response = await client.PostAsync(endpoint, content);
                var result = await response.Content.ReadAsStringAsync();
                var json = JsonConvert.SerializeObject(result);
                if (!response.IsSuccessStatusCode)
                {
                    var error = JsonConvert.DeserializeObject<Error>(result);
                    throw new FinancialServiceException(error);
                }

                return JsonConvert.DeserializeObject<T>(result);
            }
            catch (HttpRequestException)
            {
                throw;
            }
        }
    }
}
