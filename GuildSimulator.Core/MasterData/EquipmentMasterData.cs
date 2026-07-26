using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class EquipmentMasterData
{
    public string id = "";
    public string displayName = "";
    public EquipmentType type;
    public WeaponType weaponType = WeaponType.Null;
    public ArmorType armorType = ArmorType.Null;
    public float physicalCoeff = 1f;
    public float magicCoeff = 1f;
    public float healCoeff = 1f;
    public int flatPhysicalAtk;
    public int flatMagicAtk;
    public int flatHeal;

    /// <summary>攻撃時に振るダメージダイス（例: "1d6", "2d4+1"）。未設定なら素手・自然攻撃扱いの既定値を使う。</summary>
    public string damageDice = "";

    /// <summary>
    /// 能力（物攻/魔攻）でダメージを増幅できる上限。0以下なら無制限。
    /// 短剣や投石のような軽い得物は腕力を乗せきれず頭打ちになり、斧や大剣は青天井に伸びる。
    /// </summary>
    public int maxAtkBonus;
    public int weight;
    public int price;
    public StatBlock bonus;
    public Rarity rarity;

    /// <summary>商店で扱うために必要な品揃えレベル。基準の商店レベル1は常に1のみ扱える。</summary>
    public int shopTier = 1;
}
