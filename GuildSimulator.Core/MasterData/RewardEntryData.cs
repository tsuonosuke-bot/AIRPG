using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class RewardEntryData
{
    public RewardType type;
    public string relicId = "";
    public string equipmentId = "";
    public string skillId = "";
    public string consumableId = "";
    public int gold;
    public int weight = 10;
    public float chance;
    public int quantity = 1;
    public bool unique = true;

    public RelicMasterData? Relic { get; set; }
    public EquipmentMasterData? Equipment { get; set; }
    public SkillMasterData? Skill { get; set; }
    public ConsumableMasterData? Consumable { get; set; }

    /// <summary>抽選で当たったマスタ側のエントリを戦利品として切り出す。</summary>
    public RewardEntryData Copy() => new()
    {
        type = type,
        relicId = relicId, equipmentId = equipmentId, skillId = skillId, consumableId = consumableId,
        gold = gold, weight = weight, chance = chance,
        quantity = Math.Max(1, quantity), unique = unique,
        Relic = Relic, Equipment = Equipment, Skill = Skill, Consumable = Consumable,
    };
}
