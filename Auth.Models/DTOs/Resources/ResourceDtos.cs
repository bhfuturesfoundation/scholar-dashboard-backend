namespace Auth.Models.DTOs.Resources
{
    public class ResourceDocumentDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }

        /// <summary>Set for documents served by the web app; otherwise download from the API.</summary>
        public string? ExternalPath { get; set; }

        public bool IsPublished { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? NotifiedAt { get; set; }
    }

    public class UpdateResourceDocumentRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public bool IsPublished { get; set; }
        public int SortOrder { get; set; }

        /// <summary>Notify scholars if this publishes a document they haven't been told about.</summary>
        public bool NotifyScholars { get; set; } = true;
        public bool AlsoEmail { get; set; }
    }

    public class ResourceFile
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }
}
