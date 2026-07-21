using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.GameData;

public class EquipmentStack
{
    public EquipmentMasterData item;
    public int count;
    public EquipmentStack(EquipmentMasterData item, int count) { this.item = item; this.count = count; }
}
