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
        public int ShortTermHours { get; set; }
        public int LongTermHours { get; set; }
        public int OutsideBHFFHours { get; set; }
        public int WithinBHFFHours { get; set; }
        public int TotalVolunteeringHours { get; set; }
    }
}
