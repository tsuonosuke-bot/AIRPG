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

    public IReadOnlyList<EquipSlot> GetAllowedSlots()
    {
        if (allowedSlots.Count > 0) return allowedSlots;
        return type switch
        {
            EquipmentType.Weapon => new[] { EquipSlot.RightHand, EquipSlot.LeftHand },
            EquipmentType.Armor => new[] { EquipSlot.Body },
            EquipmentType.Accessory => new[] { EquipSlot.Accessory },
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
}
