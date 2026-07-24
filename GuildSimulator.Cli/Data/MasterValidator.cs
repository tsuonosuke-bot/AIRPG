using GuildSimulator.Core.Models;

namespace GuildSimulator.Cli.Data;

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
            if (dungeon.turnEndEvents.Any(e => e.options.Count < 2))
                errors.Add($"{dungeon.id}: 選択イベントには2個以上の選択肢が必要です");

        return errors;
    }
}
