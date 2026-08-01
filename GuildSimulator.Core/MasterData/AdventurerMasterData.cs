using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class AdventurerMasterData
{
    public Rarity rarity;
    public string id = "";
    public string baseName = "";
    public int defaultLevel = 1;
    public int defaultRank = 1;
    public int recruitGuildRank = 1;
    public int recruitWeight = 100;
    public int vitality;
    public int mental;
    public int strength;
    public int agility;
    public int intelligence;
    public int constitution;
    public int appearance;
    public Gender gender = Gender.Unspecified;
    public string defaultClassId = "";
    public string raceId = "";
    public string defaultWeaponId = "";
    public string defaultArmorId = "";
    public List<string> skillIds = new();

    // 人物として採用・編成を考えるためのプロフィール。
    public string background = "";

    public ClassMasterData? DefaultClass { get; set; }
    public RaceMasterData? Race { get; set; }
    public EquipmentMasterData? DefaultWeapon { get; set; }
    public EquipmentMasterData? DefaultArmor { get; set; }
    public List<SkillMasterData> Skills { get; set; } = new();
}
