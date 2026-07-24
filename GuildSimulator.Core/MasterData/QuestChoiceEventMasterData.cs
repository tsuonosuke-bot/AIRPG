using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class QuestChoiceEventMasterData
{
    public string id = "";
    public string title = "";
    public string description = "";
    public int weight = 10;
    public List<QuestChoiceOptionData> options = new();
}

public class QuestChoiceOptionData
{
    public string text = "";
    public string resultText = "";
    public QuestChoiceEffectType effectType;
    public int value;
    public string targetId = "";
    public EquipmentMasterData? Equipment { get; set; }
    public ConsumableMasterData? Consumable { get; set; }
}
