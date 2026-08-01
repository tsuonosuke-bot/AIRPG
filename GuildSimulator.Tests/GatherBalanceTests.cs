using GuildSimulator.Core.MasterData;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 採取クエストが「予定どおりに帰れる率」の帯に収まっているかを見る。
/// 届かなくても撤退が確定するわけではなく延長を選べるので、ここで見ているのは失敗率ではない。
/// </summary>
[Collection("Guild static state")]
public class GatherBalanceTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    // 目標に届いた時点で採取は止まるので、確率は「いつか届く」ではなく
    // 「予定フェーズの内に届く」でなければならない。
    [Fact]
    public void SuccessChanceIsOneWhenEveryPhaseAlreadyMeetsTheTarget()
    {
        float certain = RankBandTable.GatherSuccessPercent(
            totalPhases: 5, bossPhase: 0, hasBoss: false,
            gatherChance: 1f, minPerEvent: 3, maxPerEvent: 3, targetCount: 3);
        Assert.Equal(100d, certain, 3);

        // 5フェーズ x 毎回1個で、目標6個には決して届かない。
        float impossible = RankBandTable.GatherSuccessPercent(
            5, 0, false, 1f, 1, 1, 6);
        Assert.Equal(0d, impossible, 3);
    }

    // 採取判定が外れ続ければ届かない。10フェーズすべてで当たりを引く確率そのもの。
    [Fact]
    public void SuccessChanceMatchesTheHandComputedValue()
    {
        // 毎フェーズ 50% で1個。10フェーズで10個ちょうど要るなら、全フェーズ当たりの 0.5^10。
        float chance = RankBandTable.GatherSuccessPercent(10, 0, false, 0.5f, 1, 1, 10);
        Assert.Equal(Math.Pow(0.5, 10) * 100, chance, 4);

        // 1回でも当たれば足りるなら、余事象（全部外し）を引いたもの。
        float lenient = RankBandTable.GatherSuccessPercent(10, 0, false, 0.5f, 1, 1, 1);
        Assert.Equal((1 - Math.Pow(0.5, 10)) * 100, lenient, 4);
    }

    // ボスのフェーズでは採取判定が起きないので、そのぶん1フェーズ短く数える。
    [Fact]
    public void BossPhaseDoesNotCountAsAGatheringPhase()
    {
        float withoutBoss = RankBandTable.GatherSuccessPercent(10, 0, false, 0.5f, 1, 1, 10);
        float withBoss = RankBandTable.GatherSuccessPercent(10, 10, true, 0.5f, 1, 1, 10);

        Assert.Equal(Math.Pow(0.5, 10) * 100, withoutBoss, 4);
        Assert.Equal(0d, withBoss, 4);   // 採取できるのは9フェーズだけなので10個は不可能
    }

    // 期待量が同じでも、ブレの幅が違えば達成率は変わる。
    // 「期待量が目標の何倍か」という近似で済ませられない理由がこれ。
    [Fact]
    public void SameExpectedYieldButWiderSpreadIsLessReliable()
    {
        // どちらも期待量は 10フェーズ x 0.8 x 2.5 = 20個。
        float narrow = RankBandTable.GatherSuccessPercent(10, 0, false, 0.8f, 2, 3, 15);
        float wide = RankBandTable.GatherSuccessPercent(10, 0, false, 0.8f, 1, 4, 15);

        Assert.Equal(
            (double)RankBandTable.ExpectedGatherYield(10, 0, false, 0.8f, 2, 3),
            (double)RankBandTable.ExpectedGatherYield(10, 0, false, 0.8f, 1, 4),
            3);
        Assert.True(narrow > wide,
            $"幅の狭いほうが達成率は高いはず（narrow {narrow:0.#}% / wide {wide:0.#}%）");
    }

    [Fact]
    public void EveryGatherQuestSitsInsideItsRankBand()
    {
        var db = Load();
        var gatherQuests = db.allQuests.Where(q => q.IsGatherQuest).ToList();
        Assert.NotEmpty(gatherQuests);

        foreach (var q in gatherQuests)
        {
            var band = RankBandTable.GatherSuccessForRank(q.rank);
            Assert.True(band.HasValue, $"{q.id}: rank {q.rank} の採取達成率の帯がありません");

            float success = RankBandTable.GatherSuccessPercent(
                q.totalPhases, q.bossPhase, q.BossEnemy != null,
                q.gatherChance, q.gatherMinPerEvent, q.gatherMaxPerEvent, q.gatherTargetCount);

            Assert.True(band!.Value.Contains((int)Math.Round(success)),
                $"{q.id}: 予定内の達成率 {success:0.#}% が帯（{band.Value}%）の外です");
            Assert.True(q.gatherChance >= RankBandTable.MinGatherChance,
                $"{q.id}: gatherChance {q.gatherChance} が下限 {RankBandTable.MinGatherChance} 未満です");
        }
    }

    // 序盤ほど簡単で、奥へ行くほど延長判断が増える形になっているか。
    [Fact]
    public void HigherRanksAreHarderToFinishOnSchedule()
    {
        for (int rank = 1; rank < 7; rank++)
        {
            var here = RankBandTable.GatherSuccessForRank(rank);
            var next = RankBandTable.GatherSuccessForRank(rank + 1);
            Assert.True(here.HasValue && next.HasValue, $"rank {rank} / {rank + 1} の帯がありません");
            Assert.True(next!.Value.Min <= here!.Value.Min && next.Value.Max <= here.Value.Max,
                $"rank {rank + 1} の帯（{next.Value}）が rank {rank}（{here.Value}）より緩くなっています");
        }
    }

    // 一度延長すればほぼ確実に届く。延長は「届くかどうか」の賭けではなく、
    // 1ターンぶんの維持費と道中のリスクを払うかどうかの判断であってほしい。
    [Fact]
    public void OneExtensionAlmostAlwaysFinishesTheJob()
    {
        var db = Load();
        foreach (var q in db.allQuests.Where(q => q.IsGatherQuest))
        {
            float afterExtension = RankBandTable.GatherSuccessPercent(
                q.totalPhases + q.phasesPerTurn, q.bossPhase, q.BossEnemy != null,
                q.gatherChance, q.gatherMinPerEvent, q.gatherMaxPerEvent, q.gatherTargetCount);

            Assert.True(afterExtension >= 95f,
                $"{q.id}: 延長1回でも {afterExtension:0.#}% にしかならず、延長が賭けになっています");
        }
    }
}
