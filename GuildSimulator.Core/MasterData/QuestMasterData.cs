namespace GuildSimulator.Core.MasterData;

public class QuestMasterData
{
    public string id = "";
    public string questName = "";
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

    // ---- 採取クエスト ----
    // gatherTargetCount > 0 のとき採取クエストとして扱う。道中で採取イベントが抽選され、
    // 目標数に達した時点で即帰還できる。最終フェーズまで足りなければ残数を一括採取して達成。
    public string gatherItemName = "";
    public int gatherTargetCount;
    public int gatherMinPerEvent = 1;
    public int gatherMaxPerEvent = 3;
    public float gatherChance = 0.5f;   // 各フェーズで採取イベントになる確率
    public int gatherGoldPerItem;       // 目標数を超えた採取1個あたりの追加Gold

    public bool IsGatherQuest => gatherTargetCount > 0;

    public DungeonMasterData? Dungeon { get; set; }
    public EnemyUnitTemplate? BossEnemy { get; set; }
}
