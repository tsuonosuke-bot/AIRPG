using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

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

    public EnemyData(EnemyMasterData master, int level = 1)
    {
        this.master = master;
        this.name = master.baseName;
        this.level = level;
    }

    const double GROWTH_PER_LEVEL = 1.3; // レベルごとにステータスを30%ずつ伸ばす複利成長

    int Scaled(int baseVal) => (int)Math.Floor(baseVal * Math.Pow(GROWTH_PER_LEVEL, level - 1));

    public StatBlock GetBaseCombatStats()
    {
        int vit = Scaled(master.vitality);
        int men = Scaled(master.mental);
        int str = Scaled(master.strength);
        int agi = Scaled(master.agility);
        int intl = Scaled(master.intelligence);
        int cons = Scaled(master.constitution);
        return new StatBlock
        {
            hp = vit * 10 + cons * 5,
            san = men * 10,
            pAtk = str * 2 + cons / 2,
            pDef = cons,
            mAtk = intl * 2,
            mDef = men * 2,
            hit = agi,
            evade = agi - cons / 2,
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
        float pCoef = Weapon != null ? Weapon.physicalCoeff : 1f;
        float mCoef = Weapon != null ? Weapon.magicCoeff : 0f;
        s.pAtk = (int)Math.Floor(s.pAtk * pCoef);
        s.mAtk = (int)Math.Floor(s.mAtk * mCoef);
        if (pCoef <= 0f) s.pAtk = 0;
        if (mCoef <= 0f) s.mAtk = 0;
        return s;
    }
}
