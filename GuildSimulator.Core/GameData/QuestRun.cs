using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Battle;

namespace GuildSimulator.Core.GameData;

public class QuestRun
{
    public QuestMasterData def;
    public int startedTurn;
    public int currentPhase;

    /// <summary>全滅。報酬も戦利品も失う。</summary>
    public bool failed;

    /// <summary>士気が尽きての撤退。全滅とは違い、戦利品は持ち帰れて死者も出ない。</summary>
    public bool retreated;

    /// <summary>パーティの士気。クエスト開始時に QuestManager が san 合計で張り直す。</summary>
    public MoraleState morale = new(1);

    public bool rewarded;
    public bool completed;
    public bool rewardPresented;
    public bool bossDefeated;
    public bool baseRewardsApplied;
    public bool extraRewardTaken;
    public bool clearProgressApplied;

    public AdventurerData?[] formation = new AdventurerData?[6];
    public List<string> logs = new();

    // 道中の宝箱で拾った戦利品（クエスト成功時にまとめて付与、失敗時は失う）
    public List<RewardEntryData> pendingLoot = new();

    // 採取クエストの収集数。目標に達した時点で最終フェーズを待たずに帰還できる。
    public int gatheredCount;

    public bool GatherFulfilled => def.IsGatherQuest && gatheredCount >= def.gatherTargetCount;

    /// <summary>踏破・採取目標の達成。撤退や全滅は含まない。</summary>
    public bool ReachedGoal => currentPhase >= def.totalPhases || GatherFulfilled;

    /// <summary>正規クリア。ランクポイントや昇格はこれを満たしたときだけ。</summary>
    public bool IsCleared => !failed && !retreated && ReachedGoal;

    public bool IsInProgress => !failed && !retreated && !rewarded && !ReachedGoal;
    public bool CanComplete => !failed && !rewarded && (retreated || ReachedGoal);

    public QuestRun(QuestMasterData def, int startedTurn)
    {
        this.def = def;
        this.startedTurn = startedTurn;
    }

    public IEnumerable<AdventurerData> EnumerateMembers()
    {
        for (int i = 0; i < formation.Length; i++)
            if (formation[i] != null) yield return formation[i]!;
    }

    // 個人HPの合算（表示用）。実体は各formationメンバーのCombatHp/CombatHpMax。
    public int unitHpMax => EnumerateMembers().Sum(a => a.CombatHpMax);
    public int unitHpCurrent => EnumerateMembers().Where(a => a.isAlive).Sum(a => a.CombatHp);
}
