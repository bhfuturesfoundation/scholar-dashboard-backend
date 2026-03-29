using Auth.Models.DTOs.FLS;
using Auth.Models.Enums.FLS;
using Auth.Models.Request.FLS;
using Microsoft.AspNetCore.Http;

namespace Auth.Services.Interfaces.FLS
{
    public interface IFLSDocumentService
    {
        Task<FLSDocumentDto> UploadDocumentAsync(IFormFile file, string title, FLSDocumentType documentType, string uploadedByUserId, int? targetSpeakerProfileId = null);
        Task<List<FLSDocumentDto>> GetDocumentsAsync(FLSDocumentType? documentType = null, int? targetSpeakerProfileId = null);
        Task<FLSDocumentDto?> GetDocumentByIdAsync(int documentId);
        Task<bool> DeleteDocumentAsync(int documentId);
        Task<(byte[] FileBytes, string ContentType, string FileName)> DownloadDocumentAsync(int documentId);
        Task<SpeakerCommentDto> AddCommentAsync(int speakerProfileId, AddSpeakerCommentRequest request);
        Task<List<SpeakerCommentDto>> GetCommentsForDocumentAsync(int documentId);
        Task<int> GetCommentCountForSpeakerAsync(int speakerProfileId, int documentId);
    }
}
