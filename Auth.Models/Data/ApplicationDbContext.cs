using Auth.Models.Entities;
using Auth.Models.Entities.Email;
using Auth.Models.Entities.FLS;
using Auth.Models.Entities.Mailing;
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

            ConfigureScholarLifecycle(builder);
            ConfigureMailing(builder);
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
