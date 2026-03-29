using Auth.Models.Enums.FLS;

namespace Auth.Models.Request.FLS
{
    public class UpdateSpeakerProfileRequest
    {
        public string? Organization { get; set; }
        public string? Biography { get; set; }
        public SpeakerType? SpeakerType { get; set; }
    }

    public class UpdateSpeakerAccommodationRequest
    {
        public bool AccommodationBooked { get; set; }
        public bool FlightTicketBooked { get; set; }
    }

    public class DeregisterSpeakerRequest
    {
        public string? Reason { get; set; }
    }

    public class SetModificationDeadlineRequest
    {
        public DateTime? ModificationDeadline { get; set; }
    }
}
