using Auth.Models.DTOs.Resources;

namespace Auth.Services.Interfaces.Resources
{
    /// <summary>Documents on the Resources tab, managed by staff.</summary>
    public interface IResourceDocumentService
    {
        /// <param name="includeUnpublished">True for staff managing the list; scholars see published only.</param>
        Task<List<ResourceDocumentDto>> ListAsync(bool includeUnpublished, CancellationToken cancellationToken = default);

        /// <summary>Null when it doesn't exist, isn't published (for non-staff), or is served by the web app.</summary>
        Task<ResourceFile?> GetFileAsync(int id, bool includeUnpublished, CancellationToken cancellationToken = default);

        Task<ResourceDocumentDto> UploadAsync(
            string adminId, string title, string? description, string fileName, Stream content, long length,
            bool publish, bool notifyScholars, bool alsoEmail, CancellationToken cancellationToken = default);

        Task<ResourceDocumentDto> UpdateAsync(
            string adminId, int id, UpdateResourceDocumentRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(string adminId, int id, CancellationToken cancellationToken = default);
    }
}
