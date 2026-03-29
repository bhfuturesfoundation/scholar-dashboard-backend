using Auth.Models.Enums.FLS;
using Auth.Models.Request.FLS;
using Auth.Models.Response;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers.FLS
{
    [Route("api/fls/documents")]
    [ApiController]
    [Authorize]
    public class FLSDocumentController : ControllerBase
    {
        private readonly IFLSDocumentService _documentService;
        private readonly IFLSSpeakerService _speakerService;
        private readonly ILogger<FLSDocumentController> _logger;

        public FLSDocumentController(
            IFLSDocumentService documentService,
            IFLSSpeakerService speakerService,
            ILogger<FLSDocumentController> logger)
        {
            _documentService = documentService;
            _speakerService = speakerService;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        /// <summary>
        /// Get documents visible to the current speaker (global + speaker-specific).
        /// </summary>
        [Authorize(Roles = "FLSSpeaker")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMy([FromQuery] FLSDocumentType? documentType = null)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var documents = await _documentService.GetDocumentsAsync(documentType, profile.Id);
            return Ok(ApiResponse<object>.SuccessResponse(documents, "Documents retrieved."));
        }

        /// <summary>
        /// Admin: get all documents.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] FLSDocumentType? documentType = null)
        {
            var documents = await _documentService.GetDocumentsAsync(documentType);
            return Ok(ApiResponse<object>.SuccessResponse(documents, "All documents retrieved."));
        }

        /// <summary>
        /// Get document by ID.
        /// </summary>
        [HttpGet("{documentId:int}")]
        public async Task<IActionResult> GetById(int documentId)
        {
            var doc = await _documentService.GetDocumentByIdAsync(documentId);
            if (doc == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Document not found."));

            return Ok(ApiResponse<object>.SuccessResponse(doc, ""));
        }

        /// <summary>
        /// Download document file.
        /// </summary>
        [HttpGet("{documentId:int}/download")]
        public async Task<IActionResult> Download(int documentId)
        {
            var (fileBytes, contentType, fileName) = await _documentService.DownloadDocumentAsync(documentId);
            return File(fileBytes, contentType, fileName);
        }

        /// <summary>
        /// Admin: upload a program/timeline/visual document.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpPost]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public async Task<IActionResult> Upload(
            [FromForm] IFormFile file,
            [FromForm] string title,
            [FromForm] FLSDocumentType documentType,
            [FromForm] int? targetSpeakerProfileId = null)
        {
            var doc = await _documentService.UploadDocumentAsync(file, title, documentType, GetUserId(), targetSpeakerProfileId);
            return Ok(ApiResponse<object>.SuccessResponse(doc, "Document uploaded."));
        }

        /// <summary>
        /// Admin: delete/deactivate a document.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpDelete("{documentId:int}")]
        public async Task<IActionResult> Delete(int documentId)
        {
            await _documentService.DeleteDocumentAsync(documentId);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Document removed."));
        }

        /// <summary>
        /// Speaker: add a comment/approval to a document (max 2 comments, 500 words each).
        /// </summary>
        [Authorize(Roles = "FLSSpeaker")]
        [HttpPost("comment")]
        public async Task<IActionResult> AddComment([FromBody] AddSpeakerCommentRequest request)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var comment = await _documentService.AddCommentAsync(profile.Id, request);
            return Ok(ApiResponse<object>.SuccessResponse(comment, "Comment submitted."));
        }

        /// <summary>
        /// Admin: get all comments for a document.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpGet("{documentId:int}/comments")]
        public async Task<IActionResult> GetComments(int documentId)
        {
            var comments = await _documentService.GetCommentsForDocumentAsync(documentId);
            return Ok(ApiResponse<object>.SuccessResponse(comments, "Comments retrieved."));
        }

        /// <summary>
        /// Speaker: check how many comments they have on a document.
        /// </summary>
        [Authorize(Roles = "FLSSpeaker")]
        [HttpGet("{documentId:int}/my-comment-count")]
        public async Task<IActionResult> GetMyCommentCount(int documentId)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var count = await _documentService.GetCommentCountForSpeakerAsync(profile.Id, documentId);
            return Ok(ApiResponse<object>.SuccessResponse(new { count, maxAllowed = 2 }, ""));
        }
    }
}
