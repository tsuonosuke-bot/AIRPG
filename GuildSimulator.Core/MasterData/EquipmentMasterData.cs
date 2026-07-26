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
    public int weight;
    public int price;
    public StatBlock bonus;
    public Rarity rarity;

    /// <summary>商店で扱うために必要な品揃えレベル。基準の商店レベル1は常に1のみ扱える。</summary>
    public int shopTier = 1;
}
