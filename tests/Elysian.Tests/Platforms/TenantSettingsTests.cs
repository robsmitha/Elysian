using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Elysian.Tests.Platforms
{
    public class TenantSettingsTests
    {
        [Fact]
        public void A_tenant_with_no_stored_settings_sees_the_hosts()
        {
            using var host = new TestHost();
            var hostSettings = new BookingSettings { TimeZone = "America/Chicago", MinimumNoticeDays = 3 };

            var settings = Reader(host, hostSettings).Value;

            Assert.Equal("America/Chicago", settings.TimeZone);
            Assert.Equal(3, settings.MinimumNoticeDays);
        }

        [Fact]
        public async Task Stored_settings_override_only_what_they_set()
        {
            using var host = new TestHost();
            await StoreAsync(host, BookingSettings.SectionName, """{ "TimeZone": "America/Denver", "ContactEmailAddress": "owner@example.com" }""");
            var hostSettings = new BookingSettings { MinimumNoticeDays = 3 };

            var settings = Reader(host, hostSettings).Value;

            Assert.Equal("America/Denver", settings.TimeZone);
            Assert.Equal("owner@example.com", settings.ContactEmailAddress);
            Assert.Equal(3, settings.MinimumNoticeDays);
            Assert.Equal(2, settings.StartWindows.Count);
        }

        [Fact]
        public async Task A_stored_list_replaces_the_hosts_list()
        {
            using var host = new TestHost();
            await StoreAsync(host, BookingSettings.SectionName, """{ "StartWindows": [ { "Start": "09:00", "End": "11:00" } ] }""");

            var settings = Reader(host, new BookingSettings()).Value;

            var window = Assert.Single(settings.StartWindows);
            Assert.Equal("09:00", window.Start);
        }

        [Fact]
        public async Task The_hosts_settings_are_never_modified()
        {
            using var host = new TestHost();
            await StoreAsync(host, BookingSettings.SectionName, """{ "TimeZone": "America/Denver" }""");
            var hostSettings = new BookingSettings();

            _ = Reader(host, hostSettings).Value;

            Assert.Equal("America/New_York", hostSettings.TimeZone);
        }

        [Fact]
        public async Task A_tenant_cant_override_a_platform_only_setting()
        {
            using var host = new TestHost();
            await StoreAsync(host, ResendSettings.SectionName, """{ "ApiKey": "tenant-key", "ToEmailAddress": "owner@example.com" }""");

            var settings = Reader(host, new ResendSettings { ApiKey = "host-key" }).Value;

            Assert.Equal("host-key", settings.ApiKey);
            Assert.Equal("owner@example.com", settings.ToEmailAddress);
        }

        [Fact]
        public async Task Unreadable_stored_settings_fail_with_the_section_named()
        {
            using var host = new TestHost();
            await StoreAsync(host, BookingSettings.SectionName, "{ not json");

            var exception = Assert.Throws<InvalidOperationException>(() => Reader(host, new BookingSettings()).Value);

            Assert.Contains("Booking", exception.Message);
        }

        [Fact]
        public void The_section_comes_from_the_settings_class()
        {
            Assert.Equal("Booking", TenantSettingsSection.NameOf(typeof(BookingSettings)));
            Assert.Equal("Resend", TenantSettingsSection.NameOf(typeof(ResendSettings)));
            Assert.Equal(nameof(NoSectionName), TenantSettingsSection.NameOf(typeof(NoSectionName)));
        }

        [Fact]
        public async Task Configuration_secrets_are_read_for_the_current_tenant()
        {
            using var host = new TestHost();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TenantSecrets:test:turnstile-secret-key"] = "tenant-secret",
                ["TenantSecrets:other:turnstile-secret-key"] = "someone-elses",
            }).Build();
            var store = new ConfigurationTenantSecretStore(configuration, host.TenantAccessor);

            Assert.Equal("tenant-secret", await store.GetAsync("turnstile-secret-key"));
            Assert.Null(await store.GetAsync("missing"));
        }

        [Fact]
        public void Key_vault_secret_names_carry_the_tenant_and_only_allowed_characters()
        {
            Assert.Equal("tenant-aspensmitha-turnstile-secret-key", KeyVaultTenantSecretStore.SecretName("aspensmitha", "turnstile-secret-key"));
            Assert.Equal("tenant-a-b-c-d", KeyVaultTenantSecretStore.SecretName("a.b", "c_d"));
        }

        private static TenantSettingsReader<T> Reader<T>(TestHost host, T hostSettings) where T : class => new(
            Options.Create(hostSettings),
            host.Context,
            host.TenantAccessor,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new TenantSettingsOptions()));

        private static async Task StoreAsync(TestHost host, string section, string json)
        {
            host.Context.TenantSettings.Add(new TenantSetting { Section = section, Json = json });
            await host.Context.SaveChangesAsync();
        }

        private class NoSectionName
        {
        }
    }
}
