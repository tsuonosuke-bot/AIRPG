using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.GameData;

public class ConsumableStack
{
    public ConsumableMasterData item;
    public int count;
    public ConsumableStack(ConsumableMasterData item, int count) { this.item = item; this.count = count; }
}
