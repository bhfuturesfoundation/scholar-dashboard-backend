using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Auth.Models.DTOs
{
    public class VolunteerDto
    {
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string MemberStatus { get; set; } = string.Empty;
        public decimal ShortTermHours { get; set; }     
        public decimal LongTermHours { get; set; }       
        public decimal OutsideBHFFHours { get; set; }    
        public decimal WithinBHFFHours { get; set; }     
        public decimal TotalVolunteeringHours { get; set; } 
    }

    public class VolunteerWithTeamDto
    {
        public string Name { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string Team { get; set; } = string.Empty;
        public string PlacementType { get; set; } = string.Empty;
        public string TeamLeader { get; set; } = string.Empty;
    }
}
