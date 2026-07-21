using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

public interface IUnitMember
{
    bool IsAlive { get; set; }
    int Level { get; }
    string Name { get; }
    int CombatHp { get; set; }
    int CombatHpMax { get; set; }
    StatBlock GetFinalCombatStats();
    IReadOnlyList<SkillMasterData> Skills { get; }
    EquipmentMasterData? Weapon { get; }
    EquipmentMasterData? Armor { get; }
}
