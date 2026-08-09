using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.Systems;

/// <summary>
/// クエスト完了時に、隊員1人へ提示する特性の候補。
/// </summary>
public sealed record TraitOffer(
    AdventurerData Adventurer,
    string AwakenLine,
    string RecordLine,
    IReadOnlyList<TraitMasterData> Candidates);

/// <summary>
/// 遠征記録から特性を開花させる。
///
/// レベルアップの能力成長は種族・職業の重み付き抽選で決まり、プレイヤーには選べない。
/// その代わりに、<b>実際にどう戦ったか</b>から生える変化はプレイヤーが選ぶ。
/// 育成の主導権は能力値の振り分けではなく、こちらのチャンネルで返している。
///
/// <para>
/// 提示は1つの特性につき生涯1度きり。選ばなかった候補は二度と現れないので、
/// その場の選択がそのまま冒険者の輪郭になる。
/// </para>
/// </summary>
public static class TraitSystem
{
    /// <summary>1人に一度に見せる候補の上限。</summary>
    public const int MaxCandidatesPerOffer = 3;

    /// <summary>
    /// 記録が条件を満たし、まだ提示していない特性を隊員ごとに集める。
    /// 隊員1人につき最大1件のオファーしか作らない（1本の遠征で何度も選ばせない）。
    /// </summary>
    public static List<TraitOffer> BuildOffers(
        IEnumerable<AdventurerData> members,
        IEnumerable<TraitMasterData> allTraits)
    {
        var traits = allTraits.Where(t => t.Skill != null).ToList();
        var offers = new List<TraitOffer>();

        foreach (var adventurer in members)
        {
            if (!adventurer.isAlive) continue;

            var candidates = traits
                .Where(t => IsEligible(adventurer, t))
                // 代価を先払いした特性を先に見せる。物語として重いほうを頭に置く。
                .OrderByDescending(t => t.RequiresRisk)
                .ThenByDescending(t => Surplus(adventurer, t))
                .Take(MaxCandidatesPerOffer)
                .ToList();
            if (candidates.Count == 0) continue;

            var headline = candidates[0];
            var requirement = headline.HeadlineRequirement;
            string recordLine = requirement == null
                ? ""
                : ExpeditionRecordTypes.Narrate(
                    requirement.record, adventurer.records[requirement.record]);

            offers.Add(new TraitOffer(
                adventurer,
                $"{adventurer.name}{headline.awakenText}",
                recordLine,
                candidates));
        }

        return offers;
    }

    /// <summary>まだ提示しておらず、条件を満たし、効果をまだ持っていない特性か。</summary>
    public static bool IsEligible(AdventurerData adventurer, TraitMasterData trait)
    {
        if (trait.Skill == null) return false;
        if (adventurer.offeredTraitIds.Contains(trait.id)) return false;
        if (adventurer.AllLearnedSkills.Contains(trait.Skill)) return false;
        return trait.IsMetBy(adventurer.records);
    }

    /// <summary>条件をどれだけ超えているか。同じ資格なら、より深く踏み込んだ記録を先に見せる。</summary>
    static int Surplus(AdventurerData adventurer, TraitMasterData trait) =>
        trait.requirements.Count == 0
            ? 0
            : trait.requirements.Min(r => adventurer.records[r.record] - Math.Max(1, r.atLeast));

    /// <summary>
    /// 候補のうち1つを習得させ、オファーを閉じる。
    /// 選ばれなかった候補も「提示済み」になるので、二度と現れない。
    /// </summary>
    public static string Accept(TraitOffer offer, TraitMasterData chosen)
    {
        if (!offer.Candidates.Contains(chosen))
            throw new ArgumentException("この特性はこのオファーの候補ではありません", nameof(chosen));

        var adventurer = offer.Adventurer;
        Close(offer);
        if (chosen.Skill == null) return $"{adventurer.name} は何も掴めなかった";

        adventurer.LearnPermanentSkill(chosen.Skill);
        adventurer.RecordTraitAwakening(chosen);

        var drawbacks = chosen.Drawbacks;
        string cost = drawbacks.Count == 0 ? "" : $"（代償: {string.Join("、", drawbacks)}）";
        return $"{adventurer.name} は特性「{chosen.traitName}」を得た{cost}";
    }

    /// <summary>何も選ばずにオファーを閉じる。候補はすべて提示済みとして失われる。</summary>
    public static string Decline(TraitOffer offer)
    {
        Close(offer);
        return $"{offer.Adventurer.name} は何も変わらないことを選んだ";
    }

    static void Close(TraitOffer offer)
    {
        foreach (var candidate in offer.Candidates)
            if (!offer.Adventurer.offeredTraitIds.Contains(candidate.id))
                offer.Adventurer.offeredTraitIds.Add(candidate.id);
    }
}
