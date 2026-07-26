using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.Systems.Guild;

/// <summary>
/// 建設済み施設の効果を集計する。<see cref="RelicSystem"/> と同様、
/// 現在のギルドが所持する一覧をセットしておき、各所からstaticに参照する。
/// </summary>
public static class FacilitySystem
{
    static List<FacilityMasterData>? _facilities;

    public static void SetFacilities(List<FacilityMasterData> facilities) => _facilities = facilities;

    public static int GetFacilityUpkeepPerTurn() =>
        _facilities?.Sum(f => f.upkeepGoldPerTurn) ?? 0;

    /// <summary>クエスト掲示板の通常枠に加算する数。</summary>
    public static int GetQuestBoardBonus() =>
        _facilities?.Sum(f => f.questBoardBonus) ?? 0;

    /// <summary>商店の品揃えレベル（基準の1に加算する）。</summary>
    public static int GetShopLevelBonus() =>
        _facilities?.Sum(f => f.shopLevelBonus) ?? 0;

    /// <summary>休息回復量に掛ける倍率。施設なしなら1.0。</summary>
    public static float GetRestHealMultiplier() =>
        1f + (_facilities?.Sum(f => f.restHealBonusPercent) ?? 0) / 100f;

    /// <summary>成長判定に加算する確率（1% = 0.01）。</summary>
    public static float GetGrowthRateBonus() =>
        (_facilities?.Sum(f => f.growthRateBonusPercent) ?? 0) / 100f;
}
