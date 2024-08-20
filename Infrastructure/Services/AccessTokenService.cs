using AutoMapper;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    public class AccessTokenService(ElysianContext context, IMapper mapper) : IAccessTokenService
    {
        public async Task<List<InstitutionAccessTokenModel>> GetAccessTokensAsync(string userId)
        {
            var accessToken = await context.InstitutionAccessItems.Where(a => a.UserId == userId).ToListAsync();
            return mapper.Map<List<InstitutionAccessTokenModel>>(accessToken);
        }

        public async Task<List<InstitutionAccessTokenModel>> GetBudgetAccessTokensAsync(string userId, int budgetId)
        {
            var budgetAccessItemIds = await context.BudgetAccessItems.Where(a => a.BudgetId == budgetId).Select(a => a.InstitutionAccessItemId).ToListAsync();
            var accessToken = await context.InstitutionAccessItems.Where(a => budgetAccessItemIds.Contains(a.InstitutionAccessItemId)).ToListAsync();
            return mapper.Map<List<InstitutionAccessTokenModel>>(accessToken);
        }

        public async Task<InstitutionAccessTokenModel> GetAccessTokenAsync(string userId, string itemId)
        {
            var accessToken = await context.InstitutionAccessItems.FirstOrDefaultAsync(a => a.UserId == userId && a.ItemId == itemId);
            return mapper.Map<InstitutionAccessTokenModel>(accessToken);
        }

        public async Task<InstitutionAccessTokenModel> GetAccessTokenAsync(string userId, int institutionAccessItemId)
        {
            var accessToken = await context.InstitutionAccessItems.FirstOrDefaultAsync(a => a.UserId == userId && a.InstitutionAccessItemId == institutionAccessItemId);
            return mapper.Map<InstitutionAccessTokenModel>(accessToken);
        }

        public async Task<InstitutionAccessTokenModel> SetAccessTokenAsync(string userId, string accessToken, string itemId, string institutionId)
        {
            var userAccessItem = new InstitutionAccessItem
            {
                UserId = userId,
                AccessToken = accessToken,
                ItemId = itemId,
                InstitutionId = institutionId
            };
            await context.AddAsync(userAccessItem);
            await context.SaveChangesAsync();
            return mapper.Map<InstitutionAccessTokenModel>(userAccessItem);
        }
    }
}
