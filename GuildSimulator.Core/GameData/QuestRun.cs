using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;

namespace GuildSimulator.Core.GameData;

public class QuestRun
{
    public QuestMasterData def;
    public int startedTurn;
    public int currentPhase;

    /// <summary>全滅。報酬も戦利品も失う。</summary>
    public bool failed;

    /// <summary>撤退。全滅とは違い、道中の戦利品は持ち帰れる。</summary>
    public bool retreated;
    public ExpeditionRetreatReason retreatReason;

    /// <summary>パーティの士気。クエスト開始時に QuestManager が san 合計で張り直す。</summary>
    public MoraleState morale = new(1);

    public bool rewarded;
    public bool completed;
    public bool bossDefeated;
    public bool baseRewardsApplied;
    public bool clearProgressApplied;

    public AdventurerData?[] formation = new AdventurerData?[6];
    public List<string> logs = new();
    public List<ExpeditionEventRecord> reportEvents = new();
    public List<string> discoveredClueIds = new();
    public ExpeditionPolicy policy = ExpeditionPolicy.ObjectiveFirst;
    public Dictionary<string, int> startingLevels = new();
    public int guildUpkeepAtStart;

    // 道中で手に入れた未開封の宝箱。帰還後に開けて中身を抽選する（全滅時は失う）。
    public List<TreasureChest> chests = new();

    // 中身の分かっている戦利品。敵のレアドロップ、選択イベントの拾い物、
    // それに帰還後に開けた宝箱の中身がここへ積まれ、まとめて付与される（全滅時は失う）。
    public List<RewardEntryData> pendingLoot = new();

    // 採取クエストの収集数。目標に達した時点で最終フェーズを待たずに帰還できる。
    public int gatheredCount;
    public PendingQuestChoice? pendingChoice;
    public List<string> usedConsumableIds = new();
    public int goldRewardBonusPercent;
    public int expRewardBonusPercent;
    public int trapDamageReductionPercent;

    public bool GatherFulfilled => def.IsGatherQuest && gatheredCount >= def.gatherTargetCount;

    /// <summary>踏破・採取目標の達成。撤退や全滅は含まない。</summary>
    public bool ReachedGoal => currentPhase >= def.totalPhases || GatherFulfilled;

    /// <summary>正規クリア。ランクポイントや昇格はこれを満たしたときだけ。</summary>
    public bool IsCleared => !failed && !retreated && ReachedGoal;

    public bool IsInProgress => !failed && !retreated && !rewarded && !ReachedGoal;
    public bool CanComplete => !failed && !rewarded && (retreated || ReachedGoal);
    public bool HasPendingChoice => pendingChoice != null;

    public QuestRun(QuestMasterData def, int startedTurn)
    {
        this.def = def;
        this.startedTurn = startedTurn;
    }

    public void AddReportEvent(
        int turn,
        int phase,
        ExpeditionEventKind kind,
        string title,
        string detail,
        bool important = false,
        string actorName = "",
        string clueId = "")
    {
        reportEvents.Add(new ExpeditionEventRecord
        {
            turn = turn,
            phase = phase,
            kind = kind,
            title = title,
            detail = detail,
            important = important,
            actorName = actorName,
            clueId = clueId,
        });
    }

    public IEnumerable<AdventurerData> EnumerateMembers()
    {
        for (int i = 0; i < formation.Length; i++)
            if (formation[i] != null) yield return formation[i]!;
    }

    // 個人HPの合算（表示用）。実体は各formationメンバーのCombatHp/CombatHpMax。
    public int unitHpMax => EnumerateMembers().Sum(a => a.CombatHpMax);
    public int unitHpCurrent => EnumerateMembers().Where(a => a.isAlive).Sum(a => a.CombatHp);

    public void ApplyConsumable(ConsumableMasterData item)
    {
        usedConsumableIds.Add(item.id);
        switch (item.effectType)
        {
            case ConsumableEffectType.MaxHpPercent:
                foreach (var a in EnumerateMembers())
                {
                    int bonus = (int)Math.Ceiling(a.CombatHpMax * item.effectValue / 100f);
                    a.CombatHpMax += bonus;
                    a.CombatHp += bonus;
                }
                break;
            case ConsumableEffectType.MoralePercent:
                morale.IncreaseMaxPercent(item.effectValue);
                break;
            case ConsumableEffectType.GoldRewardPercent:
                goldRewardBonusPercent += item.effectValue;
                break;
            case ConsumableEffectType.ExpRewardPercent:
                expRewardBonusPercent += item.effectValue;
                break;
            case ConsumableEffectType.TrapDamageReductionPercent:
                trapDamageReductionPercent = Math.Clamp(trapDamageReductionPercent + item.effectValue, 0, 90);
                break;
        }
    }
}

public class PendingQuestChoice
{
    public QuestChoiceEventMasterData Event = null!;
    public int createdTurn;
}
