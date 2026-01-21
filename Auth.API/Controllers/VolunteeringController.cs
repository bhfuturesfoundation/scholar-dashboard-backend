using Auth.Models.DTOs;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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
                    "Top 3 volunteers fetched successfully"
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

        [HttpGet("all-volunteers")]
        public async Task<IActionResult> GetAllVolunteers()
        {
            try
            {
                var allVolunteers = await _volunteeringService.GetAllVolunteersAsync();

                return Ok(ApiResponse<IEnumerable<VolunteerDto>>.SuccessResponse(
                    allVolunteers,
                    "All volunteers fetched successfully" 
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all volunteers from Google Sheets");
                return StatusCode(500, ApiResponse<object>.ErrorResponse(
                    "Failed to fetch all volunteers"
                ));
            }
        }

        [HttpGet("volunteers-with-teams")]
        public async Task<IActionResult> GetVolunteersWithTeams()
        {
            try
            {
                var volunteersWithTeams = await _volunteeringService.GetVolunteersWithTeamLeadersAsync();

                return Ok(ApiResponse<IEnumerable<VolunteerWithTeamDto>>.SuccessResponse(
                    volunteersWithTeams,
                    "Volunteers with team leaders fetched successfully"
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching volunteers with team leaders from Google Sheets");
                return StatusCode(500, ApiResponse<object>.ErrorResponse(
                    "Failed to fetch volunteers with team leaders"
                ));
            }
        }
    }
}