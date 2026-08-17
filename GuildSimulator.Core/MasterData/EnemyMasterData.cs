namespace GuildSimulator.Core.MasterData;

public class EnemyMasterData
{
    public string id = "";
    public string baseName = "";
    /// <summary>モンスター図鑑に表示する生態・外見の説明。</summary>
    public string description = "";
    public int exp;

    /// <summary>
    /// 脅威度。冒険者ランクと同じ F〜S の物差し（1〜7）に乗せる。
    /// レベル倍率で強さを表すのをやめたので、強弱は個体を別々に用意して表す
    /// （はぐれゴブリン→ゴブリン→ゴブリン兵士→ゴブリン隊長）。
    /// この値は能力値には一切影響せず、士気の格上ショックと難易度表示にだけ使う。
    /// </summary>
    public int threat = Models.Rank.Min;
    public int vitality;
    public int mental;
    public int strength;
    public int agility;
    public int intelligence;
    public int constitution;
    public string defaultWeaponId = "";
    public string defaultArmorId = "";

    /// <summary>左手に握らせる武器。二刀流の敵を作れる。両手武器を持たせているときは無視される。</summary>
    public string defaultOffHandId = "";

    /// <summary>左手に構えさせる盾。重装兵のように受けてくる敵を作れる。</summary>
    public string defaultShieldId = "";

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

    /// <summary>
    /// 牙や爪そのものの攻撃種別。武器を持たせている敵では武器側の attackKind が優先される。
    /// メイジルプスのように<b>素手のまま魔法で殴る</b>個体を作るために使う。
    /// Magic にすると命中後の判定が相手のmAVとこちらのmPVで行われ、貫通値に乗る能力値もINTに変わる。
    /// </summary>
    public Models.AttackKind naturalAttackKind = Models.AttackKind.Physical;
    public List<string> skillIds = new();
    public List<RewardEntryData> dropTable = new();

    public EquipmentMasterData? DefaultWeapon { get; set; }
    public EquipmentMasterData? DefaultArmor { get; set; }
    public EquipmentMasterData? DefaultOffHand { get; set; }
    public EquipmentMasterData? DefaultShield { get; set; }
    public List<SkillMasterData> Skills { get; set; } = new();
}
