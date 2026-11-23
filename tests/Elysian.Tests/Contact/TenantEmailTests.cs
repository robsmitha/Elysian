using Elysian.Application.Exceptions;
using Elysian.Application.Features.Contact;
using Elysian.Application.Features.Contact.Commands;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Resend;

namespace Elysian.Tests.Contact
{
    public class TenantEmailTests
    {
        private const string HostSender = "Another Site <hello@another-site.example>";

        private readonly TestHost host = new();
        private readonly FakeTimeProvider time = new(DateTimeOffset.Parse("2026-11-22T12:00:00Z"));
        private readonly List<EmailMessage> sent = [];
        private readonly List<OutgoingEmail> outgoing = [];

        // ---- Who an email is from

        [Fact]
        public async Task A_site_with_its_own_code_sends_as_it_always_has_and_is_not_counted()
        {
            await Service(Custom()).SendAsync(new OutgoingEmail("to@example.com", "Hi", "<p>Hi</p>", "Hi"));
            await Service(Custom()).SendAsync(new OutgoingEmail("to@example.com", "Hi", "<p>Hi</p>", "Hi", From: "Bookings <b@another-site.example>"));

            Assert.Equal([HostSender, "Bookings <b@another-site.example>"], sent.Select(m => m.From.ToString()));
            Assert.Empty(await host.Context.TenantEmailCounts.ToListAsync());
        }

        [Fact]
        public async Task A_template_site_sends_as_its_own_name_at_the_platform_domain_never_as_the_host()
        {
            await Service(Cloud(name: "Harbor & Pine")).SendAsync(new OutgoingEmail("to@example.com", "Hi", "<p>Hi</p>", "Hi", From: HostSender));

            Assert.Equal("\"Harbor & Pine\" <harbor@send.example.com>", sent.Single().From.ToString());
        }

        [Theory]
        [InlineData("Evil\" <ceo@bank.example>, \"x", "\"Evil ceobank.example x\"")]
        [InlineData("Line\r\nBcc: someone@example.com", "\"LineBcc someoneexample.com\"")]
        [InlineData("  ", "\"harbor\"")]
        [InlineData(null, "\"harbor\"")]
        public void A_site_name_cannot_change_who_an_email_is_from(string? name, string expected)
        {
            Assert.Equal(expected, ResendEmailService.DisplayName(name, "harbor"));
        }

        [Fact]
        public async Task A_template_site_with_nothing_to_send_from_sends_nothing()
        {
            var service = Service(Cloud(), sendingDomain: "");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SendAsync(new OutgoingEmail("to@example.com", "Hi", "<p>Hi</p>", "Hi")));

            Assert.Empty(sent);
            Assert.Empty(await host.Context.TenantEmailCounts.ToListAsync());
        }

        [Fact]
        public async Task A_template_site_given_its_own_sender_uses_it()
        {
            await Service(Cloud(), tenantSender: "Harbor <hello@harborandpine.example>").SendAsync(new OutgoingEmail("to@example.com", "Hi", "<p>Hi</p>", "Hi"));

            Assert.Equal("Harbor <hello@harborandpine.example>", sent.Single().From.ToString());
        }

        // ---- How much

        [Fact]
        public async Task A_template_site_stops_at_its_daily_limit_and_starts_again_the_next_day()
        {
            var service = Service(Cloud(), dailyLimit: 2);
            var email = new OutgoingEmail("to@example.com", "Hi", "<p>Hi</p>", "Hi");

            await service.SendAsync(email);
            await service.SendAsync(email);
            var refused = await Assert.ThrowsAsync<TooManyRequestsException>(() => service.SendAsync(email));

            Assert.Equal(DateTimeOffset.Parse("2026-11-23T00:00:00Z"), refused.RetryAt);
            Assert.Equal(2, sent.Count);

            time.Advance(TimeSpan.FromHours(12));
            await service.SendAsync(email);

            Assert.Equal(3, sent.Count);
            Assert.Equal([2, 1], (await host.Context.TenantEmailCounts.OrderBy(c => c.Day).ToListAsync()).Select(c => c.Count));
        }

        // ---- The contact email

        [Fact]
        public async Task A_code_is_sent_to_the_address_and_only_its_hash_is_kept()
        {
            var model = await RequestAsync("  owner@example.com ");

            var email = outgoing.Single();
            var code = CodeIn(email);
            var row = await host.Context.TenantContactEmails.SingleAsync();
            Assert.Equal("owner@example.com", email.To);
            Assert.Matches("^[0-9]{6}$", code);
            Assert.Null(model.Email);
            Assert.Equal("owner@example.com", model.PendingEmail);
            Assert.Null(row.Email);
            Assert.DoesNotContain(code, row.CodeHash);
            Assert.Equal(ContactEmails.Hash(TestHost.Tenant.Id!, code), row.CodeHash);
        }

        [Fact]
        public async Task The_right_code_makes_it_the_contact_email()
        {
            await RequestAsync("owner@example.com");

            var model = await ConfirmAsync($" {CodeIn(outgoing.Single())[..3]} {CodeIn(outgoing.Single())[3..]} ");

            Assert.Equal("owner@example.com", model.Email);
            Assert.Equal(time.GetUtcNow(), model.VerifiedAt);
            Assert.Null(model.PendingEmail);
            Assert.Null((await host.Context.TenantContactEmails.SingleAsync()).CodeHash);
        }

        [Fact]
        public async Task A_new_address_changes_nothing_until_its_code_is_confirmed()
        {
            await RequestAsync("owner@example.com");
            await ConfirmAsync(CodeIn(outgoing.Single()));
            time.Advance(TimeSpan.FromMinutes(5));

            var model = await RequestAsync("new@example.com");

            Assert.Equal("owner@example.com", model.Email);
            Assert.Equal("new@example.com", model.PendingEmail);
        }

        [Fact]
        public async Task Wrong_codes_are_refused_and_too_many_need_a_new_code()
        {
            await RequestAsync("owner@example.com");
            var code = CodeIn(outgoing.Single());
            var wrong = code == "000000" ? "000001" : "000000";

            for (var i = 0; i < ConfirmContactEmailCommandHandler.MaxAttempts; i++)
            {
                await Assert.ThrowsAsync<CustomValidationException>(() => ConfirmAsync(wrong));
            }

            // Even the right one, now
            await Assert.ThrowsAsync<CustomValidationException>(() => ConfirmAsync(code));
            Assert.Null((await host.Context.TenantContactEmails.SingleAsync()).Email);
        }

        [Fact]
        public async Task A_code_runs_out()
        {
            await RequestAsync("owner@example.com");
            time.Advance(TimeSpan.FromMinutes(31));

            await Assert.ThrowsAsync<CustomValidationException>(() => ConfirmAsync(CodeIn(outgoing.Single())));
            Assert.Null((await GetAsync()).PendingEmail);
        }

        [Fact]
        public async Task A_code_for_one_site_is_worth_nothing_on_another()
        {
            Assert.NotEqual(ContactEmails.Hash("tenant-1", "123456"), ContactEmails.Hash("tenant-2", "123456"));
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Codes_cannot_be_asked_for_one_after_another()
        {
            await RequestAsync("owner@example.com");

            await Assert.ThrowsAsync<ConflictException>(() => RequestAsync("someone-else@example.com"));
            Assert.Single(outgoing);

            time.Advance(TimeSpan.FromSeconds(61));
            await RequestAsync("owner@example.com");
            Assert.Equal(2, outgoing.Count);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not an address")]
        [InlineData("a@example.com, b@example.com")]
        [InlineData("Someone <a@example.com>")]
        [InlineData("a@example.com\r\nBcc: b@example.com")]
        public async Task Only_one_plain_address_is_accepted(string email)
        {
            await Assert.ThrowsAsync<CustomValidationException>(() => RequestAsync(email));
            Assert.Empty(outgoing);
        }

        // ---- Where a contact form message goes

        [Fact]
        public async Task A_message_goes_to_the_confirmed_address()
        {
            await RequestAsync("owner@example.com");
            await ConfirmAsync(CodeIn(outgoing.Single()));
            outgoing.Clear();

            await SendMessageAsync();

            Assert.Equal("owner@example.com", outgoing.Single().To);
            Assert.Equal("sam@example.com", outgoing.Single().ReplyTo);
        }

        [Fact]
        public async Task A_message_is_never_sent_to_an_address_that_was_not_confirmed()
        {
            await RequestAsync("owner@example.com");
            outgoing.Clear();

            await Assert.ThrowsAsync<InvalidOperationException>(SendMessageAsync);

            Assert.Empty(outgoing);
        }

        [Fact]
        public async Task The_inbox_a_site_was_set_up_with_still_comes_first()
        {
            await RequestAsync("owner@example.com");
            await ConfirmAsync(CodeIn(outgoing.Single()));
            outgoing.Clear();

            await SendMessageAsync(configuredInbox: "studio@example.com");

            Assert.Equal("studio@example.com", outgoing.Single().To);
        }

        // ---- Helpers

        private static string CodeIn(OutgoingEmail email) => email.Subject[..6];

        private Task<ContactEmailModel> GetAsync() =>
            new GetContactEmailQueryHandler(host.Context, time).Handle(new GetContactEmailQuery(), CancellationToken.None);

        private Task<ContactEmailModel> RequestAsync(string email) =>
            new RequestContactEmailCommandHandler(host.Context, Recorder(), time, Options.Create(new PlatformEmailSettings()), host.TenantAccessor)
                .Handle(new RequestContactEmailCommand(email), CancellationToken.None);

        private Task<ContactEmailModel> ConfirmAsync(string code) =>
            new ConfirmContactEmailCommandHandler(host.Context, time, host.TenantAccessor).Handle(new ConfirmContactEmailCommand(code), CancellationToken.None);

        private Task SendMessageAsync() => SendMessageAsync(string.Empty);

        private Task SendMessageAsync(string configuredInbox)
        {
            var human = new Mock<IHumanVerificationService>();
            human.Setup(h => h.VerifyAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            return new SendContactMessageCommandHandler(human.Object, Recorder(), new FixedSettings(new ResendSettings { ToEmailAddress = configuredInbox }),
                    NullLogger<SendContactMessageCommandHandler>.Instance, host.Context)
                .Handle(new SendContactMessageCommand("Sam Rivera", "sam@example.com", null, "Weddings", "Hello", "token", null), CancellationToken.None);
        }

        private IEmailService Recorder()
        {
            var service = new Mock<IEmailService>();
            service.Setup(s => s.SendAsync(It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()))
                .Callback<OutgoingEmail, CancellationToken>((email, _) => outgoing.Add(email))
                .Returns(Task.CompletedTask);
            return service.Object;
        }

        private static ElysianTenantInfo Custom() =>
            new() { Id = TestHost.Tenant.Id, Identifier = "another", Name = "Another Site", Runtime = RuntimeNames.Custom };

        private static ElysianTenantInfo Cloud(string name = "Harbor") =>
            new() { Id = TestHost.Tenant.Id, Identifier = "harbor", Name = name, Runtime = RuntimeNames.Cloud };

        private ResendEmailService Service(ElysianTenantInfo tenant, string sendingDomain = "send.example.com", int dailyLimit = 200, string? tenantSender = null)
        {
            var resend = new Mock<IResend>();
            resend.Setup(r => r.EmailSendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .Callback<EmailMessage, CancellationToken>((message, _) => sent.Add(message))
                .ReturnsAsync(new ResendResponse<Guid>(Guid.NewGuid(), null));

            var accessor = new Mock<IMultiTenantContextAccessor<ElysianTenantInfo>>();
            accessor.SetupGet(a => a.MultiTenantContext).Returns(new Finbuckle.MultiTenant.MultiTenantContext<ElysianTenantInfo> { TenantInfo = tenant });

            var services = new ServiceCollection().AddSingleton(host.Context).BuildServiceProvider();

            return new ResendEmailService(resend.Object,
                new FixedSettings(new ResendSettings { FromEmailAddress = tenantSender ?? HostSender }),
                Options.Create(new ResendSettings { FromEmailAddress = HostSender }),
                Options.Create(new PlatformEmailSettings { SendingDomain = sendingDomain, DailyLimitPerTenant = dailyLimit }),
                services, time, NullLogger<ResendEmailService>.Instance, accessor.Object);
        }

        private sealed class FixedSettings(ResendSettings value) : ITenantSettings<ResendSettings>
        {
            public ResendSettings Value { get; } = value;
        }

        private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
        {
            private DateTimeOffset now = now;
            public override DateTimeOffset GetUtcNow() => now;
            public void Advance(TimeSpan by) => now += by;
        }
    }
}
