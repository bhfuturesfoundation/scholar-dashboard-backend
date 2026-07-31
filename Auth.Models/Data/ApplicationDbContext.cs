using Auth.Models.Entities;
using Auth.Models.Entities.Email;
using Auth.Models.Entities.Engagement;
using Auth.Models.Entities.FLS;
using Auth.Models.Entities.Mailing;
using Auth.Models.Entities.News;
using Auth.Models.Entities.Notifications;
using Auth.Models.Entities.Suggestions;
using Auth.Models.Entities.Operations;
using Auth.Models.Entities.Scholars;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Auth.Models.Data
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Existing
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<Answer> Answers { get; set; }
        public DbSet<Skill> Skills { get; set; }
        public DbSet<JournalSubmission> JournalSubmissions { get; set; }

        /// <summary>
        /// Profile pictures, one row per person. Its own table so the byte[] never rides
        /// along on the many queries that load <c>User</c>. See <see cref="UserAvatar"/>.
        /// </summary>
        public DbSet<UserAvatar> UserAvatars { get; set; }

        // Gamification
        public DbSet<GameScore> GameScores { get; set; }

        // Audit
        public DbSet<AuditEvent> AuditEvents { get; set; }

        // FLS Speaker Management
        public DbSet<SpeakerProfile> SpeakerProfiles { get; set; }
        public DbSet<SpeakerUpload> SpeakerUploads { get; set; }
        public DbSet<SpeakerComment> SpeakerComments { get; set; }
        public DbSet<MeetingTimeSlot> MeetingTimeSlots { get; set; }
        public DbSet<SpeakerTask> SpeakerTasks { get; set; }
        public DbSet<FLSDocument> FLSDocuments { get; set; }
        public DbSet<SpeakerNotification> SpeakerNotifications { get; set; }
        public DbSet<EmailCampaign> EmailCampaigns { get; set; }
        public DbSet<EmailCampaignRecipient> EmailCampaignRecipients { get; set; }

        /// <summary>Addresses that must never be mailed. See IEmailSuppressionService.</summary>
        public DbSet<EmailSuppression> EmailSuppressions { get; set; }

        /// <summary>Audit trail of database backups. See IBackupService.</summary>
        public DbSet<BackupRecord> BackupRecords { get; set; }

        // Engagement
        public DbSet<Achievement> Achievements { get; set; }
        public DbSet<Kudos> Kudos { get; set; }

        // Notifications
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<NotificationPreference> NotificationPreferences { get; set; }
        public DbSet<Auth.Models.Entities.Notifications.PushSubscription> PushSubscriptions { get; set; }
        public DbSet<Announcement> Announcements { get; set; }

        // Suggestion board
        public DbSet<Suggestion> Suggestions { get; set; }
        public DbSet<SuggestionVote> SuggestionVotes { get; set; }

        /// <summary>
        /// News mirrored from the foundation's public website. See <see cref="NewsPost"/> and
        /// INewsScraperService.
        /// </summary>
        public DbSet<NewsPost> NewsPosts { get; set; }

        // Scholar lifecycle
        public DbSet<ScholarGeneration> ScholarGenerations { get; set; }
        public DbSet<PromotionBatch> PromotionBatches { get; set; }
        public DbSet<PromotionBatchEntry> PromotionBatchEntries { get; set; }

        // Partnerships mailing — firm outreach. Independent of FLS: these records are
        // organisations the foundation contacts, not application users.
        public DbSet<FirmGroup> FirmGroups { get; set; }
        public DbSet<FirmType> FirmTypes { get; set; }
        public DbSet<Firm> Firms { get; set; }
        public DbSet<FirmImportBatch> FirmImportBatches { get; set; }
        public DbSet<MailingTemplate> MailingTemplates { get; set; }
        public DbSet<MailingCampaign> MailingCampaigns { get; set; }
        public DbSet<MailingCampaignRecipient> MailingCampaignRecipients { get; set; }
        public DbSet<MailingSchedule> MailingSchedules { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Ignore<IdentityUserLogin<string>>();
            builder.Ignore<IdentityRoleClaim<string>>();
            builder.Ignore<IdentityRole<string>>();

            // GameScore → User
            builder.Entity<GameScore>()
                .HasOne(gs => gs.User)
                .WithMany()
                .HasForeignKey(gs => gs.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<GameScore>()
                .HasIndex(gs => new { gs.UserId, gs.GameId });

            builder.Entity<GameScore>()
                .HasIndex(gs => new { gs.GameId, gs.Score });

            // AuditEvent → User (nullable)
            builder.Entity<AuditEvent>()
                .HasOne(ae => ae.User)
                .WithMany()
                .HasForeignKey(ae => ae.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<AuditEvent>()
                .HasIndex(ae => ae.Timestamp);

            builder.Entity<AuditEvent>()
                .HasIndex(ae => ae.EventType);

            // Existing relationships
            builder.Entity<RefreshToken>()
                .HasOne(rt => rt.User)
                .WithMany()
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<User>()
                .HasOne(u => u.Mentor)
                .WithMany(u => u.Scholars)
                .HasForeignKey(u => u.MentorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<User>()
                .HasIndex(u => u.MentorId)
                .IsUnique(false);

            // FLS: SpeakerProfile → User (one-to-one)
            builder.Entity<SpeakerProfile>()
                .HasOne(sp => sp.User)
                .WithMany()
                .HasForeignKey(sp => sp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SpeakerProfile>()
                .HasIndex(sp => sp.UserId)
                .IsUnique(true);

            // FLS: SpeakerUpload → SpeakerProfile
            builder.Entity<SpeakerUpload>()
                .HasOne(su => su.SpeakerProfile)
                .WithMany(sp => sp.Uploads)
                .HasForeignKey(su => su.SpeakerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SpeakerUpload>()
                .HasIndex(su => new { su.SpeakerProfileId, su.UploadType });

            // FLS: MeetingTimeSlot → SpeakerProfile (nullable)
            builder.Entity<MeetingTimeSlot>()
                .HasOne(mts => mts.BookedBySpeaker)
                .WithMany(sp => sp.MeetingSlots)
                .HasForeignKey(mts => mts.BookedBySpeakerProfileId)
                .OnDelete(DeleteBehavior.SetNull);

            // FLS: SpeakerTask → SpeakerProfile
            builder.Entity<SpeakerTask>()
                .HasOne(st => st.SpeakerProfile)
                .WithMany(sp => sp.Tasks)
                .HasForeignKey(st => st.SpeakerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // FLS: FLSDocument → SpeakerProfile (nullable target)
            builder.Entity<FLSDocument>()
                .HasOne(d => d.TargetSpeaker)
                .WithMany()
                .HasForeignKey(d => d.TargetSpeakerProfileId)
                .OnDelete(DeleteBehavior.SetNull);

            // FLS: SpeakerComment → SpeakerProfile
            builder.Entity<SpeakerComment>()
                .HasOne(sc => sc.SpeakerProfile)
                .WithMany(sp => sp.Comments)
                .HasForeignKey(sc => sc.SpeakerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // FLS: SpeakerComment → FLSDocument
            builder.Entity<SpeakerComment>()
                .HasOne(sc => sc.FLSDocument)
                .WithMany(d => d.Comments)
                .HasForeignKey(sc => sc.FLSDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // FLS: SpeakerNotification → SpeakerProfile
            builder.Entity<SpeakerNotification>()
                .HasOne(n => n.SpeakerProfile)
                .WithMany(sp => sp.Notifications)
                .HasForeignKey(n => n.SpeakerProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            builder.Entity<SpeakerNotification>()
                .HasIndex(n => new { n.SpeakerProfileId, n.IsRead });

            builder.Entity<MeetingTimeSlot>()
                .HasIndex(mts => mts.StartTime);

            // FLS: EmailCampaignRecipient → EmailCampaign
            builder.Entity<EmailCampaignRecipient>()
                .HasOne(r => r.EmailCampaign)
                .WithMany(c => c.Recipients)
                .HasForeignKey(r => r.EmailCampaignId)
                .OnDelete(DeleteBehavior.Cascade);

            // The history screen lists campaigns newest-first; this index keeps that
            // ordering off a sequential scan as the table grows.
            builder.Entity<EmailCampaign>()
                .HasIndex(c => c.CreatedAt);

            builder.Entity<EmailCampaign>()
                .HasIndex(c => c.CreatedByUserId);

            // Campaign detail filters recipients by delivery status ("show me the failures").
            builder.Entity<EmailCampaignRecipient>()
                .HasIndex(r => new { r.EmailCampaignId, r.Status });

            // Every send looks this table up by address, so the index is the difference
            // between a suppression check and a sequential scan per recipient.
            builder.Entity<EmailSuppression>()
                .HasIndex(s => s.NormalizedEmail)
                .IsUnique();

            ConfigureAvatars(builder);
            ConfigureEngagement(builder);
            ConfigureScholarLifecycle(builder);
            ConfigureMailing(builder);
            ConfigureNotifications(builder);
            ConfigureSuggestions(builder);
            ConfigureGames(builder);
            ConfigureNews(builder);
        }

        /// <summary>News mirrored from the foundation's public website.</summary>
        private static void ConfigureNews(ModelBuilder builder)
        {
            // The natural key, and the thing that makes the scrape idempotent. The unique
            // index is what actually enforces it: the scraper runs hourly-checked and can be
            // triggered by hand at the same moment, so "look it up, then insert if missing"
            // is a race two callers can pass through together. Without this, a manual refresh
            // clicked while the background run was mid-flight would duplicate every post.
            builder.Entity<NewsPost>()
                .HasIndex(p => p.SourceUrl)
                .IsUnique();

            // The widget's only query: newest first, take three.
            builder.Entity<NewsPost>()
                .HasIndex(p => new { p.PublishedAt, p.SortOrder });

            // Bounded because they are bounded in practice, and because an unbounded text
            // column invites storing something unbounded. A Squarespace URL runs to roughly
            // 200 characters; 500 leaves room without pretending there is no limit.
            builder.Entity<NewsPost>()
                .Property(p => p.SourceUrl)
                .HasMaxLength(500)
                .IsRequired();

            builder.Entity<NewsPost>()
                .Property(p => p.Title)
                .HasMaxLength(500)
                .IsRequired();

            // Generous: the source excerpts run to a couple of hundred characters today, but
            // this is someone else's editorial copy and truncating their prose at our
            // convenience would be worse than storing it.
            builder.Entity<NewsPost>()
                .Property(p => p.Excerpt)
                .HasMaxLength(2000);

            builder.Entity<NewsPost>()
                .Property(p => p.Author)
                .HasMaxLength(200);

            // Fixed-shape values the service writes, exactly as UserAvatar does.
            builder.Entity<NewsPost>()
                .Property(p => p.ImageContentType)
                .HasMaxLength(100);

            builder.Entity<NewsPost>()
                .Property(p => p.ImageETag)
                .HasMaxLength(64);
        }

        /// <summary>Profile pictures — a 1:1 side table on User holding the image bytes.</summary>
        private static void ConfigureAvatars(ModelBuilder builder)
        {
            // UserId is both the key and the foreign key. That is what makes this one-to-one:
            // there is nowhere to put a second row for the same person, so "which of this
            // user's avatars is current" is a question the schema cannot be asked.
            builder.Entity<UserAvatar>()
                .HasKey(a => a.UserId);

            // Cascade, unlike Kudos next door. A picture is entirely the account holder's own
            // — nobody else's record depends on it — so a deleted account should take it with
            // it rather than leave orphaned image bytes behind.
            builder.Entity<UserAvatar>()
                .HasOne(a => a.User)
                .WithOne()
                .HasForeignKey<UserAvatar>(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Bounded because they are: the service writes a fixed content type and a
            // fixed-length hash. Unbounded text columns here would suggest they vary.
            builder.Entity<UserAvatar>()
                .Property(a => a.ContentType)
                .HasMaxLength(100);

            builder.Entity<UserAvatar>()
                .Property(a => a.ETag)
                .HasMaxLength(64);
        }

        /// <summary>Badges and peer recognition.</summary>
        private static void ConfigureEngagement(ModelBuilder builder)
        {
            builder.Entity<Achievement>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // A badge is earned once. The unique index is what actually guarantees that —
            // EvaluateAsync runs on every progress load, so without it a race between two
            // tabs would award duplicates.
            builder.Entity<Achievement>()
                .HasIndex(a => new { a.UserId, a.Key })
                .IsUnique();

            builder.Entity<Kudos>()
                .HasOne(k => k.FromUser)
                .WithMany()
                .HasForeignKey(k => k.FromUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict on the recipient: deleting a user must not silently erase the
            // recognition other people gave them, which is somebody else's record too.
            builder.Entity<Kudos>()
                .HasOne(k => k.ToUser)
                .WithMany()
                .HasForeignKey(k => k.ToUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Kudos>()
                .HasIndex(k => new { k.ToUserId, k.IsHidden });

            // Powers the daily per-recipient cap.
            builder.Entity<Kudos>()
                .HasIndex(k => new { k.FromUserId, k.ToUserId, k.CreatedAt });
        }


        /// <summary>Server-side notifications, delivery preferences and push subscriptions.</summary>
        private static void ConfigureNotifications(ModelBuilder builder)
        {
            // The bell menu's only query: this user's undismissed notifications, newest
            // first. Covering it matters because the client polls this endpoint.
            builder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.DismissedAt, n.CreatedAt });

            // Idempotency. Filtered so the many rows with no dedupe key do not all collide
            // on a single NULL — Postgres would allow that, but the filter also keeps the
            // index small, and it is only ever probed with a value.
            builder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.DedupeKey })
                .IsUnique()
                .HasFilter("\"DedupeKey\" IS NOT NULL");

            builder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.CollapseKey, n.CreatedAt });

            // The outbox drain: everything still waiting to go out by email or push.
            builder.Entity<Notification>()
                .HasIndex(n => new { n.WantsEmail, n.EmailSentAt, n.DeferredUntil });

            builder.Entity<Notification>()
                .HasIndex(n => new { n.WantsPush, n.PushSentAt, n.DeferredUntil });

            builder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Notification>()
                .HasOne(n => n.Announcement)
                .WithMany(a => a.Notifications)
                .HasForeignKey(n => n.AnnouncementId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Notification>()
                .Property(n => n.MessageKey)
                .HasMaxLength(128);

            builder.Entity<Notification>()
                .Property(n => n.DedupeKey)
                .HasMaxLength(200);

            builder.Entity<Notification>()
                .Property(n => n.CollapseKey)
                .HasMaxLength(200);

            // One preference row per person.
            builder.Entity<NotificationPreference>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            builder.Entity<NotificationPreference>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique on the endpoint alone, not per user: the endpoint identifies a
            // physical browser. If that browser is later signed in as somebody else, the
            // row is reassigned, otherwise the previous account's notifications would keep
            // arriving on a device that is no longer theirs.
            builder.Entity<Auth.Models.Entities.Notifications.PushSubscription>()
                .HasIndex(s => s.Endpoint)
                .IsUnique();

            builder.Entity<Auth.Models.Entities.Notifications.PushSubscription>()
                .HasIndex(s => s.UserId);

            builder.Entity<Auth.Models.Entities.Notifications.PushSubscription>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Auth.Models.Entities.Notifications.PushSubscription>()
                .Property(s => s.Endpoint)
                .HasMaxLength(512);

            builder.Entity<Announcement>()
                .HasIndex(a => a.CreatedAt);

            builder.Entity<Announcement>()
                .Property(a => a.Title)
                .HasMaxLength(200);
        }


        /// <summary>The suggestion board and its votes.</summary>
        private static void ConfigureSuggestions(ModelBuilder builder)
        {
            builder.Entity<Suggestion>()
                .HasIndex(s => new { s.IsHidden, s.CreatedAt });

            builder.Entity<Suggestion>()
                .Property(s => s.Body)
                .HasMaxLength(500);

            builder.Entity<Suggestion>()
                .Property(s => s.AuthorName)
                .HasMaxLength(200);

            // Cascade: a deleted account takes its own suggestions with it. Unlike kudos —
            // which are somebody else's statement about you and therefore survive — a
            // suggestion is entirely the author's own.
            builder.Entity<Suggestion>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // One vote per person per suggestion, enforced by the database rather than by a
            // check-then-insert that two tabs can race past.
            builder.Entity<SuggestionVote>()
                .HasIndex(v => new { v.SuggestionId, v.UserId })
                .IsUnique();

            builder.Entity<SuggestionVote>()
                .HasOne(v => v.Suggestion)
                .WithMany(s => s.Votes)
                .HasForeignKey(v => v.SuggestionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SuggestionVote>()
                .HasOne(v => v.User)
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }


        /// <summary>Leaderboard reads.</summary>
        private static void ConfigureGames(ModelBuilder builder)
        {
            // The leaderboard groups by user within a game and filters on Verified, so this
            // is the covering shape for the only query that matters here.
            builder.Entity<GameScore>()
                .HasIndex(g => new { g.GameId, g.Verified, g.Score });

            builder.Entity<GameScore>()
                .HasIndex(g => new { g.UserId, g.GameId });

            builder.Entity<GameScore>()
                .Property(g => g.SessionId)
                .HasMaxLength(64);
        }

        /// <summary>Generations, cohort status and revertable promotion batches.</summary>
        private static void ConfigureScholarLifecycle(ModelBuilder builder)
        {
            builder.Entity<ScholarGeneration>()
                .HasIndex(g => g.Year)
                .IsUnique();

            // SetNull, not Cascade: deleting a generation must never delete the scholars in
            // it. They become ungrouped and the UI surfaces them for reassignment.
            builder.Entity<User>()
                .HasOne(u => u.Generation)
                .WithMany(g => g.Scholars)
                .HasForeignKey(u => u.GenerationId)
                .OnDelete(DeleteBehavior.SetNull);

            // The scholar list filters by status and cohort on every load.
            builder.Entity<User>()
                .HasIndex(u => new { u.ScholarStatus, u.GenerationId });

            builder.Entity<PromotionBatch>()
                .HasOne(b => b.Generation)
                .WithMany()
                .HasForeignKey(b => b.GenerationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Entries are meaningless without their batch, and the batch is the unit a revert
            // operates on — so cascading here is correct.
            builder.Entity<PromotionBatchEntry>()
                .HasOne(e => e.PromotionBatch)
                .WithMany(b => b.Entries)
                .HasForeignKey(e => e.PromotionBatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PromotionBatchEntry>()
                .HasIndex(e => e.PromotionBatchId);

            builder.Entity<PromotionBatch>()
                .HasIndex(b => b.PerformedAt);
        }

        /// <summary>
        /// Partnerships mailing schema. Split into its own method because the FLS block above
        /// is already long and these two subsystems have nothing to do with each other.
        /// </summary>
        private static void ConfigureMailing(ModelBuilder builder)
        {
            // ── Taxonomy ──────────────────────────────────────────────────────────

            builder.Entity<FirmGroup>()
                .HasIndex(g => g.Slug)
                .IsUnique();

            builder.Entity<FirmType>()
                .HasIndex(t => t.Slug)
                .IsUnique();

            // Deleting a group leaves its types in place, ungrouped, rather than cascading
            // away a chunk of the directory's classification.
            builder.Entity<FirmType>()
                .HasOne(t => t.FirmGroup)
                .WithMany(g => g.FirmTypes)
                .HasForeignKey(t => t.FirmGroupId)
                .OnDelete(DeleteBehavior.SetNull);

            // ── Firms ─────────────────────────────────────────────────────────────

            // The unique index is what actually prevents duplicate firms on re-import.
            // Filtered so the many firms imported without an address don't collide on NULL —
            // Postgres treats NULLs as distinct, but being explicit documents the intent.
            builder.Entity<Firm>()
                .HasIndex(f => f.NormalizedEmail)
                .IsUnique()
                .HasFilter("\"NormalizedEmail\" IS NOT NULL");

            // Retyping a firm shouldn't be blocked by, or cascade from, deleting a type.
            builder.Entity<Firm>()
                .HasOne(f => f.FirmType)
                .WithMany(t => t.Firms)
                .HasForeignKey(f => f.FirmTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            // Deleting an import batch record must not delete the firms it brought in.
            builder.Entity<Firm>()
                .HasOne(f => f.ImportBatch)
                .WithMany(b => b.Firms)
                .HasForeignKey(f => f.ImportBatchId)
                .OnDelete(DeleteBehavior.SetNull);

            // The directory's default view is "contactable firms of type X, by name".
            builder.Entity<Firm>()
                .HasIndex(f => new { f.Status, f.FirmTypeId });

            builder.Entity<Firm>()
                .HasIndex(f => f.Name);

            // Drives the "never contacted" audience and frequency checks.
            builder.Entity<Firm>()
                .HasIndex(f => f.LastContactedAt);

            // ── Templates ─────────────────────────────────────────────────────────

            builder.Entity<MailingTemplate>()
                .HasOne(t => t.FirmType)
                .WithMany(ft => ft.Templates)
                .HasForeignKey(t => t.FirmTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<MailingTemplate>()
                .HasIndex(t => new { t.IsActive, t.FirmTypeId });

            // ── Campaigns ─────────────────────────────────────────────────────────

            // A campaign is history. Deleting the template it came from must not erase the
            // record of what was sent, hence SetNull rather than Cascade.
            builder.Entity<MailingCampaign>()
                .HasOne(c => c.Template)
                .WithMany()
                .HasForeignKey(c => c.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<MailingCampaign>()
                .HasOne(c => c.Schedule)
                .WithMany(s => s.Campaigns)
                .HasForeignKey(c => c.ScheduleId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<MailingCampaign>()
                .HasIndex(c => c.CreatedAt);

            builder.Entity<MailingCampaign>()
                .HasIndex(c => c.Status);

            builder.Entity<MailingCampaignRecipient>()
                .HasOne(r => r.Campaign)
                .WithMany(c => c.Recipients)
                .HasForeignKey(r => r.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict, not Cascade: deleting a firm must not silently shred the delivery
            // history that proves what was sent to it. The service soft-deletes instead.
            builder.Entity<MailingCampaignRecipient>()
                .HasOne(r => r.Firm)
                .WithMany(f => f.CampaignRecipients)
                .HasForeignKey(r => r.FirmId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<MailingCampaignRecipient>()
                .HasIndex(r => new { r.CampaignId, r.Status });

            // Powers SkipAlreadyContacted: "has this firm been mailed by this schedule?"
            builder.Entity<MailingCampaignRecipient>()
                .HasIndex(r => new { r.FirmId, r.Status });

            // ── Schedules ─────────────────────────────────────────────────────────

            // Restrict: a template backing a live schedule can't be deleted out from under
            // it. The UI surfaces "used by N schedules" instead of failing at the database.
            builder.Entity<MailingSchedule>()
                .HasOne(s => s.Template)
                .WithMany()
                .HasForeignKey(s => s.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            // The scheduler polls "enabled and due" on every tick — this is the hot path.
            builder.Entity<MailingSchedule>()
                .HasIndex(s => new { s.IsEnabled, s.NextRunAt });
        }
    }
}
