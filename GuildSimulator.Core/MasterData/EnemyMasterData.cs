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

    /// <summary>素手の敵の牙・爪そのものの貫通値(PV)。武器を持つ敵は武器のbasePvが優先される。</summary>
    public int naturalPv = QudCombatDefaults.WeaponPv;

    /// <summary>甲殻・毛皮など、防具を着ていなくても持っている物理装甲値(AV)。</summary>
    public int naturalAv;

    /// <summary>魔よけの鱗など、防具を着ていなくても持っている魔法装甲値(mAV)。</summary>
    public int naturalMav;
    public List<string> skillIds = new();
    public List<RewardEntryData> dropTable = new();

    public EquipmentMasterData? DefaultWeapon { get; set; }
    public EquipmentMasterData? DefaultArmor { get; set; }
    public List<SkillMasterData> Skills { get; set; } = new();
}
