using Elysian.Domain.Security;

namespace Elysian.Application.Features.Members
{
    public record MemberModel(int Id, string? Email, string Role, bool HasSignedIn);

    public record InviteModel(int Id, string Role, string? Email, DateTimeOffset ExpiresAt, string Status);

    /// <param name="Code">Shown once; only its hash is kept</param>
    public record CreatedInvite(int Id, string Code, string Role, DateTimeOffset ExpiresAt)
    {
        // Keeps the code out of request logging
        public override string ToString() => $"{nameof(CreatedInvite)} {{ Id = {Id} }}";
    }

    public record RedeemedInvite(string TenantIdentifier, string Role);

    public record MyTenantModel(string Identifier, string? Name, string? Platform, string? Runtime, string Role);

    public static class MemberRoles
    {
        /// <summary>
        /// The roles a membership or invite may carry. Running the platform is never one of them.
        /// </summary>
        public static readonly IReadOnlyList<string> Assignable = [RoleNames.Owner, RoleNames.Editor];

        public static string? Normalize(string? role) =>
            Assignable.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));

        public const string InvalidInviteMessage = "This invite code isn't valid or has expired.";
    }
}
