using Auth.Models.DTOs;
using Auth.Models.Entities;

namespace Auth.Services.Interfaces
{
    public interface ISkillService
    {
        Task<IEnumerable<ScholarSkillDto>> GetSkillsAsync(string scholarId);
        Task<ScholarSkillDto> AddOrUpdateSkillAsync(string scholarId, int slot, string skillAnswer);
        Task ToggleSkillStatusAsync(int skillId);
    }
}
