namespace GuildSimulator.Core.MasterData;

public class EnemyUnitTemplate
{
    public string id = "";
    public string unitName = "";
    public List<string?> formationIds = new();

    public List<EnemyMasterData?> Formation { get; set; } = new();

    /// <summary>この隊の脅威度。構成する敵のうち最も高いものを採る。</summary>
    public int Threat => Formation.Count == 0
        ? Models.Rank.Min
        : Formation.Where(e => e != null).Select(e => e!.threat).DefaultIfEmpty(Models.Rank.Min).Max();
}
