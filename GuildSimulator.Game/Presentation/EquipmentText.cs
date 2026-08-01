using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Game.Presentation;

/// <summary>
/// 装備の性能を1行で説明する文言。商店・持ち物・冒険者の3画面が同じ書式で出せるようにここへ集めてある。
/// </summary>
public static class EquipmentText
{
    public static string WeaponClassName(WeaponType type) => type switch
    {
        WeaponType.Sword => "剣",
        WeaponType.Dagger => "短剣",
        WeaponType.Spear => "槍",
        WeaponType.Axe => "斧",
        WeaponType.Bow => "弓",
        WeaponType.Fire => "火の杖",
        WeaponType.Wind => "風の杖",
        WeaponType.Water => "水の杖",
        WeaponType.Earth => "土の杖",
        WeaponType.Dark => "闇の杖",
        WeaponType.Light => "光の杖",
        _ => "",
    };

    /// <summary>この武器クラスにしかできないこと。持っていない性能は表示しない。</summary>
    public static List<string> TraitParts(WeaponTraits traits)
    {
        var parts = new List<string>();
        if (traits.extraAttacks > 0) parts.Add($"連撃+{traits.extraAttacks}");
        if (traits.critRange > 0) parts.Add($"会心{QudCombatDefaults.CriticalRollFloor - traits.critRange}〜");
        if (traits.armorPierce > 0) parts.Add($"装甲貫通{traits.armorPierce}");
        if (traits.armorShred > 0) parts.Add($"装甲破壊{traits.armorShred}");
        if (traits.offHandBonus > 0) parts.Add($"左手+{traits.offHandBonus}%");
        return parts;
    }

    /// <summary>盾の受け性能。装甲は受けに成功した攻撃にだけ乗るので、そう読めるように書く。</summary>
    public static List<string> ShieldParts(EquipmentMasterData item)
    {
        var parts = new List<string>();
        if (!item.IsShield) return parts;
        parts.Add("[盾]");
        parts.Add($"受け{item.blockChance}%");
        parts.Add($"受け成功時AV+{item.blockAv}");
        return parts;
    }

    /// <summary>武器の攻撃性能。防具・装飾品では空になる（盾は <see cref="ShieldParts"/>）。</summary>
    public static List<string> WeaponParts(EquipmentMasterData item)
    {
        if (item.IsShield) return ShieldParts(item);

        var parts = new List<string>();
        if (item.type != EquipmentType.Weapon) return parts;

        string cls = WeaponClassName(item.weaponType);
        if (cls.Length > 0) parts.Add(item.isTwoHanded ? $"[両手{cls}]" : $"[{cls}]");

        if (item.attackKind == AttackKind.Heal)
        {
            parts.Add($"回復効果x{item.healPower:0.##}");
            return parts;
        }

        parts.Add($"{(item.attackKind == AttackKind.Magic ? "魔法" : "物理")} PV{item.basePv}");
        if (!string.IsNullOrWhiteSpace(item.damageDice)) parts.Add($"{item.damageDice}/貫通");
        parts.Add(item.maxStatBonus >= QudCombatDefaults.UnlimitedStatBonus
            ? "能力値上限なし" : $"能力値上限+{item.maxStatBonus}");
        parts.AddRange(TraitParts(item.Traits));
        return parts;
    }

    /// <summary>装備が持つステータス補正。0の項目は出さない。</summary>
    public static List<string> BonusParts(StatBlock b)
    {
        var parts = new List<string>();
        void Add(string name, int v) { if (v != 0) parts.Add($"{name}{(v > 0 ? "+" : "")}{v}"); }
        Add("HP", b.hp);
        Add("AV", b.av);
        Add("mAV", b.mav);
        Add("PV", b.pv);
        Add("mPV", b.mpv);
        Add("DV", b.dv);
        Add("命中", b.toHit);
        Add("回復力", b.heal);
        Add("装甲貫通", b.armorPierce);
        Add("装甲破壊", b.armorShred);
        Add("会心域", b.critRange);
        Add("連撃", b.extraAttacks);
        Add("左手発動%", b.offHandChance);
        Add("受け%", b.blockChance);
        return parts;
    }
}
