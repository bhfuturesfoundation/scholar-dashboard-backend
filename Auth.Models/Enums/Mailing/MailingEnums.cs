namespace Auth.Models.Enums.Mailing
{
    /// <summary>Whether a firm may be contacted, and why not if not.</summary>
    public enum FirmStatus
    {
        /// <summary>Contactable.</summary>
        Active = 0,

        /// <summary>Asked to be removed. Excluded from every audience, permanently.</summary>
        Unsubscribed = 1,

        /// <summary>Mail to this address hard-bounced. Excluded until the address is corrected.</summary>
        Bounced = 2,

        /// <summary>Manually suppressed by the team (competitor, wrong contact, on hold).</summary>
        DoNotContact = 3,

        /// <summary>Imported but missing a usable email address — needs a human before it can be mailed.</summary>
        Incomplete = 4
    }

    /// <summary>Where a firm's contact person name came from.</summary>
    public enum ContactNameSource
    {
        /// <summary>No contact name.</summary>
        None = 0,

        /// <summary>Typed or corrected by a person. Never overwritten by automatic detection.</summary>
        Manual = 1,

        /// <summary>Derived from the local part of the email address, e.g. amir.hodzic@ → Amir Hodzic.</summary>
        DerivedFromEmail = 2,

        /// <summary>Derived from the firm name itself, e.g. "Advokat Amir Hodzic" → Amir Hodzic.</summary>
        DerivedFromFirmName = 3,

        /// <summary>Came in as a column in an imported spreadsheet.</summary>
        Imported = 4
    }

    /// <summary>
    /// How much to trust an automatically derived name. Drives whether the person-variant
    /// template is used: anything below <see cref="Medium"/> falls back to the firm variant,
    /// because "Dear Info" is worse than "Dear Acme d.o.o.".
    /// </summary>
    public enum NameConfidence
    {
        None = 0,

        /// <summary>A single token that might be a first name, or an initial-plus-surname.</summary>
        Low = 1,

        /// <summary>A plausible single given name from a non-generic mailbox.</summary>
        Medium = 2,

        /// <summary>Two separated tokens, e.g. first.last — near-certainly a person.</summary>
        High = 3
    }

    /// <summary>Which wording a template row supplies.</summary>
    public enum TemplateVariant
    {
        /// <summary>Addressed to the organisation: "Dear Acme d.o.o.".</summary>
        Firm = 0,

        /// <summary>Addressed to a named human: "Dear Amir".</summary>
        Person = 1
    }

    /// <summary>How a campaign or schedule selects its firms.</summary>
    public enum FirmAudience
    {
        /// <summary>Every contactable firm.</summary>
        AllActive = 1,

        /// <summary>Contactable firms of the selected firm types.</summary>
        ByFirmType = 2,

        /// <summary>Contactable firms whose type belongs to the selected groups.</summary>
        ByFirmGroup = 3,

        /// <summary>An explicit list of firm ids chosen in the UI.</summary>
        SelectedFirms = 4,

        /// <summary>Contactable firms that have a contact person name — the warmer list.</summary>
        WithContactName = 5,

        /// <summary>Contactable firms with no contact name yet.</summary>
        WithoutContactName = 6,

        /// <summary>Contactable firms never contacted before.</summary>
        NeverContacted = 7
    }

    public enum MailingCampaignStatus
    {
        Draft = 0,
        Scheduled = 1,
        Sending = 2,

        /// <summary>Every recipient delivered.</summary>
        Completed = 3,

        /// <summary>Some delivered, some failed — see the recipient rows.</summary>
        PartiallyFailed = 4,

        /// <summary>No recipient delivered.</summary>
        Failed = 5,

        Cancelled = 6
    }

    /// <summary>How often a schedule fires.</summary>
    public enum ScheduleCadence
    {
        /// <summary>Fires once at <c>NextRunAt</c>, then disables itself.</summary>
        Once = 0,

        /// <summary>Fires every <c>IntervalMinutes</c>.</summary>
        FixedInterval = 1,

        Daily = 2,
        Weekly = 3,
        Monthly = 4
    }

    public enum ImportFormat
    {
        Csv = 0,
        Excel = 1
    }

    /// <summary>Outcome of one row in an import.</summary>
    public enum ImportRowOutcome
    {
        Created = 0,
        Updated = 1,

        /// <summary>Identical to an existing row — nothing to do.</summary>
        Skipped = 2,

        /// <summary>Rejected: no name, unparseable email, etc.</summary>
        Failed = 3
    }
}
