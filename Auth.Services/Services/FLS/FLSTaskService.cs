using Auth.Models.Data;
using Auth.Models.DTOs.FLS;
using Auth.Models.Entities;
using Auth.Models.Entities.FLS;
using Auth.Models.Exceptions;
using Auth.Models.Request.FLS;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.FLS
{
    public class FLSTaskService : IFLSTaskService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<FLSTaskService> _logger;

        public FLSTaskService(ApplicationDbContext context, UserManager<User> userManager, ILogger<FLSTaskService> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<SpeakerTaskDto> CreateTaskAsync(CreateSpeakerTaskRequest request)
        {
            var profile = await _context.SpeakerProfiles.FindAsync(request.SpeakerProfileId)
                ?? throw new NotFoundException("Speaker profile not found.");

            var task = new SpeakerTask
            {
                SpeakerProfileId = request.SpeakerProfileId,
                TaskType = request.TaskType,
                Title = request.Title,
                Description = request.Description,
                Location = request.Location,
                TaskStartTime = request.TaskStartTime,
                TaskEndTime = request.TaskEndTime,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.SpeakerTasks.Add(task);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Task '{Title}' created for speaker profile {Id}", task.Title, task.SpeakerProfileId);
            return await MapToDto(task, profile);
        }

        public async Task<List<SpeakerTaskDto>> GetTasksForSpeakerAsync(int speakerProfileId)
        {
            var profile = await _context.SpeakerProfiles.FindAsync(speakerProfileId)
                ?? throw new NotFoundException("Speaker profile not found.");

            var tasks = await _context.SpeakerTasks
                .Where(t => t.SpeakerProfileId == speakerProfileId)
                .OrderBy(t => t.TaskStartTime)
                .ToListAsync();

            var result = new List<SpeakerTaskDto>();
            foreach (var t in tasks)
                result.Add(await MapToDto(t, profile));

            return result;
        }

        public async Task<SpeakerTaskDto> UpdateTaskAsync(int taskId, UpdateSpeakerTaskRequest request)
        {
            var task = await _context.SpeakerTasks
                .Include(t => t.SpeakerProfile)
                .FirstOrDefaultAsync(t => t.Id == taskId)
                ?? throw new NotFoundException("Task not found.");

            if (request.Title != null) task.Title = request.Title;
            if (request.Description != null) task.Description = request.Description;
            if (request.Location != null) task.Location = request.Location;
            if (request.TaskStartTime.HasValue) task.TaskStartTime = request.TaskStartTime;
            if (request.TaskEndTime.HasValue) task.TaskEndTime = request.TaskEndTime;
            if (request.IsCompleted.HasValue) task.IsCompleted = request.IsCompleted.Value;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return await MapToDto(task, task.SpeakerProfile);
        }

        public async Task<bool> DeleteTaskAsync(int taskId)
        {
            var task = await _context.SpeakerTasks.FindAsync(taskId)
                ?? throw new NotFoundException("Task not found.");

            _context.SpeakerTasks.Remove(task);
            await _context.SaveChangesAsync();
            return true;
        }

        private async Task<SpeakerTaskDto> MapToDto(SpeakerTask t, SpeakerProfile profile)
        {
            var user = await _userManager.FindByIdAsync(profile.UserId);
            return new SpeakerTaskDto
            {
                Id = t.Id,
                SpeakerProfileId = t.SpeakerProfileId,
                SpeakerName = user != null ? $"{user.FirstName} {user.LastName}" : string.Empty,
                TaskType = t.TaskType,
                TaskTypeLabel = t.TaskType.ToString(),
                Title = t.Title,
                Description = t.Description,
                Location = t.Location,
                TaskStartTime = t.TaskStartTime,
                TaskEndTime = t.TaskEndTime,
                IsCompleted = t.IsCompleted,
                CreatedAt = t.CreatedAt
            };
        }
    }
}
