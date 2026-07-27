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

    // ダメージは「貫通値(PV)と装甲値(AV)を突き合わせ、貫通の深さぶんだけ武器ダイスを振る」方式で決まる。
    //   PV     = 筋力(魔法は知力) + 体格(魔法は精神) + 装備・スキル由来の攻撃補正
    //   AV     = 体格(魔法は精神)                     + 装備・スキル由来の防御補正
    //   余剰   = 3d10の最良の出目 + PV - AV
    // 余剰がTIER_STEPを超えるごとに成功レベルが1段上がり、振る武器ダイスが1本増える。
    // CoCの成功レベル（レギュラー/ハード/イクストリーム）に倣った有界な3段階なので、
    // 判定が繰り返されることはなく、1回の攻撃で振るダイスは最大3本で確定する。
    const int PENETRATION_DIE = 10;   // 貫通判定に振るダイスの面数
    const int PENETRATION_ROLLS = 3;  // 3個振って最良を採る（CoCのボーナス・ダイス2個に相当）
    const int TIER_STEP = 5;          // 成功レベルが1段上がるのに必要な余剰

    // ダメージ・ボーナスはCoCのSTR+SIZ表（帯域を上がるごとに加算ダイスが増える）に倣う。
    // CoCの帯域幅80は能力値0〜100を前提とするため、本作の能力値レンジに合わせて縮尺する。
    // 成功レベルが3本で頭打ちになるぶんの伸びしろを、こちらの緩やかな加算で受け持つ。
    const int DAMAGE_BONUS_BAND = 12;

    // 命中率：hit/evadeの「比率」ではなく「差分」を基準命中率に加減する方式。
    // hitやevadeが0以下になっても極端な0%/100%へ落ちないよう上下限でクランプする。
    const float BASE_HIT_RATE = 0.80f;
    const float HIT_RATE_PER_POINT = 0.01f; // hit-evade の差1につき命中率±1%
    const float MIN_HIT_RATE = 0.05f;
    const float MAX_HIT_RATE = 0.95f;

    // CoC（クトゥルフの呼び声）のパーセンタイル判定を踏襲：D100を振り、命中率%以下なら成功。
    // 成功のうち上位1/5（命中率の1/5以下の出目）は「決定的成功」、失敗のうち出目96以上は「大失敗」。
    const int CRIT_ROLL_DIVISOR = 5;
    const int FUMBLE_ROLL_THRESHOLD = 96;
    const string DEFAULT_DAMAGE_DICE = "1d4"; // 素手・自然武器のフォールバック
    const string FUMBLE_SELF_DICE = "1d2";    // 大失敗時、体勢を崩して自らに受けるダメージ
    const int HEAL_CRIT_ROLL = 5;
    const int HEAL_FUMBLE_ROLL = 96;

    const float FRONT_TARGET_CHANCE = 0.8f; // 前衛がいる限り80%は前衛を狙う
    const float REAR_MELEE_HIT_PENALTY = 0.15f; // 後衛から近接攻撃する場合の命中率ペナルティ
    const float REAR_COVER_EVADE_BONUS = 0.10f; // 前衛が健在な間、後衛が得る回避率ボーナス
    const float HEAL_TARGET_HP_RATE = 0.7f; // 味方のHP率がこれを下回っていたら回復を選ぶ
    const float HEAL_SCALE = 1.5f;          // heal行動1回あたりの回復量係数
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
                bool isRear = entry.slot >= 3;
                if (!AnyAlive(allySideArr) || !AnyAlive(enemySideArr)) continue;

                var actorStats = entry.stats;
                bool canHeal = actor.Weapon != null && actor.Weapon.healCoeff > 0f;
                IUnitMember? healTarget = canHeal ? PickHealTarget(allySideArr) : null;

                if (healTarget != null)
                {
                    int healRoll = GameRandom.Range(1, 101);
                    bool healCrit = healRoll <= HEAL_CRIT_ROLL;
                    bool healFumble = healRoll >= HEAL_FUMBLE_ROLL;

                    if (healFumble)
                    {
                        logs.Add($"  Phase {phase}: {actor.Name}→{healTarget.Name} 大失敗！（D100={healRoll}） 手当てがうまくいかない");
                    }
                    else
                    {
                        int healAmt = (int)Math.Ceiling(actorStats.heal * HEAL_SCALE);
                        if (healCrit) healAmt = (int)Math.Ceiling(healAmt * 1.5f);
                        healAmt = Math.Min(healAmt, healTarget.CombatHpMax - healTarget.CombatHp);
                        if (healAmt > 0)
                        {
                            healTarget.CombatHp += healAmt;
                            string healTag = healCrit ? "決定的成功の治療" : "回復";
                            logs.Add($"  Phase {phase}: {actor.Name}→{healTarget.Name} {healTag} +{healAmt}（D100={healRoll}）（{healTarget.CombatHp}/{healTarget.CombatHpMax}）");
                        }
                    }
                }
                else
                {
                    var target = PickTarget(enemySideArr);
                    if (target == null) continue;
                    var targetStats = statsByMember[target];

                    float baseHit = BASE_HIT_RATE + (actorStats.hit - targetStats.evade) * HIT_RATE_PER_POINT;
                    bool rearMeleePenalty = isRear && !IsRangedWeapon(actor);
                    if (rearMeleePenalty) baseHit -= REAR_MELEE_HIT_PENALTY;
                    bool targetHasRearCover = SlotOf(enemySideArr, target) >= 3 && HasAliveFront(enemySideArr);
                    if (targetHasRearCover) baseHit -= REAR_COVER_EVADE_BONUS;
                    int hitPercent = (int)Math.Round(Math.Clamp(baseHit, MIN_HIT_RATE, MAX_HIT_RATE) * 100f);

                    var check = RollD100(hitPercent);

                    if (!check.success)
                    {
                        if (check.fumble)
                        {
                            int selfDmg = Dice.Roll(FUMBLE_SELF_DICE);
                            actor.CombatHp -= selfDmg;
                            logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} 大失敗！（D100={check.roll}） 体勢を崩し自分に{selfDmg}ダメージ（{Math.Max(0, actor.CombatHp)}/{actor.CombatHpMax}）");

                            if (actor.CombatHp <= 0)
                            {
                                actor.CombatHp = 0;
                                actor.IsAlive = false;
                                logs.Add($"  Phase {phase}: {actor.Name} 自滅した…");
                                if (entry.isAdvSide) partyDowned++;
                            }
                        }
                        else
                        {
                            logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} 回避！（D100={check.roll} > {hitPercent}%） ダメージなし");
                        }
                    }
                    else
                    {
                        // 物理か魔法かは能力値の大小ではなく武器そのもので決まる。
                        // 魔道士が剣を持てば剣で殴り、戦士が杖を持てば魔法が飛ぶ。
                        bool isMagic = actor.IsMagicAttack;

                        // PV/AVは「素の能力値」に「装備・スキル由来の補正（最終値−基礎値）」を足して作る。
                        // こうすることで武器の攻撃ボーナスや守りのスキルが、そのまま貫通のしやすさに効く。
                        var actorBase = actor.GetBaseCombatStats();
                        var targetBase = target.GetBaseCombatStats();
                        int pv = actor.RawPenetration
                            + (isMagic ? actorStats.mAtk - actorBase.mAtk : actorStats.pAtk - actorBase.pAtk);
                        int av = (isMagic ? target.RawMagicArmor : target.RawPhysicalArmor)
                            + (isMagic ? targetStats.mDef - targetBase.mDef : targetStats.pDef - targetBase.pDef);

                        string diceNotation = string.IsNullOrWhiteSpace(actor.DamageDice)
                            ? DEFAULT_DAMAGE_DICE : actor.DamageDice;

                        var dealt = ResolvePenetration(
                            pv, av, diceNotation, actor.DamageBonusBase, check.critical);

                        target.CombatHp -= dealt.damage;
                        string tag = check.critical ? "決定的成功！" : "命中！";
                        string atkKind = isMagic ? "魔法" : "物理";
                        string judge = $"{atkKind} PV{pv} vs AV{av}、{PENETRATION_ROLLS}d{PENETRATION_DIE}→{dealt.best} 余剰{dealt.margin:+#;-#;0}";

                        if (dealt.tier == PenetrationTier.Blocked)
                        {
                            logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} {tag}（D100={check.roll}≤{hitPercent}%、{judge}） 装甲に弾かれた ダメージなし");
                        }
                        else
                        {
                            string bonusText = dealt.bonusDamage != 0 ? $" ダメージ・ボーナス{dealt.bonusDamage:+#;-#;0}" : "";
                            logs.Add($"  Phase {phase}: {actor.Name}→{target.Name} {tag}（D100={check.roll}≤{hitPercent}%、{judge}） {TierName(dealt.tier)} {diceNotation}×{dealt.weaponRolls}={dealt.weaponDamage}{bonusText} ダメージ={dealt.damage} HP={Math.Max(0, target.CombatHp)}/{target.CombatHpMax}");
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
                    res.rounds = round;
                    return res;
                }
            }

            if (lost > 0 && morale.Rate <= 0.3f)
                logs.Add($"  Phase {phase}: 士気が揺らいでいる（士気 {morale.Current}/{morale.Max}）");
        }

        logs.Add($"  Phase {phase}: 長期戦 → 撤退扱い");
        res.adventurersRetreated = true;
        res.rounds = round;
        return res;
    }

    /// <summary>貫通の深さ。CoCの成功レベルに対応し、そのまま振る武器ダイスの本数になる。</summary>
    public enum PenetrationTier { Blocked = 0, Regular = 1, Hard = 2, Extreme = 3 }

    /// <summary>ダメージの内訳。ログに計算過程を出すために各段階を持ち回る。</summary>
    public readonly record struct PenetrationResult(
        PenetrationTier tier, int best, int margin, int weaponRolls, int weaponDamage, int bonusDamage, int damage);

    /// <summary>
    /// 貫通判定。ダイスを PENETRATION_ROLLS 個振って最良の出目を採り、PV-AVを足した余剰から成功レベルを決める。
    /// 「3回振って1回でも閾値を超えたら貫通」は「最良の出目が閾値を超える」と同値なので、
    /// 判定はこの1回だけで済み、セットを繰り返す必要がない。
    /// </summary>
    public static PenetrationTier RollTier(int pv, int av, out int best, out int margin)
    {
        best = 0;
        for (int i = 0; i < PENETRATION_ROLLS; i++)
            best = Math.Max(best, GameRandom.Range(1, PENETRATION_DIE + 1));
        margin = best + pv - av;
        return TierOf(margin);
    }

    /// <summary>余剰から成功レベルを求める。TIER_STEPごとに1段上がり、イクストリームで打ち止め。</summary>
    public static PenetrationTier TierOf(int margin)
    {
        if (margin <= 0) return PenetrationTier.Blocked;
        if (margin > TIER_STEP * 2) return PenetrationTier.Extreme;
        if (margin > TIER_STEP) return PenetrationTier.Hard;
        return PenetrationTier.Regular;
    }

    /// <summary>
    /// ダメージ・ボーナス。CoCのSTR+SIZ表に倣い、帯域を1つ上がるごとに加算ダイスが1段強くなる。
    /// 最下帯（体格も筋力も乏しい相手）はダイスではなく1点の減算になる。
    /// </summary>
    public static (string dice, int flat) DamageBonus(int damageBonusBase)
    {
        int band = Math.Max(0, damageBonusBase) / DAMAGE_BONUS_BAND;
        return band switch
        {
            0 => damageBonusBase < DAMAGE_BONUS_BAND / 2 ? ("", -1) : ("", 0),
            1 => ("1d4", 0),
            2 => ("1d6", 0),
            _ => ($"{band - 1}d6", 0),
        };
    }

    /// <summary>
    /// 命中後のダメージ。成功レベルに応じて武器ダイスを1〜3回振り、ダメージ・ボーナスを加える。
    /// 貫通できなければダメージは0で、装甲に完全に弾かれたことを意味する。
    /// </summary>
    public static PenetrationResult ResolvePenetration(
        int pv, int av, string diceNotation, int damageBonusBase, bool critical)
    {
        var tier = RollTier(pv, av, out int best, out int margin);

        // 決定的成功は必ず刃が届く。装甲に完全に弾かれることはない。
        if (critical && tier == PenetrationTier.Blocked) tier = PenetrationTier.Regular;

        if (tier == PenetrationTier.Blocked)
            return new PenetrationResult(tier, best, margin, 0, 0, 0, 0);

        var dice = Dice.Parse(diceNotation);
        int rolls = (int)tier;
        int weaponDamage = 0;
        for (int i = 0; i < rolls; i++) weaponDamage += dice.Roll();

        // インペイル（CoC準拠）：決定的成功は「武器ダイスの最大値＋もう1回のロール」を上乗せする。
        if (critical) weaponDamage += dice.Max + dice.Roll();

        var (bonusDice, bonusFlat) = DamageBonus(damageBonusBase);
        int bonusDamage = bonusFlat + (bonusDice.Length > 0 ? Dice.Roll(bonusDice) : 0);

        int total = Math.Max(0, weaponDamage + bonusDamage);
        return new PenetrationResult(tier, best, margin, rolls, weaponDamage, bonusDamage, total);
    }

    static string TierName(PenetrationTier tier) => tier switch
    {
        PenetrationTier.Extreme => "イクストリーム貫通",
        PenetrationTier.Hard => "ハード貫通",
        PenetrationTier.Regular => "貫通",
        _ => "装甲に弾かれた",
    };

    // D100を振って命中率%と比較するCoC式パーセンタイル判定。
    // 成功のうち出目が命中率の1/5以下なら決定的成功、失敗のうち出目96以上なら大失敗。
    static (bool success, bool critical, bool fumble, int roll) RollD100(int successPercent)
    {
        int roll = GameRandom.Range(1, 101);
        bool success = roll <= successPercent;
        bool critical = success && roll <= Math.Max(1, successPercent / CRIT_ROLL_DIVISOR);
        bool fumble = !success && roll >= FUMBLE_ROLL_THRESHOLD;
        return (success, critical, fumble, roll);
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
        if (w.magicCoeff > 0f) return true;
        if (w.healCoeff > 0f) return true;
        return w.weaponType == WeaponType.Bow;
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
