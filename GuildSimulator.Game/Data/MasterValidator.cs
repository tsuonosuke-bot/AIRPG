using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;

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

        // 武器クラスの個性は「同じ武器種・同じ持ち手なら同じ値」で揃える。
        // Tier差は basePv とダメージダイスが表し、片手/両手の差は maxStatBonus などで表す。
        foreach (var group in db.equipment.Values
                     .Where(e => e.type == EquipmentType.Weapon && e.weaponType != WeaponType.Null)
                     .GroupBy(e => (e.weaponType, e.isTwoHanded)))
        {
            var head = group.First();
            string hands = head.isTwoHanded ? "両手" : "片手";
            foreach (var e in group.Skip(1))
            {
                if (e.maxStatBonus != head.maxStatBonus)
                    errors.Add($"{e.id}: maxStatBonusが同じ武器種({hands})の{head.id}({head.maxStatBonus})と違います({e.maxStatBonus})");
                if (e.Traits != head.Traits)
                    errors.Add($"{e.id}: 武器クラスの個性(armorPierce/armorShred/critRange/extraAttacks/offHandBonus)が"
                        + $"同じ武器種({hands})の{head.id}と違います");
            }
        }

        foreach (var e in db.equipment.Values)
        {
            if (e.critRange < 0 || e.critRange > QudCombat.MAX_CRIT_RANGE)
                errors.Add($"{e.id}: critRangeは0〜{QudCombat.MAX_CRIT_RANGE}にしてください");
            if (e.armorPierce < 0 || e.armorShred < 0 || e.extraAttacks < 0 || e.offHandBonus < 0)
                errors.Add($"{e.id}: armorPierce/armorShred/extraAttacks/offHandBonusは0以上にしてください");
            if (e.type != EquipmentType.Weapon && !e.Traits.Equals(WeaponTraits.None))
                errors.Add($"{e.id}: 武器以外に武器クラスの個性は設定できません");

            if (e.type == EquipmentType.Weapon)
            {
                // 弓と魔法は両手で構える。盾も二刀流も物理の近接武器だけの特権にする。
                bool mustBeTwoHanded = e.IsMagicWeapon || e.attackKind == AttackKind.Heal
                    || e.weaponType == WeaponType.Bow;
                if (mustBeTwoHanded && !e.isTwoHanded)
                    errors.Add($"{e.id}: 弓と魔法は両手武器にしてください（isTwoHanded: true）");
                if (e.isTwoHanded && e.offHandBonus > 0)
                    errors.Add($"{e.id}: 両手武器は左手に持てないのでoffHandBonusは設定できません");
            }

            if (e.IsShield)
            {
                if (e.blockChance <= 0)
                    errors.Add($"{e.id}: 盾にはblockChance（受け率%）が必要です");
                if (e.blockAv <= 0)
                    errors.Add($"{e.id}: 盾にはblockAv（受け成功時に乗る装甲）が必要です");
                // 盾の装甲は受けに成功したときだけ乗る。bonus.av に書くと常時加算になってしまう。
                if (e.bonus.av != 0)
                    errors.Add($"{e.id}: 盾の装甲はbonus.avではなくblockAvに書いてください（bonus.avは常時加算されます）");
                if (!e.GetAllowedSlots().Contains(EquipSlot.LeftHand) || e.GetAllowedSlots().Count != 1)
                    errors.Add($"{e.id}: 盾のallowedSlotsは[\"LeftHand\"]にしてください");
            }
            else if (e.blockChance > 0 || e.blockAv > 0)
            {
                errors.Add($"{e.id}: 盾(type:3)以外にblockChance/blockAvは設定できません");
            }
        }

        foreach (var enemy in db.enemies.Values)
        {
            // 脅威度は冒険者ランクと同じ物差し。範囲外だと士気の格上ショックが意図とずれる。
            if (enemy.threat < Rank.Min || enemy.threat > Rank.Max)
                errors.Add($"{enemy.id}: threatは{Rank.Min}〜{Rank.Max}"
                    + $"（{Rank.Label(Rank.Min)}〜{Rank.Label(Rank.Max)}）にしてください（現在{enemy.threat}）");
            if (enemy.DefaultShield != null && !enemy.DefaultShield.IsShield)
                errors.Add($"{enemy.id}: defaultShieldIdに盾でない装備が指定されています");
            if (enemy.DefaultOffHand != null && enemy.DefaultOffHand.type != EquipmentType.Weapon)
                errors.Add($"{enemy.id}: defaultOffHandIdに武器でない装備が指定されています");
            if (enemy.DefaultWeapon is { isTwoHanded: true }
                && (enemy.DefaultOffHand != null || enemy.DefaultShield != null))
                errors.Add($"{enemy.id}: 両手武器を持たせているので左手（defaultOffHandId/defaultShieldId）は使えません");
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

        foreach (var facility in db.facilities.Values)
        {
            if (string.IsNullOrWhiteSpace(facility.id)) errors.Add("facilities.json: idが空の項目があります");
            if (string.IsNullOrWhiteSpace(facility.displayName)) errors.Add($"{facility.id}: displayNameが空です");
            if (facility.buildCostGold < 0) errors.Add($"{facility.id}: buildCostGoldは0以上にしてください");
            if (facility.upkeepGoldPerTurn < 0) errors.Add($"{facility.id}: upkeepGoldPerTurnは0以上にしてください");
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
