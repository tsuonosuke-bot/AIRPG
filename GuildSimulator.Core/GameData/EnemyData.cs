using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;

namespace GuildSimulator.Core.GameData;

public class EnemyData : IUnitMember
{
    public string id = Guid.NewGuid().ToString("N");
    public EnemyMasterData master;
    public string name;
    public bool isAlive = true;

    bool IUnitMember.IsAlive { get => isAlive; set => isAlive = value; }

    /// <summary>脅威度（F〜S）。マスタに手で書いた値をそのまま使う。</summary>
    public int Threat => Models.Rank.Clamp(master.threat);
    public string Name => name;
    public int CombatHp { get; set; }
    public int CombatHpMax { get; set; }
    // 冒険者と同じく、同系統の段階スキルは最上位だけが効く。
    // マスタに Lv1 と Lv3 を並べて書いても二重には乗らない。
    public IReadOnlyList<SkillMasterData> Skills => skills ??= SkillProgression.Collapse(master.Skills);
    IReadOnlyList<SkillMasterData>? skills;
    public EquipmentMasterData? Weapon => master.DefaultWeapon;
    public EquipmentMasterData? Armor => master.DefaultArmor;

    // 両手武器を構えている敵の左手は塞がっている。冒険者側と同じ制約をかける。
    bool HasFreeOffHand => master.DefaultWeapon is not { isTwoHanded: true };
    public EquipmentMasterData? OffHandWeapon =>
        HasFreeOffHand && master.DefaultOffHand is { type: EquipmentType.Weapon } w ? w : null;
    public EquipmentMasterData? Shield =>
        HasFreeOffHand && master.DefaultShield is { } s && s.IsShield ? s : null;

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
        IsMagicAttack ? master.intelligence : master.strength);

    // 牙や爪には武器クラスの個性がない。槍持ちのゴブリンなら、その槍の貫通力をそのまま使う。
    public WeaponTraits Traits => master.DefaultWeapon?.Traits ?? WeaponTraits.None;

    public EnemyData(EnemyMasterData master)
    {
        this.master = master;
        this.name = master.baseName;
    }

    // 冒険者と同じ組み立て。敵はマスタに書かれた能力値をそのまま使う。
    // レベル倍率で一律に伸ばしていた頃は「硬いが弱い」「脆いが痛い」が作れなかったので、
    // 強弱は個体を別々に用意して表す。
    public StatBlock GetBaseCombatStats()
    {
        int vit = master.vitality;
        int men = master.mental;
        int agi = master.agility;
        int intl = master.intelligence;
        int cons = master.constitution;
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

    // 左手の武器の補正は乗せない（冒険者側と同じ取り決め）。盾は防具なので乗せるが、
    // 装甲だけは受けに成功したときにしか効かないので blockAv 側に置いてある。
    public StatBlock GetEquipmentBonus()
    {
        StatBlock b = default;
        if (Weapon != null) b += Weapon.bonus;
        if (Armor != null) b += Armor.bonus;
        if (Shield != null) b += Shield.bonus;
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
