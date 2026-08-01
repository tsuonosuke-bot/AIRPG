using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

/// <summary>
/// 冒険者1人 vs 敵1体の戦闘を、実際のギルド状態を変更せずに試せるシミュレーター。
/// バランス確認用で、勝敗・経験値・ドロップはギルドに反映しない。
/// </summary>
public static class BattleSimScreen
{
    const int MaxEnemyLevel = 30;

    public static async Task ShowAsync(GameMasterData db, GuildManager guild)
    {
        Ui.BeginScreen();
        Ui.Header("戦闘シミュレーター");
        Ui.Dim("  実際のギルド状態には影響しません（HP・経験値・ドロップは反映されません）");
        Ui.WriteLine();

        var adventurer = await PickAdventurerAsync(guild);
        if (adventurer == null) return;

        var enemyMaster = await PickEnemyAsync(db);
        if (enemyMaster == null) return;


        await RunBattleAsync(adventurer, enemyMaster);
    }

    static async Task<AdventurerData?> PickAdventurerAsync(GuildManager guild)
    {
        var advs = guild.adventurers.Where(a => a.isAlive).ToList();
        if (advs.Count == 0)
        {
            Ui.Warn("戦わせられる冒険者がいません");
            await Ui.PauseAsync();
            return null;
        }

        var entries = new List<MenuOption>();
        for (int i = 0; i < advs.Count; i++)
        {
            var a = advs[i];
            entries.Add(new MenuOption(
                (i + 1).ToString(),
                $"{a.name} Lv{a.level}",
                $"ランク{a.RankLabel} {a.ClassAndRace}",
                Ui.RarityStyle(a.master.rarity)));
        }

        int? sel = await Ui.SelectIndexAsync("戦わせる冒険者を選択", entries);
        return sel == null ? null : advs[sel.Value - 1];
    }

    static async Task<EnemyMasterData?> PickEnemyAsync(GameMasterData db)
    {
        var enemies = db.enemies.Values.OrderBy(e => e.baseName).ToList();
        if (enemies.Count == 0)
        {
            Ui.Warn("敵データがありません");
            await Ui.PauseAsync();
            return null;
        }

        var entries = new List<MenuOption>();
        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            entries.Add(new MenuOption(
                (i + 1).ToString(),
                e.baseName,
                $"VIT:{e.vitality} MEN:{e.mental} STR:{e.strength} AGI:{e.agility} INT:{e.intelligence} SIZ:{e.constitution}  武器:{e.DefaultWeapon?.displayName ?? "なし"}  防具:{e.DefaultArmor?.displayName ?? "なし"}"));
        }

        int? sel = await Ui.SelectIndexAsync("対戦相手の敵を選択", entries);
        return sel == null ? null : enemies[sel.Value - 1];
    }

    static async Task RunBattleAsync(AdventurerData adventurer, EnemyMasterData enemyMaster)
    {
        // 実データのHP/生死を退避し、シミュレーション後に必ず元へ戻す。
        int savedHp = adventurer.CombatHp;
        int savedHpMax = adventurer.CombatHpMax;
        bool savedAlive = adventurer.isAlive;

        try
        {
            var enemy = new EnemyData(enemyMaster);
            var advSide = new IUnitMember?[6];
            var enemySide = new IUnitMember?[6];
            advSide[0] = adventurer;
            enemySide[0] = enemy;

            foreach (var (m, s) in UnitCalculator.CalcPerMember(advSide, isAllySide: true))
            {
                m.CombatHpMax = s.hp;
                m.CombatHp = s.hp;
            }
            foreach (var (m, s) in UnitCalculator.CalcPerMember(enemySide, isAllySide: false))
            {
                m.CombatHpMax = s.hp;
                m.CombatHp = s.hp;
            }

            int moraleMax = UnitCalculator.CalcPerMember(advSide, isAllySide: true)
                .Sum(x => x.stats.san);
            var morale = new MoraleState(moraleMax);

            var logs = new List<string>();
            var result = BattleResolver.Resolve(
                advSide, enemySide, logs, turn: 0, phase: 1, morale, ExpeditionPolicy.ObjectiveFirst);

            Ui.BeginScreen();
            Ui.Header($"戦闘シミュレーター: {adventurer.name} vs {enemy.name}（脅威度{Rank.Label(enemy.Threat)}）");
            foreach (var line in logs)
                Ui.WriteQuestLog(line);

            Ui.WriteLine();
            if (result.adventurersRetreated)
                Ui.Warn($"結果: 撤退（{result.retreatReason}） {result.rounds}ラウンド");
            else if (!adventurer.isAlive)
                Ui.Error($"結果: 冒険者の敗北 {result.rounds}ラウンド");
            else
                Ui.Info($"結果: 冒険者の勝利 {result.rounds}ラウンド");

            await Ui.PauseAsync();
        }
        finally
        {
            adventurer.CombatHp = savedHp;
            adventurer.CombatHpMax = savedHpMax;
            adventurer.isAlive = savedAlive;
        }
    }
}
