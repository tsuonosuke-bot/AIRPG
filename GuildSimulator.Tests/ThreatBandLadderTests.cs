using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;
using Xunit.Abstractions;

namespace GuildSimulator.Tests;

/// <summary>
/// F・E・D帯の敵を<b>ひとつの物差し</b>で測り、帯が上がるほど手強いことを確かめる。
///
/// <see cref="FEnemyBalanceTests"/> はF帯だけを「駆け出し（Lv1）の敗北率」で見るので、
/// 帯をまたいだ比較ができない。ここでは全帯を同じ基準の冒険者へぶつけ、
/// 「F帯の敵がE帯の敵より手強い」といった帯の逆転を検出する。
///
/// 基準は<b>レベル上限まで育ててEへ昇格したての冒険者</b>（Lv5・ランクE）。
/// 昇格したてを選ぶのは、E帯・D帯の依頼を受け始めるのがちょうどこの時点だから
/// （適正ランクは自分のランクから2つ上まで＝E・D・Cが適正になる）。
/// 装備はE帯で買える <c>shopTier</c> 2 までの、その職業の得物・防具に揃える。
///
/// 指標は<b>平均残HP率</b>（敗北した回は0%として算入）。敗北率は上の帯以外
/// ほぼ0%に張り付いて分解能がないため、削られ具合で手強さを測る。
/// </summary>
public class ThreatBandLadderTests
{
    /// <summary>E帯で商店に並ぶ装備の上限（MASTER_DATA.md「帯の対応」）。</summary>
    const int ShopTierAtRankE = 2;

    /// <summary>1組み合わせあたりの試行数。seed固定なので結果は毎回同じになる。</summary>
    const int RunsPerMatchup = 100;

    /// <summary>
    /// 帯ごとの残HP率の窓。帯全体がまとめてずれたときに気づくための粗い網で、
    /// 帯の順序そのものは下の「帯の逆転」検査が見る。窓を狭めすぎると、
    /// 関係のない調整のたびに落ちる番人になってしまう。
    /// </summary>
    static readonly Dictionary<int, (double Min, double Max)> Windows = new()
    {
        [1] = (80.0, 100.0),
        [2] = (45.0, 76.0),
        [3] = (0.0, 44.0),
    };

    [Fact]
    public void HigherThreatBandsAreStrictlyHarderOnOneCommonYardstick()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var measured = Measure(db);

        var failures = new List<string>();
        foreach (var (threat, window) in Windows)
        {
            foreach (var row in measured.Where(r => r.Threat == threat))
                if (row.RemainingHpPercent < window.Min || row.RemainingHpPercent > window.Max)
                    failures.Add($"脅威{threat} {row.Name}: 残HP {row.RemainingHpPercent:F1}% が"
                        + $"帯の窓 {window.Min:F0}〜{window.Max:F0}% の外です");
        }

        // 帯の逆転そのもの。上の帯のいちばん楽な敵が、下の帯のいちばん手強い敵より楽ではいけない。
        foreach (int threat in new[] { 2, 3 })
        {
            var upper = measured.Where(r => r.Threat == threat).ToList();
            var lower = measured.Where(r => r.Threat == threat - 1).ToList();
            var easiestUpper = upper.MaxBy(r => r.RemainingHpPercent)!;
            var hardestLower = lower.MinBy(r => r.RemainingHpPercent)!;
            if (easiestUpper.RemainingHpPercent >= hardestLower.RemainingHpPercent)
                failures.Add($"帯の逆転: 脅威{threat} の {easiestUpper.Name}"
                    + $"（残HP {easiestUpper.RemainingHpPercent:F1}%）が"
                    + $"脅威{threat - 1} の {hardestLower.Name}"
                    + $"（残HP {hardestLower.RemainingHpPercent:F1}%）より楽です");
        }

        Assert.True(failures.Count == 0,
            string.Join("\n", failures.Prepend("帯の物差しから外れた敵:"))
                + "\n\n--- 実測（残HP降順）---\n"
                + string.Join("\n", measured
                    .OrderByDescending(r => r.RemainingHpPercent)
                    .Select(r => $"  脅威{r.Threat} 残HP{r.RemainingHpPercent,5:F1}% "
                        + $"敗北{r.LossPercent,5:F1}%  {r.Name}")));
    }

    readonly record struct BandRow(int Threat, string Name, double LossPercent, double RemainingHpPercent);

    static List<BandRow> Measure(GameMasterData db)
    {
        // 物差しは<b>Commonの駆け出し</b>だけにする。ここをF帯の全員にすると、
        // 素質の高い冒険者（レアリティ1段につき素質+5）や、得物に結びついた
        // ユニークスキル持ちを名簿へ足すたびに物差しそのものが伸びてしまい、
        // 「敵を触っていないのに数値が動いた」という読み取れない差分が出る。
        // 測りたいのは敵の側なので、棒のほうは動かさない。
        var pool = db.allAdventurers
            .Where(master => master.defaultRank == 1 && master.defaultLevel == 1)
            .Where(master => master.rarity == Rarity.Common)
            .ToList();
        var enemies = db.enemies.Values
            .Where(master => Windows.ContainsKey(master.threat))
            .OrderBy(master => master.threat).ThenBy(master => master.id)
            .ToList();

        var rows = new List<BandRow>();
        for (int enemyIndex = 0; enemyIndex < enemies.Count; enemyIndex++)
        {
            int losses = 0;
            double remainingHpSum = 0;
            foreach (var (adventurerMaster, adventurerIndex) in pool.Select((m, i) => (m, i)))
                for (int run = 0; run < RunsPerMatchup; run++)
                {
                    using var random = GameRandom.UseSeed(
                        904_000 + enemyIndex * 100_000 + adventurerIndex * RunsPerMatchup + run);
                    var adventurer = PromotedToRankE(adventurerMaster, db);
                    var enemy = new EnemyData(enemies[enemyIndex]);

                    var allies = new IUnitMember?[6];
                    allies[UsesRearPosition(adventurer) ? 3 : 0] = adventurer;
                    var foes = new IUnitMember?[6];
                    foes[enemy.Skills.Any(s => s.backOnly && !s.frontOnly) ? 3 : 0] = enemy;
                    InitializeCombat(allies, allies: true);
                    InitializeCombat(foes, allies: false);

                    var result = BattleResolver.Resolve(
                        allies, foes, new List<string>(), turn: 1, phase: 1,
                        new MoraleState(UnitCalculator.CalcPerMember(allies, true).Sum(x => x.stats.san)),
                        ExpeditionPolicy.ObjectiveFirst);

                    bool won = !result.adventurersRetreated
                        && adventurer.isAlive && !adventurer.isIncapacitated && !enemy.isAlive;
                    if (!won) losses++;
                    else if (adventurer.CombatHpMax > 0)
                        remainingHpSum += adventurer.CombatHp * 100d / adventurer.CombatHpMax;
                }

            int total = pool.Count * RunsPerMatchup;
            rows.Add(new BandRow(
                enemies[enemyIndex].threat,
                enemies[enemyIndex].baseName,
                losses * 100d / total,
                remainingHpSum / total));
        }
        return rows;
    }

    /// <summary>
    /// 物差しになる冒険者。Fの上限Lv5まで育て、昇格条件を満たしてEへ上げる。
    /// 昇格の全能力+2と習熟度+1000（＝習熟度700までの職業スキル）まで込みの姿。
    /// </summary>
    static AdventurerData PromotedToRankE(AdventurerMasterData master, GameMasterData db)
    {
        var adventurer = new AdventurerData(master);
        while (!adventurer.IsAtLevelCap
            && adventurer.AddExperience(adventurer.RequiredExpForNextLevel, out _)) { }

        var requirement = adventurer.NextRankRequirement!.Value;
        int clears = Math.Max(requirement.higherRankClears, requirement.suitableTotalClears);
        for (int i = 0; i < clears; i++) adventurer.RecordQuestClearForRank(Rank.Min + 1);
        Assert.True(adventurer.TryRankUp(out _), $"{master.baseName} をEへ昇格できませんでした");

        EquipForRankE(adventurer, db);
        return adventurer;
    }

    /// <summary>その職業の得物と防具のまま、E帯で買えるTierの品へ揃える。</summary>
    static void EquipForRankE(AdventurerData adventurer, GameMasterData db)
    {
        var shop = db.equipment.Values
            .Where(item => item.shopTier <= ShopTierAtRankE && !item.id.StartsWith("eq_drop_"))
            .ToList();

        if (adventurer.Weapon is { } weapon)
        {
            var upgrade = shop
                .Where(item => item.type == EquipmentType.Weapon
                    && item.weaponType == weapon.weaponType
                    && item.attackKind == weapon.attackKind
                    && item.isTwoHanded == weapon.isTwoHanded)
                .OrderByDescending(item => item.shopTier).ThenBy(item => item.id)
                .FirstOrDefault();
            if (upgrade != null) adventurer.SetEquipped(EquipSlot.RightHand, upgrade);
        }

        // 防具はマスタリーを持つ種別に揃える。持っていなければ布。
        var armorType = adventurer.Skills
            .Where(skill => skill.requireArmorType)
            .Select(skill => (ArmorType?)skill.requiredArmorType)
            .FirstOrDefault() ?? ArmorType.Cloth;
        var armors = shop
            .Where(item => item.type == EquipmentType.Armor
                && item.armorType == armorType
                && item.CanEquipTo(EquipSlot.Body))
            .OrderByDescending(item => item.shopTier).ThenBy(item => item.id)
            .ToList();
        // 過積載になるものは避ける。重い鎧を着せて命中とDVを削っては物差しにならない。
        foreach (var armor in armors)
        {
            adventurer.SetEquipped(EquipSlot.Body, armor);
            if (adventurer.OverweightAmount <= 0) return;
        }
        adventurer.SetEquipped(EquipSlot.Body, armors.OrderBy(item => item.weight).FirstOrDefault());
    }

    static void InitializeCombat(IUnitMember?[] members, bool allies)
    {
        foreach (var (member, stats) in UnitCalculator.CalcPerMember(members, allies))
        {
            member.CombatHpMax = stats.hp;
            member.CombatHp = stats.hp;
        }
    }

    static bool UsesRearPosition(AdventurerData adventurer) =>
        adventurer.Weapon is { } weapon
        && (weapon.IsMagicWeapon || weapon.weaponType == WeaponType.Bow);
}
