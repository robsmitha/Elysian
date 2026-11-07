using AutoMapper;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Infrastructure.Services
{
    public class AccessTokenService(ElysianContext context, IMapper mapper, IAccessTokenProtector protector,
        ILogger<AccessTokenService> logger) : IAccessTokenService
    {
        private IQueryable<InstitutionAccessItem> Owned(string userId) =>
            context.InstitutionAccessItems.Where(a => a.RemovedAt == null && a.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        public async Task<List<InstitutionAccessTokenModel>> GetAccessTokensAsync(string userId) =>
            await ToModelsAsync(await Owned(userId).ToListAsync());

        public async Task<List<InstitutionAccessTokenModel>> GetBudgetAccessTokensAsync(string userId, int budgetId)
        {
            var itemIds = context.BudgetAccessItems.Where(a => a.BudgetId == budgetId && a.Budget.UserId == userId).Select(a => a.InstitutionAccessItemId);
            var items = await context.InstitutionAccessItems.Where(a => a.RemovedAt == null && itemIds.Contains(a.InstitutionAccessItemId)).ToListAsync();
            return await ToModelsAsync(items);
        }

        public async Task<InstitutionAccessTokenModel?> GetAccessTokenAsync(string userId, string itemId) =>
            (await ToModelsAsync(await Owned(userId).Where(a => a.ItemId == itemId).Take(1).ToListAsync())).FirstOrDefault();

        public async Task<InstitutionAccessTokenModel?> GetAccessTokenAsync(string userId, int institutionAccessItemId) =>
            (await ToModelsAsync(await Owned(userId).Where(a => a.InstitutionAccessItemId == institutionAccessItemId).ToListAsync())).FirstOrDefault();

        public async Task<InstitutionAccessTokenModel?> GetAccessTokenForSyncAsync(int institutionAccessItemId) =>
            (await ToModelsAsync(await context.InstitutionAccessItems
                .Where(a => a.RemovedAt == null && a.InstitutionAccessItemId == institutionAccessItemId).ToListAsync())).FirstOrDefault();

        public async Task<InstitutionAccessTokenModel> SetAccessTokenAsync(string userId, string accessToken, string itemId, string institutionId)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.ExternalUserId == userId)
                ?? throw new InvalidOperationException("The signed-in person has no user record.");

            var userAccessItem = new InstitutionAccessItem
            {
                AccessTokenCiphertext = protector.Protect(accessToken),
                ItemId = itemId,
                InstitutionId = institutionId,
                AccessUsers = [new InstitutionAccessItemUser { UserId = user.UserId }]
            };
            // One save, so a bank is never stored without its owner
            await context.AddAsync(userAccessItem);
            await context.SaveChangesAsync();

            var model = mapper.Map<InstitutionAccessTokenModel>(userAccessItem);
            model.AccessToken = accessToken;
            return model;
        }

        private async Task<List<InstitutionAccessTokenModel>> ToModelsAsync(List<InstitutionAccessItem> items)
        {
            var upgraded = false;
            var models = new List<InstitutionAccessTokenModel>(items.Count);
            foreach (var item in items)
            {
                var model = mapper.Map<InstitutionAccessTokenModel>(item);
                if (item.AccessTokenCiphertext != null)
                {
                    model.AccessToken = protector.Unprotect(item.AccessTokenCiphertext);
                }
                else if (item.AccessToken != null)
                {
                    model.AccessToken = item.AccessToken;
                    // A token stored before encryption: encrypt it now and clear the plaintext copy
                    if (protector.CanProtect)
                    {
                        item.AccessTokenCiphertext = protector.Protect(item.AccessToken);
                        item.AccessToken = null;
                        upgraded = true;
                    }
                }
                models.Add(model);
            }

            if (upgraded)
            {
                await context.SaveChangesAsync();
                logger.LogInformation("Encrypted {Count} stored Plaid access token(s)", items.Count(i => i.AccessToken == null && i.AccessTokenCiphertext != null));
            }
            return models;
        }
    }
}
