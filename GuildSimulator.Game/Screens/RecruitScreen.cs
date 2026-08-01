using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class RecruitScreen
{
    public static async Task ShowAsync(
        List<AdventurerMasterData> candidates,
        GuildManager guild,
        int currentTurn,
        IEnumerable<AdventurerMasterData> candidatePool,
        int maxCandidateCount)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header($"冒険者雇入れ  （Turn {currentTurn} の候補）");
            Ui.WriteLine($"  所持金: {guild.Gold}G   在籍冒険者: {guild.adventurers.Count}人");
            Ui.WriteLine($"  ※候補は次のターンで入れ替わります");
            Ui.WriteLine();

            var entries = new List<MenuOption>();
            if (candidates.Count == 0)
            {
                Ui.Dim("  現在雇入れ可能な候補者はいません");
            }
            else
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var m = candidates[i];
                    bool alreadyHired = guild.adventurers.Any(a => a.master == m);
                    int hireCost = CalcHireCost(m);
                    string tag = alreadyHired ? " [雇用済]" : $"  雇用費: {hireCost}G";
                    int candidateAfterHire = guild.Gold - hireCost;
                    int adventurerUpkeep = GuildManager.CalculateAdventurerUpkeep(m.defaultLevel);
                    int candidateUpkeep = GuildManager.CalculateEffectiveUpkeep(guild.BaseUpkeepPerTurn + adventurerUpkeep);
                    int candidateSafeTurns = GuildManager.SafeUpkeepTurns(candidateAfterHire, candidateUpkeep);
                    string runway = candidateSafeTurns == int.MaxValue ? "∞" : candidateSafeTurns.ToString();

                    var detail = new List<string>
                    {
                        $"{m.DefaultClass?.className ?? "？"}/{m.Race?.raceName ?? "？"}  {Ui.RarityLabel(m.rarity)}  維持費{adventurerUpkeep}G/T",
                        $"VIT:{m.vitality} MEN:{m.mental} STR:{m.strength} AGI:{m.agility} INT:{m.intelligence} SIZ:{m.constitution}",
                        $"武器:{m.DefaultWeapon?.displayName ?? "なし"}  防具:{m.DefaultArmor?.displayName ?? "なし"}",
                    };
                    if (m.gender != Gender.Unspecified)
                        detail.Add($"性別: {(m.gender == Gender.Male ? "男性" : "女性")}");
                    if (!string.IsNullOrWhiteSpace(m.background))
                        detail.Add(m.background);
                    if (!alreadyHired && candidateAfterHire >= 0)
                        detail.Add($"雇用後: {candidateAfterHire}G  合計維持費:{candidateUpkeep}G/T  資金猶予:{runway}T");

                    entries.Add(new MenuOption(
                        (i + 1).ToString(),
                        $"{m.baseName}  Lv{m.defaultLevel} ランク{Rank.Label(m.defaultRank)}{tag}",
                        string.Join(Environment.NewLine, detail),
                        Ui.RarityStyle(m.rarity)));
                }
            }

            entries.Add(new MenuOption("r", $"候補を再抽選（{RecruitmentSystem.CandidateRerollCostGold}G）"));
            entries.Add(new MenuOption("0", "戻る", Style: TextStyle.Dim));

            string input = await Ui.SelectAsync("雇う候補", entries);
            if (input.Equals("r", StringComparison.OrdinalIgnoreCase))
            {
                await RerollCandidatesAsync(candidates, candidatePool, guild, maxCandidateCount);
                continue;
            }
            if (!int.TryParse(input, out int sel) || sel == 0) return;
            if (sel < 1 || sel > candidates.Count) { Ui.Error("無効な番号です"); continue; }

            var chosen = candidates[sel - 1];
            if (guild.adventurers.Any(a => a.master == chosen))
            {
                Ui.Warn("すでに雇用済みです");
                await Ui.PauseAsync();
                continue;
            }

            int cost = CalcHireCost(chosen);
            if (guild.Gold < cost)
            {
                Ui.Error($"資金が不足しています（必要: {cost}G  所持: {guild.Gold}G）");
                await Ui.PauseAsync();
                continue;
            }

            int afterHire = guild.Gold - cost;
            int chosenUpkeep = GuildManager.CalculateAdventurerUpkeep(chosen.defaultLevel);
            int projectedUpkeep = GuildManager.CalculateEffectiveUpkeep(guild.BaseUpkeepPerTurn + chosenUpkeep);
            int safeTurns = GuildManager.SafeUpkeepTurns(afterHire, projectedUpkeep);
            if (safeTurns <= 1)
                Ui.Warn($"  ⚠ 雇用後の資金猶予は{safeTurns}ターンです（報酬収入を除く）");
            if (afterHire - projectedUpkeep <= 0
                && !await Ui.ConfirmAsync(
                    $"次の維持費支払い後は {afterHire - projectedUpkeep}Gです。報酬がなければ破産します。それでも雇いますか？"))
                continue;
            if (!await Ui.ConfirmAsync($"{chosen.baseName} を {cost}G で雇いますか？")) continue;

            guild.SpendGold(cost, $"雇用費: {chosen.baseName}");
            var adv = new AdventurerData(chosen);
            guild.AddAdventurer(adv);
            candidates.Remove(chosen);
            Ui.Info($"{chosen.baseName} を雇いました！");
            await Ui.PauseAsync();
        }
    }

    public static int CalcHireCost(AdventurerMasterData m)
        // 維持費バランスの変更で初期雇用費まで連動しないよう、従来のLv単価を維持する。
        => Math.Max(10, Math.Max(1, m.defaultLevel) * 55);

    static async Task RerollCandidatesAsync(
        List<AdventurerMasterData> candidates,
        IEnumerable<AdventurerMasterData> candidatePool,
        GuildManager guild,
        int maxCandidateCount)
    {
        int cost = RecruitmentSystem.CandidateRerollCostGold;
        if (guild.Gold < cost)
        {
            Ui.Error($"再抽選の資金が不足しています（必要: {cost}G  所持: {guild.Gold}G）");
            await Ui.PauseAsync();
            return;
        }
        if (!await Ui.ConfirmAsync($"候補を{cost}Gで再抽選しますか？"))
            return;

        int afterReroll = guild.Gold - cost;
        int upkeep = guild.EffectiveUpkeepPerTurn;
        if (afterReroll - upkeep <= 0)
        {
            Ui.Warn($"  ⚠ 再抽選後、次の維持費支払い後は {afterReroll - upkeep}Gです");
            if (!await Ui.ConfirmAsync("報酬がなければ破産します。それでも再抽選しますか？"))
                return;
        }

        if (!RecruitmentSystem.TryRerollCandidates(
            candidatePool, guild, maxCandidateCount, out var rerolled))
        {
            Ui.Error("再抽選に失敗しました");
            await Ui.PauseAsync();
            return;
        }

        candidates.Clear();
        candidates.AddRange(rerolled);
        Ui.Info($"候補を再抽選しました（{candidates.Count}人）");
        await Ui.PauseAsync();
    }
}
