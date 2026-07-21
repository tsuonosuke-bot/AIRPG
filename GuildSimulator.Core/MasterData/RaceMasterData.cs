namespace GuildSimulator.Core.MasterData;

public class RaceMasterData
{
    public string id = "";
    public string raceName = "";
    public float vitGrowth;
    public float mentGrowth;
    public float strGrowth;
    public float intGrowth;
    public float agiGrowth;
    public List<string> allowedClassIds = new();
}
