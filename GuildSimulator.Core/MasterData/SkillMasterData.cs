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

    /// <summary>HP・状態異常・盾の受けなど、戦闘中の出来事を条件に発動する効果。</summary>
    public SkillBattleEffect battle;

    /// <summary>戦闘開始時に付与するバフ・状態効果。</summary>
    public List<CombatStatusApplicationData> battleStartStatuses = new();

    /// <summary>攻撃が命中してダメージを与えたときに付与を試みる状態効果。</summary>
    public List<CombatStatusApplicationData> onHitStatuses = new();
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

    /// <summary>敵遭遇イベントの出やすさへの増減（%）。負の値で遭遇しにくくなる。</summary>
    public int enemyEncounterChancePercent;

    /// <summary>休息イベントの出やすさへの増減（%）。</summary>
    public int healEventChancePercent;

    /// <summary>休息イベントで回復するHPへの増減（%）。</summary>
    public int restHealPercent;

    /// <summary>敵ごとのドロップ抽選率への増減（%）。</summary>
    public int enemyDropChancePercent;

    /// <summary>
    /// 装備ドロップのレアリティ1段階ごとに加える抽選率（%）。
    /// アイテム自体を別レアリティへ変えるのではなく、希少なドロップほど見つけやすくする。
    /// </summary>
    public int rareDropChancePercent;

    /// <summary>
    /// 1ターンに踏み込むエリア数への加算。踏むエリアの総数（<c>totalPhases</c>）は変わらないので、
    /// 道中の取りこぼしはなく、同じ行程をより少ないターンで往復できるようになる。
    /// </summary>
    public int phasesPerTurnBonus;

    public bool IsEmpty =>
        goldPercent == 0 && expPercent == 0
        && treasureChancePercent == 0 && trapChancePercent == 0
        && enemyEncounterChancePercent == 0 && healEventChancePercent == 0
        && restHealPercent == 0 && enemyDropChancePercent == 0
        && rareDropChancePercent == 0 && phasesPerTurnBonus == 0;
}

/// <summary>
/// 戦闘の進行中に条件を判定するスキル効果。
/// 静的な能力補正は <see cref="SkillMasterData.add"/>、開始時・命中時の状態付与は
/// <see cref="SkillMasterData.battleStartStatuses"/> / <see cref="SkillMasterData.onHitStatuses"/> を使う。
/// </summary>
public struct SkillBattleEffect
{
    /// <summary>このHP率（%）以下の味方が狙われたとき、庇護を試みる。0なら発動しない。</summary>
    public int protectAllyHpPercent;

    /// <summary>庇護して攻撃対象を自分へ変える確率（%）。</summary>
    public int protectChancePercent;

    /// <summary>毒・出血・火傷中の攻撃対象に対する物理PV加算。</summary>
    public int afflictedTargetPv;

    /// <summary>回復成功時、対象の有害状態を1つ解除する確率（%）。</summary>
    public int cleanseOnHealChancePercent;

    /// <summary>このHP率（%）以下で背水PVを得る。0なら発動しない。</summary>
    public int lowHpThresholdPercent;

    /// <summary>背水条件を満たしている間の物理PV加算。</summary>
    public int lowHpPv;

    /// <summary>盾でダメージを完全に防いだ直後、通常攻撃で反撃する確率（%）。</summary>
    public int counterChancePercent;

    public bool IsEmpty =>
        protectAllyHpPercent == 0 && protectChancePercent == 0
        && afflictedTargetPv == 0 && cleanseOnHealChancePercent == 0
        && lowHpThresholdPercent == 0 && lowHpPv == 0
        && counterChancePercent == 0;
}
