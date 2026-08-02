using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

/// <summary>実際の遠征で遭遇した敵だけを閲覧できるモンスター図鑑。</summary>
public static class MonsterGuideScreen
{
    public static async Task ShowAsync(GameMasterData db, GuildManager guild)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("モンスター図鑑");

            var monsters = guild.DiscoveredEnemyIds
                .Where(db.enemies.ContainsKey)
                .Select(id => db.enemies[id])
                .OrderBy(enemy => enemy.threat)
                .ThenBy(enemy => enemy.baseName)
                .ToList();

            Ui.WriteLine($"  登録数: {monsters.Count}/{db.enemies.Count}");
            Ui.Dim("  実際の遠征で遭遇したモンスターだけが登録されます");
            Ui.WriteLine();

            if (monsters.Count == 0)
            {
                Ui.Dim("  まだモンスターと遭遇していません");
                await Ui.PauseAsync();
                return;
            }

            var snapshots = monsters.Select(BuildSnapshot).ToList();
            var entries = snapshots.Select((snapshot, index) => new MenuOption(
                (index + 1).ToString(),
                $"【{Rank.Label(snapshot.Enemy.Threat)}】{snapshot.Enemy.Name}",
                $"HP:{snapshot.Stats.hp} AV:{CombatArmor(snapshot.Stats.av)} DV:{snapshot.Stats.dv}  "
                    + $"攻撃:{snapshot.Enemy.DamageDice} PV:{snapshot.Pv}"))
                .ToList();

            int? selected = await Ui.SelectIndexAsync("詳細を見るモンスターを選択", entries);
            if (selected == null) return;
            await ShowDetailAsync(snapshots[selected.Value - 1]);
        }
    }

    static async Task ShowDetailAsync(MonsterSnapshot snapshot)
    {
        var enemy = snapshot.Enemy;
        var master = enemy.master;
        var stats = snapshot.Stats;

        Ui.BeginScreen();
        Ui.Header($"モンスター図鑑：{enemy.Name}");
        Ui.WriteLine($"  脅威度: {Rank.Label(enemy.Threat)}   EXP: {master.exp}   想定配置: {(snapshot.IsBack ? "後衛" : "前衛")}");
        Ui.WriteLine($"  能力値: VIT:{master.vitality} MEN:{master.mental} STR:{master.strength} "
            + $"AGI:{master.agility} INT:{master.intelligence} SIZ:{master.constitution}");
        Ui.WriteLine($"  耐久値: HP:{stats.hp} SAN:{stats.san} "
            + $"AV:{CombatArmor(stats.av)} mAV:{CombatArmor(stats.mav)} DV:{stats.dv}");

        string attackName = enemy.Weapon?.displayName ?? "自然攻撃";
        Ui.WriteLine($"  攻撃  : {attackName}  ダメージ:{enemy.DamageDice} PV:{snapshot.Pv} 命中:{Signed(stats.toHit)}");

        var combatTraits = new List<string>();
        AddIfPositive(combatTraits, "装甲貫通", stats.armorPierce);
        AddIfPositive(combatTraits, "装甲破壊", stats.armorShred);
        AddIfPositive(combatTraits, "会心域", stats.critRange);
        AddIfPositive(combatTraits, "追加攻撃", stats.extraAttacks);
        if (combatTraits.Count > 0)
            Ui.WriteLine($"  攻撃特性: {string.Join(" / ", combatTraits)}");

        if (enemy.Skills.Count == 0)
            Ui.Dim("  スキル: なし");
        else
            Ui.WriteLine($"  スキル: {string.Join("、", enemy.Skills.Select(skill => skill.skillName))}");

        await Ui.PauseAsync();
    }

    static MonsterSnapshot BuildSnapshot(EnemyMasterData master)
    {
        var enemy = new EnemyData(master);
        bool isBack = enemy.Skills.Any(skill => skill.backOnly && !skill.frontOnly);
        var members = new IUnitMember?[6];
        members[isBack ? 3 : 0] = enemy;
        var stats = UnitCalculator.CalcPerMember(members, isAllySide: false).Single().stats;
        int flatPv = enemy.IsMagicAttack ? stats.mpv : stats.pv;
        int pv = QudCombat.EffectivePv(
            enemy.WeaponBasePv,
            enemy.AttackStatModifier,
            enemy.MaxStatBonus,
            flatPv);
        return new MonsterSnapshot(enemy, stats, pv, isBack);
    }

    static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

    // 戦闘解決では装甲値を0未満にしないため、図鑑も実際に使われる値を表示する。
    static int CombatArmor(int value) => Math.Max(0, value);

    static void AddIfPositive(List<string> values, string label, int value)
    {
        if (value > 0) values.Add($"{label}+{value}");
    }

    sealed record MonsterSnapshot(EnemyData Enemy, StatBlock Stats, int Pv, bool IsBack);
}
