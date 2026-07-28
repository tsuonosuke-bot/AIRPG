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

    /// <summary>能力値だけから決まる素の戦闘値。AV/DV/命中補正はここに入る。</summary>
    StatBlock GetBaseCombatStats();

    /// <summary>素の値に装備・スキル由来の補正まで乗せた最終値。</summary>
    StatBlock GetFinalCombatStats();

    IReadOnlyList<SkillMasterData> Skills { get; }
    EquipmentMasterData? Weapon { get; }
    EquipmentMasterData? Armor { get; }

    /// <summary>攻撃時に振るダメージダイス。貫通1回につき1度振る。</summary>
    string DamageDice { get; }

    /// <summary>攻撃が魔法属性か。AVとmAVのどちらと突き合わせるかを決める。</summary>
    bool IsMagicAttack { get; }

    /// <summary>武器そのものの貫通値(PV)。素手なら牙・爪の値。</summary>
    int WeaponBasePv { get; }

    /// <summary>PVに上乗せできる能力値modifierの上限。</summary>
    int MaxStatBonus { get; }

    /// <summary>PVに乗る能力値modifier（物理は筋力、魔法は知力）。上限の適用前の値。</summary>
    int AttackStatModifier { get; }
}
