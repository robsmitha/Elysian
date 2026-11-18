using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Reads a settings class for the current tenant: the host's configured values, overlaid with the tenant's
    /// <see cref="TenantSetting"/> row when it has one. A tenant with no row sees exactly the host's values,
    /// which is how a host serving one site keeps working unchanged.
    /// </summary>
    public class TenantSettingsReader<T>(
        IOptions<T> defaults,
        ElysianContext context,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        IMemoryCache cache,
        IOptions<TenantSettingsOptions> options) : ITenantSettings<T> where T : class
    {
        private T? value;

        public T Value => value ??= Load();

        private T Load()
        {
            var tenantId = multiTenantContextAccessor.MultiTenantContext?.TenantInfo?.Id;
            if (string.IsNullOrEmpty(tenantId))
            {
                return defaults.Value;
            }

            var section = TenantSettingsSection.NameOf(typeof(T));
            var key = TenantSettingsSection.CacheKey(tenantId, section);
            if (cache.TryGetValue(key, out T? cached) && cached != null)
            {
                return cached;
            }

            var json = context.TenantSettings
                .AsNoTracking()
                .Where(s => s.Section == section)
                .Select(s => s.Json)
                .FirstOrDefault();

            var merged = string.IsNullOrWhiteSpace(json) ? defaults.Value : TenantSettingsSection.Overlay(defaults.Value, json, section);
            cache.Set(key, merged, TimeSpan.FromSeconds(Math.Max(1, options.Value.CacheSeconds)));

            return merged;
        }
    }

    /// <summary>
    /// The host's configured values with no per-tenant overlay. Registered as the fallback so a feature still
    /// works in a host that hasn't called AddElysianFeatures.
    /// </summary>
    public class HostTenantSettings<T>(IOptions<T> defaults) : ITenantSettings<T> where T : class
    {
        public T Value => defaults.Value;
    }

    /// <summary>
    /// Fixed settings for tests and tools that run outside a tenant
    /// </summary>
    public class FixedTenantSettings<T>(T value) : ITenantSettings<T> where T : class
    {
        public T Value => value;
    }

    public static class TenantSettingsSection
    {
        // Replace, or a list the settings class fills in its initializer would be doubled by the copy
        private static readonly JsonSerializerSettings copySettings = new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        private static readonly JsonSerializerSettings overlaySettings = new()
        {
            // Lists are replaced, not appended to: a tenant's start windows aren't added to the host's
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            ContractResolver = new TenantOverlayContractResolver(),
        };

        /// <summary>
        /// The settings class's SectionName constant, or its type name when it has none
        /// </summary>
        public static string NameOf(Type settingsType) =>
            settingsType.GetField("SectionName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                ?.GetRawConstantValue() as string ?? settingsType.Name;

        public static string CacheKey(string tenantId, string section) => $"tenant-settings:{tenantId}:{section}";

        /// <summary>
        /// A copy of <paramref name="defaults"/> with the tenant's JSON applied, leaving <paramref name="defaults"/> untouched
        /// </summary>
        public static T Overlay<T>(T defaults, string tenantJson, string section) where T : class
        {
            try
            {
                var copy = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(defaults), copySettings)!;
                JsonConvert.PopulateObject(tenantJson, copy, overlaySettings);
                return copy;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"The tenant's stored {section} settings can't be read: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Skips <see cref="PlatformOnlyAttribute"/> properties when applying a tenant's values
        /// </summary>
        private sealed class TenantOverlayContractResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                if (member.GetCustomAttribute<PlatformOnlyAttribute>() != null)
                {
                    property.ShouldDeserialize = _ => false;
                    property.Writable = false;
                }

                return property;
            }
        }
    }
}
