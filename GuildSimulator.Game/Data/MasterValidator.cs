using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;

namespace GuildSimulator.Game.Data;

public static class MasterValidator
{
    public static List<string> Validate(GameMasterData db)
    {
        var errors = new List<string>();

        // MasterLoader は解決できないIDを黙って読み飛ばすので、そのままだと
        // 「エラーは出ないがゲーム内に一生出てこない」データができる。ここで必ず落とす。
        errors.AddRange(db.unresolvedRefs);

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

        foreach (var ev in db.choiceEvents.Values)
        {
            bool hasPurchase = ev.options.Any(option =>
                option.Outcomes.Any(outcome => outcome.effectType == QuestChoiceEffectType.Purchase));
            if (hasPurchase && !ev.options.Any(option =>
                    option.Outcomes.All(outcome => outcome.effectType != QuestChoiceEffectType.Purchase)))
                errors.Add($"{ev.id}: 購入イベントには資金を使わない選択肢も1つ必要です");

            foreach (var option in ev.options)
            {
                // 結果テーブルの重みが全部0だと抽選できず、常に先頭の結果になってしまう。
                if (option.outcomes.Count > 0 && option.outcomes.Sum(o => Math.Max(0, o.weight)) <= 0)
                    errors.Add($"{ev.id}: 選択肢「{option.text}」の結果テーブルの重みが全て0です");

                if (option.outcomes.Count > 0
                    && option.Outcomes.Any(outcome => outcome.effectType == QuestChoiceEffectType.Purchase))
                    errors.Add($"{ev.id}: 選択肢「{option.text}」では購入をoutcomesに入れられません");

                foreach (var outcome in option.Outcomes)
                {
                    bool needsMember = outcome.effectType is
                        QuestChoiceEffectType.AdventurerStatUp or QuestChoiceEffectType.AdventurerStatDown
                        or QuestChoiceEffectType.AdventurerSkill or QuestChoiceEffectType.AdventurerDamage;
                    if (needsMember && !option.targetsOneMember)
                        errors.Add($"{ev.id}: 選択肢「{option.text}」は隊員1人に効く効果"
                            + $"（{outcome.effectType}）を持つので targetsOneMember を true にしてください");

                    if (outcome.effectType == QuestChoiceEffectType.AdventurerSkill && outcome.Skill == null)
                        errors.Add($"{ev.id}: 選択肢「{option.text}」のスキル付与で"
                            + $"不明なskillId '{outcome.targetId}' が指定されています");

                    if (outcome.effectType == QuestChoiceEffectType.Purchase)
                    {
                        if (outcome.value <= 0)
                            errors.Add($"{ev.id}: 選択肢「{option.text}」の購入価格は1G以上にしてください");
                        if ((outcome.Equipment == null) == (outcome.Consumable == null))
                            errors.Add($"{ev.id}: 選択肢「{option.text}」の購入対象 '{outcome.targetId}' は"
                                + "装備または消耗品のどちらか1件に解決できる必要があります");
                    }

                    if (outcome.effectType is QuestChoiceEffectType.AdventurerStatUp
                            or QuestChoiceEffectType.AdventurerStatDown
                        && !string.IsNullOrEmpty(outcome.targetId)
                        && !Enum.TryParse<StatType>(outcome.targetId, ignoreCase: true, out _))
                        errors.Add($"{ev.id}: 選択肢「{option.text}」の能力指定 '{outcome.targetId}' が不明です"
                            + "（空にするとランダム）");
                }
            }
        }

        foreach (var dungeon in db.dungeons.Values)
        {
            if (dungeon.turnEndEvents.Any(e => e.options.Count < 2))
                errors.Add($"{dungeon.id}: 選択イベントには2個以上の選択肢が必要です");

            foreach (var reward in dungeon.treasureTable)
            {
                if (reward.minQuestRank < Rank.Min || reward.maxQuestRank > Rank.Max
                    || reward.minQuestRank > reward.maxQuestRank)
                    errors.Add($"{dungeon.id}: treasureTableの依頼ランク範囲"
                        + $" {reward.minQuestRank}〜{reward.maxQuestRank} が不正です");
            }

            // 宝箱の中身は帰還後に treasureTable から抽選する。空だと必ず空っぽになる。
            if (dungeon.treasureTable.Count == 0)
            {
                if (dungeon.eventTable.GetValueOrDefault(DungeonEventType.Treasure) > 0)
                    errors.Add($"{dungeon.id}: 宝箱イベントがあるのにtreasureTableが空です");
                if (dungeon.turnEndEvents.Any(
                        e => e.options.Any(o => o.effectType == QuestChoiceEffectType.Treasure)))
                    errors.Add($"{dungeon.id}: 宝箱の選択肢があるのにtreasureTableが空です");
            }

            // 遺物システムの凍結中、遺物エントリは抽選候補から外れる。
            // 遺物しか残らないテーブルは必ず空っぽになるので、他の中身も置いておく。
            if (!GameFeatures.RelicsEnabled
                && dungeon.treasureTable.Count > 0
                && !dungeon.treasureTable.Any(e => e.weight > 0 && e.type != RewardType.Relic))
                errors.Add($"{dungeon.id}: treasureTableに遺物以外の中身がありません"
                    + "（遺物システムは凍結中なので、宝箱が必ず空っぽになります）");
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

            // 同じく、遺物しか入っていないボスの宝箱は凍結中に必ず空っぽになる。
            if (!GameFeatures.RelicsEnabled
                && quest.bossDrops.Count > 0
                && quest.bossDrops.All(d => d.type == RewardType.Relic))
                errors.Add($"{quest.id}: bossDropsに遺物以外の中身がありません"
                    + "（遺物システムは凍結中なので、ボスの宝箱が必ず空っぽになります）");

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

        ValidateTraits(db, errors);

        return errors;
    }

    /// <summary>
    /// 特性の設計判断をデータ検査として固定する。
    ///
    /// 芯は「<b>代償は先払い</b>」。特性は原則として利点と欠点を併せ持つ諸刃であり、
    /// 欠点のない素直な強化を出してよいのは、リスク記録（瀕死・戦闘不能・仲間の死線）を
    /// 解禁条件に含めているときだけ。欠点の有無は宣言ではなくスキルの数値そのものから
    /// 導いている（<see cref="TraitAnalysis"/>）ので、数値だけ書き換えて規則をすり抜けることはできない。
    /// </summary>
    static void ValidateTraits(GameMasterData db, List<string> errors)
    {
        var byFamily = new Dictionary<string, string>();

        foreach (var trait in db.traits.Values)
        {
            if (string.IsNullOrWhiteSpace(trait.id))
            {
                errors.Add("traits.json: idが空の項目があります");
                continue;
            }
            if (string.IsNullOrWhiteSpace(trait.traitName))
                errors.Add($"{trait.id}: traitNameが空です");
            if (trait.requirements.Count == 0)
                errors.Add($"{trait.id}: requirementsが空です（条件のない特性は永久に開花しません）");

            if (trait.Skill == null) continue; // 未解決IDは unresolvedRefs 側で報告済み

            // 「代償は先払い」は担い手の型ごとに成り立っていなければならない。
            // 物理前提で書いた欠点は術者にはタダになり（リスクなしの純粋強化）、
            // 物理前提の利点は術者には消える（代償だけの罰則）。宣言した型それぞれで検査する。
            foreach (var lens in trait.Builds)
            {
                var effect = TraitAnalysis.Evaluate(trait.Skill, lens);
                string who = TraitAnalysis.LensName(lens);

                if (effect.Benefits.Count == 0)
                    errors.Add($"{trait.id}: {who}型には利点が1つも残りません"
                        + "（その型を builds から外すか、その型に効く数値を足してください）");

                if (effect.Drawbacks.Count == 0 && !trait.RequiresRisk)
                    errors.Add($"{trait.id}: {who}型には代償がないのにリスク記録を要求していません"
                        + $"（リスク記録は {string.Join("／", ExpeditionRecordTypes.All
                            .Where(ExpeditionRecordTypes.IsRisk)
                            .Select(ExpeditionRecordTypes.DisplayName))}）。"
                        + "素直な強化の代価は先払いにする設計です");
            }

            // 同じ family は最上位1つしか効かない。職業マスタリーと family を共有すると
            // 特性がマスタリーを黙って押しのけるので、名前空間の衝突をここで止める。
            string family = trait.Skill.family;
            if (string.IsNullOrWhiteSpace(family))
            {
                errors.Add($"{trait.id}: 特性のスキル {trait.Skill.id} には固有のfamilyを付けてください");
                continue;
            }
            if (byFamily.TryGetValue(family, out string? owner))
                errors.Add($"{trait.id}: family '{family}' が {owner} と重複しています"
                    + "（同じfamilyは最上位1つしか効きません）");
            else
                byFamily[family] = trait.id;

            foreach (var other in db.skills.Values)
            {
                if (other == trait.Skill || other.family != family) continue;
                errors.Add($"{trait.id}: family '{family}' を特性以外のスキル {other.id} と共有しています");
                break;
            }
        }
    }
}
