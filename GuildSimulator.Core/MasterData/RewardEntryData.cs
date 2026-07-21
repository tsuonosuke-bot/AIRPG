using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class RewardEntryData
{
    public RewardType type;
    public string relicId = "";
    public string equipmentId = "";
    public string skillId = "";
    public int gold;
    public int weight = 10;
    public bool unique = true;

    public RelicMasterData? Relic { get; set; }
    public EquipmentMasterData? Equipment { get; set; }
    public SkillMasterData? Skill { get; set; }
}
