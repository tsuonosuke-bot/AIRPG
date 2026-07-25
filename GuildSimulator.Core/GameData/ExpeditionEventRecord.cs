using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

/// <summary>
/// 帰還報告を組み立てるための構造化された遠征記録。
/// 詳細な戦闘ログは QuestRun.logs に残し、こちらには物語上の要点だけを記録する。
/// </summary>
public class ExpeditionEventRecord
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
