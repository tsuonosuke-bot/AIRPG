using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.Systems.Quest;

public class QuestRewardService
{
    /// <summary>GP進行を早めず、資金と育成だけを少し厚くする基本報酬係数。</summary>
    public const float BaseGoldRewardMultiplier = 1.15f;
    public const float BaseExpRewardMultiplier = 1.15f;

    public static int AdjustedBaseGold(int baseGold) =>
        (int)Math.Ceiling(Math.Max(0, baseGold) * BaseGoldRewardMultiplier);

    public static int AdjustedBaseExp(int baseExp) =>
        (int)Math.Floor(Math.Max(0, baseExp) * BaseExpRewardMultiplier);

    /// <summary>
    /// 撤退時に受け取れる基本報酬の割合。道中の戦利品は減額せず全て持ち帰れる。
    /// 一度引き返しただけで遠征費が丸ごと焦げ付くと、失敗が即立て直し不能につながる。
    /// 依頼の達成そのものはギルドポイントで報いるので、撤退では金銭だけを部分的に支払う。
    /// </summary>
    public const float RetreatRewardRate = 0.4f;

    /// <summary>
    /// 撤退報酬の割合を日本語の文へそのまま差し込める表記にしたもの。
    /// パーセント書式は実行環境によって「40 %」と空白が入るので、画面表示ではこちらを使う。
    /// </summary>
    public static string RetreatRewardRateText =>
        $"{(int)Math.Round(RetreatRewardRate * 100)}%";

    /// <summary>宝箱がハズレ（空っぽ）になる確率。ボスの宝箱はこの抽選を受けない。</summary>
    public const float EmptyChestRate = 0.1f;

    /// <summary>
    /// 持ち帰った宝箱を開ける。中身は開封時に抽選するので、道中では何が入っているか分からない。
    /// 出た中身は pendingLoot に積み、付与自体は ApplyPendingLoot に任せる。
    /// </summary>
    public void OpenChests(QuestRun q, GuildManager guild, string prefix)
    {
        foreach (var chest in q.chests)
        {
            bool usesKey = !chest.IsBossChest && q.guaranteedNonEmptyChestCount > 0;
            if (usesKey) q.guaranteedNonEmptyChestCount--;
            var contents = chest.IsBossChest
                ? RollBossChest(q)
                : RollDungeonChest(q, guild, skipEmptyRoll: usesKey);
            string keyTag = usesKey ? "（盗掘者の合鍵を使用）" : "";

            if (contents.Count == 0)
            {
                q.logs.Add($"{prefix} {chest.Label}を開けた{keyTag} → 空っぽだった");
                continue;
            }

            q.pendingLoot.AddRange(contents);
            string found = string.Join("、", contents.Select(
                e => RewardDescription.DescribeLoot(e) + RewardDescription.DescribeQuantity(e)));
            q.logs.Add($"{prefix} {chest.Label}を開けた{keyTag} → {found}");
        }
        q.chests.Clear();
    }

    // ボスの宝箱はクエストのボスドロップから。エントリごとの chance 抽選は残るが、
    // 空っぽ抽選は受けない（bossDropsAreGuaranteed なら chance も無視して全部入る）。
    static List<RewardEntryData> RollBossChest(QuestRun q)
    {
        var contents = new List<RewardEntryData>();
        foreach (var entry in q.def.bossDrops)
        {
            if (RelicSystem.IsFrozenRelicReward(entry)) continue;
            if (!q.def.bossDropsAreGuaranteed
                && (entry.chance <= 0f || GameRandom.NextFloat() >= entry.chance)) continue;
            contents.Add(entry.Copy());
        }
        return contents;
    }

    // 道中の宝箱はダンジョンの宝箱テーブルから、クエストランクに合う中身を1件。一定確率で空っぽ。
    // 所持済みの遺物は開けても捨てるだけなので抽選から外す。
    // 遺物システムの凍結中は遺物エントリを丸ごと除外し、残りの中身で抽選し直す
    // （重みの合計を取り直すので、他の中身の出やすさの比率は変わらない）。
    static List<RewardEntryData> RollDungeonChest(
        QuestRun q, GuildManager guild, bool skipEmptyRoll = false)
    {
        if (!skipEmptyRoll && GameRandom.NextFloat() < EmptyChestRate) return new();

        var table = q.def.Dungeon?.treasureTable;
        if (table == null) return new();

        var candidates = table
            .Where(e => e.weight > 0
                && q.def.rank >= e.minQuestRank
                && q.def.rank <= e.maxQuestRank
                && !RelicSystem.IsFrozenRelicReward(e)
                && !IsOwnedRelic(e, guild))
            .ToList();

        int total = 0;
        foreach (var e in candidates) total += e.weight;
        if (total <= 0) return new();

        int roll = GameRandom.Range(0, total);
        foreach (var e in candidates)
        {
            roll -= e.weight;
            if (roll < 0) return new() { e.Copy() };
        }
        return new();
    }

    static bool IsOwnedRelic(RewardEntryData e, GuildManager guild) =>
        e.type == RewardType.Relic && e.Relic != null && guild.relics.Contains(e.Relic);

    public void ApplyBaseRewards(QuestRun q, GuildManager guild, string prefix)
    {
        int baseGold = q.def.rewardGold;
        int adjustedBaseGold = AdjustedBaseGold(baseGold);

        // 目標数はクエスト達成のための納品分。目標を超えた余剰分だけを買い取る。
        int surplusGathered = q.def.IsGatherQuest
            ? Math.Max(0, q.gatheredCount - q.def.gatherTargetCount)
            : 0;
        int gatherGold = surplusGathered * q.def.gatherGoldPerItem;
        if (gatherGold > 0)
            q.logs.Add($"{prefix} {q.def.gatherItemName} 余剰 {surplusGathered}個 買取 +{gatherGold}G"
                + (q.retreated ? $"（撤退のため {RetreatRewardRateText} 支給）" : ""));

        float rate = q.retreated ? RetreatRewardRate : 1f;
        if (q.retreated)
            q.logs.Add($"{prefix} 撤退のため基本報酬は {RetreatRewardRateText}（戦利品はそのまま持ち帰り）");

        // 連れて行った顔ぶれのスキル（値切り・目利き・教導など）は報酬そのものに効く。
        var partySkills = PartySkillEffects.Of(q.formation);

        int gold = (int)Math.Floor((adjustedBaseGold + gatherGold) * rate
            * RelicSystem.GetGoldRewardMultiplier() * (1f + q.goldRewardBonusPercent / 100f)
            * partySkills.GoldMultiplier);
        guild.AddGold(gold, $"クエスト報酬: {q.def.questName}");
        string goldSkillNote = partySkills.goldPercent != 0 ? $" / スキル {partySkills.goldPercent:+#;-#;0}%" : "";
        if (q.retreated)
        {
            int fullGold = adjustedBaseGold + gatherGold;
            q.logs.Add($"{prefix} 資金 +{gold}G"
                + $"（撤退のため基本報酬{fullGold}Gの{RetreatRewardRateText}のみ支給）");
        }
        else
        {
            q.logs.Add($"{prefix} 資金 +{gold}G（基本 {baseGold}G + 活躍手当 {adjustedBaseGold - baseGold}G"
                + $"{(gatherGold > 0 ? $" + 買取 {gatherGold}G" : "")}{goldSkillNote}）");
        }

        int totalExp = (int)Math.Floor(
            q.def.rewardExp * BaseExpRewardMultiplier * rate
            * (1f + q.expRewardBonusPercent / 100f) * partySkills.ExpMultiplier);
        var members = q.formation.Where(x => x != null).ToList();
        int memberCount = members.Count;
        for (int i = 0; i < memberCount; i++)
        {
            var a = members[i]!;
            int questExp = ExperienceRewardSplitter.ShareFor(totalExp, memberCount, i);
            int levelBefore = a.level;
            bool wasAtLevelCap = a.IsAtLevelCap;
            a.AddExperience(questExp, out var ups, out var grownStats);
            if (ups > 0) q.RecordLevelGrowth(a.id, grownStats);
            string levelUpText = ups > 0
                ? $"（レベルアップ {levelBefore}lv→{a.level}lv、{QuestManager.FormatGrownStats(grownStats)}）"
                : "";
            string expText = wasAtLevelCap
                ? $"経験値は{a.RankLabel}ランク上限Lv{a.LevelCap}のため蓄積されない"
                : $"経験値 +{questExp}{levelUpText}";
            q.logs.Add($"{prefix} {a.name} {expText}");
        }

        // ギルドポイントは達成の証なので撤退では入らない。
        if (q.def.rewardGuildPoints != 0 && !q.retreated)
        {
            int appearanceBonus = AppearanceSystem.GuildPointBonus(q.def.rewardGuildPoints, q.formation);
            int guildPoints = q.def.rewardGuildPoints + appearanceBonus;
            // 昇格試験の報酬は累積実績には残すが、昇格後の次試験進捗には持ち越さない。
            guild.AddGuildPoints(
                guildPoints,
                $"クエストGP: {q.def.questName}",
                countTowardRankProgress: q.def.rankUpOnClear <= 0);
            q.logs.Add($"{prefix} ギルドポイント +{guildPoints}"
                + (appearanceBonus > 0 ? $"（容姿ボーナス +{appearanceBonus}）" : ""));
        }
    }

    // 持ち帰った戦利品を付与する（全滅以外で呼ばれる。宝箱の中身は OpenChests 済み）。
    public void ApplyPendingLoot(QuestRun q, GuildManager guild, string prefix)
    {
        foreach (var e in q.pendingLoot)
        {
            switch (e.type)
            {
                case RewardType.Gold:
                    guild.AddGold(e.gold, $"戦利品: {q.def.questName}");
                    q.logs.Add($"{prefix} 戦利品 資金 +{e.gold}G");
                    break;
                case RewardType.Relic:
                    // 凍結前のセーブに積まれたままの遺物は、黙って無かったことにする。
                    if (RelicSystem.IsFrozenRelicReward(e) || e.Relic == null) break;
                    if (guild.relics.Contains(e.Relic))
                        q.logs.Add($"{prefix} 戦利品 遺物「{e.Relic.relicName}」は所持済みのため見送り");
                    else { guild.AddRelic(e.Relic, q.def.questName); q.logs.Add($"{prefix} 戦利品 遺物入手: {e.Relic.relicName}"); }
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
                    q.logs.Add($"{prefix} 戦利品 消費アイテム入手: {e.Consumable.displayName} x{Math.Max(1, e.quantity)}");
                    break;
            }
        }
    }

}
