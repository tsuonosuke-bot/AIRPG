using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Game.Data;

// セーブファイルの中身。マスタデータへの参照はすべて id 文字列で持ち、
// ロード時に GameMasterData から実体を引き直す（マスタデータの更新に耐えるため）。

public class SaveGameData
{
    public const int CurrentVersion = 12;
    public int saveVersion = CurrentVersion;
    public int currentTurn = 1;
    public GuildSaveData guild = new();
    public QuestManagerSaveData questManager = new();
    public List<string> recruitCandidateIds = new();
}

public class GuildSaveData
{
    public int gold;
    public int guildRank;
    public int guildPoints;
    public List<string> economyLogs = new();
    public List<string> relicIds = new();
    public List<string> facilityIds = new();
    public List<InventoryEntrySave> inventory = new();
    public List<InventoryEntrySave> consumables = new();
    public int lastShopRefreshTurn;
    public Dictionary<string, int> shopEquipmentStock = new();
    public Dictionary<string, int> shopConsumableStock = new();
    public List<AdventurerSaveData> adventurers = new();
    public List<BurialRecordSave> burialRecords = new();
    public List<string> discoveredEnemyIds = new();
}

public class BurialRecordSave
{
    public string name = "";
    public int level;
    public string classAndRace = "";
    public int buriedTurn;
    public int expeditionCount;
    public int successCount;
}

public class InventoryEntrySave
{
    public string itemId = "";
    public int count;
}

public class AdventurerSaveData
{
    public string id = "";
    public string masterId = "";
    public string name = "";
    public int level;
    public int experience;
    public bool isAlive = true;
    public bool isIncapacitated;
    public int pendingInjurySeverity;
    public int rank;
    public int higherRankClears;
    public int suitableRankClearsTotal;
    public string raceId = "";
    public string classId = "";
    public string weaponId = "";
    public string armorId = "";
    public Dictionary<EquipSlot, string> equippedSlotIds = new();
    public int vitality, mental, strength, agility, intelligence, constitution, appearance;
    public int combatHp;
    public int combatHpMax;
    public int expeditionCount;
    public int successfulExpeditionCount;
    public int retreatCount;
    public List<string> adventureHistory = new();
    public List<InjurySaveData> injuries = new();
    public List<ScarSaveData> scars = new();
    public List<LearnedSkillSave> learnedSkills = new();
    public Dictionary<string, int> classMasteryPoints = new();

    /// <summary>生涯の遠征記録。特性の解禁条件が参照する。</summary>
    public Dictionary<ExpeditionRecordType, int> expeditionRecords = new();

    /// <summary>提示済みの特性ID。習得したものも見送ったものも含む（再提示しないため）。</summary>
    public List<string> offeredTraitIds = new();
}

public class InjurySaveData
{
    public InjuryType type;
    public int remainingRestTurns;
    public int scarChancePercent;
}

public class ScarSaveData
{
    public ScarType type;
}

public class LearnedSkillSave
{
    public string skillId = "";
    public string? ownerClassId;
}

public class QuestManagerSaveData
{
    public List<BoardEntrySave> questBoard = new();
    public List<QuestRunSaveData> activeQuests = new();
    public List<QuestHistoryEntrySaveData> questHistory = new();
    public List<string> clearedOneShotIds = new();
    public List<string> clearedQuestIds = new();
    public List<string> discoveredClueIds = new();
    public List<string> selectedBranchIds = new();
}

public class QuestHistoryEntrySaveData
{
    public string questId = "";
    public string questName = "";
    public int startedTurn;
    public int completedTurn;
    public QuestHistoryOutcome outcome;
    public List<string> logs = new();
}

public class BoardEntrySave
{
    public string questId = "";
    public int postedTurn;
}

public class QuestRunSaveData
{
    public string questId = "";
    public int startedTurn;
    public int currentPhase;
    public bool failed;
    public bool retreated;
    public ExpeditionRetreatReason retreatReason;
    public int moraleCurrent;
    public int moraleMax;
    public bool rewarded;
    public bool completed;
    public bool bossDefeated;
    public string bossFinisherAdventurerId = "";
    public bool baseRewardsApplied;
    public bool clearProgressApplied;
    public string?[] formationAdventurerIds = new string?[6];
    public List<string> logs = new();
    public List<ExpeditionEventSave> reportEvents = new();
    public List<string> discoveredClueIds = new();
    public List<string> resolvedFixedChoiceEventIds = new();
    public ExpeditionPolicy policy = ExpeditionPolicy.ObjectiveFirst;
    public Dictionary<string, int> startingLevels = new();
    public Dictionary<string, List<StatType>> levelGrowthsByAdventurerId = new();
    public int guildUpkeepAtStart;
    public List<PendingLootSave> pendingLoot = new();
    public List<TreasureChestSave> chests = new();
    public int gatheredCount;
    public bool gatherDecisionPending;
    public int gatherDecisionTurn;
    public int extraPhases;
    public int gatherExtensions;
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
    public string pendingChoiceEventId = "";
    public int pendingChoiceCreatedTurn;

    /// <summary>
    /// この遠征でここまでに積んだ隊員ごとの記録（冒険者ID → 記録）。
    /// 帰還時に生涯記録へ合流するので、途中セーブで失うと特性の開花が遅れる。
    /// </summary>
    public Dictionary<string, Dictionary<ExpeditionRecordType, int>> expeditionRecords = new();
}

public class ExpeditionEventSave
{
    public int turn;
    public int phase;
    public ExpeditionEventKind kind;
    public string title = "";
    public string detail = "";
    public string actorName = "";
    public string clueId = "";
    public bool important;
}

public class TreasureChestSave
{
    public TreasureChestKind kind;
    public int foundPhase;
}

public class PendingLootSave
{
    public RewardType type;
    public string relicId = "";
    public string equipmentId = "";
    public string skillId = "";
    public string consumableId = "";
    public int gold;
    public int weight = 10;
    public int quantity = 1;
    public bool unique = true;
}
