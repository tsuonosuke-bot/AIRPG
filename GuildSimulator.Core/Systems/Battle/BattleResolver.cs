using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Battle;

public static class BattleResolver
{
    public class Result
    {
        public bool adventurersRetreated;
        public int rounds;
    }

    const float DAMAGE_K = 1f;

    // 命中率：hit/evadeの「比率」ではなく「差分」を基準命中率に加減する方式。
    // hitやevadeが0以下になっても極端な0%/100%へ落ちないよう上下限でクランプする。
    const float BASE_HIT_RATE = 0.80f;
    const float HIT_RATE_PER_POINT = 0.01f; // hit-evade の差1につき命中率±1%
    const float MIN_HIT_RATE = 0.05f;
    const float MAX_HIT_RATE = 0.95f;

    const float FRONT_TARGET_CHANCE = 0.8f; // 前衛がいる限り80%は前衛を狙う
    const float HEAL_TARGET_HP_RATE = 0.7f; // 味方のHP率がこれを下回っていたら回復を選ぶ
    const float HEAL_SCALE = 1.5f;          // heal行動1回あたりの回復量係数
    const float HEAL_CAP_RATE = 0.3f;       // 1回の回復量は対象の最大HPのこの割合まで
    const int MAX_ACTIONS = 300;            // 長期戦の安全弁（個別行動の総数）

    public static Result Resolve(
        IUnitMember?[] advSide,
        IUnitMember?[] enemySide,
        List<string> logs,
        int turn,
        int phase,
        MoraleState morale)
    {
        var res = new Result();
        int actions = 0;
        int round = 0;

        logs.Add($"[Turn {turn}] Phase {phase}: 戦闘開始 冒険者 vs {FirstAliveName(enemySide)}");

        // 格上との遭遇はそれ自体が士気を削る。
        int levelGap = UnitCalculator.AvgLevel(enemySide) - UnitCalculator.AvgLevel(advSide);
        int shock = morale.DrainLevelGap(levelGap);
        if (shock > 0)
            logs.Add($"  Phase {phase}: 格上の相手に気圧された（士気 -{shock} → {morale.Current}/{morale.Max}）");

        int partyMaxHp = SumMaxHp(advSide);

        while (actions < MAX_ACTIONS)
        {
            round++;
            int partyHpAtRoundStart = SumCurrentHp(advSide);
            int partyDowned = 0;
            var advCalc = UnitCalculator.CalcPerMember(advSide);
            var enemyCalc = UnitCalculator.CalcPerMember(enemySide);
            if (advCalc.Length == 0 || enemyCalc.Length == 0) break;

            var statsByMember = new Dictionary<IUnitMember, StatBlock>();
            foreach (var (m, s) in advCalc) statsByMember[m] = s;
            foreach (var (m, s) in enemyCalc) statsByMember[m] = s;

            var queue = advCalc.Select(x => (member: x.member, isAdvSide: true, stats: x.stats))
                .Concat(enemyCalc.Select(x => (member: x.member, isAdvSide: false, stats: x.stats)))
                .OrderByDescending(x => x.stats.hit)
                .ThenBy(_ => GameRandom.NextFloat())
                .ToList();

            foreach (var entry in queue)
            {
                if (actions >= MAX_ACTIONS) break;
                var actor = entry.member;
                if (!actor.IsAlive) continue;

                var allySideArr = entry.isAdvSide ? advSide : enemySide;
                var enemySideArr = entry.isAdvSide ? enemySide : advSide;
                if (!AnyAlive(allySideArr) || !AnyAlive(enemySideArr)) continue;

                var actorStats = entry.stats;
                bool canHeal = actor.Weapon != null && actor.Weapon.healCoeff > 0f;
                IUnitMember? healTarget = canHeal ? PickHealTarget(allySideArr) : null;

                if (healTarget != null)
                {
                    int healAmt = (int)Math.Ceiling(actorStats.heal * HEAL_SCALE);
                    int cap = (int)Math.Ceiling(healTarget.CombatHpMax * HEAL_CAP_RATE);
                    healAmt = Math.Min(Math.Min(healAmt, cap), healTarget.CombatHpMax - healTarget.CombatHp);
                    if (healAmt > 0)
                    {
                        healTarget.CombatHp += healAmt;
                        logs.Add($"  Phase {phase}: {actor.Name}→{healTarget.Name} 回復 +{healAmt}（{healTarget.CombatHp}/{healTarget.CombatHpMax}）");
                    }
                }
                else
                {
                    var target = PickTarget(enemySideArr);
                    if (target == null) continue;
                    var targetStats = statsByMember[target];

                    float baseDmg =
                        (AttackTerm(actorStats.pAtk, targetStats.pDef) +
                         AttackTerm(actorStats.mAtk, targetStats.mDef)) * DAMAGE_K;

                    float hitRate = Math.Clamp(
                        BASE_HIT_RATE + (actorStats.hit - targetStats.evade) * HIT_RATE_PER_POINT,
                        MIN_HIT_RATE, MAX_HIT_RATE);

                    if (GameRandom.NextFloat() >= hitRate)
                    {
                        logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} 回避！（命中率={hitRate:P0}） ダメージなし");
                    }
                    else
                    {
                        float levelBonus = 1f + (actor.Level - target.Level) / 100f;
                        float randBonus = GameRandom.Range(0.95f, 1.05f);
                        int dmg = Math.Max(1, (int)Math.Floor(baseDmg * levelBonus * randBonus));

                        target.CombatHp -= dmg;
                        logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} 命中！ ダメージ={dmg}（命中率={hitRate:P0}） HP={Math.Max(0, target.CombatHp)}/{target.CombatHpMax}");

                        if (target.CombatHp <= 0)
                        {
                            target.CombatHp = 0;
                            target.IsAlive = false;
                            logs.Add($"  Phase {phase}: {target.Name} 撃破！");
                            if (!entry.isAdvSide) partyDowned++;
                        }
                    }
                }

                actions++;

                if (!AnyAlive(enemySideArr) || !AnyAlive(allySideArr))
                {
                    res.rounds = round;
                    res.adventurersRetreated = false;
                    return res;
                }
            }

            // 士気は「押し込まれた分だけ」削れる。回復で押し返せた分は勘定に入らないので、
            // 治療師がHPを保っている限り士気も保たれ、優勢なまま突然逃げ出すことはない。
            int netHpLoss = partyHpAtRoundStart - SumCurrentHp(advSide);
            int lost = morale.DrainFromDamage(netHpLoss, partyMaxHp);
            if (partyDowned > 0)
            {
                int downLoss = morale.DrainAllyDown(partyDowned);
                lost += downLoss;
                if (downLoss > 0) logs.Add($"  Phase {phase}: 仲間が倒れて動揺した（士気 -{downLoss}）");
            }

            if (morale.IsBroken)
            {
                logs.Add($"  Phase {phase}: 士気が尽きた！パーティは撤退する（士気 0/{morale.Max}）");
                res.adventurersRetreated = true;
                res.rounds = round;
                return res;
            }

            if (lost > 0 && morale.Rate <= 0.3f)
                logs.Add($"  Phase {phase}: 士気が揺らいでいる（士気 {morale.Current}/{morale.Max}）");
        }

        logs.Add($"  Phase {phase}: 長期戦 → 撤退扱い");
        res.adventurersRetreated = true;
        res.rounds = round;
        return res;
    }

    // atk^2/(atk+def) 方式。防御が0でもatk相当で頭打ちになり、比率方式のように発散しない。
    static float AttackTerm(int atkStat, int defStat)
    {
        if (atkStat <= 0) return 0f;
        return (float)atkStat * atkStat / (atkStat + Math.Max(0, defStat));
    }

    static string FirstAliveName(IUnitMember?[] side)
        => side.FirstOrDefault(a => a != null && a.IsAlive)?.Name ?? "敵";

    static bool AnyAlive(IUnitMember?[] side)
        => side.Any(a => a != null && a.IsAlive);

    static float HpRate(IUnitMember m)
        => m.CombatHpMax <= 0 ? 0f : Math.Clamp((float)m.CombatHp / m.CombatHpMax, 0f, 1f);

    static int SumCurrentHp(IUnitMember?[] side)
    {
        int hp = 0;
        foreach (var a in side)
            if (a != null && a.IsAlive) hp += Math.Max(0, a.CombatHp);
        return hp;
    }

    // 分母は「倒れた者も含む」全体。死亡で分母が縮んで損耗率が下がる逆転を避ける。
    static int SumMaxHp(IUnitMember?[] side)
    {
        int hp = 0;
        foreach (var a in side)
            if (a != null) hp += Math.Max(0, a.CombatHpMax);
        return hp;
    }

    static IUnitMember? PickHealTarget(IUnitMember?[] side)
    {
        IUnitMember? lowest = null;
        float lowestRate = float.MaxValue;
        foreach (var a in side)
        {
            if (a == null || !a.IsAlive) continue;
            float r = HpRate(a);
            if (r < lowestRate) { lowestRate = r; lowest = a; }
        }
        return lowestRate < HEAL_TARGET_HP_RATE ? lowest : null;
    }

    // 前衛優先（前衛がいれば80%の確率で前衛から）で対象の列を選び、列内では硬さ・回避のしにくさに
    // 反比例する重み付け抽選で1体選ぶ。
    static IUnitMember? PickTarget(IUnitMember?[] side)
    {
        var front = new List<IUnitMember>();
        var back = new List<IUnitMember>();
        for (int i = 0; i < side.Length; i++)
        {
            var a = side[i];
            if (a == null || !a.IsAlive) continue;
            (i < 3 ? front : back).Add(a);
        }
        if (front.Count == 0 && back.Count == 0) return null;

        bool pickFront = front.Count > 0 && (back.Count == 0 || GameRandom.NextFloat() < FRONT_TARGET_CHANCE);
        var pool = pickFront ? front : back;
        if (pool.Count == 0) pool = pickFront ? back : front;
        return PickWeightedBySquishiness(pool);
    }

    static IUnitMember? PickWeightedBySquishiness(List<IUnitMember> pool)
    {
        if (pool.Count == 0) return null;
        float sum = 0;
        var weights = new float[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            var s = pool[i].GetFinalCombatStats();
            float w = 1f / (1f + (s.pDef + s.mDef) / 200f + s.evade / 200f);
            weights[i] = w; sum += w;
        }
        if (sum <= 0) return pool[GameRandom.Range(0, pool.Count)];
        float r = GameRandom.NextFloat() * sum;
        float acc = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += weights[i];
            if (r <= acc) return pool[i];
        }
        return pool[^1];
    }
}
