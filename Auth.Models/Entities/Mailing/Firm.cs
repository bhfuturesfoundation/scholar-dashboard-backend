using Auth.Models.Enums.Mailing;

namespace Auth.Models.Entities.Mailing
{
    /// <summary>
    /// One organisation in the partnerships outreach directory.
    ///
    /// Note this is NOT an application user — firms never log in. It is a contact record,
    /// which is why the email address lives here rather than on an Identity account.
    /// </summary>
    public class Firm
    {
        public int Id { get; set; }

        /// <summary>Trading name, as the team refers to it.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Registered name if it differs, e.g. "Acme d.o.o. Sarajevo".</summary>
        public string? LegalName { get; set; }

        public int? FirmTypeId { get; set; }
        public FirmType? FirmType { get; set; }

        /// <summary>Primary contact address. Stored as entered for display.</summary>
        public string? Email { get; set; }

        /// <summary>
        /// Lowercased, trimmed <see cref="Email"/>. Persisted rather than computed so it can
        /// carry a unique index — the database, not application code, is what actually
        /// guarantees an import can't create a second row for the same address.
        /// </summary>
        public string? NormalizedEmail { get; set; }

        public string? Website { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }

        /// <summary>
        /// Named human to address, when known. Populated manually, by import, or by
        /// <c>IContactNameExtractor</c> via the bulk detect action.
        /// </summary>
        public string? ContactPersonName { get; set; }

        /// <summary>Job title, purely informational.</summary>
        public string? ContactPersonRole { get; set; }

        public ContactNameSource ContactNameSource { get; set; } = ContactNameSource.None;

        /// <summary>
        /// Trust level of <see cref="ContactPersonName"/>. Only Medium or better is used to
        /// select the person-variant template.
        /// </summary>
        public NameConfidence ContactNameConfidence { get; set; } = NameConfidence.None;

        public FirmStatus Status { get; set; } = FirmStatus.Active;

        public string? Notes { get; set; }

        /// <summary>Import batch this firm arrived in, when it wasn't created by hand.</summary>
        public int? ImportBatchId { get; set; }
        public FirmImportBatch? ImportBatch { get; set; }

        /// <summary>Last successful delivery to this firm — drives "never contacted" audiences.</summary>
        public DateTime? LastContactedAt { get; set; }

        /// <summary>Count of successful deliveries, for light frequency awareness.</summary>
        public int ContactCount { get; set; }

        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<MailingCampaignRecipient> CampaignRecipients { get; set; } = new List<MailingCampaignRecipient>();

        /// <summary>
        /// Whether this firm can be included in a send: contactable status and a usable address.
        /// </summary>
        public bool IsContactable =>
            Status == FirmStatus.Active && !string.IsNullOrWhiteSpace(NormalizedEmail);

        /// <summary>Whether the person-variant template should be used for this firm.</summary>
        public bool HasUsableContactName =>
            !string.IsNullOrWhiteSpace(ContactPersonName) &&
            ContactNameConfidence >= NameConfidence.Medium;
    }
}
