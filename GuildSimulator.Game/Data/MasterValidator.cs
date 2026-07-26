using GuildSimulator.Core.Models;

namespace GuildSimulator.Game.Data;

public static class MasterValidator
{
    public static List<string> Validate(GameMasterData db)
    {
        var errors = new List<string>();

        foreach (var a in db.allAdventurers)
        {
            if (string.IsNullOrWhiteSpace(a.id)) errors.Add("adventurers.json: idが空の項目があります");
            if (string.IsNullOrWhiteSpace(a.baseName)) errors.Add($"{a.id}: baseNameが空です");
            if (!string.IsNullOrEmpty(a.defaultClassId) && a.DefaultClass == null)
                errors.Add($"{a.id}: 不明なdefaultClassId '{a.defaultClassId}'");
            if (!string.IsNullOrEmpty(a.raceId) && a.Race == null)
                errors.Add($"{a.id}: 不明なraceId '{a.raceId}'");
            if (!string.IsNullOrEmpty(a.defaultWeaponId) && a.DefaultWeapon == null)
                errors.Add($"{a.id}: 不明なdefaultWeaponId '{a.defaultWeaponId}'");
            if (!string.IsNullOrEmpty(a.defaultArmorId) && a.DefaultArmor == null)
                errors.Add($"{a.id}: 不明なdefaultArmorId '{a.defaultArmorId}'");
        }

        foreach (var enemy in db.enemies.Values)
        foreach (var drop in enemy.dropTable)
        {
            bool resolved = drop.type switch
            {
                RewardType.Relic => drop.Relic != null,
                RewardType.Equipment => drop.Equipment != null,
                RewardType.Skill => drop.Skill != null,
                RewardType.Consumable => drop.Consumable != null,
                RewardType.Gold => drop.gold > 0,
                _ => false,
            };
            if (!resolved) errors.Add($"{enemy.id}: 解決できないドロップ設定があります ({drop.type})");
            if (drop.chance <= 0f || drop.chance > 1f)
                errors.Add($"{enemy.id}: drop chanceは0より大きく1以下にしてください");
        }

        foreach (var dungeon in db.dungeons.Values)
        {
            if (dungeon.turnEndEvents.Any(e => e.options.Count < 2))
                errors.Add($"{dungeon.id}: 選択イベントには2個以上の選択肢が必要です");

            // 宝箱の中身は帰還後に treasureTable から抽選する。空だと必ず空っぽになる。
            if (dungeon.treasureTable.Count == 0)
            {
                if (dungeon.eventTable.GetValueOrDefault(DungeonEventType.Treasure) > 0)
                    errors.Add($"{dungeon.id}: 宝箱イベントがあるのにtreasureTableが空です");
                if (dungeon.turnEndEvents.Any(
                        e => e.options.Any(o => o.effectType == QuestChoiceEffectType.Treasure)))
                    errors.Add($"{dungeon.id}: 宝箱の選択肢があるのにtreasureTableが空です");
            }
        }

        var questIds = db.allQuests.Select(q => q.id).ToHashSet();
        foreach (var quest in db.allQuests)
        {
            foreach (var requiredQuestId in quest.requiredQuestIds)
                if (!questIds.Contains(requiredQuestId))
                    errors.Add($"{quest.id}: 不明なrequiredQuestId '{requiredQuestId}'");
            foreach (var clueId in quest.requiredClueIds.Concat(quest.grantedClueIds))
                if (!db.clues.ContainsKey(clueId))
                    errors.Add($"{quest.id}: 不明なclueId '{clueId}'");

            // ボスドロップは1件ずつ確率抽選する。bossDropsAreGuaranteed のクエストだけ抽選しない。
            if (quest.bossDropsAreGuaranteed) continue;
            foreach (var drop in quest.bossDrops)
                if (drop.chance <= 0f || drop.chance > 1f)
                    errors.Add($"{quest.id}: ボスドロップのchanceは0より大きく1以下にしてください"
                        + "（確定で落としたいならbossDropsAreGuaranteedを使う）");
        }

        foreach (var clue in db.clues.Values)
        {
            if (string.IsNullOrWhiteSpace(clue.id))
                errors.Add("clues.json: idが空の項目があります");
            if (string.IsNullOrWhiteSpace(clue.title))
                errors.Add($"{clue.id}: titleが空です");
        }

        return errors;
    }
}
