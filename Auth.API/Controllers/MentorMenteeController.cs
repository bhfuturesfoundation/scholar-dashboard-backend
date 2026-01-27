using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MentorMenteeController : ControllerBase
    {
        private readonly IMentorMenteeService _mentorMenteeService;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<MentorMenteeController> _logger;

        public MentorMenteeController(
            IMentorMenteeService mentorMenteeService,
            UserManager<User> userManager,
            ILogger<MentorMenteeController> logger)
        {
            _mentorMenteeService = mentorMenteeService;
            _userManager = userManager;
            _logger = logger;
        }

        /// <summary>
        /// Gets the current mentor's information
        /// </summary>
        [HttpGet("me")]
        public async Task<ActionResult<MentorDto>> GetCurrentMentor()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var mentor = await _mentorMenteeService.GetMentorByIdAsync(userId);
            if (mentor == null)
            {
                return NotFound(new { message = "Mentor not found or user is not a mentor" });
            }

            return Ok(mentor);
        }

        /// <summary>
        /// Gets the current mentor's mentees
        /// </summary>
        [HttpGet("me/mentees")]
        public async Task<ActionResult<List<MenteeDto>>> GetMyMentees()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var mentees = await _mentorMenteeService.GetMenteesByMentorIdAsync(userId);
            return Ok(mentees);
        }

        /// <summary>
        /// Gets the current mentor with all their mentees
        /// </summary>
        [HttpGet("me/with-mentees")]
        public async Task<ActionResult<MentorWithMenteesDto>> GetMeWithMentees()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var result = await _mentorMenteeService.GetMentorWithMenteesAsync(userId);
            if (result == null)
            {
                return NotFound(new { message = "Mentor not found or user is not a mentor" });
            }

            return Ok(result);
        }

        /// <summary>
        /// Gets a specific mentee's journal history (requires mentee's permission)
        /// </summary>
        [HttpGet("mentee/{menteeId}/journals")]
        public async Task<ActionResult<MenteeJournalDto>> GetMenteeJournals(string menteeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Admins can access without permission check
            if (!User.IsInRole("Admin"))
            {
                // Check if mentor has permission to access journals
                var canAccess = await _mentorMenteeService.CanMentorAccessJournalAsync(userId, menteeId);
                if (!canAccess)
                {
                    return StatusCode(403, new
                    {
                        message = "You do not have permission to view this mentee's journals. The mentee must grant you access first.",
                        accessDenied = true,
                        requiresPermission = true
                    });
                }
            }

            var result = await _mentorMenteeService.GetMenteeJournalsAsync(menteeId);
            if (result == null)
            {
                return NotFound(new { message = "Mentee not found" });
            }

            return Ok(result);
        }

        /// <summary>
        /// Gets a specific mentee's journal for a particular month (requires mentee's permission)
        /// </summary>
        [HttpGet("mentee/{menteeId}/journal/{monthYear}")]
        public async Task<ActionResult<MenteeJournalDetailDto>> GetMenteeJournalForMonth(
            string menteeId,
            string monthYear)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Admins can access without permission check
            if (!User.IsInRole("Admin"))
            {
                // Check if mentor has permission to access journals
                var canAccess = await _mentorMenteeService.CanMentorAccessJournalAsync(userId, menteeId);
                if (!canAccess)
                {
                    return StatusCode(403, new
                    {
                        message = "You do not have permission to view this mentee's journals. The mentee must grant you access first.",
                        accessDenied = true,
                        requiresPermission = true
                    });
                }
            }

            var result = await _mentorMenteeService.GetMenteeJournalForMonthAsync(menteeId, monthYear);
            if (result == null)
            {
                return NotFound(new { message = "Mentee or journal not found" });
            }

            return Ok(result);
        }

        /// <summary>
        /// Gets all journals for all mentees of the current mentor (only includes mentees who granted permission)
        /// </summary>
        [HttpGet("me/all-mentees-journals")]
        public async Task<ActionResult<List<MenteeJournalDto>>> GetAllMyMenteesJournals()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var result = await _mentorMenteeService.GetAllMenteesJournalsForMentorAsync(userId);
            return Ok(result);
        }

        /// <summary>
        /// Gets mentor information by ID (Admin only)
        /// </summary>
        [HttpGet("{mentorId}")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<MentorDto>> GetMentorById(string mentorId)
        {
            var mentor = await _mentorMenteeService.GetMentorByIdAsync(mentorId);
            if (mentor == null)
            {
                return NotFound(new { message = "Mentor not found" });
            }

            return Ok(mentor);
        }

        /// <summary>
        /// Gets mentor's mentees by mentor ID (Admin only)
        /// </summary>
        [HttpGet("{mentorId}/mentees")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<List<MenteeDto>>> GetMenteesByMentorId(string mentorId)
        {
            var mentees = await _mentorMenteeService.GetMenteesByMentorIdAsync(mentorId);
            return Ok(mentees);
        }

        /// <summary>
        /// Gets mentor with all their mentees by mentor ID (Admin only)
        /// </summary>
        [HttpGet("{mentorId}/with-mentees")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<MentorWithMenteesDto>> GetMentorWithMenteesById(string mentorId)
        {
            var result = await _mentorMenteeService.GetMentorWithMenteesAsync(mentorId);
            if (result == null)
            {
                return NotFound(new { message = "Mentor not found" });
            }

            return Ok(result);
        }

        // ==================== MENTEE ENDPOINTS ====================

        /// <summary>
        /// Gets the current user's mentor information (for mentees)
        /// Returns 200 with null mentor if no mentor is assigned
        /// </summary>
        [HttpGet("my-mentor")]
        public async Task<ActionResult<MentorDto>> GetMyMentor()
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var mentor = await _mentorMenteeService.GetMentorByMenteeIdAsync(userId);

            return Ok(mentor);
        }

        /// <summary>
        /// Gets the current user's information with their mentor (for mentees)
        /// Mentor will be null if no mentor is assigned
        /// </summary>
        [HttpGet("me-with-mentor")]
        public async Task<ActionResult<MenteeWithMentorDto>> GetMeWithMentor()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var result = await _mentorMenteeService.GetMenteeWithMentorAsync(userId);
            if (result == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(result);
        }

        /// <summary>
        /// Gets mentee information by ID (Admin only or the mentee's own mentor)
        /// </summary>
        [HttpGet("mentee/{menteeId}")]
        public async Task<ActionResult<MenteeDto>> GetMenteeById(string menteeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Check if current user is admin, the mentee themselves, or the mentee's mentor
            if (!User.IsInRole("Admin") && userId != menteeId)
            {
                var isMenteeOfCurrentUser = await _mentorMenteeService.IsMenteeOfMentorAsync(menteeId, userId);
                if (!isMenteeOfCurrentUser)
                {
                    return Forbid();
                }
            }

            var mentee = await _mentorMenteeService.GetMenteeByIdAsync(menteeId);
            if (mentee == null)
            {
                return NotFound(new { message = "Mentee not found" });
            }

            return Ok(mentee);
        }

        /// <summary>
        /// Gets mentee with their mentor by mentee ID (Admin only or the mentee's own mentor or the mentee themselves)
        /// </summary>
        [HttpGet("mentee/{menteeId}/with-mentor")]
        public async Task<ActionResult<MenteeWithMentorDto>> GetMenteeWithMentorById(string menteeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Check if current user is admin, the mentee themselves, or the mentee's mentor
            if (!User.IsInRole("Admin") && userId != menteeId)
            {
                var isMenteeOfCurrentUser = await _mentorMenteeService.IsMenteeOfMentorAsync(menteeId, userId);
                if (!isMenteeOfCurrentUser)
                {
                    return Forbid();
                }
            }

            var result = await _mentorMenteeService.GetMenteeWithMentorAsync(menteeId);
            if (result == null)
            {
                return NotFound(new { message = "Mentee not found" });
            }

            return Ok(result);
        }

        // ==================== PERMISSION MANAGEMENT ENDPOINTS ====================

        /// <summary>
        /// Updates the current mentee's journal access permission for their mentor
        /// </summary>
        [HttpPut("me/journal-access")]
        public async Task<ActionResult> UpdateMyJournalAccess([FromBody] UpdateJournalAccessDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Verify user is a mentee
            var isMentee = await _mentorMenteeService.IsMenteeAsync(userId);
            if (!isMentee)
            {
                return BadRequest(new { message = "User is not a mentee" });
            }

            var success = await _mentorMenteeService.UpdateMentorJournalAccessAsync(userId, dto.AllowAccess);

            if (!success)
            {
                return StatusCode(500, new { message = "Failed to update journal access permission" });
            }

            return Ok(new
            {
                message = dto.AllowAccess
                    ? "Journal access granted to your mentor"
                    : "Journal access revoked from your mentor",
                allowAccess = dto.AllowAccess
            });
        }

        /// <summary>
        /// Gets the current user's journal access permission status
        /// </summary>
        [HttpGet("me/journal-access")]
        public async Task<ActionResult<JournalAccessStatusDto>> GetMyJournalAccessStatus()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var hasAccess = await _mentorMenteeService.HasMentorJournalAccessAsync(userId);
            var mentor = await _mentorMenteeService.GetMentorByMenteeIdAsync(userId);

            return Ok(new JournalAccessStatusDto
            {
                AllowMentorJournalAccess = hasAccess,
                HasMentor = mentor != null,
                Mentor = mentor
            });
        }

        /// <summary>
        /// Admin endpoint to update any mentee's journal access permission
        /// </summary>
        [HttpPut("mentee/{menteeId}/journal-access")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult> UpdateMenteeJournalAccess(
            string menteeId,
            [FromBody] UpdateJournalAccessDto dto)
        {
            var success = await _mentorMenteeService.UpdateMentorJournalAccessAsync(menteeId, dto.AllowAccess);

            if (!success)
            {
                return NotFound(new { message = "Mentee not found or update failed" });
            }

            return Ok(new
            {
                message = dto.AllowAccess
                    ? "Journal access granted"
                    : "Journal access revoked",
                allowAccess = dto.AllowAccess
            });
        }

        /// <summary>
        /// Checks if the current mentor can access a specific mentee's journals
        /// </summary>
        [HttpGet("can-access-journal/{menteeId}")]
        public async Task<ActionResult<CanAccessJournalDto>> CanAccessMenteeJournal(string menteeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Admins always have access
            if (User.IsInRole("Admin"))
            {
                return Ok(new CanAccessJournalDto
                {
                    CanAccess = true,
                    IsAdmin = true,
                    IsMentor = false,
                    HasPermission = false,
                    Message = "Admin access granted"
                });
            }

            var isMenteeOfMentor = await _mentorMenteeService.IsMenteeOfMentorAsync(menteeId, userId);
            if (!isMenteeOfMentor)
            {
                return Ok(new CanAccessJournalDto
                {
                    CanAccess = false,
                    IsAdmin = false,
                    IsMentor = false,
                    HasPermission = false,
                    Message = "This mentee does not belong to you"
                });
            }

            var hasPermission = await _mentorMenteeService.HasMentorJournalAccessAsync(menteeId);

            return Ok(new CanAccessJournalDto
            {
                CanAccess = hasPermission,
                IsAdmin = false,
                IsMentor = true,
                HasPermission = hasPermission,
                Message = hasPermission
                    ? "Access granted by mentee"
                    : "Waiting for mentee to grant access"
            });
        }
    }

    // DTOs for the new endpoints
    public class UpdateJournalAccessDto
    {
        public bool AllowAccess { get; set; }
    }

    public class JournalAccessStatusDto
    {
        public bool AllowMentorJournalAccess { get; set; }
        public bool HasMentor { get; set; }
        public MentorDto? Mentor { get; set; }
    }

    public class CanAccessJournalDto
    {
        public bool CanAccess { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsMentor { get; set; }
        public bool HasPermission { get; set; }
        public string Message { get; set; }
    }
}