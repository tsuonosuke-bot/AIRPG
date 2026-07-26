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

    /// <summary>
    /// 武器を持たない敵の牙・爪・体当たりのダメージダイス。未設定なら既定値。
    /// ダメージの基礎値は武器ダイスなので、素手の敵はここで打撃力を表現する。
    /// </summary>
    public string naturalDamageDice = "";
    public List<string> skillIds = new();
    public List<RewardEntryData> dropTable = new();

    public EquipmentMasterData? DefaultWeapon { get; set; }
    public EquipmentMasterData? DefaultArmor { get; set; }
    public List<SkillMasterData> Skills { get; set; } = new();
}
