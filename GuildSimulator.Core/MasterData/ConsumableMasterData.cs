using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class ConsumableMasterData
{
    public string id = "";
    public string displayName = "";
    public string description = "";
    public Rarity rarity;
    public int price;
    public ConsumableEffectType effectType;
    public int effectValue;
}
