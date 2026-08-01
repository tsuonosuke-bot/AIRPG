namespace GuildSimulator.Core.Systems.Battle;

/// <summary>
/// Caves of Qud のダメージ計算式。攻撃は「命中判定」と「貫通判定」の二段構えで、
/// ダメージの大きさはダメージダイスそのものではなく<b>何回貫通したか</b>で決まる。
///
///   1. 命中: 1d20 + 命中補正 が相手のDV(回避値)を上回れば命中
///   2. 貫通: (1d10-2) + PV を AV と比べる試行を3回で1セット。1回でも上回れば1貫通。
///            3回とも上回ったらPVを2下げて次のセットへ進み、貫通を積み増す
///   3. 損傷: 貫通回数ぶんだけ武器のダメージダイスを振って合計する
///
/// 能力値は直接ダメージに乗らない。筋力(魔法は知力)はPVに、敏捷はDVと命中に効き、
/// 装甲はAVとして「そもそも通るかどうか」を決める。
/// </summary>
public static class QudCombat
{
    // ---- 能力値modifier ----
    // Qudは能力値16を平均として (能力値-16)/2 をmodifierにする。
    // 本作の能力値はLv1で4〜14、成長後で30前後というレンジなので、平均点を8に置き換えて縮尺する。
    public const int MODIFIER_BASELINE = 8;
    public const int MODIFIER_STEP = 2;

    /// <summary>能力値modifier。基準値からMODIFIER_STEPごとに±1。</summary>
    public static int Modifier(int stat)
        => (int)Math.Floor((stat - MODIFIER_BASELINE) / (double)MODIFIER_STEP);

    // ---- 命中判定 ----
    public const int HIT_DIE = 20;
    public const int BASE_DV = 6;          // Qud同様、DVには常に下駄6を履かせる
    public const int CRITICAL_ROLL = 20;   // 出目20は素の値で判定し、DVに関わらず命中する
    public const int FUMBLE_ROLL = 1;      // 出目1は補正に関わらず必中しない
    public const int MAX_CRIT_RANGE = 5;   // 会心域の上限。広げても15〜20（30%）で頭打ちにする

    // ---- 貫通判定 ----
    public const int PENETRATION_DIE = 10;         // 1d10
    public const int PENETRATION_OFFSET = -2;      // -2して振る（-1〜8）
    public const int PENETRATION_ROLLS_PER_SET = 3; // 3回で1セット
    public const int PENETRATION_PV_DECAY = 2;     // 3回とも抜けたら次セットはPV-2
    public const int CRITICAL_PV_BONUS = 1;        // 決定的命中はPV+1
    public const int MAX_PENETRATIONS = 20;        // PVは必ず減衰するので理論上不要だが安全弁として置く

    public const string DEFAULT_DAMAGE_DICE = "1d2"; // 素手・自然攻撃のフォールバック
    public const int DEFAULT_WEAPON_PV = 4;          // Qudの標準的な武器のPV

    // ---- 武器クラスの個性 ----
    /// <summary>追撃はn回目ごとにPVがこれだけ下がる。手数がそのまま火力倍増にならないための減衰。</summary>
    public const int FOLLOW_UP_PV_PENALTY = 2;

    /// <summary>1体につき1回の戦闘で削れる装甲の上限。斧の削りが青天井に走らないための蓋。</summary>
    public const int MAX_ARMOR_SHRED = 3;

    // ---- 二刀流と盾 ----
    /// <summary>
    /// 二刀流スキルを持たない者が左手の武器を振れる確率（%）。
    /// 誰でも持てば少しは振れるが、当てにできる頻度にはスキルが要る。
    /// </summary>
    public const int OFF_HAND_BASE_CHANCE = 15;

    /// <summary>左手の攻撃が発動したか。</summary>
    public static bool RollOffHand(int chance)
        => chance > 0 && GameRandom.Range(0, 100) < Math.Min(chance, 100);

    /// <summary>盾で受け止められたか。成功した攻撃にだけ盾の装甲が乗る。</summary>
    public static bool RollBlock(int chance)
        => chance > 0 && GameRandom.Range(0, 100) < Math.Min(chance, 100);

    /// <summary>命中判定の結果。ログに出目をそのまま載せられるよう内訳を持ち回る。</summary>
    public readonly record struct HitResult(int roll, int total, bool hit, bool critical);

    /// <summary>会心になる最小の出目。critRangeを広げるほど下がる（0なら20のみ）。</summary>
    public static int CriticalThreshold(int critRange)
        => CRITICAL_ROLL - Math.Clamp(critRange, 0, MAX_CRIT_RANGE);

    /// <summary>
    /// 1d20を振って命中補正を足し、DVと比べる。
    /// critRangeを持つ武器（短剣）は出目20だけでなく19、18…も会心になる。
    /// ただし出目1は会心域には決してならず、常に外れる。
    /// </summary>
    public static HitResult RollToHit(int toHitBonus, int dv, int critRange = 0)
    {
        int roll = GameRandom.Range(1, HIT_DIE + 1);
        bool fumble = roll == FUMBLE_ROLL;
        bool critical = !fumble && roll >= CriticalThreshold(critRange);
        int total = roll + toHitBonus;
        bool hit = critical || (!fumble && total > dv);
        return new HitResult(roll, total, hit, critical);
    }

    /// <summary>
    /// 貫通ダイス1個ぶん。1d10-2を振り、最大の出目(10 → +8)が出るたびに振り足して加算する。
    /// 上振れが青天井なので、AVが高くても薄い可能性で貫通の目が残る。
    /// </summary>
    public static int RollPenetrationDie()
    {
        int total = 0;
        int die;
        do
        {
            die = GameRandom.Range(1, PENETRATION_DIE + 1);
            total += die + PENETRATION_OFFSET;
        } while (die == PENETRATION_DIE);
        return total;
    }

    /// <summary>
    /// 貫通回数。3回1セットで振り、1回でも抜ければ1貫通。
    /// セット内の3回すべてが抜けた場合だけPVを2下げて次のセットへ進む。
    /// PVが必ず減っていくので、どれだけ格上でも貫通回数は有限で止まる。
    /// </summary>
    public static int RollPenetrations(int pv, int av)
    {
        int penetrations = 0;
        int currentPv = pv;

        while (penetrations < MAX_PENETRATIONS)
        {
            int successes = 0;
            for (int i = 0; i < PENETRATION_ROLLS_PER_SET; i++)
                if (RollPenetrationDie() + currentPv > av) successes++;

            if (successes == 0) break;
            penetrations++;
            if (successes < PENETRATION_ROLLS_PER_SET) break;
            currentPv -= PENETRATION_PV_DECAY;
        }
        return penetrations;
    }

    /// <summary>攻撃1回の結果。avは装甲貫通を差し引いた後の実効値。</summary>
    public readonly record struct AttackResult(
        int pv, int av, int penetrations, int damage, bool autoPenetrated = false);

    /// <summary>装甲判定を無条件で1回通したか。貫通が0だったときにだけ振る。</summary>
    public static bool RollAutoPenetrate(int chance)
        => chance > 0 && GameRandom.Range(0, 100) < Math.Min(chance, 100);

    /// <summary>盾で受けきってダメージを丸ごと消したか。受けに成功した一撃にだけ振る。</summary>
    public static bool RollBlockNegate(int chance)
        => chance > 0 && GameRandom.Range(0, 100) < Math.Min(chance, 100);

    /// <summary>
    /// 命中後の解決。貫通回数を出し、その回数だけダメージダイスを振って合計する。
    /// 決定的命中はPVに+1され、かつ1回も抜けなかった場合でも最低1貫通は保証される。
    /// 貫通が0なら装甲に阻まれてダメージは通らない。最低保証ダメージはない。
    ///
    /// armorPierce（槍の貫通力）は相手のAVをその値だけ無視する。PVを上げるのとは違い、
    /// 硬い相手ほど効き、素肌の相手にはまったく効かない。
    ///
    /// critPv は会心の「効き」そのものを重くする上乗せぶん。
    /// autoPenetrate は貫通が1回も出なかったときにだけ振る救済で、
    /// 成功すれば装甲に関わらず1貫通を拾う（格上相手に手も足も出ない事故を減らす）。
    /// </summary>
    public static AttackResult ResolveAttack(
        int pv, int av, string? diceNotation, bool critical,
        int armorPierce = 0, int critPv = 0, int autoPenetrate = 0)
    {
        if (critical) pv += CRITICAL_PV_BONUS + Math.Max(0, critPv);
        av = Math.Max(0, av - Math.Max(0, armorPierce));

        int penetrations = RollPenetrations(pv, av);
        if (critical && penetrations == 0) penetrations = 1;

        bool autoPenetrated = false;
        if (penetrations == 0 && RollAutoPenetrate(autoPenetrate))
        {
            penetrations = 1;
            autoPenetrated = true;
        }

        int damage = 0;
        if (penetrations > 0)
        {
            var dice = Dice.Parse(string.IsNullOrWhiteSpace(diceNotation) ? DEFAULT_DAMAGE_DICE : diceNotation);
            for (int i = 0; i < penetrations; i++) damage += dice.Roll();
            damage = Math.Max(0, damage);
        }
        return new AttackResult(pv, av, penetrations, damage, autoPenetrated);
    }

    /// <summary>
    /// 実効PV。武器の基礎PVに能力値modifierを足すが、乗せられる量は武器ごとの上限で頭打ちになる。
    /// 短剣に膂力を乗せきれず、斧なら青天井、という差はここで出る。
    /// </summary>
    public static int EffectivePv(int weaponBasePv, int statModifier, int maxStatBonus, int flatBonus)
        => weaponBasePv + Math.Min(statModifier, maxStatBonus) + flatBonus;

    /// <summary>
    /// 同じ手番のうちswingIndex回目（0が本命、1以降が追撃）の実効PV。
    /// 追撃ほど軽くなるので、連撃の手数は火力の掛け算にはならない。
    /// </summary>
    public static int FollowUpPv(int pv, int swingIndex)
        => pv - FOLLOW_UP_PV_PENALTY * Math.Max(0, swingIndex);
}
