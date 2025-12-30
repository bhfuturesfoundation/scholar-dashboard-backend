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
        /// Gets a specific mentee's journal history
        /// </summary>
        [HttpGet("mentee/{menteeId}/journals")]
        public async Task<ActionResult<MenteeJournalDto>> GetMenteeJournals(string menteeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            // Verify the mentee belongs to this mentor (unless admin)
            if (!User.IsInRole("Admin"))
            {
                var isMenteeOfMentor = await _mentorMenteeService.IsMenteeOfMentorAsync(menteeId, userId);
                if (!isMenteeOfMentor)
                {
                    return Forbid();
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
        /// Gets a specific mentee's journal for a particular month
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

            // Verify the mentee belongs to this mentor (unless admin)
            if (!User.IsInRole("Admin"))
            {
                var isMenteeOfMentor = await _mentorMenteeService.IsMenteeOfMentorAsync(menteeId, userId);
                if (!isMenteeOfMentor)
                {
                    return Forbid();
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
        /// Gets all journals for all mentees of the current mentor
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
        public async Task<ActionResult<List<MenteeDto>>> GetMenteesByMentorId(string mentorId)
        {
            var mentees = await _mentorMenteeService.GetMenteesByMentorIdAsync(mentorId);
            return Ok(mentees);
        }

        /// <summary>
        /// Gets mentor with all their mentees by mentor ID (Admin only)
        /// </summary>
        [HttpGet("{mentorId}/with-mentees")]
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
    }
}