using Microsoft.Extensions.Configuration;

namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// App-only Microsoft Graph credentials and the enterprise application users are assigned to.
    /// Read from flat keys (GRAPH_TENANT_ID, ...) rather than a section, since they're shared with other tooling.
    /// The secret is never logged or returned.
    /// </summary>
    public class EntraSettings
    {
        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Object id of the enterprise application's service principal (not the app registration's client id)
        /// </summary>
        public string EnterpriseAppServicePrincipalId { get; set; } = string.Empty;

        /// <summary>
        /// App role assigned to invited users. All zeros is Default Access.
        /// </summary>
        public string AppRoleId { get; set; } = Guid.Empty.ToString();

        /// <summary>
        /// Where invitees land after redeeming
        /// </summary>
        public string AppUrl { get; set; } = string.Empty;

        public static void Bind(EntraSettings settings, IConfiguration configuration)
        {
            settings.TenantId = configuration["GRAPH_TENANT_ID"] ?? string.Empty;
            settings.ClientId = configuration["GRAPH_CLIENT_ID"] ?? string.Empty;
            settings.ClientSecret = configuration["GRAPH_CLIENT_SECRET"] ?? string.Empty;
            settings.EnterpriseAppServicePrincipalId = configuration["ENTERPRISE_APP_SP_ID"] ?? string.Empty;
            settings.AppRoleId = string.IsNullOrWhiteSpace(configuration["APP_ROLE_ID"]) ? Guid.Empty.ToString() : configuration["APP_ROLE_ID"]!;
            settings.AppUrl = configuration["APP_URL"] ?? string.Empty;
        }

        /// <summary>
        /// Names of required settings that are empty, for a startup-time error that doesn't echo any values
        /// </summary>
        public IEnumerable<string> MissingSettings()
        {
            if (string.IsNullOrWhiteSpace(TenantId)) yield return "GRAPH_TENANT_ID";
            if (string.IsNullOrWhiteSpace(ClientId)) yield return "GRAPH_CLIENT_ID";
            if (string.IsNullOrWhiteSpace(ClientSecret)) yield return "GRAPH_CLIENT_SECRET";
            if (string.IsNullOrWhiteSpace(EnterpriseAppServicePrincipalId)) yield return "ENTERPRISE_APP_SP_ID";
            if (string.IsNullOrWhiteSpace(AppUrl)) yield return "APP_URL";
        }
    }
}
