using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    public class EntraDirectoryService(IGraphApiClient graph, IOptions<EntraSettings> options) : IEntraDirectoryService
    {
        private const string UserSelect = "$select=id,displayName,mail,userPrincipalName,userType,accountEnabled,externalUserState";

        // Advanced queries (any() over otherMails) need this header and $count
        private static readonly Dictionary<string, string> EventualConsistency = new() { ["ConsistencyLevel"] = "eventual" };

        private EntraSettings Settings => options.Value;

        public async Task<EntraUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            var value = Uri.EscapeDataString(email.Replace("'", "''"));
            var filter = $"mail eq '{value}' or userPrincipalName eq '{value}' or otherMails/any(m:m eq '{value}')";

            var users = await graph.SendAsync<Collection<EntraUser>>(HttpMethod.Get,
                $"users?$filter={filter}&$count=true&{UserSelect}", headers: EventualConsistency, cancellationToken: cancellationToken);

            // Prefer a member over a guest if both exist for the same address
            return users?.Value?
                .OrderBy(u => u.UserType == "Member" ? 0 : 1)
                .FirstOrDefault();
        }

        public async Task<EntraUser?> GetUserAsync(string objectId, CancellationToken cancellationToken = default)
        {
            try
            {
                return await graph.SendAsync<EntraUser>(HttpMethod.Get, $"users/{Escape(objectId)}?{UserSelect}", cancellationToken: cancellationToken);
            }
            catch (GraphNotFoundException)
            {
                return null;
            }
        }

        public async Task<Dictionary<string, EntraUser>> GetUsersAsync(IEnumerable<string> objectIds, CancellationToken cancellationToken = default)
        {
            var paths = objectIds.Distinct().ToDictionary(id => $"users/{Escape(id)}?{UserSelect}", id => id);
            var responses = await graph.BatchGetAsync(paths.Keys, cancellationToken);

            var users = new Dictionary<string, EntraUser>();
            foreach (var (path, body) in responses)
            {
                if (body is JsonElement element && element.Deserialize<EntraUser>(GraphApiClient.JsonOptions) is EntraUser user)
                {
                    users[paths[path]] = user;
                }
            }
            return users;
        }

        public async Task<EntraInvitation> InviteGuestAsync(string email, string displayName, CancellationToken cancellationToken = default)
        {
            var response = await graph.SendAsync<InvitationResponse>(HttpMethod.Post, "invitations", new
            {
                invitedUserEmailAddress = email,
                invitedUserDisplayName = displayName,
                inviteRedirectUrl = Settings.AppUrl,
                sendInvitationMessage = false
            }, cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(response?.InvitedUser?.Id) || string.IsNullOrWhiteSpace(response.InviteRedeemUrl))
            {
                throw new GraphApiException("Graph's invitation response did not include the invited user and redeem URL.");
            }

            return new EntraInvitation(response.InvitedUser.Id, response.InviteRedeemUrl, response.Status);
        }

        public Task UpdateDisplayNameAsync(string objectId, string displayName, CancellationToken cancellationToken = default)
        {
            return graph.SendAsync(HttpMethod.Patch, $"users/{Escape(objectId)}", new { displayName }, cancellationToken);
        }

        public async Task DeleteUserAsync(string objectId, CancellationToken cancellationToken = default)
        {
            try
            {
                await graph.SendAsync(HttpMethod.Delete, $"users/{Escape(objectId)}", cancellationToken: cancellationToken);
            }
            catch (GraphNotFoundException)
            {
                // Already gone
            }
        }

        public async Task<EntraAppRoleAssignment?> FindAssignmentAsync(string principalId, CancellationToken cancellationToken = default)
        {
            List<AssignmentResponse> assignments;
            try
            {
                assignments = await graph.GetAllPagesAsync<AssignmentResponse>(
                    $"users/{Escape(principalId)}/appRoleAssignments?$filter=resourceId eq {Escape(Settings.EnterpriseAppServicePrincipalId)}",
                    cancellationToken: cancellationToken);
            }
            catch (GraphNotFoundException)
            {
                return null;
            }

            return assignments
                .Where(a => string.Equals(a.ResourceId, Settings.EnterpriseAppServicePrincipalId, StringComparison.OrdinalIgnoreCase))
                .Select(ToModel)
                .OrderBy(a => string.Equals(a.AppRoleId, Settings.AppRoleId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
        }

        public async Task<List<EntraAppRoleAssignment>> ListAssignmentsAsync(CancellationToken cancellationToken = default)
        {
            var assignments = await graph.GetAllPagesAsync<AssignmentResponse>(
                $"servicePrincipals/{Escape(Settings.EnterpriseAppServicePrincipalId)}/appRoleAssignedTo?$top=999",
                cancellationToken: cancellationToken);

            return assignments
                .Where(a => a.PrincipalType == "User")
                .Select(ToModel)
                .ToList();
        }

        public async Task<EntraAppRoleAssignment> AssignAsync(string principalId, CancellationToken cancellationToken = default)
        {
            try
            {
                var created = await graph.SendAsync<AssignmentResponse>(HttpMethod.Post,
                    $"servicePrincipals/{Escape(Settings.EnterpriseAppServicePrincipalId)}/appRoleAssignedTo", new
                    {
                        principalId,
                        resourceId = Settings.EnterpriseAppServicePrincipalId,
                        appRoleId = Settings.AppRoleId
                    }, cancellationToken: cancellationToken);

                return created == null
                    ? throw new GraphApiException("Graph's app role assignment response was empty.")
                    : ToModel(created);
            }
            catch (GraphConflictException)
            {
                return await FindAssignmentAsync(principalId, cancellationToken)
                    ?? throw new GraphApiException("Graph reported the app role assignment exists, but it could not be found.");
            }
        }

        public async Task RemoveAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default)
        {
            try
            {
                await graph.SendAsync(HttpMethod.Delete,
                    $"servicePrincipals/{Escape(Settings.EnterpriseAppServicePrincipalId)}/appRoleAssignedTo/{Escape(assignmentId)}",
                    cancellationToken: cancellationToken);
            }
            catch (GraphNotFoundException)
            {
                // Already gone
            }
        }

        private static EntraAppRoleAssignment ToModel(AssignmentResponse a) => new(a.Id, a.PrincipalId, a.AppRoleId);

        private static string Escape(string value) => Uri.EscapeDataString(value);

        private class Collection<T>
        {
            public List<T>? Value { get; set; }
        }

        private class InvitationResponse
        {
            public string? InviteRedeemUrl { get; set; }
            public string? Status { get; set; }
            public InvitedUserResponse? InvitedUser { get; set; }
        }

        private class InvitedUserResponse
        {
            public string? Id { get; set; }
        }

        private class AssignmentResponse
        {
            public string Id { get; set; } = string.Empty;
            public string PrincipalId { get; set; } = string.Empty;
            public string? PrincipalType { get; set; }
            public string ResourceId { get; set; } = string.Empty;
            public string AppRoleId { get; set; } = string.Empty;
        }
    }
}
