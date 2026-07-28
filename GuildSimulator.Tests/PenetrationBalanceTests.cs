using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;
using Xunit.Abstractions;

namespace GuildSimulator.Tests;

/// <summary>
/// 貫通判定のバランス測定。実マスタデータの代表的な組み合わせで平均ダメージと貫通回数の分布を出し、
/// 武器のbasePv / 防具のAV を動かしたときに戦闘のテンポがどう変わるかを数字で確認できるようにする。
/// 出力を見るには: dotnet test --filter PenetrationBalance --logger "console;verbosity=detailed"
/// </summary>
public class PenetrationBalanceTests
{
    readonly ITestOutputHelper output;

    public PenetrationBalanceTests(ITestOutputHelper output) => this.output = output;

    const int Trials = 20_000;

    record Matchup(string name, IUnitMember attacker, IUnitMember defender);

    [Fact]
    public void PenetrationBalanceAcrossRepresentativeMatchups()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = MasterLoader.Load(dataDir);

        var satoru = new AdventurerData(db.allAdventurers.First(a => a.id == "adv_0001"));
        var goblin = new EnemyData(db.enemies["enemy_goblin"]);
        var heavy = new EnemyData(db.enemies["enemy_goblin_heavy"]);

        var matchups = new[]
        {
            new Matchup("Lv1冒険者 → ゴブリン", satoru, goblin),
            new Matchup("ゴブリン → Lv1冒険者", goblin, satoru),
            new Matchup("Lv1冒険者 → 重装歩兵", satoru, heavy),
            new Matchup("重装歩兵 → Lv1冒険者", heavy, satoru),
        };

        output.WriteLine($"{"組み合わせ",-24} {"PV",4} {"AV",4} {"平均",6} {"弾かれ",7} {"1貫通",7} {"2貫通",7} {"3+貫通",7} {"平均貫通",8} {"HP",5} {"必要命中数",10}");

        foreach (var m in matchups)
        {
            var s = Measure(m);
            double hitsToKill = s.defenderHp / Math.Max(0.01, s.average);
            output.WriteLine(
                $"{m.name,-24} {s.pv,4} {s.av,4} {s.average,6:F1} " +
                $"{s.blocked,6:P0} {s.once,6:P0} {s.twice,6:P0} {s.thrice,6:P0} {s.avgPenetrations,8:F2} " +
                $"{s.defenderHp,5} {hitsToKill,10:F1}");

            // 数値そのものは調整対象なので固定しない。破綻だけを検出する。
            Assert.True(s.average > 0, $"{m.name}: 平均ダメージが0（一切通らない）");
            Assert.True(s.average < s.defenderHp, $"{m.name}: 1発で相手を倒しきっている（平均{s.average:F1} ≧ HP{s.defenderHp}）");
        }
    }

    [Fact]
    public void HeavyArmourShutsOutAttackersWhoCannotReachIt()
    {
        // 重鎧を着込んだ相手に弱い敵の攻撃が通らなくなること。最低保証ダメージが無いので、
        // AVへの投資がそのまま被弾の遮断になる。「装甲に弾かれた」経路が実プレイで到達可能かの確認でもある。
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = MasterLoader.Load(dataDir);

        var master = db.allAdventurers.First(a => a.id == "adv_0001");
        var inCloth = new AdventurerData(master);
        inCloth.SetEquipped(EquipSlot.Body, db.equipment["eq_cloth_01"]);
        var inPlate = new AdventurerData(master);
        inPlate.SetEquipped(EquipSlot.Body, db.equipment["eq_plate_02"]);
        var slime = new EnemyData(db.enemies["enemy_slime"]);

        var cloth = Measure(new Matchup("スライム → 布の服", slime, inCloth));
        var plate = Measure(new Matchup("スライム → 騎士の重鎧", slime, inPlate));

        output.WriteLine($"スライム → 布の服   : PV{cloth.pv} vs AV{cloth.av} 弾かれ{cloth.blocked:P0} 平均{cloth.average:F1}");
        output.WriteLine($"スライム → 騎士の重鎧: PV{plate.pv} vs AV{plate.av} 弾かれ{plate.blocked:P0} 平均{plate.average:F1}");

        Assert.True(plate.blocked > cloth.blocked, "重鎧にしても弾かれる割合が増えていない");
        Assert.True(plate.average < cloth.average, "重鎧にしても被ダメージが減っていない");
        Assert.True(plate.blocked > 0.2, $"「装甲に弾かれた」がほぼ発生しない（弾かれ{plate.blocked:P0}）");
    }

    record Stats(
        int pv, int av, double average, double blocked, double once, double twice, double thrice,
        double avgPenetrations, int defenderHp);

    static Stats Measure(Matchup m)
    {
        var attacker = m.attacker;
        var defender = m.defender;
        bool isMagic = attacker.IsMagicAttack;

        var attackerStats = attacker.GetFinalCombatStats();
        var defenderStats = defender.GetFinalCombatStats();

        int pv = QudCombat.EffectivePv(
            attacker.WeaponBasePv, attacker.AttackStatModifier, attacker.MaxStatBonus,
            isMagic ? attackerStats.mpv : attackerStats.pv);
        int av = Math.Max(0, isMagic ? defenderStats.mav : defenderStats.av);

        long totalDamage = 0;
        long totalPenetrations = 0;
        var buckets = new int[4]; // 0, 1, 2, 3以上
        for (int i = 0; i < Trials; i++)
        {
            var r = QudCombat.ResolveAttack(pv, av, attacker.DamageDice, critical: false);
            totalDamage += r.damage;
            totalPenetrations += r.penetrations;
            buckets[Math.Min(3, r.penetrations)]++;
        }

        return new Stats(
            pv, av,
            (double)totalDamage / Trials,
            (double)buckets[0] / Trials,
            (double)buckets[1] / Trials,
            (double)buckets[2] / Trials,
            (double)buckets[3] / Trials,
            (double)totalPenetrations / Trials,
            defenderStats.hp);
    }
}
