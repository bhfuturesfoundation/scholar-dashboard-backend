namespace Auth.Models.Entities
{
    /// <summary>
    /// One person's profile picture, stored as bytes in Postgres.
    ///
    /// ── Why the database and not object storage ──────────────────────────────
    ///
    /// The platform already has Dropbox wired up, so it was the obvious candidate and it is
    /// deliberately not used here. Dropbox serves files through share links that expire, and
    /// this deployment has already seen one return an HTML error page under HTTP 200 — a
    /// response that is "successful" as far as any caller can tell while containing markup
    /// instead of an image. An avatar is decoration; it is not worth inheriting a whole class
    /// of failure whose symptom is a broken picture nobody can explain.
    ///
    /// A file on disk is not an option either: Railway containers have ephemeral disks, so
    /// every deploy would silently wipe everyone's picture.
    ///
    /// The size argument settles it. A few hundred scholars at a 256px WebP is single-digit
    /// megabytes for the entire table — small enough that Postgres is simply the least
    /// moving parts, and it comes along in the existing backup for free.
    ///
    /// ── Why a separate table and not a column on User ────────────────────────
    ///
    /// <c>User</c> is loaded everywhere: every roster, every export, every <c>Include</c> in
    /// this codebase. A byte[] column would ride along on all of them, because EF materialises
    /// every scalar property of a tracked entity unless the query explicitly projects. Keeping
    /// the bytes in their own table means the common paths never pull image data across the
    /// wire, and the only query that reads it is the one endpoint that serves it.
    /// </summary>
    public class UserAvatar
    {
        /// <summary>
        /// Primary key *and* foreign key. One avatar per person, enforced by the shape of the
        /// table rather than by a unique index the service has to remember to respect.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        public User User { get; set; } = null!;

        /// <summary>
        /// Always "image/webp" today. Stored rather than hardcoded at the endpoint so a future
        /// format change does not start serving new bytes under a stale content type — the
        /// rows written before the change would still be WebP.
        /// </summary>
        public string ContentType { get; set; } = string.Empty;

        /// <summary>
        /// The re-encoded image. Never the uploaded bytes — see <c>AvatarService</c> for why
        /// that distinction is the security control rather than a detail.
        /// </summary>
        public byte[] Bytes { get; set; } = Array.Empty<byte>();

        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>
        /// Duplicates <c>Bytes.Length</c>, deliberately. It lets an operator answer "how much
        /// space is this costing" with a SUM over an int column instead of a scan that reads
        /// every image out of the table.
        /// </summary>
        public int SizeBytes { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Short hash of <see cref="Bytes"/>, used as the HTTP entity tag.
        ///
        /// Stored rather than computed per request: the point of the ETag is to answer
        /// <c>If-None-Match</c> with a 304 *without* touching the image, and hashing on the fly
        /// would mean loading the bytes to decide we do not need to send them.
        /// </summary>
        public string ETag { get; set; } = string.Empty;
    }
}
