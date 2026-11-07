using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;

namespace Elysian.Application.Features.Financial
{
    /// <summary>
    /// Reads one linked bank from Plaid for display. A bank Plaid can't read still comes back, with a status and a
    /// message, so one broken connection never hides the others or crashes the page.
    /// </summary>
    public static class AccessItemLoader
    {
        // Item errors the person fixes by signing in again through Plaid Link (update mode)
        private static readonly HashSet<string> LoginCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ITEM_LOGIN_REQUIRED", "PENDING_EXPIRATION", "PENDING_DISCONNECT", "ACCESS_NOT_GRANTED",
            "NO_ACCOUNTS", "USER_PERMISSION_REVOKED", "INSUFFICIENT_CREDENTIALS", "INVALID_CREDENTIALS"
        };

        // Error types that are Plaid's or the bank's problem, usually temporary
        private static readonly HashSet<string> TemporaryTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "API_ERROR", "INSTITUTION_ERROR", "RATE_LIMIT_EXCEEDED"
        };

        public const string LoginRequiredMessage = "This bank needs you to sign in again before new transactions can sync.";
        public const string UnavailableMessage = "This bank isn't responding right now. It usually comes back on its own.";
        public const string ErrorMessage = "Something went wrong reading this bank.";

        public static bool IsLoginRequired(string? errorCode) => errorCode != null && LoginCodes.Contains(errorCode);

        public static bool IsLoginRequired(FinancialServiceException exception) => IsLoginRequired(exception.Error?.error_code);

        public static async Task<InstitutionModel> GetInstitutionAsync(IFinancialService financialService, string institutionId)
        {
            try
            {
                return await financialService.GetInstitutionAsync(institutionId) ?? InstitutionModel.Unknown(institutionId);
            }
            catch (FinancialServiceException)
            {
                // Name and logo are for display only
                return InstitutionModel.Unknown(institutionId);
            }
        }

        public static async Task<InstitutionAccessItemModel> LoadAsync(IFinancialService financialService, InstitutionAccessTokenModel token)
        {
            var institution = await GetInstitutionAsync(financialService, token.InstitutionId);
            var model = new InstitutionAccessItemModel
            {
                InstitutionAccessItemId = token.InstitutionAccessItemId,
                Institution = institution,
                Item = new ItemModel { ItemId = token.ItemId, InstitutionId = token.InstitutionId }
            };

            try
            {
                model.Accounts = await financialService.GetAccountsAsync(token.AccessToken) ?? [];
                model.Item = await financialService.GetItemAsync(token.AccessToken) ?? model.Item;
                if (model.Item.HasError)
                {
                    ApplyError(model, model.Item.ErrorCode, errorType: "ITEM_ERROR", model.Item.ErrorDisplayMessage);
                }
            }
            catch (FinancialServiceException fex)
            {
                ApplyError(model, fex.Error?.error_code, fex.Error?.error_type, fex.Error?.display_message);
            }

            return model;
        }

        /// <summary>Marks a bank as needing attention from a Plaid error.</summary>
        public static void ApplyError(InstitutionAccessItemModel model, string? errorCode, string? errorType, string? displayMessage)
        {
            model.Item.ErrorCode = errorCode;
            if (IsLoginRequired(errorCode))
            {
                model.Status = AccessItemStatus.LoginRequired;
                model.StatusMessage = string.IsNullOrWhiteSpace(displayMessage) ? LoginRequiredMessage : displayMessage;
                model.ExpiredAccessItem = new ExpiredAccessItem(model.InstitutionAccessItemId, model.StatusMessage, model.Institution);
            }
            else if (errorType != null && TemporaryTypes.Contains(errorType))
            {
                model.Status = AccessItemStatus.Unavailable;
                model.StatusMessage = UnavailableMessage;
            }
            else
            {
                model.Status = AccessItemStatus.Error;
                model.StatusMessage = string.IsNullOrWhiteSpace(displayMessage) ? ErrorMessage : displayMessage;
            }
            model.Item.ErrorDisplayMessage = model.StatusMessage;
        }

        /// <summary>The "sign in again" record for a bank whose transactions couldn't be read, or null for other errors.</summary>
        public static ExpiredAccessItem? ExpiredFor(FinancialServiceException exception, int institutionAccessItemId, InstitutionModel institution)
        {
            if (!IsLoginRequired(exception))
            {
                return null;
            }
            var message = string.IsNullOrWhiteSpace(exception.Error?.display_message) ? LoginRequiredMessage : exception.Error.display_message;
            return new ExpiredAccessItem(institutionAccessItemId, message, institution);
        }
    }
}
