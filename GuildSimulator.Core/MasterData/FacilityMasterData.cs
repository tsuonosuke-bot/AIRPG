namespace GuildSimulator.Core.MasterData;

/// <summary>
/// ギルド施設。建設するとゴールドを消費し、以後は維持費が毎ターン加算される代わりに、
/// クエスト掲示枠・商店品揃え・休息回復・成長率のいずれかを恒常的に強化する。
/// </summary>
public class FacilityMasterData
{
    public string id = "";
    public string displayName = "";
    public string description = "";
    public int buildCostGold;
    public int upkeepGoldPerTurn;
    public int requiredGuildRank = 1;

    /// <summary>クエスト掲示板の通常枠を増やす数。</summary>
    public int questBoardBonus;

    /// <summary>商店の品揃えレベル（扱える装備のtier上限）を増やす数。</summary>
    public int shopLevelBonus;

    /// <summary>クエスト休息時の回復量を増やす割合（%）。</summary>
    public int restHealBonusPercent;

    /// <summary>冒険者の成長率を増やす割合（%）。1%単位で調整する想定。</summary>
    public int growthRateBonusPercent;

    /// <summary>毎ターンの雇入れ候補の最低人数を増やす数。</summary>
    public int recruitMinBonus;
}
