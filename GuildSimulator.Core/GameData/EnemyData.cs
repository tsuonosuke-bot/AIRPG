using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;

namespace GuildSimulator.Core.GameData;

public class EnemyData : IUnitMember
{
    public string id = Guid.NewGuid().ToString("N");
    public EnemyMasterData master;
    public string name;
    public int level;
    public bool isAlive = true;

    bool IUnitMember.IsAlive { get => isAlive; set => isAlive = value; }
    public int Level => level;
    public string Name => name;
    public int CombatHp { get; set; }
    public int CombatHpMax { get; set; }
    public IReadOnlyList<SkillMasterData> Skills => master.Skills;
    public EquipmentMasterData? Weapon => master.DefaultWeapon;
    public EquipmentMasterData? Armor => master.DefaultArmor;

    // 武器を持つ敵は武器ダイス、素手の敵は種族固有の自然攻撃ダイスで殴る。
    public string DamageDice
    {
        get
        {
            var w = master.DefaultWeapon;
            if (w != null && !string.IsNullOrWhiteSpace(w.damageDice)) return w.damageDice;
            return master.naturalDamageDice;
        }
    }

    public bool IsMagicAttack => master.DefaultWeapon != null && master.DefaultWeapon.IsMagicWeapon;

    // 素手の敵は牙・爪そのもののPVで貫く。膂力の乗せ方に上限を設けないのは、
    // 大型の獣がそのまま体重を叩きつけてくる感触を残すため。
    public int WeaponBasePv => master.DefaultWeapon?.basePv ?? master.naturalPv;
    public int MaxStatBonus => master.DefaultWeapon?.maxStatBonus ?? QudCombatDefaults.UnlimitedStatBonus;
    public int AttackStatModifier => QudCombat.Modifier(
        IsMagicAttack ? Scaled(master.intelligence) : Scaled(master.strength));

    public EnemyData(EnemyMasterData master, int level = 1)
    {
        this.master = master;
        this.name = master.baseName;
        this.level = level;
    }

    public const double GROWTH_PER_LEVEL = 0.3; // レベルごとにステータスを30%ずつ伸ばす線形成長（冒険者と同様、複利にしない）

    int Scaled(int baseVal) => (int)Math.Floor(baseVal * (1.0 + GROWTH_PER_LEVEL * (level - 1)));

    // 冒険者と同じ組み立て。敵はレベル成長を通した能力値からAV/DV/命中を出す。
    public StatBlock GetBaseCombatStats()
    {
        int vit = Scaled(master.vitality);
        int men = Scaled(master.mental);
        int agi = Scaled(master.agility);
        int intl = Scaled(master.intelligence);
        int cons = Scaled(master.constitution);
        return new StatBlock
        {
            hp = (vit * 10 + cons * 5) / 2,
            san = men * 10,
            // 防具を着ていない獣でも、甲殻や毛皮のぶんの装甲を持つ。
            av = QudCombat.Modifier(cons) + master.naturalAv,
            mav = QudCombat.Modifier(men) + master.naturalMav,
            dv = QudCombat.BASE_DV + QudCombat.Modifier(agi),
            toHit = QudCombat.Modifier(agi),
            heal = men + intl / 2,
        };
    }

    public StatBlock GetEquipmentBonus()
    {
        StatBlock b = default;
        if (Weapon != null) b += Weapon.bonus;
        if (Armor != null) b += Armor.bonus;
        return b;
    }

    public StatBlock GetFinalCombatStats()
    {
        var s = GetBaseCombatStats() + GetEquipmentBonus();
        float hCoef = Weapon?.healPower ?? 0f;
        s.heal = hCoef > 0f ? (int)Math.Floor((s.heal + (Weapon?.flatHeal ?? 0)) * hCoef) : 0;
        return s;
    }
}
