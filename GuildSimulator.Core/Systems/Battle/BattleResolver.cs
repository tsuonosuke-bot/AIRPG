using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;

namespace GuildSimulator.Core.Systems.Battle;

public static class BattleResolver
{
    public const int SurvivalPartyHpPercent = 50;
    public const int SurvivalMemberHpPercent = 25;
    public class Result
    {
        public bool adventurersRetreated;
        public ExpeditionRetreatReason retreatReason;
        public int rounds;

        /// <summary>
        /// 敵側の最後の生存者へとどめを刺した冒険者ID。
        /// ボス編成を全滅させた場合は、そのまま主討伐の本人判定に使える。
        /// </summary>
        public string finishingAdventurerId = "";
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

    /// <summary>
    /// 狙われにくさの下限。どれだけ気配を消しても完全に的から外れることはない
    /// （全員が隠形を積んだ編成で抽選が成り立たなくなるのを防ぐ）。
    /// </summary>
    public const float MIN_THREAT_WEIGHT_SCALE = 0.1f;

    /// <summary>応急処置が発動するHP率。これを下回った瞬間に1戦闘1度だけ効く。</summary>
    public const float EMERGENCY_HEAL_HP_RATE = 0.5f;

    public static Result Resolve(
        IUnitMember?[] advSide,
        IUnitMember?[] enemySide,
        List<string> logs,
        int turn,
        int phase,
        MoraleState morale,
        ExpeditionPolicy policy = ExpeditionPolicy.ObjectiveFirst,
        Func<IUnitMember, StatBlock>? temporaryStatBonus = null,
        int emergencyRetreatHpPercent = 0,
        ExpeditionRecorder? recorder = null)
    {
        var res = new Result();
        int actions = 0;
        int round = 0;

        // 斧に削られた装甲は戦闘が終わるまで戻らない。ラウンドをまたいで積み上がるので、
        // 毎ラウンド作り直される statsByMember ではなくここで持つ。
        var armorShredded = new Dictionary<IUnitMember, int>();

        // 応急処置は1回の戦闘につき1度きり。ラウンドをまたいで覚えておく。
        var emergencyHealUsed = new HashSet<IUnitMember>();

        // 毒・出血・火傷・凍結と、一時的な攻勢/守勢/再生はこの戦闘の中だけ保持する。
        var statuses = new CombatStatusTracker();

        logs.Add($"[Turn {turn}] エリア {phase}: 戦闘開始 冒険者 vs {DescribeComposition(enemySide)}");
        ApplyBattleStartStatuses(advSide, enemySide, statuses, logs, phase);
        ApplyBattleStartStatuses(enemySide, advSide, statuses, logs, phase);

        // 格上との遭遇はそれ自体が士気を削る。物差しは冒険者ランクと敵の脅威度（F〜S）。
        int rankGap = UnitCalculator.AvgThreat(enemySide) - UnitCalculator.AvgThreat(advSide);
        int shock = morale.DrainThreatGap(rankGap);
        if (shock > 0)
            logs.Add($"  エリア {phase}: 格上の相手に気圧された（士気 -{shock} → {morale.Current}/{morale.Max}）");

        int partyMaxHp = SumMaxHp(advSide);
        if (ShouldUseSmokeBomb(advSide, enemySide, emergencyRetreatHpPercent))
            return RetreatWithSmoke(logs, phase, round);

        while (actions < MAX_ACTIONS)
        {
            round++;
            logs.Add($"  ── ラウンド {round} ──");
            int partyHpAtRoundStart = SumCurrentHp(advSide);
            int partyDowned = 0;
            partyDowned += statuses.ProcessRoundStart(
                advSide,
                round,
                logs,
                phase,
                (_, fallen) => RecordComradeFell(advSide, fallen, recorder));
            statuses.ProcessRoundStart(
                enemySide,
                round,
                logs,
                phase,
                (source, _) =>
                {
                    // 冒険者以外の継続ダメージが最後なら、直前の撃破者を持ち越さない。
                    // ボス編成の「最後の1体」を誰が倒したかだけが主討伐の本人になる。
                    res.finishingAdventurerId = source is AdventurerData finisher
                        ? finisher.id
                        : "";
                    if (source is AdventurerData adventurer)
                        recorder?.Add(adventurer, ExpeditionRecordType.Kills);
                });
            if (!AnyAlive(advSide) || !AnyAlive(enemySide))
            {
                res.rounds = round;
                return res;
            }
            // 身の置き方を数える。倒したかどうかではなく、どれだけ削られたまま立っていたか。
            RecordStandingGround(advSide, recorder);

            int moraleRecovery = morale.Restore(AppearanceSystem.BattleMoralePerRound(advSide));
            if (moraleRecovery > 0)
                logs.Add($"  エリア {phase}: 隊員の華やかな存在感で士気 +{moraleRecovery}（{morale.Current}/{morale.Max}）");
            var advCalc = UnitCalculator.CalcPerMember(advSide, isAllySide: true);
            if (temporaryStatBonus != null)
            {
                advCalc = advCalc.Select(entry =>
                {
                    var stats = entry.stats;
                    stats += temporaryStatBonus(entry.member);
                    return (entry.member, stats);
                }).ToArray();
            }
            var enemyCalc = UnitCalculator.CalcPerMember(enemySide, isAllySide: false);
            if (advCalc.Length == 0 || enemyCalc.Length == 0) break;

            var statsByMember = new Dictionary<IUnitMember, StatBlock>();
            foreach (var (m, s) in advCalc) statsByMember[m] = statuses.ApplyStatModifiers(m, s);
            foreach (var (m, s) in enemyCalc) statsByMember[m] = statuses.ApplyStatModifiers(m, s);

            var queue = advCalc.Select(x => (member: x.member, isAdvSide: true, stats: statsByMember[x.member], slot: SlotOf(advSide, x.member)))
                .Concat(enemyCalc.Select(x => (member: x.member, isAdvSide: false, stats: statsByMember[x.member], slot: SlotOf(enemySide, x.member))))
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

                if (statuses.TryConsumeStun(actor, logs, phase))
                {
                    actions++;
                    continue;
                }

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
                        logs.Add($"  エリア {phase}: {actor.Name}→{healTarget.Name} 手当て失敗（1d20={healRoll}） うまくいかない");
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
                            logs.Add($"  エリア {phase}: {actor.Name}→{healTarget.Name} {healTag} +{healAmt}（1d20={healRoll}）（{healTarget.CombatHp}/{healTarget.CombatHpMax}）");
                            var regen = CombatStatusDefaults.OnHeal(actor.Weapon);
                            if (regen != null)
                                statuses.Apply(
                                    healTarget,
                                    regen,
                                    actor.Weapon!.displayName,
                                    round,
                                    logs,
                                    phase,
                                    actor);
                            TryCleanseOnHeal(
                                actor, healTarget, entry.slot, statuses, logs, phase);

                            // 士気はパーティ側の指標なので、敵の術者が味方を癒しても動かさない。
                            if (entry.isAdvSide)
                                TryRestoreMoraleOnHeal(
                                    actor, entry.slot, morale, logs, phase);
                        }
                    }
                }
                else
                {
                    // 物理か魔法かは能力値の大小ではなく武器そのもので決まる。
                    // 魔術師が剣を持てば剣で殴り、斧戦士が杖を持てば魔法が飛ぶ。
                    bool isMagic = actor.IsMagicAttack;

                    // 武器クラスの個性は「得物そのもの」＋「スキル・遺物の補正」。
                    // 短剣なら連撃と広い会心域、槍なら装甲貫通、斧なら装甲破壊がここに入る。
                    var traits = actor.Traits.Combine(actorStats);

                    int toHit = actorStats.toHit;
                    if (isRear && !IsRangedWeapon(actor)) toHit -= REAR_MELEE_TO_HIT_PENALTY;

                    // PVは武器の基礎値に能力値modifierを（武器ごとの上限つきで）乗せ、装備・スキル補正を足す。
                    int basePv = QudCombat.EffectivePv(
                        actor.WeaponBasePv, actor.AttackStatModifier, actor.MaxStatBonus,
                        isMagic ? actorStats.mpv : actorStats.pv);

                    // 1振りぶんの解決。右手の連撃も左手の追撃も、得物の数値を差し替えて同じ経路を通る。
                    void Swing(
                        int pv,
                        string? dice,
                        WeaponTraits w,
                        bool magic,
                        string swingTag,
                        EquipmentMasterData? usedWeapon)
                    {
                        var target = PickTarget(enemySideArr, statsByMember);
                        if (target == null) return;
                        target = TryProtectTarget(
                            target, enemySideArr, statsByMember, logs, phase, recorder);
                        var targetStats = statsByMember[target];

                        var conditionalPv = magic
                            ? (bonus: 0, detail: "")
                            : ConditionalPvBonus(actor, entry.slot, target, statuses);
                        int effectivePv = pv + conditionalPv.bonus;

                        int dv = targetStats.dv;
                        if (SlotOf(enemySideArr, target) >= 3 && HasAliveFront(enemySideArr))
                            dv += REAR_COVER_DV_BONUS;

                        var check = QudCombat.RollToHit(toHit, dv, w.critRange);
                        // 守りの隙は当たった一撃だけを重くする。外れた攻撃は会心にならない。
                        check = QudCombat.RaiseToCritical(check, targetStats.incomingCritChancePercent);

                        if (!check.hit)
                        {
                            logs.Add($"  エリア {phase}: {actor.Name}→{target.Name} {swingTag}回避！（1d20={check.roll}{toHit:+#;-#;+0}={check.total} ≦ DV{dv}） ダメージなし");
                            return;
                        }

                        // AVは装甲そのもの。どちらも小さな整数で、1点が貫通回数に効く。
                        // 斧に削られたぶんは物理AVからのみ引く（魔法装甲は割れない）。
                        int av = magic
                            ? Math.Max(0, targetStats.mav)
                            : Math.Max(0, targetStats.av - ShredOf(armorShredded, target));

                        // 盾は常時硬くするのではなく、受けに成功した一撃だけを重くする。
                        // 削られた素の装甲とは別枠で足すので、装甲破壊では剥がせない。
                        string blockTag = "";
                        bool shieldBlocked = false;
                        var shield = target.Shield;
                        if (shield != null && !magic
                            && QudCombat.RollBlock(shield.blockChance + targetStats.blockChance))
                        {
                            shieldBlocked = true;
                            recorder?.Add(target, ExpeditionRecordType.ShieldBlocks);
                            // 高位の盾術は「受けたうえで完全に殺す」。相手のPVがいくつでも通らない。
                            if (QudCombat.RollBlockNegate(targetStats.blockNegate))
                            {
                                logs.Add($"  エリア {phase}: {actor.Name}→{target.Name} {swingTag}{target.Name}は{shield.displayName}で完全に受けきった ダメージなし");
                                if (TryCounterattack(
                                        target, actor, enemySideArr, allySideArr,
                                        statsByMember, armorShredded, statuses, round, logs, phase,
                                        advSide, recorder))
                                {
                                    if (entry.isAdvSide)
                                        partyDowned++;
                                    else if (target is AdventurerData finisher)
                                        res.finishingAdventurerId = finisher.id;
                                }
                                return;
                            }
                            av += shield.blockAv;
                            blockTag = $"（{shield.displayName}で受け AV+{shield.blockAv}）";
                        }

                        var dealt = QudCombat.ResolveAttack(
                            effectivePv, av, dice, check.critical,
                            w.armorPierce, actorStats.critPv, actorStats.autoPenetrate);

                        int hpBefore = target.CombatHp;
                        target.CombatHp -= dealt.damage;
                        string tag = check.critical ? "会心！" : "命中！";
                        string atkKind = magic ? "魔法" : "物理";
                        string roll = $"1d20={check.roll}{toHit:+#;-#;+0}={check.total} > DV{dv}";
                        string pierce = w.armorPierce > 0 && !magic ? $"（装甲貫通-{w.armorPierce}）" : "";
                        string skillPvTag = conditionalPv.bonus != 0
                            ? $"（{conditionalPv.detail} PV+{conditionalPv.bonus}）"
                            : "";
                        string judge = $"{atkKind} PV{dealt.pv} vs AV{dealt.av}{pierce}{skillPvTag}{blockTag}";

                        if (dealt.penetrations == 0)
                        {
                            // 弾かれた経験も財産になる。力任せをやめて隙を探すようになるのはここから。
                            recorder?.Add(actor, ExpeditionRecordType.RepelledByArmor);
                            logs.Add($"  エリア {phase}: {actor.Name}→{target.Name} {swingTag}{tag}（{roll}、{judge}） 装甲に弾かれた ダメージなし");
                            if (shieldBlocked
                                && TryCounterattack(
                                    target, actor, enemySideArr, allySideArr,
                                    statsByMember, armorShredded, statuses, round, logs, phase,
                                    advSide, recorder))
                            {
                                if (entry.isAdvSide)
                                    partyDowned++;
                                else if (target is AdventurerData finisher)
                                    res.finishingAdventurerId = finisher.id;
                            }
                        }
                        else
                        {
                            string shown = string.IsNullOrWhiteSpace(dice) ? QudCombat.DEFAULT_DAMAGE_DICE : dice;
                            string forced = dealt.autoPenetrated ? "急所を突いた！ " : "";
                            logs.Add($"  エリア {phase}: {actor.Name}→{target.Name} {swingTag}{tag}（{roll}、{judge}） {forced}{dealt.penetrations}回貫通 {shown}×{dealt.penetrations} ダメージ={dealt.damage} HP={Math.Max(0, target.CombatHp)}/{target.CombatHpMax}");

                            // 装甲破壊は「貫通した攻撃」にだけ乗る。削れた装甲は味方全員の攻撃にも効く。
                            int shred = ApplyArmorShred(armorShredded, target, w.armorShred, magic);
                            if (shred > 0)
                                logs.Add($"  エリア {phase}: {target.Name} の装甲が砕けた（AV-{shred} 累計-{ShredOf(armorShredded, target)}）");
                        }

                        if (target.CombatHp <= 0)
                        {
                            int severity = 1;
                            if (check.critical) severity++;
                            if (dealt.damage - hpBefore >= Math.Max(1, target.CombatHpMax / 5)) severity++;
                            SetCombatDown(target, severity, logs, phase);
                            if (entry.isAdvSide)
                            {
                                recorder?.Add(actor, ExpeditionRecordType.Kills);
                                if (check.critical) recorder?.Add(actor, ExpeditionRecordType.CritKills);
                                if (actor is AdventurerData finisher)
                                    res.finishingAdventurerId = finisher.id;
                            }
                            else
                            {
                                RecordComradeFell(advSide, target, recorder);
                                partyDowned++;
                            }
                        }
                        else if (dealt.damage > 0)
                        {
                            ApplyOnHitStatuses(
                                actor, target, allySideArr, enemySideArr, usedWeapon,
                                entry.slot, statuses, round, logs, phase);
                            // 深手を負った瞬間に手が動く。倒れてしまってからでは間に合わない。
                            TryEmergencyHeal(target, targetStats, emergencyHealUsed, logs, phase);
                        }
                    }

                    // 連撃は同じ手番のうちに続けて振るう。狙いは1振りごとに選び直す。
                    int swings = 1 + traits.extraAttacks;
                    for (int swing = 0; swing < swings; swing++)
                    {
                        if (!AnyAlive(enemySideArr) || !actor.IsAlive) break;
                        Swing(QudCombat.FollowUpPv(basePv, swing), actor.DamageDice, traits, isMagic,
                            swing == 0 ? "" : "追撃 ", actor.Weapon);
                    }

                    // 左手の武器は確率でしか振れない。連撃の減衰とは別枠で、常に本来のPVで入る
                    // （連撃は右手の性質、二刀流は装備構成の話なので、混ぜると二重取りになる）。
                    var offHand = actor.OffHandWeapon;
                    if (offHand != null && AnyAlive(enemySideArr) && actor.IsAlive)
                    {
                        var offTraits = offHand.Traits.Combine(actorStats);
                        int chance = QudCombat.OFF_HAND_BASE_CHANCE
                            + offHand.offHandBonus + actorStats.offHandChance;
                        if (QudCombat.RollOffHand(chance))
                        {
                            // 左手に魔法は持てない（魔法は両手武器）ので、常に物理として解決する。
                            int offPv = QudCombat.EffectivePv(
                                offHand.basePv, actor.AttackStatModifier, offHand.maxStatBonus, actorStats.pv);
                            Swing(offPv, offHand.damageDice, offTraits, magic: false, "左手 ", offHand);
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
                if (ShouldUseSmokeBomb(advSide, enemySide, emergencyRetreatHpPercent))
                    return RetreatWithSmoke(logs, phase, round);
            }

            // 士気は「押し込まれた分だけ」削れる。回復で押し返せた分は勘定に入らないので、
            // 神官がHPを保っている限り士気も保たれ、優勢なまま突然逃げ出すことはない。
            int netHpLoss = partyHpAtRoundStart - SumCurrentHp(advSide);
            int lost = morale.DrainFromDamage(netHpLoss, partyMaxHp);
            if (partyDowned > 0)
            {
                int downLoss = morale.DrainAllyDown(partyDowned);
                lost += downLoss;
                if (downLoss > 0) logs.Add($"  エリア {phase}: 仲間が倒れて動揺した（士気 -{downLoss}）");
            }

            if (morale.IsBroken)
            {
                logs.Add($"  エリア {phase}: 士気が尽きた！パーティは撤退する（士気 0/{morale.Max}）");
                res.adventurersRetreated = true;
                res.retreatReason = ExpeditionRetreatReason.MoraleBroken;
                res.rounds = round;
                return res;
            }

            // 生還優先では、全滅寸前まで粘らず、損耗が危険域へ入った時点で引き返す。
            // 士気切れは方針に関わらず上の撤退ロジックが担うため、ここでは損耗（HP）だけで判断する。
            if (policy == ExpeditionPolicy.SurvivalFirst && AnyAlive(advSide) && AnyAlive(enemySide))
            {
                if (ShouldSurvivalFirstRetreat(advSide))
                {
                    logs.Add($"  エリア {phase}: 生還優先の命令に従い、損耗が危険域へ達する前に撤退した");
                    res.adventurersRetreated = true;
                    res.retreatReason = ExpeditionRetreatReason.SurvivalPolicy;
                    res.rounds = round;
                    return res;
                }
            }

            if (lost > 0 && morale.Rate <= 0.3f)
                logs.Add($"  エリア {phase}: 士気が揺らいでいる（士気 {morale.Current}/{morale.Max}）");

            statuses.EndRound(round);
        }

        logs.Add($"  エリア {phase}: 長期戦 → 撤退扱い");
        res.adventurersRetreated = true;
        res.retreatReason = ExpeditionRetreatReason.BattleStalemate;
        res.rounds = round;
        return res;
    }

    /// <summary>
    /// 生還優先の撤退線。生存者の合計HPが50%以下、または誰か1人が25%以下なら撤退する。
    /// 整数比較にして、ちょうど閾値に達した場合も確実に撤退させる。
    /// </summary>
    public static bool ShouldSurvivalFirstRetreat(IEnumerable<IUnitMember?> party)
    {
        var alive = party.Where(member => member != null && member.IsAlive).Select(member => member!).ToList();
        if (alive.Count == 0) return false;

        int partyMaxHp = alive.Sum(member => Math.Max(1, member.CombatHpMax));
        int partyCurrentHp = alive.Sum(member => Math.Max(0, member.CombatHp));
        bool partyBadlyHurt = partyCurrentHp * 100 <= partyMaxHp * SurvivalPartyHpPercent;
        bool memberInDanger = alive.Any(member =>
            Math.Max(0, member.CombatHp) * 100
            <= Math.Max(1, member.CombatHpMax) * SurvivalMemberHpPercent);
        return partyBadlyHurt || memberInDanger;
    }

    static bool ShouldUseSmokeBomb(
        IUnitMember?[] advSide, IUnitMember?[] enemySide, int hpPercent)
    {
        if (hpPercent <= 0 || !AnyAlive(advSide) || !AnyAlive(enemySide)) return false;
        int maxHp = advSide.Where(a => a != null).Sum(a => Math.Max(0, a!.CombatHpMax));
        int currentHp = advSide
            .Where(a => a != null && a.IsAlive)
            .Sum(a => Math.Max(0, a!.CombatHp));
        return maxHp > 0 && currentHp * 100 <= maxHp * Math.Clamp(hpPercent, 1, 99);
    }

    static Result RetreatWithSmoke(List<string> logs, int phase, int round)
    {
        logs.Add($"  エリア {phase}: 機関の煙玉を展開！煙に紛れて戦闘から離脱した");
        return new Result
        {
            adventurersRetreated = true,
            retreatReason = ExpeditionRetreatReason.SmokeBomb,
            rounds = round,
        };
    }

    static void ApplyBattleStartStatuses(
        IUnitMember?[] side,
        IUnitMember?[] opponents,
        CombatStatusTracker statuses,
        List<string> logs,
        int phase)
    {
        for (int slot = 0; slot < side.Length; slot++)
        {
            var actor = side[slot];
            if (actor == null || !actor.IsAlive) continue;

            var defaultEffect = CombatStatusDefaults.BattleStart(actor.Weapon);
            if (defaultEffect != null)
                ApplyStatusTargets(actor, null, side, opponents, defaultEffect,
                    actor.Weapon!.displayName, statuses, currentRound: 1, logs, phase);

            foreach (var effect in actor.Weapon?.battleStartStatuses ?? new())
                ApplyStatusTargets(actor, null, side, opponents, effect,
                    actor.Weapon!.displayName, statuses, currentRound: 1, logs, phase);

            foreach (var skill in actor.Skills.Where(skill =>
                         UnitCalculator.IsSkillActive(skill, actor, isFront: slot < 3)))
                foreach (var effect in skill.battleStartStatuses)
                    ApplyStatusTargets(actor, null, side, opponents, effect,
                        skill.skillName, statuses, currentRound: 1, logs, phase);
        }
    }

    static void ApplyOnHitStatuses(
        IUnitMember actor,
        IUnitMember hitTarget,
        IUnitMember?[] allies,
        IUnitMember?[] enemies,
        EquipmentMasterData? usedWeapon,
        int actorSlot,
        CombatStatusTracker statuses,
        int currentRound,
        List<string> logs,
        int phase)
    {
        var defaultEffect = CombatStatusDefaults.OnHit(usedWeapon);
        if (defaultEffect != null)
            ApplyStatusTargets(actor, hitTarget, allies, enemies, defaultEffect,
                usedWeapon!.displayName, statuses, currentRound, logs, phase);

        foreach (var effect in usedWeapon?.onHitStatuses ?? new())
            ApplyStatusTargets(actor, hitTarget, allies, enemies, effect,
                usedWeapon!.displayName, statuses, currentRound, logs, phase);

        foreach (var skill in actor.Skills.Where(skill =>
                     UnitCalculator.IsSkillActive(skill, actor, isFront: actorSlot < 3)))
            foreach (var effect in skill.onHitStatuses)
                ApplyStatusTargets(actor, hitTarget, allies, enemies, effect,
                    skill.skillName, statuses, currentRound, logs, phase);
    }

    static void TryCleanseOnHeal(
        IUnitMember healer,
        IUnitMember target,
        int healerSlot,
        CombatStatusTracker statuses,
        List<string> logs,
        int phase)
    {
        if (!statuses.HasHarmfulStatus(target)) return;

        SkillMasterData? source = null;
        foreach (var skill in healer.Skills)
        {
            if (!UnitCalculator.IsSkillActive(skill, healer, isFront: healerSlot < 3)) continue;
            if (skill.battle.cleanseOnHealChancePercent <= 0) continue;
            if (source == null
                || skill.battle.cleanseOnHealChancePercent > source.battle.cleanseOnHealChancePercent)
                source = skill;
        }

        if (source == null || !RollPercent(source.battle.cleanseOnHealChancePercent)) return;
        statuses.CleanseOneHarmful(target, source.skillName, logs, phase);
    }

    /// <summary>
    /// 手当てが通ったときに士気を少しだけ戻す。回復役が「立て直している」ことを、
    /// HPだけでなくパーティの粘りにも反映させるための経路。
    /// 同じ効果を複数のスキルから持っていても、いちばん高い1つだけが効く（浄化と同じ扱い）。
    /// </summary>
    static void TryRestoreMoraleOnHeal(
        IUnitMember healer,
        int healerSlot,
        MoraleState morale,
        List<string> logs,
        int phase)
    {
        int percent = 0;
        SkillMasterData? source = null;
        foreach (var skill in healer.Skills)
        {
            if (!UnitCalculator.IsSkillActive(skill, healer, isFront: healerSlot < 3)) continue;
            if (skill.battle.moraleOnHealPercent <= percent) continue;
            percent = skill.battle.moraleOnHealPercent;
            source = skill;
        }

        if (source == null || percent <= 0) return;
        int restored = morale.RestoreRate(percent / 100f);
        if (restored <= 0) return;
        logs.Add($"  エリア {phase}: {healer.Name}の{source.skillName}で士気 +{restored}（{morale.Current}/{morale.Max}）");
    }

    /// <summary>瀕死の味方への攻撃を、庇護スキルを持つ別の生存者へ差し替える。</summary>
    static IUnitMember TryProtectTarget(
        IUnitMember originalTarget,
        IUnitMember?[] defendingSide,
        IReadOnlyDictionary<IUnitMember, StatBlock> statsByMember,
        List<string> logs,
        int phase,
        ExpeditionRecorder? recorder = null)
    {
        IUnitMember? protector = null;
        SkillMasterData? source = null;

        for (int slot = 0; slot < defendingSide.Length; slot++)
        {
            var candidate = defendingSide[slot];
            if (candidate == null || !candidate.IsAlive || candidate == originalTarget) continue;

            foreach (var skill in candidate.Skills)
            {
                if (!UnitCalculator.IsSkillActive(skill, candidate, isFront: slot < 3)) continue;
                int threshold = Math.Clamp(skill.battle.protectAllyHpPercent, 0, 100);
                int chance = Math.Clamp(skill.battle.protectChancePercent, 0, 100);
                if (threshold <= 0 || chance <= 0 || originalTarget.CombatHpMax <= 0) continue;
                if (Math.Max(0, originalTarget.CombatHp) * 100
                    > originalTarget.CombatHpMax * threshold) continue;

                if (source == null || chance > source.battle.protectChancePercent)
                {
                    protector = candidate;
                    source = skill;
                }
            }
        }

        if (protector == null || source == null
            || !statsByMember.ContainsKey(protector)
            || !RollPercent(source.battle.protectChancePercent))
            return originalTarget;

        recorder?.Add(protector, ExpeditionRecordType.ProtectedAlly);
        logs.Add($"  エリア {phase}: {protector.Name}が{originalTarget.Name}を庇った（{source.skillName}）");
        return protector;
    }

    /// <summary>
    /// ラウンド頭の身の置き方を数える。HP率だけを見るので、殴り勝っているかどうかとは無関係に
    /// 「削られたまま踏みとどまっていた時間」が積み上がる。
    /// </summary>
    static void RecordStandingGround(IUnitMember?[] advSide, ExpeditionRecorder? recorder)
    {
        if (recorder == null) return;
        foreach (var member in advSide)
        {
            if (member == null || !member.IsAlive || member.CombatHpMax <= 0) continue;
            int hp = Math.Max(0, member.CombatHp);
            if (hp * 4 <= member.CombatHpMax)
                recorder.Add(member, ExpeditionRecordType.NearDeathRounds);
            if (hp * 2 <= member.CombatHpMax)
                recorder.Add(member, ExpeditionRecordType.LowHpRounds);
        }
    }

    /// <summary>味方が倒れた瞬間を、本人と、それを見ていた生存者の双方に刻む。</summary>
    static void RecordComradeFell(
        IUnitMember?[] advSide, IUnitMember fallen, ExpeditionRecorder? recorder)
    {
        if (recorder == null) return;
        recorder.Add(fallen, ExpeditionRecordType.TimesDowned);
        foreach (var member in advSide)
        {
            if (member == null || member == fallen) continue;
            // まだ立っている者だけが「見ていた」。同じ攻撃で共に崩れた者には刻まない。
            if (!member.IsAlive || member.CombatHp <= 0) continue;
            recorder.Add(member, ExpeditionRecordType.AlliesFellBeside);
        }
    }

    /// <summary>処刑人と背水の、攻撃対象・現在HPを見て初めて決まる物理PV。</summary>
    static (int bonus, string detail) ConditionalPvBonus(
        IUnitMember actor,
        int actorSlot,
        IUnitMember target,
        CombatStatusTracker statuses)
    {
        int total = 0;
        var sources = new List<string>();
        foreach (var skill in actor.Skills)
        {
            if (!UnitCalculator.IsSkillActive(skill, actor, isFront: actorSlot < 3)) continue;
            int skillBonus = 0;
            if (skill.battle.afflictedTargetPv != 0 && statuses.HasDamagingAilment(target))
                skillBonus += skill.battle.afflictedTargetPv;

            int threshold = Math.Clamp(skill.battle.lowHpThresholdPercent, 0, 100);
            if (threshold > 0 && skill.battle.lowHpPv != 0 && actor.CombatHpMax > 0
                && Math.Max(0, actor.CombatHp) * 100 <= actor.CombatHpMax * threshold)
                skillBonus += skill.battle.lowHpPv;

            if (skillBonus == 0) continue;
            total += skillBonus;
            sources.Add(skill.skillName);
        }
        return (total, string.Join("+", sources));
    }

    /// <summary>
    /// 盾で完全に防いだ者が、構えている主武器で1回だけ反撃する。
    /// 反撃には連撃・左手・盾受け・再反撃を発生させず、再帰する戦闘を防ぐ。
    /// </summary>
    static bool TryCounterattack(
        IUnitMember defender,
        IUnitMember attacker,
        IUnitMember?[] defenderSide,
        IUnitMember?[] attackerSide,
        IReadOnlyDictionary<IUnitMember, StatBlock> statsByMember,
        Dictionary<IUnitMember, int> armorShredded,
        CombatStatusTracker statuses,
        int currentRound,
        List<string> logs,
        int phase,
        IUnitMember?[]? advSide = null,
        ExpeditionRecorder? recorder = null)
    {
        if (!defender.IsAlive || !attacker.IsAlive || defender.Weapon == null
            || defender.IsMagicAttack || defender.Weapon.IsHealWeapon)
            return false;

        int defenderSlot = SlotOf(defenderSide, defender);
        var counterSkills = defender.Skills
            .Where(skill => UnitCalculator.IsSkillActive(
                skill, defender, isFront: defenderSlot < 3)
                && skill.battle.counterChancePercent > 0)
            .ToList();
        int chance = Math.Clamp(counterSkills.Sum(skill => skill.battle.counterChancePercent), 0, 100);
        if (chance <= 0 || !RollPercent(chance)) return false;
        if (!statsByMember.TryGetValue(defender, out var defenderStats)
            || !statsByMember.TryGetValue(attacker, out var attackerStats))
            return false;

        int toHit = defenderStats.toHit;
        if (defenderSlot >= 3 && !IsRangedWeapon(defender))
            toHit -= REAR_MELEE_TO_HIT_PENALTY;
        int dv = attackerStats.dv;
        if (SlotOf(attackerSide, attacker) >= 3 && HasAliveFront(attackerSide))
            dv += REAR_COVER_DV_BONUS;

        var traits = defender.Traits.Combine(defenderStats);
        var check = QudCombat.RollToHit(toHit, dv, traits.critRange);
        // 反撃も普通の一撃と同じ扱い。受け手の隙はここでも会心を呼び込む。
        check = QudCombat.RaiseToCritical(check, attackerStats.incomingCritChancePercent);
        string source = string.Join("+", counterSkills.Select(skill => skill.skillName));
        if (!check.hit)
        {
            logs.Add($"  エリア {phase}: {defender.Name}→{attacker.Name} 反撃は回避された"
                + $"（1d20={check.roll}{toHit:+#;-#;+0}={check.total} ≦ DV{dv}、{source}）");
            return false;
        }

        var conditional = ConditionalPvBonus(defender, defenderSlot, attacker, statuses);
        int pv = QudCombat.EffectivePv(
            defender.WeaponBasePv,
            defender.AttackStatModifier,
            defender.MaxStatBonus,
            defenderStats.pv) + conditional.bonus;
        int av = Math.Max(0, attackerStats.av - ShredOf(armorShredded, attacker));
        var dealt = QudCombat.ResolveAttack(
            pv, av, defender.DamageDice, check.critical,
            traits.armorPierce, defenderStats.critPv, defenderStats.autoPenetrate);

        attacker.CombatHp -= dealt.damage;
        if (dealt.penetrations == 0)
        {
            recorder?.Add(defender, ExpeditionRecordType.RepelledByArmor);
            logs.Add($"  エリア {phase}: {defender.Name}→{attacker.Name} 反撃（{source}）は装甲に弾かれた");
            return false;
        }

        logs.Add($"  エリア {phase}: {defender.Name}→{attacker.Name} 反撃（{source}） "
            + $"{dealt.penetrations}回貫通 ダメージ={dealt.damage} "
            + $"HP={Math.Max(0, attacker.CombatHp)}/{attacker.CombatHpMax}");
        int shred = ApplyArmorShred(armorShredded, attacker, traits.armorShred, isMagic: false);
        if (shred > 0)
            logs.Add($"  エリア {phase}: {attacker.Name} の装甲が砕けた"
                + $"（AV-{shred} 累計-{ShredOf(armorShredded, attacker)}）");

        if (attacker.CombatHp <= 0)
        {
            int severity = check.critical ? 2 : 1;
            SetCombatDown(attacker, severity, logs, phase);
            recorder?.Add(defender, ExpeditionRecordType.Kills);
            if (check.critical) recorder?.Add(defender, ExpeditionRecordType.CritKills);
            if (advSide != null && advSide.Contains(attacker))
                RecordComradeFell(advSide, attacker, recorder);
            return true;
        }

        ApplyOnHitStatuses(
            defender, attacker, defenderSide, attackerSide, defender.Weapon,
            defenderSlot, statuses, currentRound, logs, phase);
        return false;
    }

    static bool RollPercent(int chance)
    {
        chance = Math.Clamp(chance, 0, 100);
        return chance >= 100 || (chance > 0 && GameRandom.Range(1, 101) <= chance);
    }

    static void ApplyStatusTargets(
        IUnitMember actor,
        IUnitMember? hitTarget,
        IUnitMember?[] allies,
        IUnitMember?[] enemies,
        CombatStatusApplicationData effect,
        string sourceName,
        CombatStatusTracker statuses,
        int currentRound,
        List<string> logs,
        int phase)
    {
        switch (effect.target)
        {
            case CombatStatusTarget.Self:
                statuses.Apply(actor, effect, sourceName, currentRound, logs, phase, actor);
                break;
            case CombatStatusTarget.Allies:
                foreach (var ally in allies.Where(m => m != null && m.IsAlive).Select(m => m!))
                    statuses.Apply(ally, effect, sourceName, currentRound, logs, phase, actor);
                break;
            default:
                var target = hitTarget ?? PickRandomAlive(enemies);
                if (target != null)
                    statuses.Apply(target, effect, sourceName, currentRound, logs, phase, actor);
                break;
        }
    }

    static IUnitMember? PickRandomAlive(IUnitMember?[] side)
    {
        var alive = side.Where(m => m != null && m.IsAlive).Select(m => m!).ToList();
        return alive.Count == 0 ? null : alive[GameRandom.Range(0, alive.Count)];
    }

    static void SetCombatDown(IUnitMember target, int severity, List<string> logs, int phase)
    {
        target.CombatHp = 0;
        if (target is AdventurerData adventurer)
        {
            adventurer.RegisterKnockout(severity);
            logs.Add($"  エリア {phase}: {target.Name} は戦闘不能！ 帰還後に生死・負傷を判定する");
        }
        else
        {
            target.IsAlive = false;
            logs.Add($"  エリア {phase}: {target.Name} 撃破！");
        }
    }

    // 敵の内訳（名前・レベル・頭数）をログに残し、戦闘ログだけで強さの見立てができるようにする。
    static string DescribeComposition(IUnitMember?[] side)
    {
        var groups = side.Where(a => a != null && a.IsAlive)
            .GroupBy(a => (a!.Name, a.Threat))
            .Select(g => g.Count() > 1
                ? $"{g.Key.Name}({Models.Rank.Label(g.Key.Threat)})×{g.Count()}"
                : $"{g.Key.Name}({Models.Rank.Label(g.Key.Threat)})");
        var desc = string.Join("、", groups);
        return desc.Length > 0 ? desc : "敵";
    }

    static int ShredOf(Dictionary<IUnitMember, int> shredded, IUnitMember target)
        => shredded.TryGetValue(target, out var v) ? v : 0;

    /// <summary>
    /// 装甲破壊を積む。1体につき<see cref="QudCombat.MAX_ARMOR_SHRED"/>までで打ち止めにして、
    /// 長引いた戦闘で装甲が意味を失うのを防ぐ。実際に削れた量を返す。
    /// </summary>
    static int ApplyArmorShred(
        Dictionary<IUnitMember, int> shredded, IUnitMember target, int amount, bool isMagic)
    {
        if (amount <= 0 || isMagic) return 0;
        int current = ShredOf(shredded, target);
        int applied = Math.Min(amount, QudCombat.MAX_ARMOR_SHRED - current);
        if (applied <= 0) return 0;
        shredded[target] = current + applied;
        return applied;
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

    /// <summary>
    /// 応急処置。HPが半分を切った瞬間に、最大HPの emergencyHeal% を自分で取り返す。
    /// 1回の戦闘につき1度きりなので、粘りを生むが立て直しの手にはならない。
    /// </summary>
    static void TryEmergencyHeal(
        IUnitMember target, StatBlock stats, HashSet<IUnitMember> used, List<string> logs, int phase)
    {
        if (stats.emergencyHeal <= 0 || !target.IsAlive) return;
        if (target.CombatHpMax <= 0) return;
        if (HpRate(target) >= EMERGENCY_HEAL_HP_RATE) return;
        if (!used.Add(target)) return;

        int amount = (int)Math.Ceiling(target.CombatHpMax * stats.emergencyHeal / 100f);
        amount = Math.Min(amount, target.CombatHpMax - target.CombatHp);
        if (amount <= 0) return;

        target.CombatHp += amount;
        logs.Add($"  エリア {phase}: {target.Name} 応急処置 +{amount}（{target.CombatHp}/{target.CombatHpMax}）");
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
    static IUnitMember? PickTarget(IUnitMember?[] side, IReadOnlyDictionary<IUnitMember, StatBlock> statsByMember)
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
        return PickWeightedBySquishiness(pool, statsByMember);
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
    // そのうえで threatWeight（挑発・隠形）を掛ける。囮は硬くても引きつけられ、
    // 気配を消した者は柔らかくても後回しにされる。
    static IUnitMember? PickWeightedBySquishiness(
        List<IUnitMember> pool, IReadOnlyDictionary<IUnitMember, StatBlock> statsByMember)
    {
        if (pool.Count == 0) return null;
        float sum = 0;
        var weights = new float[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            // 隊列を織り込んだ最終値があればそれを使う。無ければ素の値で近似する。
            var s = statsByMember.TryGetValue(pool[i], out var known)
                ? known
                : pool[i].GetFinalCombatStats();
            int toughness = Math.Max(0, s.av) + Math.Max(0, s.mav) + Math.Max(0, s.dv);
            float w = 1f / (1f + toughness / 10f);
            w *= Math.Max(MIN_THREAT_WEIGHT_SCALE, 1f + s.threatWeight / 100f);
            w *= AppearanceSystem.TargetWeightMultiplier(pool[i]);
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
