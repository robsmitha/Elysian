using Elysian.Domain.Platforms;
using System.Collections.Concurrent;
using System.Reflection;

namespace Elysian.Application.Behaviors
{
    /// <summary>
    /// Works out which module a request belongs to. A request takes the module of its feature folder
    /// (Elysian.Application.Features.{Feature}), so a new request in a gated feature is gated without anyone
    /// remembering to mark it; <see cref="RequiresModuleAttribute"/> overrides that for a single request.
    /// </summary>
    public static class RequestModules
    {
        private const string FeaturesNamespace = "Elysian.Application.Features.";

        /// <summary>
        /// Feature folder to module. A null module means the feature is core and open to every tenant.
        /// Every feature folder must be listed; a test fails when one isn't.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string?> FeatureModules = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Assistant"] = ModuleNames.Assistant,
            ["Booking"] = ModuleNames.Booking,
            ["CloudStorage"] = null,
            ["Code"] = ModuleNames.Code,
            ["Congress"] = ModuleNames.Congress,
            ["Contact"] = null,
            ["ContentManagement"] = null,
            ["Financial"] = ModuleNames.Finance,
            ["GoogleCalendar"] = ModuleNames.Booking,
            ["Instagram"] = ModuleNames.Instagram,
            ["Members"] = null,
            ["Merchants"] = null,
            ["Photos"] = ModuleNames.Photos,
            ["Placements"] = ModuleNames.Photos,
            ["SiteContent"] = null,
            ["Sites"] = null,
            ["Platform"] = null,
            ["Stories"] = ModuleNames.Photos,
            ["UserManagement"] = null,
        };

        private static readonly ConcurrentDictionary<Type, string?> cache = new();

        public static string? For(Type requestType) => cache.GetOrAdd(requestType, Resolve);

        /// <summary>
        /// The feature folder a type lives in, or null when it's outside Features
        /// </summary>
        public static string? FeatureOf(Type type)
        {
            var ns = type.Namespace;
            if (ns == null || !ns.StartsWith(FeaturesNamespace, StringComparison.Ordinal))
            {
                return null;
            }

            var rest = ns[FeaturesNamespace.Length..];
            var dot = rest.IndexOf('.');
            return dot < 0 ? rest : rest[..dot];
        }

        private static string? Resolve(Type requestType)
        {
            var attribute = requestType.GetCustomAttribute<RequiresModuleAttribute>();
            if (attribute != null)
            {
                return attribute.Module;
            }

            var feature = FeatureOf(requestType);
            return feature != null && FeatureModules.TryGetValue(feature, out var module) ? module : null;
        }
    }
}
