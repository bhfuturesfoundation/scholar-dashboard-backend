namespace Auth.Models.Entities.Resources
{
    /// <summary>
    /// A document on the Resources tab. Either uploaded by staff (bytes in
    /// <see cref="ResourceDocumentFile"/>) or one of the PDFs shipped with the web app
    /// (<see cref="ExternalPath"/>), so both kinds can be listed, renamed and removed alike.
    /// </summary>
    public class ResourceDocument
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }

        /// <summary>Path of a file served by the web app, e.g. "/Guidebook.pdf". Null for uploads.</summary>
        public string? ExternalPath { get; set; }

        public bool IsPublished { get; set; }
        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedByUserId { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>When scholars were told about it. Set once, so republishing doesn't notify twice.</summary>
        public DateTime? NotifiedAt { get; set; }

        public ResourceDocumentFile? File { get; set; }
    }

    /// <summary>
    /// The bytes of an uploaded document, kept apart so listing documents never reads them.
    /// </summary>
    public class ResourceDocumentFile
    {
        public int DocumentId { get; set; }
        public ResourceDocument? Document { get; set; }
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
