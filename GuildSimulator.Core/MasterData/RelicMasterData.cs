using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class RelicMasterData
{
    public string id = "";
    public string relicName = "";
    public string description = "";
    public RelicEffectType effectType;
    public StatBlock add;
    public StatMultiplier mul = StatMultiplier.One;
    public float rate = 1f;
}
