using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

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
/// 欠点のない素直な強化が欲しければ、リスク記録（瀕死・戦闘不能・仲間の死線）を
/// 条件に含めていなければならない。この規則は <c>MasterValidator</c> が検査する。
/// </para>
/// </summary>
public class TraitMasterData
{
    public string id = "";
    public string traitName = "";

    /// <summary>効果の実体。<c>skills.json</c> のスキルID。</summary>
    public string skillId = "";
    public SkillMasterData? Skill;

    /// <summary>選択肢に並べる効果の要約。</summary>
    public string description = "";

    /// <summary>開花したことを告げる一文。「〜は死線を越えた」のように書く。</summary>
    public string awakenText = "";

    /// <summary>習得したときに冒険者の記録へ残す一文。</summary>
    public string flavorText = "";

    public List<TraitRequirementData> requirements = new();

    /// <summary>解禁条件にリスク記録を含むか。</summary>
    public bool RequiresRisk =>
        requirements.Any(r => ExpeditionRecordTypes.IsRisk(r.record));

    /// <summary>この特性が背負う欠点。空なら純粋強化。</summary>
    public IReadOnlyList<string> Drawbacks =>
        Skill == null ? Array.Empty<string>() : TraitAnalysis.DescribeDrawbacks(Skill);

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

/// <summary>
/// スキルの数値を読んで「これは担い手にとって不利か」を判定する。
/// 特性が諸刃かどうかを人手のフラグではなくデータそのものから導くことで、
/// 「欠点を書き忘れたまま純粋強化になっていた」というずれを起こさないようにする。
/// </summary>
public static class TraitAnalysis
{
    public static IReadOnlyList<string> DescribeDrawbacks(SkillMasterData skill)
    {
        var drawbacks = new List<string>();

        void Penalty(int value, string label)
        {
            if (value < 0) drawbacks.Add($"{label}{value}");
        }

        var add = skill.add;
        Penalty(add.hp, "HP");
        Penalty(add.san, "士気");
        Penalty(add.av, "AV");
        Penalty(add.mav, "mAV");
        Penalty(add.pv, "PV");
        Penalty(add.mpv, "mPV");
        Penalty(add.dv, "DV");
        Penalty(add.toHit, "命中");
        Penalty(add.heal, "回復量");
        Penalty(add.armorPierce, "装甲貫通");
        Penalty(add.armorShred, "装甲破壊");
        Penalty(add.critRange, "会心域");
        Penalty(add.extraAttacks, "連撃");
        Penalty(add.offHandChance, "左手発動率");
        Penalty(add.blockChance, "盾の受け");
        Penalty(add.blockNegate, "完全受け");
        Penalty(add.carry, "積載");
        Penalty(add.autoPenetrate, "急所突き");
        Penalty(add.critPv, "会心PV");
        Penalty(add.emergencyHeal, "応急処置");

        // 狙われやすさだけは向きが逆。+ は敵を引きつけるので、担い手にとっては危険が増える。
        if (add.threatWeight > 0)
            drawbacks.Add($"狙われやすさ+{add.threatWeight}");

        if (skill.mul.hp < 1f) drawbacks.Add($"最大HP×{skill.mul.hp:0.##}");
        if (skill.mul.san < 1f) drawbacks.Add($"士気×{skill.mul.san:0.##}");
        if (skill.mul.heal < 1f) drawbacks.Add($"回復量×{skill.mul.heal:0.##}");

        Penalty(skill.battle.afflictedTargetPv, "弱った敵へのPV");
        Penalty(skill.battle.protectChancePercent, "庇護率");
        Penalty(skill.battle.cleanseOnHealChancePercent, "浄化率");
        Penalty(skill.battle.lowHpPv, "背水PV");
        Penalty(skill.battle.counterChancePercent, "反撃率");

        Penalty(skill.expedition.goldPercent, "報酬金");
        Penalty(skill.expedition.expPercent, "経験値");
        Penalty(skill.expedition.treasureChancePercent, "宝箱発見率");
        Penalty(skill.expedition.healEventChancePercent, "休息遭遇率");
        Penalty(skill.expedition.restHealPercent, "休息回復量");
        Penalty(skill.expedition.enemyDropChancePercent, "ドロップ率");
        Penalty(skill.expedition.rareDropChancePercent, "レアドロップ率");

        // 罠と敵遭遇も向きが逆。踏みやすく・出会いやすくなるのは不利。
        if (skill.expedition.trapChancePercent > 0)
            drawbacks.Add($"罠遭遇率+{skill.expedition.trapChancePercent}%");
        if (skill.expedition.enemyEncounterChancePercent > 0)
            drawbacks.Add($"敵遭遇率+{skill.expedition.enemyEncounterChancePercent}%");

        return drawbacks;
    }
}
