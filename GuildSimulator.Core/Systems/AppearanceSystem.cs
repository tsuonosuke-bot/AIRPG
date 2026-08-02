using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;

namespace GuildSimulator.Core.Systems;

public enum HumanEncounterKind
{
    TravelingMerchant,
    Bandits,
}

/// <summary>
/// 容姿（APP）が遠征へ与える効果をまとめる。
/// 高いAPPは名声と士気、極端なAPPは戦場での目立ちやすさに効く。
/// </summary>
public static class AppearanceSystem
{
    public const int HighAppearanceThreshold = 12;
    public const int NeutralAppearance = 10;
    public const int SocialCheckDifficulty = 12;
    public const int ExceptionalSocialResult = 18;
    public const float HumanEncounterChance = 0.25f;
    public const float BanditMoraleRate = 0.05f;
    public const int MerchantSavingsPerQuestRank = 5;
    public const int MaxGuildPointBonusPercent = 25;
    public const int MaxBattleMoralePerRound = 3;
    public const int MaxExtremeTargetPercent = 25;

    public static int HighestAppearance(IEnumerable<AdventurerData?> members) =>
        members.Where(a => a != null && a.isAlive && !a.isIncapacitated)
            .Select(a => a!.appearance)
            .DefaultIfEmpty(0)
            .Max();

    public static int GuildPointBonusPercent(IEnumerable<AdventurerData?> members)
    {
        int appearance = HighestAppearance(members);
        if (appearance < HighAppearanceThreshold) return 0;
        return Math.Min(MaxGuildPointBonusPercent, (appearance - NeutralAppearance) * 5);
    }

    public static int GuildPointBonus(int basePoints, IEnumerable<AdventurerData?> members)
    {
        if (basePoints <= 0) return 0;
        int percent = GuildPointBonusPercent(members);
        return percent <= 0
            ? 0
            : Math.Max(1, (int)Math.Floor(basePoints * percent / 100f));
    }

    public static int BattleMoralePerRound(IEnumerable<IUnitMember?> party)
    {
        int appearance = party
            .OfType<AdventurerData>()
            .Where(a => a.isAlive && !a.isIncapacitated)
            .Select(a => a.appearance)
            .DefaultIfEmpty(0)
            .Max();
        return appearance < HighAppearanceThreshold
            ? 0
            : Math.Min(MaxBattleMoralePerRound, appearance - NeutralAppearance);
    }

    /// <summary>
    /// APP8～12は通常。7以下または13以上から、中心から離れるほど少し狙われやすくなる。
    /// </summary>
    public static float TargetWeightMultiplier(IUnitMember member)
    {
        if (member is not AdventurerData adventurer) return 1f;
        int percent = Math.Clamp(
            (Math.Abs(adventurer.appearance - NeutralAppearance) - 2) * 10,
            0,
            MaxExtremeTargetPercent);
        return 1f + percent / 100f;
    }

    public static bool TryRunHumanEncounter(QuestRun quest, int currentTurn)
    {
        if (HighestAppearance(quest.formation) <= 0
            || GameRandom.NextFloat() >= HumanEncounterChance)
            return false;

        var kind = GameRandom.NextFloat() < 0.5f
            ? HumanEncounterKind.TravelingMerchant
            : HumanEncounterKind.Bandits;
        ResolveHumanEncounter(quest, currentTurn, kind, GameRandom.Range(1, QudCombat.HIT_DIE + 1));
        return true;
    }

    /// <summary>テストとイベント抽選の共通経路。dieRollは1～20にクランプする。</summary>
    public static string ResolveHumanEncounter(
        QuestRun quest,
        int currentTurn,
        HumanEncounterKind kind,
        int dieRoll)
    {
        var face = quest.EnumerateMembers()
            .Where(a => a.isAlive && !a.isIncapacitated)
            .OrderByDescending(a => a.appearance)
            .First();
        int roll = Math.Clamp(dieRoll, 1, QudCombat.HIT_DIE);
        int modifier = QudCombat.Modifier(face.appearance);
        int total = roll + modifier;
        bool success = total >= SocialCheckDifficulty;
        string check = $"1d20={roll}{modifier:+#;-#;+0}={total} / 目標{SocialCheckDifficulty}（{face.name} APP{face.appearance}）";

        string title;
        string result;
        switch (kind)
        {
            case HumanEncounterKind.TravelingMerchant:
                title = "行商人との遭遇";
                if (!success)
                {
                    result = $"値段が折り合わず、取引を見送った（{check}）";
                    break;
                }

                int savings = MerchantSavingsPerQuestRank * Math.Max(1, quest.def.rank);
                quest.pendingLoot.Add(new RewardEntryData
                {
                    type = RewardType.Gold,
                    gold = savings,
                    quantity = 1,
                });
                string rareItem = total >= ExceptionalSocialResult
                    ? TryAddMerchantRareItem(quest)
                    : "";
                result = $"値切りに成功し、{savings}Gを節約した（帰還時に加算）"
                    + (rareItem.Length > 0 ? $"。さらに希少品「{rareItem}」を仕入れた" : "")
                    + $"（{check}）";
                break;

            default:
                title = "盗賊との遭遇";
                if (success)
                {
                    int restored = quest.morale.RestoreRate(BanditMoraleRate);
                    result = "交渉で争いを避けた"
                        + (restored > 0 ? $"。士気 +{restored}（{quest.morale.Current}/{quest.morale.Max}）" : "")
                        + $"（{check}）";
                }
                else
                {
                    int lost = quest.morale.Drain((int)Math.Ceiling(quest.morale.Max * BanditMoraleRate));
                    result = $"交渉がこじれ、消耗を強いられた。士気 -{lost}（{quest.morale.Current}/{quest.morale.Max}）（{check}）";
                    if (quest.morale.IsBroken)
                    {
                        quest.retreated = true;
                        quest.retreatReason = ExpeditionRetreatReason.MoraleBroken;
                        result += " → 士気崩壊で撤退";
                    }
                }
                break;
        }

        quest.logs.Add($"[Turn {currentTurn}] 対人遭遇: {title} - {result}");
        quest.AddReportEvent(
            currentTurn,
            quest.currentPhase,
            ExpeditionEventKind.Encounter,
            title,
            result,
            important: true,
            actorName: face.name);
        return result;
    }

    static string TryAddMerchantRareItem(QuestRun quest)
    {
        var item = quest.def.Dungeon?.treasureTable
            .Where(entry => entry.weight > 0
                && entry.type is RewardType.Equipment or RewardType.Consumable
                && (entry.Equipment != null || entry.Consumable != null))
            .OrderByDescending(entry => entry.Equipment?.rarity ?? Rarity.Common)
            .FirstOrDefault();
        if (item == null) return "";

        var loot = item.Copy();
        quest.pendingLoot.Add(loot);
        return RewardDescription.DescribeLoot(loot);
    }
}
