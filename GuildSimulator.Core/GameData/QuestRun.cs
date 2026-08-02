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

    // 採取クエストの収集数。目標に達した時点で最終エリアを待たずに帰還できる。
    public int gatheredCount;

    /// <summary>
    /// 採取が目標に届かないまま予定エリアを使い切ったとき、パーティは撤退せず判断を仰いでくる。
    /// これが立っている間は進行が止まり、プレイヤーが「延長」か「撤退」を選ぶまで動かない。
    /// </summary>
    public bool gatherDecisionPending;

    /// <summary>判断を仰いだターン。報告書の並びを合わせるために覚えておく。</summary>
    public int gatherDecisionTurn;

    /// <summary>延長で積み増したエリア数。1回の延長で phasesPerTurn ぶん伸びる。</summary>
    public int extraPhases;

    /// <summary>延長した回数。回数制限はなく、届くまで何度でも聞かれる。</summary>
    public int gatherExtensions;

    public PendingQuestChoice? pendingChoice;
    public List<string> usedConsumableIds = new();
    public int goldRewardBonusPercent;
    public int expRewardBonusPercent;
    public int trapDamageReductionPercent;
    public int restHealBonusPercent;
    public int treasureFromNothingPercent;
    public int enemyFromNothingPercent;
    public int battleExpBonusPercent;
    public int guaranteedNonEmptyChestCount;
    public int emergencyRetreatHpPercent;
    public Dictionary<string, int> targetPvBonusByAdventurerId = new();
    public Dictionary<string, int> targetMpvBonusByAdventurerId = new();

    public bool GatherFulfilled => def.IsGatherQuest && gatheredCount >= def.gatherTargetCount;

    /// <summary>今回の遠征で踏み込めるエリア数。延長するたびに伸びる。</summary>
    public int PhaseLimit => def.totalPhases + extraPhases;

    /// <summary>
    /// 踏破・採取目標の達成。撤退や全滅は含まない。
    /// <b>採取クエストの達成は素材が揃ったかどうかだけで決まる</b>。エリアを使い切っても
    /// 手ぶらならクリアではなく、延長するか撤退するかの判断待ちになる。
    /// </summary>
    public bool ReachedGoal => def.IsGatherQuest ? GatherFulfilled : currentPhase >= PhaseLimit;

    /// <summary>正規クリア。ランクポイントや昇格はこれを満たしたときだけ。</summary>
    public bool IsCleared => !failed && !retreated && ReachedGoal;

    public bool IsInProgress =>
        !failed && !retreated && !rewarded && !ReachedGoal && !gatherDecisionPending;

    public bool CanComplete => !failed && !rewarded && (retreated || ReachedGoal);
    public bool HasPendingChoice => pendingChoice != null;

    /// <summary>採取の続行判断を待っている。進行も完了もここで止まる。</summary>
    public bool HasGatherDecision => gatherDecisionPending && !failed && !retreated && !rewarded;

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

    public void ApplyConsumable(ConsumableMasterData item, AdventurerData? target = null)
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
            case ConsumableEffectType.RestHealPercent:
                restHealBonusPercent = Math.Clamp(restHealBonusPercent + item.effectValue, 0, 200);
                break;
            case ConsumableEffectType.TreasureFromNothingPercent:
                treasureFromNothingPercent = Math.Clamp(
                    treasureFromNothingPercent + item.effectValue, 0, 100);
                break;
            case ConsumableEffectType.TargetPv:
                if (target != null)
                    targetPvBonusByAdventurerId[target.id] =
                        targetPvBonusByAdventurerId.GetValueOrDefault(target.id) + item.effectValue;
                break;
            case ConsumableEffectType.TargetMpv:
                if (target != null)
                    targetMpvBonusByAdventurerId[target.id] =
                        targetMpvBonusByAdventurerId.GetValueOrDefault(target.id) + item.effectValue;
                break;
            case ConsumableEffectType.GuaranteedNonEmptyChest:
                guaranteedNonEmptyChestCount += Math.Max(0, item.effectValue);
                break;
            case ConsumableEffectType.BattleHorn:
                enemyFromNothingPercent = Math.Clamp(
                    enemyFromNothingPercent + item.effectValue, 0, 100);
                battleExpBonusPercent = Math.Clamp(
                    battleExpBonusPercent + item.secondaryEffectValue, 0, 300);
                break;
            case ConsumableEffectType.EmergencyRetreatPercent:
                emergencyRetreatHpPercent = Math.Max(
                    emergencyRetreatHpPercent, Math.Clamp(item.effectValue, 1, 99));
                break;
        }
    }

    /// <summary>特定の冒険者だけに乗る、今回の遠征限定の戦闘補正。</summary>
    public StatBlock ConsumableCombatBonusFor(IUnitMember member)
    {
        if (member is not AdventurerData adventurer) return default;
        return new StatBlock
        {
            pv = targetPvBonusByAdventurerId.GetValueOrDefault(adventurer.id),
            mpv = targetMpvBonusByAdventurerId.GetValueOrDefault(adventurer.id),
        };
    }

    public bool IsEmergencyRetreatThresholdReached
    {
        get
        {
            if (emergencyRetreatHpPercent <= 0 || unitHpMax <= 0) return false;
            return unitHpCurrent * 100 <= unitHpMax * emergencyRetreatHpPercent;
        }
    }
}

public class PendingQuestChoice
{
    public QuestChoiceEventMasterData Event = null!;
    public int createdTurn;
}
