using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class EquipmentMasterData
{
    public string id = "";
    public string displayName = "";
    public EquipmentType type;
    public WeaponType weaponType = WeaponType.Null;
    public ArmorType armorType = ArmorType.Null;

    /// <summary>この武器が撃つもの。物理ならAV、魔法ならmAVと突き合わせる。</summary>
    public AttackKind attackKind = AttackKind.Physical;

    /// <summary>攻撃時に振るダメージダイス（例: "1d6", "2d4+1"）。貫通1回につき1度振る。</summary>
    public string damageDice = "";

    /// <summary>武器そのものの貫通値(PV)。ここに使い手の能力値modifierが上乗せされる。</summary>
    public int basePv = QudCombatDefaults.WeaponPv;

    /// <summary>
    /// PVに上乗せできる能力値modifier（物理は筋力／魔法は知力）の上限。
    /// 短剣や投石のような軽い得物は小さく、斧や大剣は実質無制限にして膂力をそのまま乗せる。
    /// </summary>
    public int maxStatBonus = QudCombatDefaults.UnlimitedStatBonus;

    /// <summary>
    /// 攻撃時に無視する相手の装甲値。槍の「貫通力」。PVを上げるのではなく相手のAVを差し引くので、
    /// 硬い相手ほど恩恵が大きく、素肌の相手には何も起きない。
    /// </summary>
    public int armorPierce;

    /// <summary>
    /// 貫通に成功した攻撃1回につき、相手のAVを恒久的に削る量。斧の「装甲破壊」。
    /// 削れた装甲はその戦闘のあいだ戻らず、味方全員の攻撃が通りやすくなる。
    /// </summary>
    public int armorShred;

    /// <summary>
    /// 会心になる1d20の出目の幅。0なら20のみ、1なら19〜20。短剣の「会心の出やすさ」。
    /// </summary>
    public int critRange;

    /// <summary>
    /// 1手番あたりの追撃回数。短剣の「連続攻撃」。追撃はPVが下がっていくので手数ほどには伸びない。
    /// </summary>
    public int extraAttacks;

    /// <summary>
    /// 左手に持ったときの発動率への加算（%）。短剣の「取り回しの良さ」。
    /// 右手に持っているあいだは使われない。
    /// </summary>
    public int offHandBonus;

    /// <summary>
    /// 両手で構える武器。左手が塞がるので、盾も二刀流も併用できない。
    /// 弓と魔法はすべてこれ。
    /// </summary>
    public bool isTwoHanded;

    /// <summary>盾で受け止められる確率（%）。0なら受けない。</summary>
    public int blockChance;

    /// <summary>
    /// 受けに成功した攻撃にだけ乗る装甲値。
    /// <b><see cref="bonus"/> の av とは別物。</b>bonus は常時加算されるので、
    /// 盾の装甲をそちらに書くと「構えていなくても硬い」ことになってしまう。
    /// </summary>
    public int blockAv;

    /// <summary>回復杖の回復力倍率。0なら回復武器ではない。</summary>
    public float healPower;

    /// <summary>回復量への固定加算。</summary>
    public int flatHeal;

    public int weight;
    public int price;
    public StatBlock bonus;
    public Rarity rarity;

    /// <summary>商店で扱うために必要な品揃えレベル。基準の商店レベル1は常に1のみ扱える。</summary>
    public int shopTier = 1;

    /// <summary>この装備を着けられるスロット一覧。空ならtypeから自動推定する。</summary>
    public List<EquipSlot> allowedSlots = new();

    public bool IsHealWeapon => attackKind == AttackKind.Heal && healPower > 0f;
    public bool IsMagicWeapon => attackKind == AttackKind.Magic;
    public bool IsShield => type == EquipmentType.Shield;

    /// <summary>武器クラスの個性をまとめたもの。スキル由来の補正はここには含まれない。</summary>
    public WeaponTraits Traits =>
        new(armorPierce, armorShred, critRange, extraAttacks, offHandBonus);

    public IReadOnlyList<EquipSlot> GetAllowedSlots()
    {
        if (allowedSlots.Count > 0) return allowedSlots;
        return type switch
        {
            // 両手武器は右手に構える。左手は塞がるだけで、装備先にはならない。
            EquipmentType.Weapon => isTwoHanded
                ? new[] { EquipSlot.RightHand }
                : new[] { EquipSlot.RightHand, EquipSlot.LeftHand },
            EquipmentType.Armor => new[] { EquipSlot.Body },
            EquipmentType.Accessory => new[] { EquipSlot.Accessory },
            EquipmentType.Shield => new[] { EquipSlot.LeftHand },
            _ => new[] { EquipSlot.RightHand },
        };
    }

    public bool CanEquipTo(EquipSlot slot) => GetAllowedSlots().Contains(slot);
}

/// <summary>マスタ未設定時の既定値。Core.Systems.Battle への参照を張らずに済ませるために切り出してある。</summary>
public static class QudCombatDefaults
{
    /// <summary>Qudの標準的な武器のPV。</summary>
    public const int WeaponPv = 4;

    /// <summary>能力値modifierの上限なしを表す値。</summary>
    public const int UnlimitedStatBonus = 99;

    /// <summary>会心になる出目の下限（critRange 0 のとき）。表示用に切り出してある。</summary>
    public const int CriticalRollFloor = 20;
}
