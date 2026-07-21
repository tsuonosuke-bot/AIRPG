namespace GuildSimulator.Core.MasterData;

/// <summary>
/// ダンジョンの敵遭遇テーブル1行。重みと出現フェーズ帯を持つ。
/// </summary>
public class EncounterEntry
{
    public string unitId = "";
    public int weight = 1;
    public int minPhase = 1;   // このフェーズ以上で出現
    public int maxPhase = 0;   // このフェーズ以下で出現（0 = 上限なし）

    public EnemyUnitTemplate? Unit { get; set; }

    public bool IsEligible(int phase)
        => phase >= minPhase && (maxPhase <= 0 || phase <= maxPhase);
}
