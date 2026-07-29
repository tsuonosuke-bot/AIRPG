using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Battle;

public static class BattleResolver
{
    public class Result
    {
        public bool adventurersRetreated;
        public ExpeditionRetreatReason retreatReason;
        public int rounds;
    }

    // 戦闘の解決は Caves of Qud に倣う（計算そのものは QudCombat を参照）。
    //   命中: 1d20 + 命中補正 > 相手のDV
    //   貫通: (1d10-2)+PV vs AV を3回1セット。1回でも抜ければ1貫通、3回とも抜ければPV-2で次のセットへ
    //   損傷: 貫通回数ぶんだけ武器のダメージダイスを振って合計
    // PV = 武器の基礎PV + min(能力値modifier, 武器ごとの上限) + 装備・スキル由来のPV補正。

    // ヘルプ画面が計算式をそのまま説明できるよう、プレイヤーに見える係数は公開している。
    public const int REAR_MELEE_TO_HIT_PENALTY = 3; // 後衛から近接攻撃する場合の命中ペナルティ（1d20スケール）
    public const int REAR_COVER_DV_BONUS = 2;       // 前衛が健在な間、後衛が得るDVボーナス

    public const float FRONT_TARGET_CHANCE = 0.8f; // 前衛がいる限り80%は前衛を狙う
    public const float HEAL_SCALE = 1.5f;          // heal行動1回あたりの回復量係数
    public const float HEAL_CRIT_SCALE = 1.5f;     // 出目20の手当ては効きが良い

    const float HEAL_TARGET_HP_RATE = 0.7f; // 味方のHP率がこれを下回っていたら回復を選ぶ
    const int MAX_ACTIONS = 300;            // 長期戦の安全弁（個別行動の総数）

    public static Result Resolve(
        IUnitMember?[] advSide,
        IUnitMember?[] enemySide,
        List<string> logs,
        int turn,
        int phase,
        MoraleState morale,
        ExpeditionPolicy policy = ExpeditionPolicy.ObjectiveFirst)
    {
        var res = new Result();
        int actions = 0;
        int round = 0;

        logs.Add($"[Turn {turn}] Phase {phase}: 戦闘開始 冒険者 vs {DescribeComposition(enemySide)}");

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
            var advCalc = UnitCalculator.CalcPerMember(advSide, isAllySide: true);
            var enemyCalc = UnitCalculator.CalcPerMember(enemySide, isAllySide: false);
            if (advCalc.Length == 0 || enemyCalc.Length == 0) break;

            var statsByMember = new Dictionary<IUnitMember, StatBlock>();
            foreach (var (m, s) in advCalc) statsByMember[m] = s;
            foreach (var (m, s) in enemyCalc) statsByMember[m] = s;

            var queue = advCalc.Select(x => (member: x.member, isAdvSide: true, stats: x.stats, slot: SlotOf(advSide, x.member)))
                .Concat(enemyCalc.Select(x => (member: x.member, isAdvSide: false, stats: x.stats, slot: SlotOf(enemySide, x.member))))
                .OrderByDescending(x => x.stats.toHit)
                .ThenBy(_ => GameRandom.NextFloat())
                .ToList();

            foreach (var entry in queue)
            {
                if (actions >= MAX_ACTIONS) break;
                var actor = entry.member;
                if (!actor.IsAlive) continue;

                var allySideArr = entry.isAdvSide ? advSide : enemySide;
                var enemySideArr = entry.isAdvSide ? enemySide : advSide;
                bool isRear = entry.slot >= 3;
                if (!AnyAlive(allySideArr) || !AnyAlive(enemySideArr)) continue;

                var actorStats = entry.stats;
                bool canHeal = actor.Weapon != null && actor.Weapon.IsHealWeapon;
                IUnitMember? healTarget = canHeal ? PickHealTarget(allySideArr) : null;

                if (healTarget != null)
                {
                    // 手当ても攻撃と同じ1d20で解決する。出目20は効きが良く、出目1は手元が狂う。
                    int healRoll = GameRandom.Range(1, QudCombat.HIT_DIE + 1);
                    bool healCrit = healRoll == QudCombat.CRITICAL_ROLL;
                    bool healFumble = healRoll == QudCombat.FUMBLE_ROLL;

                    if (healFumble)
                    {
                        logs.Add($"  Phase {phase}: {actor.Name}→{healTarget.Name} 手当て失敗（1d20={healRoll}） うまくいかない");
                    }
                    else
                    {
                        int healAmt = (int)Math.Ceiling(actorStats.heal * HEAL_SCALE);
                        if (healCrit) healAmt = (int)Math.Ceiling(healAmt * HEAL_CRIT_SCALE);
                        healAmt = Math.Min(healAmt, healTarget.CombatHpMax - healTarget.CombatHp);
                        if (healAmt > 0)
                        {
                            healTarget.CombatHp += healAmt;
                            string healTag = healCrit ? "会心の治療" : "回復";
                            logs.Add($"  Phase {phase}: {actor.Name}→{healTarget.Name} {healTag} +{healAmt}（1d20={healRoll}）（{healTarget.CombatHp}/{healTarget.CombatHpMax}）");
                        }
                    }
                }
                else
                {
                    var target = PickTarget(enemySideArr);
                    if (target == null) continue;
                    var targetStats = statsByMember[target];

                    // 物理か魔法かは能力値の大小ではなく武器そのもので決まる。
                    // 魔道士が剣を持てば剣で殴り、戦士が杖を持てば魔法が飛ぶ。
                    bool isMagic = actor.IsMagicAttack;

                    int toHit = actorStats.toHit;
                    if (isRear && !IsRangedWeapon(actor)) toHit -= REAR_MELEE_TO_HIT_PENALTY;

                    int dv = targetStats.dv;
                    if (SlotOf(enemySideArr, target) >= 3 && HasAliveFront(enemySideArr))
                        dv += REAR_COVER_DV_BONUS;

                    var check = QudCombat.RollToHit(toHit, dv);

                    if (!check.hit)
                    {
                        logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} 回避！（1d20={check.roll}{toHit:+#;-#;+0}={check.total} ≦ DV{dv}） ダメージなし");
                    }
                    else
                    {
                        // PVは武器の基礎値に能力値modifierを（武器ごとの上限つきで）乗せ、装備・スキル補正を足す。
                        // AVは装甲そのもの。どちらも小さな整数で、1点が貫通回数に効く。
                        int pv = QudCombat.EffectivePv(
                            actor.WeaponBasePv, actor.AttackStatModifier, actor.MaxStatBonus,
                            isMagic ? actorStats.mpv : actorStats.pv);
                        int av = Math.Max(0, isMagic ? targetStats.mav : targetStats.av);

                        var dealt = QudCombat.ResolveAttack(pv, av, actor.DamageDice, check.critical);

                        target.CombatHp -= dealt.damage;
                        string tag = check.critical ? "会心！" : "命中！";
                        string atkKind = isMagic ? "魔法" : "物理";
                        string roll = $"1d20={check.roll}{toHit:+#;-#;+0}={check.total} > DV{dv}";
                        string judge = $"{atkKind} PV{dealt.pv} vs AV{dealt.av}";

                        if (dealt.penetrations == 0)
                        {
                            logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} {tag}（{roll}、{judge}） 装甲に弾かれた ダメージなし");
                        }
                        else
                        {
                            string dice = string.IsNullOrWhiteSpace(actor.DamageDice)
                                ? QudCombat.DEFAULT_DAMAGE_DICE : actor.DamageDice;
                            logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} {tag}（{roll}、{judge}） {dealt.penetrations}回貫通 {dice}×{dealt.penetrations} ダメージ={dealt.damage} HP={Math.Max(0, target.CombatHp)}/{target.CombatHpMax}");
                        }

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
                res.retreatReason = ExpeditionRetreatReason.MoraleBroken;
                res.rounds = round;
                return res;
            }

            // 生還優先では、全滅寸前まで粘らず、損耗が危険域へ入った時点で引き返す。
            // 士気切れは方針に関わらず上の撤退ロジックが担うため、ここでは損耗（HP）だけで判断する。
            if (policy == ExpeditionPolicy.SurvivalFirst && AnyAlive(advSide) && AnyAlive(enemySide))
            {
                int aliveMaxHp = advSide
                    .Where(a => a != null && a.IsAlive)
                    .Sum(a => Math.Max(1, a!.CombatHpMax));
                int aliveCurrentHp = SumCurrentHp(advSide);
                bool partyBadlyHurt = aliveMaxHp > 0 && (float)aliveCurrentHp / aliveMaxHp <= 0.60f;
                bool memberInDanger = advSide
                    .Any(a => a != null && a.IsAlive && HpRate(a) <= 0.30f);
                if (partyBadlyHurt || memberInDanger)
                {
                    logs.Add($"  Phase {phase}: 生還優先の命令に従い、損耗が危険域へ達する前に撤退した");
                    res.adventurersRetreated = true;
                    res.retreatReason = ExpeditionRetreatReason.SurvivalPolicy;
                    res.rounds = round;
                    return res;
                }
            }

            if (lost > 0 && morale.Rate <= 0.3f)
                logs.Add($"  Phase {phase}: 士気が揺らいでいる（士気 {morale.Current}/{morale.Max}）");
        }

        logs.Add($"  Phase {phase}: 長期戦 → 撤退扱い");
        res.adventurersRetreated = true;
        res.retreatReason = ExpeditionRetreatReason.BattleStalemate;
        res.rounds = round;
        return res;
    }

    // 敵の内訳（名前・レベル・頭数）をログに残し、戦闘ログだけで強さの見立てができるようにする。
    static string DescribeComposition(IUnitMember?[] side)
    {
        var groups = side.Where(a => a != null && a.IsAlive)
            .GroupBy(a => (a!.Name, a.Level))
            .Select(g => g.Count() > 1
                ? $"{g.Key.Name}(Lv{g.Key.Level})×{g.Count()}"
                : $"{g.Key.Name}(Lv{g.Key.Level})");
        var desc = string.Join("、", groups);
        return desc.Length > 0 ? desc : "敵";
    }

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

    static int SlotOf(IUnitMember?[] side, IUnitMember member)
    {
        for (int i = 0; i < side.Length; i++)
            if (side[i] == member) return i;
        return 0;
    }

    static bool HasAliveFront(IUnitMember?[] side)
    {
        for (int i = 0; i < Math.Min(3, side.Length); i++)
            if (side[i] != null && side[i]!.IsAlive) return true;
        return false;
    }

    static bool IsRangedWeapon(IUnitMember member)
    {
        var w = member.Weapon;
        if (w == null) return false;
        if (w.attackKind != AttackKind.Physical) return true;
        return w.weaponType == WeaponType.Bow;
    }

    // 硬い相手・避ける相手ほど狙われにくい。AV/DVは1桁の整数なので、旧来の200分率ではなく
    // 「装甲と回避の合計1点につき狙われにくさが効く」スケールで重みを取る。
    static IUnitMember? PickWeightedBySquishiness(List<IUnitMember> pool)
    {
        if (pool.Count == 0) return null;
        float sum = 0;
        var weights = new float[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            var s = pool[i].GetFinalCombatStats();
            int toughness = Math.Max(0, s.av) + Math.Max(0, s.mav) + Math.Max(0, s.dv);
            float w = 1f / (1f + toughness / 10f);
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
