using System.Text.RegularExpressions;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Contact
{
    /// <summary>What a contact form message says, apart from who it is for</summary>
    public record ContactMessageText(string Name, string Email, string Subject, string Message);

    /// <param name="Source">What decided: <see cref="ContactSpamSources"/></param>
    /// <param name="Reason">A few words for the owner on why, when it was flagged</param>
    public record ContactSpamVerdict(bool IsSpam, string Source, string? Reason = null)
    {
        public static readonly ContactSpamVerdict NotChecked = new(false, ContactSpamSources.None);
    }

    public static class ContactSpamSources
    {
        public const string None = "none";
        public const string Rules = "rules";
        public const string Model = "model";
    }

    /// <summary>
    /// The human check and the honeypot stop scripts. They do nothing about a person, or a person's browser,
    /// sending a sales pitch through the form: an SEO offer, a redesign, a loan. These rules read the message
    /// for that. They look for someone selling to the business ("your website", "your business"), so a real
    /// customer asking a web studio about their own site is not caught by the same words.
    ///
    /// A match never drops a message. It is delivered with its subject marked, so a wrong guess costs the
    /// owner a glance and never an inquiry.
    /// </summary>
    public static class ContactSpamRules
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
        private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(100);

        private static Regex Rule(string pattern) => new(pattern, Options, Limit);

        /// <summary>Said to the business about what it owns</summary>
        private static readonly Regex Yours = Rule(@"\byour\s+(web\s?site|site|business|company|brand|online\s+presence|page|store|listing|google\s+(business\s+)?(profile|listing))\b");

        /// <summary>Things sold to small businesses by cold message. Alone they prove nothing; with <see cref="Yours"/> they do.</summary>
        private static readonly (Regex Pattern, string Reason)[] Services =
        [
            (Rule(@"\bseo\b|search\s+engine\s+optimi[sz]ation|\brank(s|ing|ings|ed)?\b|\bkeywords?\b"), "search ranking pitch"),
            (Rule(@"\b(web|website|app|logo|graphic)\s+(re)?design(ing|er|ers)?\b|\bredesign\b|\bweb\s+develop(ment|er|ers)\b|\bapp\s+develop(ment|er|ers)\b"), "design or development pitch"),
            (Rule(@"\b(digital|social\s+media|email|content)\s+marketing\b|\blead\s+generation\b|\b(more|new|qualified)\s+(leads|customers|clients)\b|\bgoogle\s+ads\b|\bppc\b"), "marketing pitch"),
            (Rule(@"\b(increase|boost|grow|improve|double)\s+your\s+(traffic|sales|revenue|visibility|rankings?|leads|conversions?)\b"), "growth pitch"),
            (Rule(@"\b(explainer|promo(tional)?)\s+videos?\b|\bvirtual\s+assistants?\b|\boutsourc(e|ing)\b|\bwhite[\s-]label\b"), "outsourcing pitch"),
        ];

        /// <summary>Enough on their own, whoever they are said to</summary>
        private static readonly (Regex Pattern, string Reason)[] Certain =
        [
            (Rule(@"\b(our|my|a)\s+(review|audit|analysis)\s+of\s+your\s+(web\s?site|site)\b|\b(reviewed|audited|analy[sz]ed|was\s+(looking|going)\s+(at|through)|came\s+across|while\s+browsing)\s+your\s+(web\s?site|site)\b"), "unrequested website review"),
            (Rule(@"\bbacklinks?\b|\blink[\s-]building\b|\bguest\s+post(s|ing)?\b|\bsponsored\s+(post|article)s?\b|\bdomain\s+authority\b|\bdofollow\b"), "link selling"),
            (Rule(@"\b(first|1st|top)\s+(page|spot|position)s?\s+(of|on|in)\s+google\b|\bnot\s+ranking\s+on\s+google\b|\branking\s+on\s+google\b"), "search ranking pitch"),
            (Rule(@"\breply\s+with\s+your\s+(contact\s+|phone\s+|mobile\s+|whatsapp\s+)?number\b|\bshare\s+your\s+(contact\s+|phone\s+|whatsapp\s+)number\b|\bsend\s+you\s+(a|our|the)\s+(quote|proposal|price\s+list|portfolio|samples?)\b"), "cold sales follow-up"),
            (Rule(@"\bbusiness\s+(loan|funding|line\s+of\s+credit)s?\b|\bmerchant\s+cash\b|\bworking\s+capital\b|\b(crypto|bitcoin|forex)\b|\binvestment\s+opportunity\b"), "finance pitch"),
            (Rule(@"\bfind(ing)?\s+your\s+competitors\s+first\b|\bcompetitors\s+(are\s+)?outrank(ing)?\b|\blost\s+customers\b|\blosing\s+(customers|clients|business)\s+to\s+(your\s+)?competitors\b"), "search ranking pitch"),
        ];

        /// <summary>
        /// Whether the message reads like a sales pitch to the business. Never throws: a pattern that runs too
        /// long on an odd message counts as no match.
        /// </summary>
        public static ContactSpamVerdict Check(ContactMessageText text)
        {
            // Everything the sender wrote, on one line. The subject of a site's form is usually one of its own
            // choices, so it rarely says anything; it is read all the same.
            var words = Regex.Replace($"{text.Subject} {text.Message}", @"\s+", " ");

            try
            {
                foreach (var (pattern, reason) in Certain)
                {
                    if (pattern.IsMatch(words)) return new ContactSpamVerdict(true, ContactSpamSources.Rules, reason);
                }

                if (Yours.IsMatch(words))
                {
                    foreach (var (pattern, reason) in Services)
                    {
                        if (pattern.IsMatch(words)) return new ContactSpamVerdict(true, ContactSpamSources.Rules, reason);
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Read as no match
            }

            return new ContactSpamVerdict(false, ContactSpamSources.Rules);
        }
    }

    /// <summary>
    /// A second opinion on a message the rules let through. Null means there is no answer: it is switched off,
    /// it took too long, or there is no allowance left. Never throws.
    /// </summary>
    public interface IContactSpamClassifier
    {
        Task<ContactSpamVerdict?> ClassifyAsync(ContactMessageText text, CancellationToken cancellationToken = default);
    }

    public interface IContactSpamFilter
    {
        Task<ContactSpamVerdict> CheckAsync(ContactMessageText text, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The rules first: they cost nothing and are instant. A message they flag is flagged. A message they pass
    /// is put to the classifier, whose answer stands when it has one; when it has none, the rules' answer does.
    /// </summary>
    public class ContactSpamFilter(IOptions<ContactSpamSettings> options, ILogger<ContactSpamFilter> logger,
        IContactSpamClassifier? classifier = null) : IContactSpamFilter
    {
        public async Task<ContactSpamVerdict> CheckAsync(ContactMessageText text, CancellationToken cancellationToken = default)
        {
            if (!options.Value.Enabled)
            {
                return ContactSpamVerdict.NotChecked;
            }

            var byRules = ContactSpamRules.Check(text);
            if (byRules.IsSpam || classifier == null || !options.Value.UseModel)
            {
                return byRules;
            }

            ContactSpamVerdict? byModel = null;
            try
            {
                byModel = await classifier.ClassifyAsync(text, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A classifier is meant never to throw. One that does must not be why a message is lost.
                logger.LogWarning(ex, "The contact spam classifier failed; the rules decide");
            }

            return byModel ?? byRules;
        }
    }
}
