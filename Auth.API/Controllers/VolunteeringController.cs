using Auth.Models.DTOs;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    [ApiController]
    [Route("api/volunteering")]
    //[Authorize]
    public class VolunteeringController : ControllerBase
    {
        private readonly IVolunteeringService _volunteeringService;
        private readonly ILogger<VolunteeringController> _logger;

        public VolunteeringController(
            IVolunteeringService volunteeringService,
            ILogger<VolunteeringController> logger)
        {
            _volunteeringService = volunteeringService;
            _logger = logger;
        }


        [HttpGet("top-volunteers")]
        public async Task<IActionResult> GetTopVolunteers()
        {
            try
            {
                var topVolunteers = await _volunteeringService.GetTopVolunteersAsync();
                return Ok(ApiResponse<IEnumerable<VolunteerDto>>.SuccessResponse(
                    topVolunteers,
                    "Top volunteers fetched successfully"
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching top volunteers from Google Sheets");
                return StatusCode(500, ApiResponse<object>.ErrorResponse(
                    "Failed to fetch top volunteers"
                ));
            }
        }
    }
}
