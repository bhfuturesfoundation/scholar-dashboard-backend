namespace Auth.Models.Enums.Email
{
    /// <summary>How an address ended up on the suppression list.</summary>
    public enum SuppressionSource
    {
        /// <summary>Added by staff through the admin UI.</summary>
        Manual = 0,

        /// <summary>The recipient used an unsubscribe link.</summary>
        Unsubscribe = 1,

        /// <summary>Recorded after a hard bounce.</summary>
        HardBounce = 2,

        /// <summary>The recipient marked a message as spam.</summary>
        SpamComplaint = 3,

        /// <summary>Imported from a previous system's do-not-contact list.</summary>
        Imported = 4
    }
}
