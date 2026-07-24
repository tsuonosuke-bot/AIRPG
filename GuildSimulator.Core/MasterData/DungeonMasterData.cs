using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class DungeonMasterData
{
    public string id = "";
    public string dungeonName = "";

    // クリア報酬プール（クエスト完了時に使用）
    public List<RewardEntryData> rewardTable = new();
    public int rewardChoiceMin = 3;
    public int rewardChoiceMax = 5;

    // フェーズごとに抽選するイベントの重み表（値が大きいほど出やすい）
    public Dictionary<DungeonEventType, int> eventTable = new();

    // 敵遭遇時の出現候補（重み・出現フェーズ帯つき）
    public List<EncounterEntry> encounterTable = new();

    // 道中の宝箱イベントで即時に拾える戦利品の重み表（クエスト成功時に付与）
    public List<RewardEntryData> treasureTable = new();

    // 敵レベルのフェーズスケーリング（実効Lv = baseLevel + floor((phase-1) * これ)）
    public float enemyLevelPerPhase = 0f;

    // ターン内の最終フェーズ処理後、まだ進行中なら最大1件抽選する選択イベント。
    public List<QuestChoiceEventMasterData> turnEndEvents = new();
    public float turnEndEventChance = 0.35f;
}
