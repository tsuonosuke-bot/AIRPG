namespace GuildSimulator.Core.MasterData;

public class EnemyMasterData
{
    public string id = "";
    public string baseName = "";
    public int exp;
    public int vitality;
    public int mental;
    public int strength;
    public int agility;
    public int intelligence;
    public int constitution;
    public string defaultWeaponId = "";
    public string defaultArmorId = "";
    public List<string> skillIds = new();

    public EquipmentMasterData? DefaultWeapon { get; set; }
    public EquipmentMasterData? DefaultArmor { get; set; }
    public List<SkillMasterData> Skills { get; set; } = new();
}
