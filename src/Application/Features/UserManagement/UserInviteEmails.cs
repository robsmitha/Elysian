using System.Net;
using Elysian.Application.Interfaces;

namespace Elysian.Application.Features.UserManagement
{
    /// <summary>
    /// The invitation sent in place of Microsoft's own. Every value is HTML-encoded. The redeem URL is a credential
    /// for the invitee: it's only ever placed in this email.
    /// </summary>
    public static class UserInviteEmails
    {
        public static OutgoingEmail Invitation(string to, string displayName, string appName, string redeemUrl, string? invitedBy)
        {
            var inviter = string.IsNullOrWhiteSpace(invitedBy) ? null : invitedBy.Trim();
            var intro = inviter == null
                ? $"You've been invited to sign in to {appName}."
                : $"{inviter} has invited you to sign in to {appName}.";

            var text = $"""
                Hi {displayName},

                {intro}

                Accept your invitation here:
                {redeemUrl}

                You'll sign in with your existing Microsoft account, or get a one-time code by email if you don't have one.
                After accepting, you'll be taken to {appName}. Next time, just sign in there.

                If you weren't expecting this, you can ignore this email.
                """;

            var html = $"""
                <h2 style="font-family: Georgia, serif; font-weight: normal;">You're invited to {Encode(appName)}</h2>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">Hi {Encode(displayName)},</p>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">{Encode(intro)}</p>
                <p style="margin: 24px 0;">
                    <a href="{Encode(redeemUrl)}" style="font-family: Arial, sans-serif; font-size: 14px; font-weight: bold; color: #ffffff; background: #0f62fe; padding: 12px 22px; border-radius: 999px; text-decoration: none; display: inline-block;">Accept invitation</a>
                </p>
                <p style="font-family: Arial, sans-serif; font-size: 14px;">You'll sign in with your existing Microsoft account, or get a one-time code by email if you don't have one.
                After accepting, you'll be taken to {Encode(appName)}. Next time, just sign in there.</p>
                <p style="font-family: Arial, sans-serif; font-size: 12px; color: #6b7280;">If the button doesn't work, paste this link into your browser:<br>
                <span style="word-break: break-all;">{Encode(redeemUrl)}</span></p>
                <p style="font-family: Arial, sans-serif; font-size: 12px; color: #6b7280;">If you weren't expecting this, you can ignore this email.</p>
                """;

            return new OutgoingEmail(to, $"You're invited to {appName}", html, text);
        }

        private static string Encode(string value) => WebUtility.HtmlEncode(value);
    }
}
