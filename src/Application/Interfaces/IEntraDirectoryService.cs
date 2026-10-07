namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// The Entra ID operations user management needs: B2B invitations, and app role assignments on the configured
    /// enterprise application.
    /// </summary>
    public interface IEntraDirectoryService
    {
        /// <summary>
        /// Finds a user whose mail, user principal name or other mails match. Null when there's none.
        /// </summary>
        Task<EntraUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken = default);

        Task<EntraUser?> GetUserAsync(string objectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Batched lookup. Ids Graph doesn't know are left out.
        /// </summary>
        Task<Dictionary<string, EntraUser>> GetUsersAsync(IEnumerable<string> objectIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates (or, for an existing guest, refreshes) a B2B invitation without Microsoft's own email.
        /// The result holds a redeem URL: send it to the invitee and nowhere else.
        /// </summary>
        Task<EntraInvitation> InviteGuestAsync(string email, string displayName, CancellationToken cancellationToken = default);

        Task UpdateDisplayNameAsync(string objectId, string displayName, CancellationToken cancellationToken = default);

        Task DeleteUserAsync(string objectId, CancellationToken cancellationToken = default);

        /// <summary>
        /// The user's assignment to the enterprise app, or null
        /// </summary>
        Task<EntraAppRoleAssignment?> FindAssignmentAsync(string principalId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Every user assignment on the enterprise app
        /// </summary>
        Task<List<EntraAppRoleAssignment>> ListAssignmentsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Assigns the configured app role. An assignment that already exists counts as success and is returned.
        /// </summary>
        Task<EntraAppRoleAssignment> AssignAsync(string principalId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes an assignment. One that's already gone counts as success.
        /// </summary>
        Task RemoveAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default);
    }

    /// <param name="ExternalUserState">PendingAcceptance or Accepted for guests; null for members</param>
    public record EntraUser(string Id, string? DisplayName, string? Mail, string? UserPrincipalName, string? UserType,
        bool? AccountEnabled, string? ExternalUserState);

    /// <param name="RedeemUrl">Sensitive: lets whoever holds it redeem the invitation. Never log or persist it.</param>
    public record EntraInvitation(string UserId, string RedeemUrl, string? Status)
    {
        // Keeps the redeem URL out of anything that logs this record
        public override string ToString() => $"{nameof(EntraInvitation)} {{ UserId = {UserId}, Status = {Status} }}";
    }

    public record EntraAppRoleAssignment(string Id, string PrincipalId, string AppRoleId);
}
