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
    StatBlock GetBaseCombatStats();
    StatBlock GetFinalCombatStats();
    IReadOnlyList<SkillMasterData> Skills { get; }
    EquipmentMasterData? Weapon { get; }
    EquipmentMasterData? Armor { get; }

    /// <summary>攻撃時に振るダメージダイス。空文字なら素手扱いの既定値が使われる。</summary>
    string DamageDice { get; }

    /// <summary>PVに算入できる主能力（筋力／知力）の上限。0以下なら無制限。</summary>
    int MaxAtkBonus { get; }

    /// <summary>攻撃が魔法属性か。魔攻・魔防で解決するかどうかを決める。</summary>
    bool IsMagicAttack { get; }

    /// <summary>
    /// 貫通値（PV）の素。物理は min(筋力, MaxAtkBonus) + 体格、魔法は min(知力, MaxAtkBonus) + 精神。
    /// 装備・スキル由来の補正はここに含めず、BattleResolverがStatBlockの差分として上乗せする。
    /// </summary>
    int RawPenetration { get; }

    /// <summary>装甲値（AV）の素。物理は体格。</summary>
    int RawPhysicalArmor { get; }

    /// <summary>装甲値（AV）の素。魔法は精神。</summary>
    int RawMagicArmor { get; }

    /// <summary>ダメージ・ボーナスの算出元。物理は筋力+体格、魔法は知力+精神。</summary>
    int DamageBonusBase { get; }
}
