namespace Auth.Models.Enums.FLS
{
    /// <summary>Who a campaign is addressed to.</summary>
    public enum CampaignAudience
    {
        /// <summary>Every speaker who has not been deregistered.</summary>
        ActiveSpeakers = 1,

        /// <summary>Active speakers missing at least one required upload.</summary>
        SpeakersWithIncompleteUploads = 2,

        /// <summary>Active speakers of one <see cref="SpeakerType"/>.</summary>
        SpeakersByType = 3,

        /// <summary>Deregistered speakers — for reinstatement or wrap-up messages.</summary>
        DeregisteredSpeakers = 4,

        /// <summary>FLS staff: Admin, FLSAdmin and PartnerMember accounts.</summary>
        FlsStaff = 5,

        /// <summary>An explicit list of speaker profile ids chosen in the UI.</summary>
        SelectedSpeakers = 6
    }

    public enum CampaignStatus
    {
        Draft = 0,
        Sending = 1,

        /// <summary>Every recipient was delivered successfully.</summary>
        Completed = 2,

        /// <summary>Some recipients failed — see the per-recipient rows for detail.</summary>
        PartiallyFailed = 3,

        /// <summary>No recipient was delivered.</summary>
        Failed = 4
    }

    public enum EmailDeliveryStatus
    {
        Pending = 0,
        Sent = 1,
        Failed = 2,

        /// <summary>Excluded before sending — e.g. the account has no email address.</summary>
        Skipped = 3
    }
}
