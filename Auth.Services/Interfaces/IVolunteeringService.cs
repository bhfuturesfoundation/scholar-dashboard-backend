using Auth.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Auth.Services.Interfaces
{
    public interface IVolunteeringService
    {
        Task<IEnumerable<VolunteerDto>> GetTopVolunteersAsync();
        Task<IEnumerable<VolunteerDto>> GetAllVolunteersAsync();
        Task<IEnumerable<VolunteerWithTeamDto>> GetVolunteersWithTeamLeadersAsync();
    }
}
