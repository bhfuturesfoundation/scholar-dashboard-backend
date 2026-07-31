namespace Auth.Models.Enums.Notifications
{
    /// <summary>
    /// How a notification reaches someone. Each is a separate opt-out because they have
    /// genuinely different costs to the recipient: an in-app entry waits quietly, an email
    /// lands in a mailbox they may share with their work, and a push wakes a phone.
    /// </summary>
    public enum NotificationChannel
    {
        /// <summary>The bell menu. Always on — this is the record of what happened.</summary>
        InApp = 0,

        Email = 1,

        /// <summary>Web push to an installed PWA or a subscribed browser.</summary>
        Push = 2
    }
}
