using Auth.Models.DTOs;
using Auth.Models.Entities;

namespace Auth.Services.Interfaces
{
    public interface ISkillService
    {
        Task<IEnumerable<ScholarSkillDto>> GetActiveSkillsAsync(string scholarId);
        Task AddSkillAsync(string scholarId, Skill skill, int slot);
        Task DeactivateSkillAsync(int skillId);
    }
}
