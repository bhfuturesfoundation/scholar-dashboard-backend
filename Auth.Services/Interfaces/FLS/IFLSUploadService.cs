using Auth.Models.DTOs.FLS;
using Auth.Models.Enums.FLS;
using Auth.Models.Request.FLS;
using Microsoft.AspNetCore.Http;

namespace Auth.Services.Interfaces.FLS
{
    public interface IFLSUploadService
    {
        Task<SpeakerUploadDto> UploadFileAsync(int speakerProfileId, IFormFile file, UploadType uploadType);
        Task<SpeakerUploadDto?> GetUploadAsync(int uploadId);
        Task<List<SpeakerUploadDto>> GetUploadsForSpeakerAsync(int speakerProfileId);
        Task<SpeakerUploadDto> VerifyUploadAsync(int uploadId, VerifyUploadRequest request, string adminUserId);
        Task<bool> DeleteUploadAsync(int uploadId, int speakerProfileId);
        Task<(byte[] FileBytes, string ContentType, string FileName)> DownloadFileAsync(int uploadId);
        Task<SpeakerCompletionStatusDto> GetCompletionStatusAsync(int speakerProfileId);
    }
}
