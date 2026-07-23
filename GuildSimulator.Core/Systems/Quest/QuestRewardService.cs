using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.Systems.Quest;

public class QuestRewardService
{
    /// <summary>撤退時に受け取れる基本報酬の割合。道中の戦利品は減額せず全て持ち帰れる。</summary>
    public const float RetreatRewardRate = 0f;


    public List<RewardOption> BuildRewardOptions(QuestRun q, GuildManager guild, int defaultChoiceCount = 3)
    {
        var d = q.def.Dungeon;
        int choiceCount = d != null
            ? GameRandom.Range(d.rewardChoiceMin, d.rewardChoiceMax + 1)
            : defaultChoiceCount;

        var options = new List<RewardOption>();

        if (q.bossDefeated)
        {
            foreach (var e in q.def.bossDrops)
            {
                var opt = ToOption(e);
                if (opt != null) options.Add(opt);
            }
        }

        if (d?.rewardTable != null)
        {
            int guard = 200;
            while (options.Count < choiceCount && guard-- > 0)
            {
                var picked = PickWeighted(d.rewardTable);
                if (picked == null) break;
                var opt = ToOption(picked);
                if (opt == null) continue;
                if (IsDuplicateChoice(opt, options)) continue;
                if (picked.unique && IsAlreadyOwned(opt, guild)) continue;
                options.Add(opt);
            }
        }

        if (options.Count == 0)
            options.Add(new RewardOption { type = RewardType.Gold, gold = 50 });
        if (options.Count > choiceCount)
            options = options.Take(choiceCount).ToList();

        return options;
    }

    public void ApplyBaseRewards(QuestRun q, GuildManager guild, string prefix)
    {
        int baseGold = q.def.rewardGold;

        // 採取クエストは納品数に応じた買取分を基本報酬に上乗せする。
        int gatherGold = q.def.IsGatherQuest ? q.gatheredCount * q.def.gatherGoldPerItem : 0;
        if (gatherGold > 0)
            q.logs.Add($"{prefix} {q.def.gatherItemName} {q.gatheredCount}個 買取 +{gatherGold}G");

        float rate = q.retreated ? RetreatRewardRate : 1f;
        if (q.retreated)
            q.logs.Add($"{prefix} 撤退のため基本報酬は {RetreatRewardRate:P0}（戦利品はそのまま持ち帰り）");

        int gold = (int)Math.Floor((baseGold + gatherGold) * rate * RelicSystem.GetGoldRewardMultiplier());
        guild.AddGold(gold, $"クエスト報酬: {q.def.questName}");
        q.logs.Add($"{prefix} 資金 +{gold}G（基本 {baseGold}{(gatherGold > 0 ? $" + 買取 {gatherGold}" : "")}）");

        int questExp = (int)Math.Floor(q.def.rewardExp * rate);
        foreach (var a in q.formation.Where(x => x != null))
        {
            a!.AddExperience(questExp, out var ups);
            q.logs.Add($"{prefix} {a.name} 経験値 +{questExp}（レベルアップ +{ups}）");
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
                    guild.AddEquipment(e.Equipment, 1, "宝箱");
                    q.logs.Add($"{prefix} 宝箱 装備入手: {e.Equipment.displayName}");
                    break;
                case RewardType.Skill:
                    if (e.Skill != null) q.logs.Add($"{prefix} 宝箱 スキル「{e.Skill.skillName}」（付与先未実装）");
                    break;
            }
        }
    }

    public void ApplyChosenReward(QuestRun q, RewardOption opt, GuildManager guild)
    {
        switch (opt.type)
        {
            case RewardType.Relic:
                if (opt.relic != null) { guild.AddRelic(opt.relic, q.def.questName); q.logs.Add($"[選択報酬] 遺物: {opt.relic.relicName}"); }
                break;
            case RewardType.Equipment:
                if (opt.equipment != null)
                {
                    int quantity = Math.Max(1, opt.quantity);
                    guild.AddEquipment(opt.equipment, quantity, "報酬");
                    q.logs.Add($"[選択報酬] 装備: {opt.equipment.displayName} x{quantity}");
                }
                break;
            case RewardType.Skill:
                if (opt.skill != null) q.logs.Add($"[選択報酬] スキル: {opt.skill.skillName}（未実装）");
                break;
            case RewardType.Gold:
                guild.AddGold(opt.gold, $"選択報酬: {q.def.questName}"); q.logs.Add($"[選択報酬] 資金 +{opt.gold}G");
                break;
        }
    }

    RewardEntryData? PickWeighted(List<RewardEntryData> table)
    {
        int sum = table.Where(e => e.weight > 0).Sum(e => e.weight);
        if (sum <= 0) return null;
        int r = GameRandom.Range(0, sum);
        int acc = 0;
        foreach (var e in table)
        {
            if (e.weight <= 0) continue;
            acc += e.weight;
            if (r < acc) return e;
        }
        return null;
    }

    static bool IsDuplicateChoice(RewardOption opt, List<RewardOption> options) =>
        options.Any(o =>
            (opt.type == RewardType.Relic && o.type == RewardType.Relic && o.relic == opt.relic) ||
            (opt.type == RewardType.Equipment && o.type == RewardType.Equipment && o.equipment == opt.equipment) ||
            (opt.type == RewardType.Skill && o.type == RewardType.Skill && o.skill == opt.skill) ||
            (opt.type == RewardType.Gold && o.type == RewardType.Gold && o.gold == opt.gold));

    static bool IsAlreadyOwned(RewardOption opt, GuildManager guild)
    {
        if (opt.type == RewardType.Relic && opt.relic != null && guild.relics.Contains(opt.relic)) return true;
        return false;
    }

    static RewardOption? ToOption(RewardEntryData e) => new()
    {
        type = e.type, relic = e.Relic, equipment = e.Equipment, skill = e.Skill, gold = e.gold,
    };
}
