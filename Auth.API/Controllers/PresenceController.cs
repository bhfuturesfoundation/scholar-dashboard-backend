using Auth.Services.Interfaces.Notifications;
using Auth.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    /// <summary>
    /// Who is on the app right now.
    ///
    /// The live path is the notifications hub; this exists so the indicator has something
    /// to render before the socket finishes negotiating, and so it still shows a number at
    /// all on a network where websockets are blocked.
    /// </summary>
    [Route("api/presence")]
    [ApiController]
    [Authorize]
    public class PresenceController : ControllerBase
    {
        private readonly IPresenceTracker _presence;

        public PresenceController(IPresenceTracker presence)
        {
            _presence = presence;
        }

        [HttpGet]
        public ActionResult<ApiResponse<PresenceSnapshotDto>> GetOnline() =>
            Ok(ApiResponse<PresenceSnapshotDto>.SuccessResponse(
                new PresenceSnapshotDto
                {
                    Users = _presence.GetOnline().ToList(),
                    Count = _presence.OnlineCount,
                },
                "Presence retrieved"));
    }

    public class PresenceSnapshotDto
    {
        public List<PresenceUser> Users { get; set; } = new();
        public int Count { get; set; }
    }
}
