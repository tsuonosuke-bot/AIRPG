using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.Systems.Quest;

/// <summary>
/// クエストボードに掲示されている1枠。掲示ターンを持ち、一定ターン受注されないと差し替えられる。
/// </summary>
public class QuestBoardEntry
{
    public QuestMasterData quest;
    public int postedTurn;

    public QuestBoardEntry(QuestMasterData quest, int postedTurn)
    {
        this.quest = quest;
        this.postedTurn = postedTurn;
    }

    public int PostedTurns(int currentTurn) => Math.Max(0, currentTurn - postedTurn);

    /// <summary>掲示から expireTurns ターン経過したら期限切れ。</summary>
    public bool IsExpired(int currentTurn, int expireTurns) => PostedTurns(currentTurn) >= expireTurns;

    /// <summary>期限切れまでの残りターン数。</summary>
    public int RemainingTurns(int currentTurn, int expireTurns)
        => Math.Max(0, expireTurns - PostedTurns(currentTurn));
}
