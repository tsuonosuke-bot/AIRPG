using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class FacilityScreen
{
    public static async Task ShowAsync(GameMasterData db, GuildManager guild)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("ギルド施設");
            Ui.WriteLine($"  所持金: {guild.Gold}G   施設維持費: {guild.FacilityUpkeepPerTurn}G/Turn");
            Ui.WriteLine();

            if (guild.facilities.Count == 0)
                Ui.Dim("  まだ施設を建設していません");
            else
            {
                Ui.WriteLine("  ── 建設済み ──");
                foreach (var f in guild.facilities)
                {
                    Ui.WriteLine($"  ◆ {f.displayName}（維持費 {f.upkeepGoldPerTurn}G/Turn）");
                    Ui.Dim($"      {f.description}");
                    Ui.Dim($"      {DescribeEffects(f)}");
                }
                Ui.WriteLine();
            }

            var candidates = db.facilities.Values
                .Where(f => !guild.HasFacility(f))
                .OrderBy(f => f.requiredGuildRank)
                .ThenBy(f => f.buildCostGold)
                .ToList();

            if (candidates.Count == 0)
            {
                Ui.Dim("  建設できる施設は残っていません");
                await Ui.PauseAsync();
                return;
            }

            Ui.WriteLine("  ── 建設可能 ──");
            var options = new List<MenuOption>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var f = candidates[i];
                bool rankOk = guild.GuildRank >= f.requiredGuildRank;
                bool affordable = guild.Gold >= f.buildCostGold;
                string tag = !rankOk ? $"[ギルドランク{Rank.Label(f.requiredGuildRank)}必要]" : !affordable ? "[資金不足]" : "";
                string label = $"{f.displayName}  建設費{f.buildCostGold}G 維持費{f.upkeepGoldPerTurn}G/Turn {tag}";
                string effects = DescribeEffects(f);
                Ui.WriteLine($"  {i + 1}. {label}");
                Ui.Dim($"       {f.description}");
                Ui.Dim($"       {effects}");

                options.Add(new MenuOption(
                    (i + 1).ToString(),
                    label,
                    $"{f.description}{Environment.NewLine}{effects}",
                    rankOk && affordable ? TextStyle.Normal : TextStyle.Dim));
            }

            int? sel = await Ui.SelectIndexAsync("建設する施設", options);
            if (sel == null) return;

            var chosen = candidates[sel.Value - 1];
            if (guild.GuildRank < chosen.requiredGuildRank)
            {
                Ui.Error($"ギルドランクが不足しています（必要: {Rank.Label(chosen.requiredGuildRank)}）");
                await Ui.PauseAsync();
                continue;
            }
            if (guild.Gold < chosen.buildCostGold)
            {
                Ui.Error($"資金が不足しています（必要: {chosen.buildCostGold}G  所持: {guild.Gold}G）");
                await Ui.PauseAsync();
                continue;
            }
            if (!await Ui.ConfirmAsync(
                $"{chosen.displayName} を {chosen.buildCostGold}G で建設しますか？（維持費 +{chosen.upkeepGoldPerTurn}G/Turn）"))
                continue;

            if (guild.TryBuildFacility(chosen, out string reason))
                Ui.Info($"{chosen.displayName} を建設しました");
            else
                Ui.Error(reason);
            await Ui.PauseAsync();
        }
    }

    static string DescribeEffects(FacilityMasterData f)
    {
        var parts = new List<string>();
        if (f.questBoardBonus != 0) parts.Add($"クエスト掲示枠+{f.questBoardBonus}");
        if (f.shopLevelBonus != 0) parts.Add($"商店レベル+{f.shopLevelBonus}");
        if (f.restHealBonusPercent != 0) parts.Add($"休息回復量+{f.restHealBonusPercent}%");
        if (f.growthRateBonusPercent != 0) parts.Add($"成長率+{f.growthRateBonusPercent}%");
        if (f.recruitMinBonus != 0) parts.Add($"雇入れ候補の最低人数+{f.recruitMinBonus}");
        if (f.injuryRecoveryBonus != 0) parts.Add($"休養時の負傷回復+{f.injuryRecoveryBonus}/T");
        if (f.fatalityReductionPercent != 0) parts.Add($"帰還時死亡率-{f.fatalityReductionPercent}%");
        if (f.scarPreventionPercent != 0) parts.Add($"傷痕発生率-{f.scarPreventionPercent}%");
        return parts.Count > 0 ? string.Join(" ", parts) : "効果なし";
    }
}
