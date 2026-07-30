using Auth.Models.Entities;
using Auth.Models.Entities.FLS;

namespace Auth.Services.Services.Email
{
    /// <summary>
    /// Single source of truth for the <c>{{placeholders}}</c> available in FLS emails.
    ///
    /// Both the notification service and the campaign service build their variables here so
    /// the set the UI advertises, the set the preview resolves, and the set the real send
    /// substitutes can never drift apart — that drift is exactly how "Dear {{firstName}}"
    /// reaches a real inbox.
    /// </summary>
    public static class TemplateVariables
    {
        /// <summary>Placeholder names offered in the compose UI, with a short description.</summary>
        public static readonly IReadOnlyList<(string Name, string Description)> Supported = new List<(string, string)>
        {
            ("firstName",    "Recipient's first name"),
            ("lastName",     "Recipient's last name"),
            ("fullName",     "Recipient's full name"),
            ("email",        "Recipient's email address"),
            ("organization", "Speaker's organisation (blank for staff)"),
            ("speakerType",  "Plenary / Track / Panel / Workshop / Other"),
            ("year",         "Current calendar year"),
            ("deadline",     "Deadline supplied when composing"),
            ("portalUrl",    "Link to the FLS speaker portal"),
        };

        public const string PortalUrl = "https://scholar-dashboard-frontend.vercel.app/fls/login";

        /// <summary>Variables for a speaker recipient.</summary>
        public static Dictionary<string, string?> ForSpeaker(
            SpeakerProfile profile,
            User user,
            string? deadline = null,
            DateTime? now = null)
        {
            var vars = ForUser(user, deadline, now);
            vars["organization"] = profile.Organization ?? string.Empty;
            vars["speakerType"] = profile.SpeakerType.ToString();
            return vars;
        }

        /// <summary>Variables for any platform user (FLS staff, admins, program managers).</summary>
        public static Dictionary<string, string?> ForUser(
            User user,
            string? deadline = null,
            DateTime? now = null)
        {
            var first = user.FirstName ?? string.Empty;
            var last = user.LastName ?? string.Empty;

            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["firstName"] = first,
                ["lastName"] = last,
                ["fullName"] = $"{first} {last}".Trim(),
                ["email"] = user.Email ?? string.Empty,
                ["organization"] = string.Empty,
                ["speakerType"] = string.Empty,
                ["year"] = (now ?? DateTime.UtcNow).Year.ToString(),
                ["deadline"] = deadline ?? string.Empty,
                ["portalUrl"] = PortalUrl,
            };
        }
    }
}
