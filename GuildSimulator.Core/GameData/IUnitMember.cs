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

    /// <summary>攻撃時に振るダメージダイス。空文字なら素手扱いの既定値が使われる。</summary>
    string DamageDice { get; }

    /// <summary>能力による増幅の上限。0以下なら無制限。</summary>
    int MaxAtkBonus { get; }

    /// <summary>攻撃が魔法属性か。魔攻・魔防で解決するかどうかを決める。</summary>
    bool IsMagicAttack { get; }
}
