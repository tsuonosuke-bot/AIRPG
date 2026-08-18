namespace GuildSimulator.Core.MasterData;

public class QuestMasterData
{
    public string id = "";
    public string questName = "";
    public string clientName = "";
    public string description = "";
    public int rank = 1;
    public int totalPhases = 15;
    public int phasesPerTurn = 5;
    public int rewardGold = 100;
    public int rewardGuildPoints;
    public int rewardExp = 100;
    public bool isEmergencyQuest;
    public int rankUpOnClear;
    public int requiredGuildPoints;
    public string dungeonId = "";
    public string bossEnemyId = "";
    public int bossPhase = 15;
    public List<RewardEntryData> bossDrops = new();
    public bool bossDropsAreGuaranteed;
    public List<QuestPhaseEvent> fixedEvents = new();

    // ---- ストーリー進行 ----
    public bool isStoryQuest;
    public List<string> requiredQuestIds = new();
    public List<string> requiredClueIds = new();
    public List<string> grantedClueIds = new();
    public string storyBranchId = "";
    public List<StoryClueMasterData> GrantedClues { get; set; } = new();

    /// <summary>この依頼で解決必須の固定選択イベント。</summary>
    public IEnumerable<QuestPhaseEvent> FixedChoiceEvents =>
        fixedEvents.Where(e => e.type == Models.QuestEventType.ForceChoice && e.ChoiceEvent != null);

    // ---- 採取クエスト ----
    // gatherTargetCount > 0 のとき採取クエストとして扱う。道中で採取イベントが抽選され、
    // 目標数に達した時点で即帰還できる。**達成条件は素材が揃ったかどうかだけ**で、
    // 予定エリアを踏破しても手ぶらならクリアにはならない（QuestRun.ReachedGoal）。
    //
    // 予定エリアを使い切って足りないときは撤退ではなく指示待ちになり、行程を延ばすか
    // 引き上げるかをプレイヤーが選ぶ（QuestManager.ResolveGatherDecision）。延長は無制限だが、
    // そのぶん維持費と道中のリスクを負う。とはいえ毎回延長になるようでは予定が予定にならないので、
    // 目標数は「採取の期待量の 1/1.5 以下」に置く。決め方は MASTER_DATA.md「採取クエスト」を参照。
    public string gatherItemName = "";
    public int gatherTargetCount;
    public int gatherMinPerEvent = 1;
    public int gatherMaxPerEvent = 3;
    public float gatherChance = 0.7f;   // 各エリアで採取イベントになる確率
    public int gatherGoldPerItem;       // 目標数を超えた採取1個あたりの追加Gold

    public bool IsGatherQuest => gatherTargetCount > 0;

    public DungeonMasterData? Dungeon { get; set; }
    public EnemyUnitTemplate? BossEnemy { get; set; }
}
