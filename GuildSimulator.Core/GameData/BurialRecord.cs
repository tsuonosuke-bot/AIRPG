namespace GuildSimulator.Core.GameData;

public class BurialRecord
{
    public string name;
    public int level;
    public string classAndRace;
    public int buriedTurn;
    public int expeditionCount;
    public int successCount;

    public BurialRecord(string name, int level, string classAndRace, int buriedTurn, int expeditionCount, int successCount)
    {
        this.name = name;
        this.level = level;
        this.classAndRace = classAndRace;
        this.buriedTurn = buriedTurn;
        this.expeditionCount = expeditionCount;
        this.successCount = successCount;
    }
}
