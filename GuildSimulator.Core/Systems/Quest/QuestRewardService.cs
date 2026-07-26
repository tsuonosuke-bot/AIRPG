using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.Systems.Quest;

public class QuestRewardService
{
    /// <summary>撤退時に受け取れる基本報酬の割合。道中の戦利品は減額せず全て持ち帰れる。</summary>
    public const float RetreatRewardRate = 0f;

    public void ApplyBaseRewards(QuestRun q, GuildManager guild, string prefix)
    {
        int baseGold = q.def.rewardGold;

        // 目標数はクエスト達成のための納品分。目標を超えた余剰分だけを買い取る。
        int surplusGathered = q.def.IsGatherQuest
            ? Math.Max(0, q.gatheredCount - q.def.gatherTargetCount)
            : 0;
        int gatherGold = surplusGathered * q.def.gatherGoldPerItem;
        if (gatherGold > 0)
            q.logs.Add($"{prefix} {q.def.gatherItemName} 余剰 {surplusGathered}個 買取 +{gatherGold}G");

        float rate = q.retreated ? RetreatRewardRate : 1f;
        if (q.retreated)
            q.logs.Add($"{prefix} 撤退のため基本報酬は {RetreatRewardRate:P0}（戦利品はそのまま持ち帰り）");

        int gold = (int)Math.Floor((baseGold + gatherGold) * rate
            * RelicSystem.GetGoldRewardMultiplier() * (1f + q.goldRewardBonusPercent / 100f));
        guild.AddGold(gold, $"クエスト報酬: {q.def.questName}");
        q.logs.Add($"{prefix} 資金 +{gold}G（基本 {baseGold}{(gatherGold > 0 ? $" + 買取 {gatherGold}" : "")}）");

        int totalExp = (int)Math.Floor(q.def.rewardExp * rate * (1f + q.expRewardBonusPercent / 100f));
        var members = q.formation.Where(x => x != null).ToList();
        int memberCount = members.Count;
        int share = memberCount > 0 ? totalExp / memberCount : 0;
        int remainder = memberCount > 0 ? totalExp % memberCount : 0;
        for (int i = 0; i < memberCount; i++)
        {
            var a = members[i]!;
            int questExp = share + (i < remainder ? 1 : 0);
            int levelBefore = a.level;
            a.AddExperience(questExp, out var ups);
            string levelUpText = ups > 0 ? $"（レベルアップ {levelBefore}lv→{a.level}lv）" : "";
            q.logs.Add($"{prefix} {a.name} 経験値 +{questExp}{levelUpText}");
        }

        // ギルドポイントは達成の証なので撤退では入らない。
        if (q.def.rewardGuildPoints != 0 && !q.retreated)
        {
            guild.AddGuildPoints(q.def.rewardGuildPoints, $"クエストGP: {q.def.questName}");
            q.logs.Add($"{prefix} ギルドポイント +{q.def.rewardGuildPoints}");
        }
    }

    // 道中の宝箱で拾った戦利品を付与する（クエスト成功時のみ呼ばれる想定）。
    public void ApplyPendingLoot(QuestRun q, GuildManager guild, string prefix)
    {
        foreach (var e in q.pendingLoot)
        {
            switch (e.type)
            {
                case RewardType.Gold:
                    guild.AddGold(e.gold, $"宝箱: {q.def.questName}");
                    q.logs.Add($"{prefix} 宝箱 資金 +{e.gold}G");
                    break;
                case RewardType.Relic:
                    if (e.Relic == null) break;
                    if (guild.relics.Contains(e.Relic))
                        q.logs.Add($"{prefix} 宝箱 遺物「{e.Relic.relicName}」は所持済みのため見送り");
                    else { guild.AddRelic(e.Relic, q.def.questName); q.logs.Add($"{prefix} 宝箱 遺物入手: {e.Relic.relicName}"); }
                    break;
                case RewardType.Equipment:
                    if (e.Equipment == null) break;
                    guild.AddEquipment(e.Equipment, Math.Max(1, e.quantity), "戦利品");
                    q.logs.Add($"{prefix} 戦利品 装備入手: {e.Equipment.displayName} x{Math.Max(1, e.quantity)}");
                    break;
                case RewardType.Skill:
                    if (e.Skill != null)
                    {
                        var learner = q.EnumerateMembers().FirstOrDefault(a => a.isAlive && a.LearnPermanentSkill(e.Skill));
                        q.logs.Add(learner != null
                            ? $"{prefix} {learner.name}がスキル「{e.Skill.skillName}」を習得"
                            : $"{prefix} スキル「{e.Skill.skillName}」は全員習得済み");
                    }
                    break;
                case RewardType.Consumable:
                    if (e.Consumable == null) break;
                    guild.AddConsumable(e.Consumable, Math.Max(1, e.quantity));
                    q.logs.Add($"{prefix} 消費アイテム入手: {e.Consumable.displayName} x{Math.Max(1, e.quantity)}");
                    break;
            }
        }
    }

}
