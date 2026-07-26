using GuildSimulator.Core.Models;

namespace GuildSimulator.Game.Data;

// セーブファイルの中身。マスタデータへの参照はすべて id 文字列で持ち、
// ロード時に GameMasterData から実体を引き直す（マスタデータの更新に耐えるため）。

public class SaveGameData
{
    public int saveVersion = 3;
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
    public int rank;
    public int rankPoint;
    public string raceId = "";
    public string classId = "";
    public string weaponId = "";
    public string armorId = "";
    public int vitality, mental, strength, agility, intelligence, constitution, appearance;
    public int combatHp;
    public int combatHpMax;
    public int expeditionCount;
    public int successfulExpeditionCount;
    public int retreatCount;
    public List<string> adventureHistory = new();
    public List<LearnedSkillSave> learnedSkills = new();
    public Dictionary<string, int> classClearCounts = new();
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
    public List<string> clearedOneShotIds = new();
    public List<string> clearedQuestIds = new();
    public List<string> discoveredClueIds = new();
    public List<string> selectedBranchIds = new();
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
    public int moraleCurrent;
    public int moraleMax;
    public bool rewarded;
    public bool completed;
    public bool bossDefeated;
    public bool baseRewardsApplied;
    public bool clearProgressApplied;
    public string?[] formationAdventurerIds = new string?[6];
    public List<string> logs = new();
    public List<ExpeditionEventSave> reportEvents = new();
    public List<string> discoveredClueIds = new();
    public ExpeditionPolicy policy = ExpeditionPolicy.ObjectiveFirst;
    public Dictionary<string, int> startingLevels = new();
    public int guildUpkeepAtStart;
    public List<PendingLootSave> pendingLoot = new();
    public List<TreasureChestSave> chests = new();
    public int gatheredCount;
    public List<string> usedConsumableIds = new();
    public int goldRewardBonusPercent;
    public int expRewardBonusPercent;
    public int trapDamageReductionPercent;
    public string pendingChoiceEventId = "";
    public int pendingChoiceCreatedTurn;
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
