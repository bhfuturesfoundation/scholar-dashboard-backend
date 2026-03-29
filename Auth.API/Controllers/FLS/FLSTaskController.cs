using Auth.Models.Request.FLS;
using Auth.Models.Response;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers.FLS
{
    [Route("api/fls/tasks")]
    [ApiController]
    [Authorize]
    public class FLSTaskController : ControllerBase
    {
        private readonly IFLSTaskService _taskService;
        private readonly IFLSSpeakerService _speakerService;
        private readonly ILogger<FLSTaskController> _logger;

        public FLSTaskController(
            IFLSTaskService taskService,
            IFLSSpeakerService speakerService,
            ILogger<FLSTaskController> logger)
        {
            _taskService = taskService;
            _speakerService = speakerService;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        /// <summary>
        /// Speaker: get their personalized tasks/timeline.
        /// </summary>
        [Authorize(Roles = "FLSSpeaker")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyTasks()
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var tasks = await _taskService.GetTasksForSpeakerAsync(profile.Id);
            return Ok(ApiResponse<object>.SuccessResponse(tasks, "Tasks retrieved."));
        }

        /// <summary>
        /// Admin: get tasks for a specific speaker.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpGet("speaker/{speakerProfileId:int}")]
        public async Task<IActionResult> GetForSpeaker(int speakerProfileId)
        {
            var tasks = await _taskService.GetTasksForSpeakerAsync(speakerProfileId);
            return Ok(ApiResponse<object>.SuccessResponse(tasks, ""));
        }

        /// <summary>
        /// Admin: create a task for a speaker.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateSpeakerTaskRequest request)
        {
            var task = await _taskService.CreateTaskAsync(request);
            return Ok(ApiResponse<object>.SuccessResponse(task, "Task created and assigned to speaker."));
        }

        /// <summary>
        /// Admin: update a task.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpPut("{taskId:int}")]
        public async Task<IActionResult> Update(int taskId, [FromBody] UpdateSpeakerTaskRequest request)
        {
            var task = await _taskService.UpdateTaskAsync(taskId, request);
            return Ok(ApiResponse<object>.SuccessResponse(task, "Task updated."));
        }

        /// <summary>
        /// Admin: delete a task.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpDelete("{taskId:int}")]
        public async Task<IActionResult> Delete(int taskId)
        {
            await _taskService.DeleteTaskAsync(taskId);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Task deleted."));
        }
    }
}
