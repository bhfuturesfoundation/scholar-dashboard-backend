using Auth.Models.Constants;
using Auth.Models.DTOs.Settings;
using Auth.Models.Response;
using Auth.Services.Interfaces.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    [Route("api/settings")]
    [ApiController]
    [Authorize(Roles = AppRoles.Admin)]
    public class AppSettingsController : ControllerBase
    {
        private readonly IAppSettingsService _settings;

        public AppSettingsController(IAppSettingsService settings)
        {
            _settings = settings;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<AppSettingDto>>>> Get(CancellationToken ct) =>
            Ok(ApiResponse<List<AppSettingDto>>.SuccessResponse(await _settings.GetAllAsync(ct), "Settings retrieved"));

        [HttpPut]
        public async Task<ActionResult<ApiResponse<List<AppSettingDto>>>> Update(
            [FromBody] UpdateAppSettingsRequest request, CancellationToken ct) =>
            Ok(ApiResponse<List<AppSettingDto>>.SuccessResponse(
                await _settings.UpdateAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, request, ct),
                "Settings saved"));
    }
}
