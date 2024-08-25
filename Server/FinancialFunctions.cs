using Elysian.Application.Extensions;
using Elysian.Application.Features.Financial.Commands;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Features.Financial.Queries;
using MediatR;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Elysian
{
    public class FinancialFunctions(IMediator mediator)
    {
        #region Institution Access Items

        [Function("GetUserAccessItems")]
        public async Task<HttpResponseData> GetUserAccessItems([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            return await req.WriteJsonResponseAsync(await mediator.Send(new GetUserAccessItemsQuery()));
        }

        [Function("GetUserAccessItem")]
        public async Task<HttpResponseData> GetUserAccessItem([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            _ = int.TryParse(req.Query["userAccessItemId"], out var userAccessItemId);

            return await req.WriteJsonResponseAsync(await mediator.Send(new GetUserAccessItemQuery(userAccessItemId)));
        }

        [Function("CreateFinancialLinkToken")]
        public async Task<HttpResponseData> CreateFinancialLinkToken([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var accessToken = req.Query["accessToken"];

            return await req.WriteJsonResponseAsync(await mediator.Send(new CreateLinkTokenCommand(accessToken)));
        }

        [Function("SetInstitutionAccessToken")]
        public async Task<HttpResponseData> SetInstitutionAccessToken([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var exchangePublicToken = await req.DeserializeBodyAsync<ExchangePublicTokenModel>();

            return await req.WriteJsonResponseAsync(await mediator.Send(new SetAccessTokenCommand(exchangePublicToken?.public_token)));
        }

        #endregion

        #region Financial Categories

        [Function("FinancialCategories")]
        public async Task<HttpResponseData> FinancialCategories([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            return await req.WriteJsonResponseAsync(await mediator.Send(new GetCategoriesQuery()));
        }

        [Function("SaveFinancialCategory")]
        public async Task<HttpResponseData> SaveFinancialCategory([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var model = await req.DeserializeBodyAsync<SaveCategoryModel>();

            return await req.WriteJsonResponseAsync(await mediator.Send(new SaveCategoryCommand(model, model.BudgetId, model.Estimate)));
        }

        #endregion

        #region Budgets


        [Function("GetBudgets")]
        public async Task<HttpResponseData> GetBudgets([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            return await req.WriteJsonResponseAsync(await mediator.Send(new GetBudgetsQuery()));
        }

        [Function("GetBudget")]
        public async Task<HttpResponseData> GetBudget([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            _ = int.TryParse(req.Query["budgetId"], out var budgetId);

            return await req.WriteJsonResponseAsync(await mediator.Send(new GetBudgetQuery(budgetId)));
        }

        [Function("GetBudgetTransactions")]
        public async Task<HttpResponseData> GetBudgetTransactions([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            _ = int.TryParse(req.Query["budgetId"], out var budgetId);

            return await req.WriteJsonResponseAsync(await mediator.Send(new GetTransactionsQuery(budgetId)));
        }

        [Function("SaveBudget")]
        public async Task<HttpResponseData> SaveBudget([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var model = await req.DeserializeBodyAsync<BudgetModel>();

            return await req.WriteJsonResponseAsync(await mediator.Send(new SaveBudgetCommand(model)));
        }

        [Function("SetTransactionCategory")]
        public async Task<HttpResponseData> SetTransactionCategory([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var model = await req.DeserializeBodyAsync<TransactionCategoryModel>();

            return await req.WriteJsonResponseAsync(await mediator.Send(new SetTransactionCategoryCommand(model.TransactionId, model.FinancialCategoryId, model.BudgetId)));
        }

        [Function("BulkUpdateTransactionCategory")]
        public async Task<HttpResponseData> BulkUpdateTransactionCategory([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var model = await req.DeserializeBodyAsync<List<TransactionCategoryModel>>();
            var results = new List<TransactionCategoryModel>();
            foreach (var item in model)
            {
                var result = await mediator.Send(new SetTransactionCategoryCommand(item.TransactionId, item.FinancialCategoryId, item.BudgetId));
                results.Add(result);
            }
            return await req.WriteJsonResponseAsync(results);
        }

        [Function("UpdateBudgetCategoryEstimate")]
        public async Task<HttpResponseData> UpdateBudgetCategoryEstimate([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var model = await req.DeserializeBodyAsync<BudgetCategoryModel>();

            return await req.WriteJsonResponseAsync(await mediator.Send(new UpdateBudgetCategoryCommand(model.BudgetId, model.FinancialCategoryName, model.Estimate)));
        }

        [Function("SetExcludedTransaction")]
        public async Task<HttpResponseData> SetExcludedTransaction([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var model = await req.DeserializeBodyAsync<BudgetExcludedTransactionModel>();

            return await req.WriteJsonResponseAsync(await mediator.Send(new SetExcludedTransactionCommand(model.TransactionId, model.BudgetId)));
        }

        [Function("RestoreExcludedTransaction")]
        public async Task<HttpResponseData> RestoreExcludedTransaction([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            var model = await req.DeserializeBodyAsync<BudgetExcludedTransactionModel>();

            return await req.WriteJsonResponseAsync(await mediator.Send(new RestoreExcludedTransactionCommand(model.TransactionId, model.BudgetId)));
        }

        #endregion
    }
}
