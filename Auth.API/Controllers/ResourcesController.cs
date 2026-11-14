using Auth.Models.Constants;
using Auth.Models.DTOs.Resources;
using Auth.Models.Response;
using Auth.Services.Interfaces.Resources;
using Auth.Services.Services.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    [Route("api/resources")]
    [ApiController]
    [Authorize]
    public class ResourcesController : ControllerBase
    {
        private readonly IResourceDocumentService _documents;

        public ResourcesController(IResourceDocumentService documents)
        {
            _documents = documents;
        }

        private string CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        private bool IsStaff => User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.ProgramManager);

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<ResourceDocumentDto>>>> List(CancellationToken ct) =>
            Ok(ApiResponse<List<ResourceDocumentDto>>.SuccessResponse(await _documents.ListAsync(IsStaff, ct), "Documents retrieved"));

        [HttpGet("{id:int}/file")]
        public async Task<IActionResult> Download(int id, CancellationToken ct)
        {
            var file = await _documents.GetFileAsync(id, IsStaff, ct);
            if (file is null) return NotFound();

            // Served as data, never sniffed into something the browser would run.
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers.CacheControl = "private, max-age=3600";

            var inline = file.ContentType is "application/pdf" or "image/png" or "image/jpeg";
            return inline
                ? File(file.Content, file.ContentType)
                : File(file.Content, file.ContentType, file.FileName);
        }

        [Authorize(Roles = AppRoles.Operations)]
        [HttpPost]
        [RequestSizeLimit(ResourceDocumentService.MaxFileBytes + 1024 * 1024)]
        public async Task<ActionResult<ApiResponse<ResourceDocumentDto>>> Upload(
            [FromForm] IFormFile file,
            [FromForm] string title,
            [FromForm] string? description,
            [FromForm] bool publish = true,
            [FromForm] bool notifyScholars = true,
            [FromForm] bool alsoEmail = false,
            CancellationToken ct = default)
        {
            if (file is null || file.Length == 0)
                return BadRequest(ApiResponse<ResourceDocumentDto>.ErrorResponse("No file was uploaded."));

            await using var stream = file.OpenReadStream();
            var document = await _documents.UploadAsync(
                CallerId, title, description, file.FileName, stream, file.Length, publish, notifyScholars, alsoEmail, ct);

            return Ok(ApiResponse<ResourceDocumentDto>.SuccessResponse(document, "Document uploaded"));
        }

        [Authorize(Roles = AppRoles.Operations)]
        [HttpPut("{id:int}")]
        public async Task<ActionResult<ApiResponse<ResourceDocumentDto>>> Update(
            int id, [FromBody] UpdateResourceDocumentRequest request, CancellationToken ct) =>
            Ok(ApiResponse<ResourceDocumentDto>.SuccessResponse(
                await _documents.UpdateAsync(CallerId, id, request, ct), "Document updated"));

        [Authorize(Roles = AppRoles.Operations)]
        [HttpDelete("{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id, CancellationToken ct)
        {
            await _documents.DeleteAsync(CallerId, id, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Document deleted"));
        }
    }
}
