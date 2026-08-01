using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class SkillMasterData
{
    public string id = "";
    public string skillName = "";

    /// <summary>
    /// 同系統スキルの束ね名。空なら単独のスキル。
    /// 同じ family を複数覚えていても効くのは <see cref="level"/> がいちばん大きい1つだけで、
    /// 段階が上がるほど数値は積み上がらず<b>置き換わる</b>。
    /// なので各段階の値は「その段階での最終的な強さ」をそのまま書けばよい。
    /// </summary>
    public string family = "";

    /// <summary>段階（Lv）。同じ family の中で大きいほうが下位を押しのける。</summary>
    public int level;

    public SkillScope scope = SkillScope.Self;
    public bool frontOnly;
    public bool backOnly;
    public bool requireWeaponType;
    public WeaponType requiredWeaponType;
    public bool requireArmorType;
    public ArmorType requiredArmorType;

    /// <summary>右手に何も握っていないときだけ有効。格闘スキルはこれで素手を要求する。</summary>
    public bool requireUnarmed;

    /// <summary>両手で構える武器のときだけ有効。盾も二刀流も捨てた構えへの見返り。</summary>
    public bool requireTwoHanded;

    /// <summary>左手に盾を構えているときだけ有効。</summary>
    public bool requireShield;

    /// <summary>左手に武器を握っているときだけ有効。二刀流スキルはこれを要求する。</summary>
    public bool requireOffHandWeapon;

    /// <summary>
    /// 物理武器を構えているときだけ有効。
    /// 「両手<b>近接</b>武器」のように、両手持ちの杖を巻き込みたくない条件に使う。
    /// </summary>
    public bool requirePhysicalWeapon;

    /// <summary>
    /// 素手のダメージダイスの差し替え。空なら差し替えない。
    /// 複数持っていても最大出目がもっとも大きいものだけが採用される（段階スキルの重ねがけ防止）。
    /// </summary>
    public string unarmedDamageDice = "";

    public StatBlock add;
    public StatMultiplier mul = StatMultiplier.One;

    /// <summary>戦闘の外――遠征そのものに効く補正。</summary>
    public SkillExpeditionEffect expedition;
}

/// <summary>
/// 遠征レベルのスキル効果。戦闘の数値ではなく「持ち帰るもの」と「道中で起きること」に効く。
/// パーティ内の合計で働くので、同じスキルを複数人が持てばそのぶん積み上がる。
/// </summary>
public struct SkillExpeditionEffect
{
    /// <summary>クエスト報酬のゴールドへの増減（%）。</summary>
    public int goldPercent;

    /// <summary>クエスト報酬の経験値への増減（%）。</summary>
    public int expPercent;

    /// <summary>ダンジョンの宝箱イベントの出やすさへの増減（%）。</summary>
    public int treasureChancePercent;

    /// <summary>ダンジョンの罠イベントの出やすさへの増減（%）。負の値で踏みにくくなる。</summary>
    public int trapChancePercent;

    public bool IsEmpty =>
        goldPercent == 0 && expPercent == 0
        && treasureChancePercent == 0 && trapChancePercent == 0;
}
