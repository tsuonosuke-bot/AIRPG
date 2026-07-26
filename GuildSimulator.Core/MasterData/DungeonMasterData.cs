using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class DungeonMasterData
{
    public string id = "";
    public string dungeonName = "";

    // フェーズごとに抽選するイベントの重み表（値が大きいほど出やすい）
    public Dictionary<DungeonEventType, int> eventTable = new();

    // 敵遭遇時の出現候補（重み・出現フェーズ帯つき）
    public List<EncounterEntry> encounterTable = new();

    // 道中の宝箱イベントで拾える戦利品の重み表（帰還時に付与）。
    // このダンジョンで手に入るアイテムはすべてここから出る。
    public List<RewardEntryData> treasureTable = new();

    // 敵レベルのフェーズスケーリング（実効Lv = baseLevel + floor((phase-1) * これ)）
    public float enemyLevelPerPhase = 0f;

    // ターン内の最終フェーズ処理後、まだ進行中なら最大1件抽選する選択イベント。
    public List<QuestChoiceEventMasterData> turnEndEvents = new();
    public float turnEndEventChance = 0.35f;
}
