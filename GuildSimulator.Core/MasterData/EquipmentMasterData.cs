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
}
