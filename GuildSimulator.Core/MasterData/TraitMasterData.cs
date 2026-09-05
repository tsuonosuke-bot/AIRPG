using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

/// <summary>
/// 特性を評価するときの「担い手の型」。
///
/// 戦闘の計算は物理と魔法で経路が分かれていて、<c>pv</c> は魔法攻撃に乗らず、
/// <c>mpv</c> は物理攻撃に乗らない。回復役はそもそも攻撃判定を通らない。
/// つまり<b>同じ数値でも、誰が持つかで意味がまるで変わる</b>。
/// 「利点があるか」「代償を払っているか」はこの型ごとに判定しないと嘘になる。
/// </summary>
public enum TraitLens
{
    /// <summary>物理武器で殴る者（素手を含む）。盾を構えられるのもこの型だけ。</summary>
    Physical = 0,

    /// <summary>魔法で撃つ者。魔法・回復武器はすべて両手なので盾は持てない。</summary>
    Magic = 1,

    /// <summary>回復杖を構える者。攻撃判定そのものを通らない。</summary>
    Heal = 2,
}

/// <summary>特性が開花するために必要な、遠征記録の到達量。</summary>
public class TraitRequirementData
{
    public ExpeditionRecordType record;
    public int atLeast = 1;
}

/// <summary>
/// 特性 —— レベルアップの乱数ではなく、<b>その冒険者が実際にどう戦ってきたか</b>から生える恒久的な変化。
///
/// 効果そのものは <c>skills.json</c> のスキルに書き、ここは「どの記録がいくつ溜まったら開花するか」と
/// 見せ方だけを持つ。こうすると <see cref="AdventurerData.LearnPermanentSkill"/> にそのまま乗るので、
/// 戦闘への反映もセーブ／ロードも既存の経路で動く。
///
/// <para>
/// 設計の芯は「<b>代償は先払い</b>」。特性は原則として利点と欠点を併せ持つ諸刃であり、
/// 欠点のない素直な強化が欲しければ、リスク記録（瀕死・戦闘不能・仲間の死線・全滅・喪失）を
/// 条件に含めていなければならない。
/// </para>
/// <para>
/// この規則は<b>担い手の型ごとに</b>成り立っていなければならない。物理前提で書いた欠点は
/// 術者にはタダになり（＝リスクなしの純粋強化）、物理前提の利点は術者には消える（＝罰則だけ）。
/// <see cref="builds"/> で「誰に出す特性か」を宣言し、<c>MasterValidator</c> が
/// 宣言した型それぞれについて規則を検査する。
/// </para>
/// </summary>
public class TraitMasterData
{
    public string id = "";
    public string traitName = "";

    /// <summary>効果の実体。<c>skills.json</c> のスキルID。</summary>
    public string skillId = "";
    public SkillMasterData? Skill;

    /// <summary>
    /// 同じ機会にまとめて提示する選択肢のグループ。空なら単独候補。
    /// グループは候補上限をまたいで分割せず、1つを選べば同時に提示済みになる。
    /// </summary>
    public string offerGroup = "";

    /// <summary>選択肢に並べる効果の要約。</summary>
    public string description = "";

    /// <summary>開花したことを告げる一文。「〜は死線を越えた」のように書く。</summary>
    public string awakenText = "";

    /// <summary>習得したときに冒険者の記録へ残す一文。</summary>
    public string flavorText = "";

    public List<TraitRequirementData> requirements = new();

    /// <summary>
    /// この特性を提示する担い手の型。空なら全型（＝どの型で見ても意味が通ることを求める）。
    /// </summary>
    public List<TraitLens> builds = new();

    public IReadOnlyList<TraitLens> Builds =>
        builds.Count > 0 ? builds : TraitAnalysis.AllLenses;

    /// <summary>解禁条件にリスク記録を含むか。</summary>
    public bool RequiresRisk =>
        requirements.Any(r => ExpeditionRecordTypes.IsRisk(r.record));

    /// <summary>
    /// この特性が背負う欠点（型を問わず、書かれている数値そのもの）。
    /// 一覧表示にはこちらを使う。規則の検査には型ごとの
    /// <see cref="TraitAnalysis.Evaluate"/> を使う。
    /// </summary>
    public IReadOnlyList<string> Drawbacks =>
        Skill == null ? Array.Empty<string>() : TraitAnalysis.Evaluate(Skill, null).Drawbacks;

    public bool IsPureUpgrade => Drawbacks.Count == 0;

    /// <summary>記録が条件を満たしているか。条件が空の特性は決して開花しない。</summary>
    public bool IsMetBy(ExpeditionRecord record) =>
        requirements.Count > 0
        && requirements.All(r => record[r.record] >= Math.Max(1, r.atLeast));

    /// <summary>
    /// 条件のうち、開花のきっかけとして読ませるにふさわしい記録。
    /// リスク記録があればそれを優先する（そちらのほうが物語として重い）。
    /// </summary>
    public TraitRequirementData? HeadlineRequirement =>
        requirements.FirstOrDefault(r => ExpeditionRecordTypes.IsRisk(r.record))
        ?? requirements.FirstOrDefault();
}

/// <summary>この型にとって、その特性が何をもたらし何を奪うか。</summary>
public readonly record struct TraitEffect(
    IReadOnlyList<string> Benefits,
    IReadOnlyList<string> Drawbacks);

/// <summary>
/// スキルの数値を読んで「担い手にとって得か損か」を判定する。
/// 特性が諸刃かどうかを人手のフラグではなくデータそのものから導くことで、
/// 「欠点を書き忘れたまま純粋強化になっていた」というずれを起こさないようにする。
/// </summary>
public static class TraitAnalysis
{
    public static readonly IReadOnlyList<TraitLens> AllLenses =
        new[] { TraitLens.Physical, TraitLens.Magic, TraitLens.Heal };

    public static string LensName(TraitLens lens) => lens switch
    {
        TraitLens.Physical => "物理",
        TraitLens.Magic => "魔法",
        TraitLens.Heal => "回復",
        _ => lens.ToString(),
    };

    // ---- どの数値がどの型で生きているか ----
    // ここが本体。戦闘コードの分岐（BattleResolver / QudCombat / UnitCalculator）を写したものなので、
    // 戦闘側の経路を変えたらここも直す。

    /// <summary>物理攻撃にしか乗らない。魔法は mpv を見るし、回復役は攻撃判定を通らない。</summary>
    static bool PhysicalAttackLive(TraitLens? lens) => lens is null or TraitLens.Physical;

    /// <summary>魔法攻撃にしか乗らない。</summary>
    static bool MagicAttackLive(TraitLens? lens) => lens is null or TraitLens.Magic;

    /// <summary>攻撃する型なら物理・魔法どちらでも乗る。回復役は殴らないので死ぬ。</summary>
    static bool AnyAttackLive(TraitLens? lens) => lens is not TraitLens.Heal;

    /// <summary>回復行動でしか使われない。</summary>
    static bool HealLive(TraitLens? lens) => lens is null or TraitLens.Heal;

    /// <summary>盾を構えられるのは物理型だけ（魔法・回復武器はすべて両手）。</summary>
    static bool ShieldLive(TraitLens? lens) => lens is null or TraitLens.Physical;

    /// <summary>
    /// 指定した型から見た利点と欠点を数え上げる。
    /// <paramref name="lens"/> に null を渡すと型を問わず、書かれている数値をそのまま読む。
    /// </summary>
    public static TraitEffect Evaluate(SkillMasterData skill, TraitLens? lens)
    {
        var benefits = new List<string>();
        var drawbacks = new List<string>();

        // 大きいほど担い手に有利な項目。
        void Higher(int value, string label, bool live)
        {
            if (value == 0 || !live) return;
            (value > 0 ? benefits : drawbacks).Add($"{label}{value:+#;-#;0}");
        }

        // 大きいほど担い手に不利な項目（狙われやすさ・罠や敵との遭遇率）。
        void Lower(int value, string label, bool live, string unit = "")
        {
            if (value == 0 || !live) return;
            (value < 0 ? benefits : drawbacks).Add($"{label}{value:+#;-#;0}{unit}");
        }

        void Rate(float value, string label, bool live)
        {
            if (Math.Abs(value - 1f) < 0.0001f || !live) return;
            (value > 1f ? benefits : drawbacks).Add($"{label}×{value:0.##}");
        }

        var add = skill.add;
        bool anyAttack = AnyAttackLive(lens);
        bool physical = PhysicalAttackLive(lens);
        bool magic = MagicAttackLive(lens);
        bool shield = ShieldLive(lens);
        bool heal = HealLive(lens);

        // 常に効く（守りと素の量）。
        Higher(add.hp, "HP", true);
        Higher(add.san, "士気", true);
        Higher(add.av, "AV", true);
        Higher(add.mav, "mAV", true);
        Higher(add.dv, "DV", true);
        Higher(add.carry, "積載", true);
        Higher(add.emergencyHeal, "応急処置", true);
        Lower(add.threatWeight, "狙われやすさ", true);
        Lower(add.incomingCritChancePercent, "被会心率", true, "%");

        // 攻撃する型だけ。
        Higher(add.toHit, "命中", anyAttack);
        Higher(add.critRange, "会心域", anyAttack);
        Higher(add.critPv, "会心PV", anyAttack);
        Higher(add.autoPenetrate, "急所突き", anyAttack);
        Higher(add.extraAttacks, "連撃", anyAttack);
        Higher(add.offHandChance, "左手発動率", anyAttack);

        // 物理攻撃だけ。
        Higher(add.pv, "PV", physical);
        Higher(add.armorPierce, "装甲貫通", physical);
        Higher(add.armorShred, "装甲破壊", physical);
        Higher(skill.battle.lowHpPv, "背水PV", physical);
        Higher(skill.battle.afflictedTargetPv, "弱った敵へのPV", physical);

        // 魔法攻撃だけ。
        Higher(add.mpv, "mPV", magic);

        // 盾を構えられる型だけ。
        Higher(add.blockChance, "盾の受け", shield);
        Higher(add.blockNegate, "完全受け", shield);
        Higher(skill.battle.counterChancePercent, "反撃率", shield);

        // 回復役だけ。
        Higher(add.heal, "回復量", heal);
        Higher(skill.battle.cleanseOnHealChancePercent, "浄化率", heal);
        Higher(skill.battle.moraleOnHealPercent, "回復時士気", heal);
        Rate(skill.mul.heal, "回復量", heal);

        // 庇護は攻撃でも回復でもなく、狙われ先の付け替えなのでどの型でも働く。
        Higher(skill.battle.protectChancePercent, "庇護率", true);

        Rate(skill.mul.hp, "最大HP", true);
        Rate(skill.mul.san, "士気", true);

        var expedition = skill.expedition;
        Higher(expedition.goldPercent, "報酬金", true);
        Higher(expedition.expPercent, "経験値", true);
        Higher(expedition.treasureChancePercent, "宝箱発見率", true);
        Higher(expedition.healEventChancePercent, "休息遭遇率", true);
        Higher(expedition.restHealPercent, "休息回復量", true);
        Higher(expedition.enemyDropChancePercent, "ドロップ率", true);
        Higher(expedition.rareDropChancePercent, "レアドロップ率", true);
        Higher(expedition.phasesPerTurnBonus, "行軍速度", true);
        Lower(expedition.trapChancePercent, "罠遭遇率", true, "%");
        Lower(expedition.enemyEncounterChancePercent, "敵遭遇率", true, "%");

        return new TraitEffect(benefits, drawbacks);
    }
}
