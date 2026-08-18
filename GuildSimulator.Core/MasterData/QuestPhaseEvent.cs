using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class QuestPhaseEvent
{
    public int phase;
    public QuestEventType type;
    public string choiceEventId = "";
    public QuestChoiceEventMasterData? ChoiceEvent { get; set; }
}
