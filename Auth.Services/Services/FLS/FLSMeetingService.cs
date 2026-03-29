using Auth.Models.Data;
using Auth.Models.DTOs.FLS;
using Auth.Models.Entities.FLS;
using Auth.Models.Exceptions;
using Auth.Models.Request.FLS;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Auth.Models.Entities;

namespace Auth.Services.Services.FLS
{
    public class FLSMeetingService : IFLSMeetingService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<FLSMeetingService> _logger;

        public FLSMeetingService(ApplicationDbContext context, UserManager<User> userManager, ILogger<FLSMeetingService> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<MeetingSlotDto> CreateSlotAsync(CreateMeetingSlotRequest request)
        {
            if (request.EndTime <= request.StartTime)
                throw new ValidationException("End time must be after start time.");

            if (await HasConflictAsync(request.StartTime, request.EndTime))
                throw new ConflictException("A meeting slot already exists that overlaps with the requested time.");

            var slot = new MeetingTimeSlot
            {
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                OrganizerName = request.OrganizerName,
                Notes = request.Notes,
                MeetingLink = request.MeetingLink,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.MeetingTimeSlots.Add(slot);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Meeting slot created: {Start} - {End} for {Organizer}", request.StartTime, request.EndTime, request.OrganizerName);
            return MapToDto(slot);
        }

        public async Task<List<MeetingSlotDto>> GetAvailableSlotsAsync()
        {
            var slots = await _context.MeetingTimeSlots
                .Where(s => !s.IsBooked && s.StartTime >= DateTime.UtcNow)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            return slots.Select(s => MapToDto(s)).ToList();
        }

        public async Task<List<MeetingSlotDto>> GetAllSlotsAsync()
        {
            var slots = await _context.MeetingTimeSlots
                .Include(s => s.BookedBySpeaker)
                    .ThenInclude(sp => sp != null ? sp.User : null)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            var dtos = new List<MeetingSlotDto>();
            foreach (var s in slots)
            {
                var dto = MapToDto(s);
                if (s.BookedBySpeaker?.User != null)
                {
                    var user = await _userManager.FindByIdAsync(s.BookedBySpeaker.UserId);
                    dto.BookedBySpeakerName = user != null ? $"{user.FirstName} {user.LastName}" : null;
                }
                dtos.Add(dto);
            }
            return dtos;
        }

        public async Task<List<MeetingSlotDto>> GetSlotsForSpeakerAsync(int speakerProfileId)
        {
            var slots = await _context.MeetingTimeSlots
                .Where(s => s.BookedBySpeakerProfileId == speakerProfileId)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            return slots.Select(MapToDto).ToList();
        }

        public async Task<MeetingSlotDto> BookSlotAsync(int slotId, int speakerProfileId, BookMeetingSlotRequest request)
        {
            var slot = await _context.MeetingTimeSlots.FindAsync(slotId)
                ?? throw new NotFoundException("Meeting slot not found.");

            if (slot.IsBooked)
                throw new ConflictException("This meeting slot is already booked.");

            // Check if speaker already has a booking in the same time window
            var existingBooking = await _context.MeetingTimeSlots
                .AnyAsync(s => s.BookedBySpeakerProfileId == speakerProfileId
                            && s.StartTime < slot.EndTime
                            && s.EndTime > slot.StartTime
                            && s.Id != slotId);

            if (existingBooking)
                throw new ConflictException("You already have a meeting booked in this time range.");

            slot.IsBooked = true;
            slot.BookedBySpeakerProfileId = speakerProfileId;
            if (!string.IsNullOrWhiteSpace(request.Notes))
                slot.Notes = request.Notes;
            slot.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Slot {SlotId} booked by speaker profile {SpeakerId}", slotId, speakerProfileId);
            return MapToDto(slot);
        }

        public async Task<bool> CancelBookingAsync(int slotId, int speakerProfileId)
        {
            var slot = await _context.MeetingTimeSlots
                .FirstOrDefaultAsync(s => s.Id == slotId && s.BookedBySpeakerProfileId == speakerProfileId)
                ?? throw new NotFoundException("Booking not found.");

            slot.IsBooked = false;
            slot.BookedBySpeakerProfileId = null;
            slot.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteSlotAsync(int slotId)
        {
            var slot = await _context.MeetingTimeSlots.FindAsync(slotId)
                ?? throw new NotFoundException("Meeting slot not found.");

            _context.MeetingTimeSlots.Remove(slot);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> HasConflictAsync(DateTime startTime, DateTime endTime, int? excludeSlotId = null)
        {
            return await _context.MeetingTimeSlots
                .AnyAsync(s => (!excludeSlotId.HasValue || s.Id != excludeSlotId.Value)
                            && s.StartTime < endTime
                            && s.EndTime > startTime);
        }

        private static MeetingSlotDto MapToDto(MeetingTimeSlot s) => new()
        {
            Id = s.Id,
            StartTime = s.StartTime,
            EndTime = s.EndTime,
            OrganizerName = s.OrganizerName,
            IsBooked = s.IsBooked,
            BookedBySpeakerProfileId = s.BookedBySpeakerProfileId,
            Notes = s.Notes,
            MeetingLink = s.MeetingLink,
            CreatedAt = s.CreatedAt
        };
    }
}
