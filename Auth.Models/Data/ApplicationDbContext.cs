using Auth.Models.Entities;
using Auth.Models.Entities.FLS;
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
        }
    }
}
