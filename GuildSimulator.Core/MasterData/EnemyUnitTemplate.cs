namespace GuildSimulator.Core.MasterData;

public class EnemyUnitTemplate
{
    public string id = "";
    public string unitName = "";
    public int baseLevel = 1;
    public List<string?> formationIds = new();

    public List<EnemyMasterData?> Formation { get; set; } = new();
}
