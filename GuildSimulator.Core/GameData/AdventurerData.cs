using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.GameData;

public readonly record struct ClassMasteryProgress(
    int PointsGained,
    int TotalPoints,
    IReadOnlyList<SkillMasterData> UnlockedSkills)
{
    public static readonly ClassMasteryProgress None =
        new(0, 0, Array.Empty<SkillMasterData>());
}

public class AdventurerData : IUnitMember
{
    public string id = Guid.NewGuid().ToString("N");
    public AdventurerMasterData master;
    public string name;
    public int level;
    public int experience;
    public bool isAlive = true;
    public int rank;

    /// <summary>今のランクになってから、格上のクエストを正規クリアした回数。昇格すると0に戻る。</summary>
    public int higherRankClears;

    /// <summary>これまでに正規クリアした適正ランク帯のクエスト総数。昇格しても減らない。</summary>
    public int suitableRankClearsTotal;
    public int expeditionCount;
    public int successfulExpeditionCount;
    public int retreatCount;
    public List<string> adventureHistory = new();

    /// <summary>戦闘中に倒れて行動不能だが、死亡判定はまだ行われていない状態。</summary>
    public bool isIncapacitated;

    /// <summary>帰還時に負傷・死亡を決めるための暫定重症度（1〜3）。</summary>
    public int pendingInjurySeverity;

    public List<AdventurerInjury> injuries = new();
    public List<AdventurerScar> scars = new();

    /// <summary>
    /// 生涯にわたる遠征記録。特性の解禁条件はここを見る。
    /// レベルアップの成長を絞ってあるぶん、キャラクターの個性はこちらの蓄積から生まれる。
    /// </summary>
    public ExpeditionRecord records = new();

    /// <summary>
    /// 一度でも選択肢として提示した特性のID。習得したものも、見送ったものも入る。
    /// <b>提示は1つの特性につき生涯1度きり</b>で、選ばなかった特性は二度と現れない。
    /// 毎クエスト同じ問いを繰り返さないためと、その場の選択に重みを持たせるための両方。
    /// </summary>
    public List<string> offeredTraitIds = new();

    public RaceMasterData? race;
    public ClassMasterData? currentClass;

    readonly Dictionary<EquipSlot, EquipmentMasterData> equippedSlots = new();

    public int vitality, mental, strength, agility, intelligence, constitution, appearance;

    bool IUnitMember.IsAlive
    {
        get => isAlive && !isIncapacitated;
        set
        {
            if (value)
                isIncapacitated = false;
            else if (isAlive)
                RegisterKnockout(1);
        }
    }

    /// <summary>脅威度は認定ランクそのもの。敵の threat と同じ物差しで比べる。</summary>
    public int Threat => rank;

    public string Name => name;
    public int CombatHp { get; set; }
    public int CombatHpMax { get; set; }
    public IReadOnlyList<SkillMasterData> Skills => GetActiveSkills();

    public EquipmentMasterData? Weapon => GetEquipped(EquipSlot.RightHand);
    public EquipmentMasterData? Armor => GetEquipped(EquipSlot.Body);

    /// <summary>左手に構えた盾。両手武器を持っていれば常にnull。</summary>
    public EquipmentMasterData? Shield =>
        GetEquipped(EquipSlot.LeftHand) is { } left && left.IsShield ? left : null;

    /// <summary>左手に握った武器（二刀流の対象）。盾でも両手武器でもないときだけ返す。</summary>
    public EquipmentMasterData? OffHandWeapon =>
        GetEquipped(EquipSlot.LeftHand) is { type: EquipmentType.Weapon } left ? left : null;
    public string DamageDice => Weapon?.damageDice ?? BestUnarmedDamageDice();
    public bool IsMagicAttack => Weapon != null && Weapon.IsMagicWeapon;

    /// <summary>
    /// 素手で殴るときのダメージダイス。格闘スキルを持っていれば拳そのものが強くなる。
    /// 段階スキルを重ねて持っていても足し算にはならず、いちばん強い一本だけが採用される。
    /// </summary>
    string BestUnarmedDamageDice()
    {
        string best = UNARMED_DAMAGE_DICE;
        int bestMax = Dice.Parse(best).Max;
        foreach (var sk in Skills)
        {
            if (string.IsNullOrWhiteSpace(sk.unarmedDamageDice)) continue;
            if (!UnitCalculator.MeetsGearRequirements(sk, this)) continue;
            int max = Dice.Parse(sk.unarmedDamageDice).Max;
            if (max > bestMax) { best = sk.unarmedDamageDice; bestMax = max; }
        }
        return best;
    }

    // 素手は武器そのもののPVが小さく、殴る力も1d2しかない。ただし拳に上限はないので膂力はそのまま乗る。
    public int WeaponBasePv => Weapon?.basePv ?? UNARMED_PV;
    public int MaxStatBonus => Weapon?.maxStatBonus ?? UNARMED_MAX_STAT_BONUS;
    public int AttackStatModifier => QudCombat.Modifier(IsMagicAttack ? intelligence : strength);

    // 素手には武器クラスの個性がない。連撃も装甲破壊も、得物を持って初めて手に入る。
    public WeaponTraits Traits => Weapon?.Traits ?? WeaponTraits.None;

    public const int UNARMED_PV = 2;

    /// <summary>素手のダメージダイス。</summary>
    public const string UNARMED_DAMAGE_DICE = QudCombat.DEFAULT_DAMAGE_DICE;

    /// <summary>素手はPVに乗せる能力値modifierを制限しない。伸びないのは基礎PVとダメージダイスの側。</summary>
    public const int UNARMED_MAX_STAT_BONUS = QudCombatDefaults.UnlimitedStatBonus;

    public EquipmentMasterData? GetEquipped(EquipSlot slot) =>
        equippedSlots.TryGetValue(slot, out var item) ? item : null;

    public void SetEquipped(EquipSlot slot, EquipmentMasterData? item)
    {
        if (item == null)
            equippedSlots.Remove(slot);
        else
            equippedSlots[slot] = item;
    }

    public IReadOnlyDictionary<EquipSlot, EquipmentMasterData> GetAllEquipped() => equippedSlots;

    public IEnumerable<EquipmentMasterData> AllEquippedItems() => equippedSlots.Values;

    readonly List<LearnedSkill> learnedSkills = new();
    readonly List<SkillMasterData> activeSkillCache = new();
    bool activeSkillDirty = true;

    public class LearnedSkill
    {
        public SkillMasterData skill = null!;
        public ClassMasterData? ownerClass;
    }

    // INTで加速する職業別の習熟度。1適正クリア=100+INTを加算していく素点。
    readonly Dictionary<string, int> classMasteryPoints = new();

    /// <summary>適正クエスト1回の基礎習熟度。</summary>
    public const int BaseMasteryPerClear = 100;

    /// <summary>INTによる習熟度加算の上限。低INTへの減点は行わない。</summary>
    public const int MaxIntMasteryBonus = 30;

    public AdventurerData(AdventurerMasterData master)
    {
        this.master = master;
        name = master.baseName;
        level = master.defaultLevel;
        rank = Rank.Clamp(master.defaultRank);
        race = master.Race;
        currentClass = master.DefaultClass;
        vitality = master.vitality;
        mental = master.mental;
        strength = master.strength;
        agility = master.agility;
        intelligence = master.intelligence;
        constitution = master.constitution;
        appearance = master.appearance;
        if (master.DefaultWeapon != null)
            SetEquipped(EquipSlot.RightHand, master.DefaultWeapon);
        if (master.DefaultArmor != null)
            SetEquipped(EquipSlot.Body, master.DefaultArmor);

        foreach (var s in master.Skills)
            LearnSkill(s, null);

        GrantEntryClassSkills();
        MarkDirty();
    }

    void MarkDirty() => activeSkillDirty = true;

    IReadOnlyList<SkillMasterData> GetActiveSkills()
    {
        if (!activeSkillDirty) return activeSkillCache;

        // 覚えたものはすべて残るが、同系統の段階スキルは最上位だけが効く。
        SkillProgression.CollapseInto(learnedSkills.Select(ls => ls.skill), activeSkillCache);
        activeSkillDirty = false;
        return activeSkillCache;
    }

    /// <summary>覚えた順の全スキル（下位の段階も含む）。習得履歴の表示に使う。</summary>
    public IEnumerable<SkillMasterData> AllLearnedSkills => learnedSkills.Select(ls => ls.skill);

    bool HasLearnedAny(SkillMasterData skill)
        => learnedSkills.Any(x => x.skill == skill);

    bool LearnSkill(SkillMasterData skill, ClassMasterData? ownerClass)
    {
        if (HasLearnedAny(skill)) return false;
        learnedSkills.Add(new LearnedSkill { skill = skill, ownerClass = ownerClass });
        MarkDirty();
        return true;
    }

    public bool LearnPermanentSkill(SkillMasterData skill)
    {
        return LearnSkill(skill, null);
    }

    IReadOnlyList<SkillMasterData> GrantEntryClassSkills()
    {
        if (currentClass == null) return Array.Empty<SkillMasterData>();
        var unlocked = new List<SkillMasterData>();
        foreach (var e in currentClass.classSkills)
            if (e.requiredClearCount <= 0 && e.Skill != null)
                if (LearnSkill(e.Skill, currentClass)) unlocked.Add(e.Skill);
        return unlocked;
    }

    IReadOnlyList<SkillMasterData> CheckClassSkillUnlock()
    {
        if (currentClass == null) return Array.Empty<SkillMasterData>();
        int mastery = GetClassMastery(currentClass.id);
        var unlocked = new List<SkillMasterData>();
        foreach (var e in currentClass.classSkills)
            if (e.Skill != null && mastery >= Math.Max(0, e.requiredClearCount))
                if (LearnSkill(e.Skill, currentClass)) unlocked.Add(e.Skill);
        return unlocked;
    }

    int GetClassMastery(string classId)
        => classMasteryPoints.TryGetValue(classId, out var v) ? v : 0;

    /// <summary>今就いている職業の習熟度。1適正クリア=100+INTで積み上がる。</summary>
    public int CurrentClassMastery =>
        currentClass != null ? GetClassMastery(currentClass.id) : 0;

    public int MasteryPerSuitableClear =>
        BaseMasteryPerClear + Math.Clamp(intelligence, 0, MaxIntMasteryBonus);

    public IReadOnlyList<SkillMasterData> ChangeClass(ClassMasterData next)
    {
        if (currentClass == next) return Array.Empty<SkillMasterData>();
        currentClass = next;
        var unlocked = GrantEntryClassSkills().ToList();
        unlocked.AddRange(CheckClassSkillUnlock());
        MarkDirty();
        return unlocked;
    }

    /// <summary>
    /// クラス習熟度は、適正ランクのクエストを正規クリアするとINTに応じたポイントで増える。
    /// 格下では学ぶものがなく、格上すぎるクエストは連れ回されているだけなので、どちらも数えない。
    /// </summary>
    public ClassMasteryProgress OnClearQuest(int questRank)
    {
        if (!isAlive || !Rank.IsSuitable(questRank, rank)) return ClassMasteryProgress.None;
        if (currentClass == null) return ClassMasteryProgress.None;

        int gained = MasteryPerSuitableClear;
        classMasteryPoints.TryGetValue(currentClass.id, out int current);
        int total = current + gained;
        classMasteryPoints[currentClass.id] = total;
        return new ClassMasteryProgress(gained, total, CheckClassSkillUnlock());
    }

    /// <summary>今の自分にとって適正ランクのクエストか。冒険者詳細やクエストボードの目印に使う。</summary>
    public bool IsSuitableQuestRank(int questRank) => Rank.IsSuitable(questRank, rank);

    /// <summary>適正帯の表記（例: "D〜B"）。</summary>
    public string SuitableRankRangeLabel => Rank.SuitableRangeLabel(rank);

    // ---- Adventurer rank ----
    public string RankLabel => Rank.Label(rank);

    public bool IsMaxRank => Rank.IsMax(rank);

    /// <summary>
    /// ランク別の昇格条件。「格上クエストの正規クリア数」と「累積の適正クエスト正規クリア数」の
    /// 両方を満たすと1つ上がる。低ランクは軽く、Aあたりで急に重くなる曲線に載せてある。
    /// 添字は現在ランク(F=1..A=6)。Sは打ち止めなので入っていない。
    /// </summary>
    static readonly RankPromotionRequirement[] PromotionTable =
    {
        new(higherRankClears: 1, suitableTotalClears: 3),   // F → E
        new(higherRankClears: 2, suitableTotalClears: 10),  // E → D
        new(higherRankClears: 3, suitableTotalClears: 30),  // D → C
        new(higherRankClears: 3, suitableTotalClears: 50),  // C → B
        new(higherRankClears: 4, suitableTotalClears: 75),  // B → A
        new(higherRankClears: 4, suitableTotalClears: 100), // A → S
    };

    public readonly record struct RankPromotionRequirement(int higherRankClears, int suitableTotalClears);

    /// <summary>今のランクから次に上がるための条件。Sだと null。</summary>
    public RankPromotionRequirement? NextRankRequirement =>
        IsMaxRank ? null : PromotionTable[Math.Clamp(rank, Rank.Min, Rank.Max - 1) - Rank.Min];

    /// <summary>UIの下位互換用: 昇格に必要な格上クリア数。</summary>
    public int RequiredClearsForNextRank => NextRankRequirement?.higherRankClears ?? 0;

    /// <summary>
    /// クエストの正規クリアを昇格用のカウンタに積む。昇格そのものは行わない
    /// （プレイヤーが明示的に <see cref="TryRankUp"/> を呼ぶまで待つ）。
    ///   ・適正ランク帯のクリアは「累積の適正クリア数」に載る。昇格しても減らない。
    ///   ・格上（自分より上のランク）のクリアは「格上クリア」に載る。こちらは昇格で0に戻る。
    /// 同ランク以下は格上にも載らない。
    /// </summary>
    public void RecordQuestClearForRank(int questRank)
    {
        // Sに達したら数えない。溜まり続けると昇格できるように見えてしまう。
        if (!isAlive || IsMaxRank) return;

        if (Rank.IsSuitable(questRank, rank)) suitableRankClearsTotal++;
        if (IsHigherRankQuest(questRank)) higherRankClears++;
    }

    /// <summary>昇格に数えられるクエストか。クエストボードの目印にも使う。</summary>
    public bool IsHigherRankQuest(int questRank) => questRank > rank;

    /// <summary>今すぐ昇格できる条件を満たしているか。UIの解禁判定に使う。</summary>
    public bool CanRankUp
    {
        get
        {
            if (!isAlive || IsMaxRank) return false;
            var req = PromotionTable[rank - Rank.Min];
            return higherRankClears >= req.higherRankClears
                && suitableRankClearsTotal >= req.suitableTotalClears;
        }
    }

    /// <summary>ランクアップで一律に配る報酬。全能力+1と、その職業への習熟度加算。</summary>
    public const int RankUpStatGain = 1;
    public const int RankUpMasteryGain = 500;

    /// <summary>
    /// 昇格処理そのもの。<see cref="CanRankUp"/> が真のときだけ実行できる。
    /// プレイヤー選択で呼ばれる想定で、能力+1・習熟度+500・スキル解禁までを一度に返す。
    /// </summary>
    public bool TryRankUp(out RankUpResult result)
    {
        result = default;
        if (!CanRankUp) return false;

        int previousRank = rank;
        higherRankClears = 0;
        rank = Rank.Clamp(rank + 1);

        var grownStats = ApplyRankUpStatGains();
        int masteryGained = 0;
        IReadOnlyList<SkillMasterData> unlocked = Array.Empty<SkillMasterData>();
        string? className = currentClass?.className;
        if (currentClass != null)
        {
            masteryGained = RankUpMasteryGain;
            classMasteryPoints.TryGetValue(currentClass.id, out int current);
            classMasteryPoints[currentClass.id] = current + masteryGained;
            unlocked = CheckClassSkillUnlock();
        }

        result = new RankUpResult(
            PreviousRank: previousRank,
            NewRank: rank,
            GrownStats: grownStats,
            StatGainPerStat: RankUpStatGain,
            ClassName: className,
            MasteryGained: masteryGained,
            UnlockedSkills: unlocked);

        AddHistory(result.HistoryLine());
        return true;
    }

    IReadOnlyList<StatType> ApplyRankUpStatGains()
    {
        // 全能力値+1。SIZ(体格)とAPP(容姿)を含めて一律。
        vitality += RankUpStatGain;
        mental += RankUpStatGain;
        strength += RankUpStatGain;
        agility += RankUpStatGain;
        intelligence += RankUpStatGain;
        constitution += RankUpStatGain;
        appearance += RankUpStatGain;
        return new[]
        {
            StatType.Vitality, StatType.Mental, StatType.Strength,
            StatType.Agility, StatType.Intelligence,
            StatType.Constitution, StatType.Appearance,
        };
    }

    /// <summary>
    /// 昇格1回分の結果。プレイヤーへの通知とログの両方で使うので、
    /// 差分そのものを持っておく（現在値からは差分が読み取れない）。
    /// </summary>
    public readonly record struct RankUpResult(
        int PreviousRank,
        int NewRank,
        IReadOnlyList<StatType> GrownStats,
        int StatGainPerStat,
        string? ClassName,
        int MasteryGained,
        IReadOnlyList<SkillMasterData> UnlockedSkills)
    {
        public string HistoryLine()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{Rank.Label(PreviousRank)}→{Rank.Label(NewRank)} に昇格");
            sb.Append($"、全能力値+{StatGainPerStat}");
            if (MasteryGained > 0 && ClassName != null)
                sb.Append($"、{ClassName}習熟度+{MasteryGained}");
            if (UnlockedSkills.Count > 0)
                sb.Append("、スキル「")
                    .Append(string.Join("」「", UnlockedSkills.Select(s => s.skillName)))
                    .Append("」を習得");
            return sb.ToString();
        }
    }

    // ---- Exp / Level ----
    public int RequiredExpForNextLevel => 10 + (level - 1) * 5;

    public bool AddExperience(int amount, out int levelUps) => AddExperience(amount, out levelUps, out _);

    public bool AddExperience(int amount, out int levelUps, out List<StatType> grownStats)
    {
        levelUps = 0;
        grownStats = new List<StatType>();
        if (!isAlive || amount <= 0) return false;
        int levelBefore = level;
        experience += amount;
        while (experience >= RequiredExpForNextLevel)
        {
            experience -= RequiredExpForNextLevel;
            grownStats.AddRange(LevelUp());
            levelUps++;
        }
        // 戦闘経験値は詳細ログに畳まれることがあるため、成長結果を本人の履歴にも必ず残す。
        if (levelUps > 0)
            AddHistory($"Lv{levelBefore}→{level}、{FormatGrownStats(grownStats)}");
        return true;
    }

    /// <summary>
    /// レベルアップで伸びる能力の候補。体格(SIZ)は含まない。
    /// 素の装甲値(AV)と積載上限は雇用時の素質で決まる、というのがこのゲームの取り決め。
    /// </summary>
    public static readonly IReadOnlyList<StatType> GrowableStats = new[]
    {
        StatType.Vitality, StatType.Mental, StatType.Strength,
        StatType.Agility, StatType.Intelligence,
    };

    /// <summary>
    /// 1レベルにつき伸びる能力の数。
    /// レベルを重ねるだけでキャラクターが置き換え可能になるのを避けるため、成長は1点に絞ってある。
    /// 育ちの差は「どの能力に振られたか」の運と、クエスト中の出来事が作る。
    /// </summary>
    public const int StatPointsPerLevel = 1;

    public static string StatDisplayName(StatType stat) => stat switch
    {
        StatType.Vitality => "体力",
        StatType.Mental => "精神力",
        StatType.Strength => "筋力",
        StatType.Agility => "敏捷",
        StatType.Intelligence => "知力",
        StatType.Constitution => "体格",
        StatType.Appearance => "容姿",
        _ => stat.ToString(),
    };

    /// <summary>伸びた能力を「体力+1、敏捷+2」の形にまとめる。</summary>
    public static string FormatGrownStats(IEnumerable<StatType> grownStats) =>
        string.Join("、", grownStats
            .GroupBy(stat => stat)
            .Select(group => $"{StatDisplayName(group.Key)}+{group.Count()}"));

    List<StatType> LevelUp()
    {
        level++;
        var grown = new List<StatType>();
        for (int i = 0; i < StatPointsPerLevel; i++) grown.Add(GrowOneStat());

        // 施設の成長支援は「たまにもう1点」という形で効かせる。
        // 全能力に薄く配ると、1レベル1点という枠組みそのものが崩れてしまう。
        float bonus = FacilitySystem.GetGrowthRateBonus();
        if (bonus > 0f && GameRandom.NextFloat() < bonus) grown.Add(GrowOneStat());

        return grown;
    }

    /// <summary>
    /// 種族とクラスの重みで1能力だけ選んで+1する。
    /// どこが伸びるかはプレイヤーには選べない。特定の能力だけを狙って伸ばせてしまうと、
    /// 過積載や命中のような釣り合いを一方向に崩せてしまうため。
    /// </summary>
    StatType GrowOneStat()
    {
        Span<float> weights = stackalloc float[GrowableStats.Count];
        float total = 0f;
        for (int i = 0; i < GrowableStats.Count; i++)
        {
            var t = GrowableStats[i];
            // 下駄を履かせたうえで種族・クラスの得手不得手を足す。不得手でも0未満にはしない。
            weights[i] = Math.Max(0f, BaseGrowthWeight + GetRaceGrowth(t) + GetClassGrowth(t));
            total += weights[i];
        }

        int picked = GrowableStats.Count - 1;
        if (total <= 0f)
        {
            picked = GameRandom.Range(0, GrowableStats.Count);
        }
        else
        {
            float roll = GameRandom.NextFloat() * total;
            for (int i = 0; i < GrowableStats.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0f) { picked = i; break; }
            }
        }

        var chosen = GrowableStats[picked];
        switch (chosen)
        {
            case StatType.Vitality: vitality++; break;
            case StatType.Mental: mental++; break;
            case StatType.Strength: strength++; break;
            case StatType.Agility: agility++; break;
            case StatType.Intelligence: intelligence++; break;
        }
        return chosen;
    }

    /// <summary>全能力に共通の下駄。得意でない能力もときどき伸びるようにするための底上げ。</summary>
    public const float BaseGrowthWeight = 0.2f;

    /// <summary>能力値の下限。0以下になると modifier が壊れるので、削られても1では止める。</summary>
    public const int MinStatValue = 1;

    /// <summary>
    /// 能力を恒久的に増減させる。クエスト中の出来事（祭壇・呪い・鍛錬など）から呼ぶ。
    /// レベルアップの成長を絞ったぶん、ここで生まれる差がキャラクターの個性になる。
    /// 実際に動いた量を返す（下限で止まったときは0や部分適用になる）。
    /// </summary>
    public int AdjustStatPermanently(StatType type, int amount)
    {
        if (amount == 0) return 0;
        ref int stat = ref vitality;
        switch (type)
        {
            case StatType.Vitality: stat = ref vitality; break;
            case StatType.Mental: stat = ref mental; break;
            case StatType.Strength: stat = ref strength; break;
            case StatType.Agility: stat = ref agility; break;
            case StatType.Intelligence: stat = ref intelligence; break;
            case StatType.Constitution: stat = ref constitution; break;
            default: return 0;
        }

        int before = stat;
        stat = Math.Max(MinStatValue, stat + amount);
        return stat - before;
    }

    float GetRaceGrowth(StatType t) => race == null ? 0f : t switch
    {
        StatType.Vitality => race.vitGrowth,
        StatType.Mental => race.mentGrowth,
        StatType.Strength => race.strGrowth,
        StatType.Agility => race.agiGrowth,
        StatType.Intelligence => race.intGrowth,
        _ => 0f,
    };

    float GetClassGrowth(StatType t) => currentClass == null ? 0f : t switch
    {
        StatType.Vitality => currentClass.vitGrowth,
        StatType.Mental => currentClass.mentGrowth,
        StatType.Strength => currentClass.strGrowth,
        StatType.Agility => currentClass.agiGrowth,
        StatType.Intelligence => currentClass.intGrowth,
        _ => 0f,
    };

    // ---- Combat ----
    /// <summary>現在装備している全スロットの合計重量。</summary>
    public int TotalEquipmentWeight => equippedSlots.Values.Sum(e => e.weight);

    /// <summary>
    /// 担げる重さ。素の上限に、スキルで鍛えた「担ぐ余裕」を足す。
    /// 立ち位置に左右されない構えの条件だけで判定するので、隊列を組む前でも同じ値になる。
    /// </summary>
    public int CarryLimit => constitution + (strength + vitality) / 2
        + SkillCarryBonus + EquipmentCarryBonus;

    /// <summary>現在の装備から得ている積載上限ボーナス。</summary>
    public int EquipmentCarryBonus => GetEquipmentBonus().carry;

    /// <summary>今の装備で有効になっているスキル由来の積載ボーナス。</summary>
    public int SkillCarryBonus
    {
        get
        {
            int bonus = 0;
            foreach (var sk in Skills)
                if (sk.add.carry != 0 && UnitCalculator.MeetsGearRequirements(sk, this))
                    bonus += sk.add.carry;
            return bonus;
        }
    }
    /// <summary>積載上限を超えた重量。上限内なら0。</summary>
    public int OverweightAmount => Math.Max(0, TotalEquipmentWeight - CarryLimit);

    public float OverweightRate
    {
        get
        {
            int over = OverweightAmount;
            if (over <= 0) return 0f;
            return Math.Clamp(over * OVERWEIGHT_RATE_PER_POINT, 0f, 1f);
        }
    }

    public int OverweightDvPenalty =>
        OverweightRate > 0f ? (int)Math.Ceiling(OVERWEIGHT_DV_PENALTY * OverweightRate) : 0;

    public int OverweightToHitPenalty =>
        OverweightRate > 0f ? (int)Math.Ceiling(OVERWEIGHT_TO_HIT_PENALTY * OverweightRate) : 0;

    // 能力値は直接ダメージに乗らない。体格は素肌の硬さ(AV)、精神は魔法への抵抗(mAV)、
    // 敏捷は避けやすさ(DV)と当てやすさ(命中)に変わる。攻撃側の筋力・知力はPVへ回るので、
    // ここではなく AttackStatModifier が受け持つ。
    public StatBlock GetBaseCombatStats()
    {
        return new StatBlock
        {
            hp = (vitality * 10 + constitution * 5) / 2,
            san = mental * 10,
            av = QudCombat.Modifier(constitution),
            mav = QudCombat.Modifier(mental),
            dv = QudCombat.BASE_DV + QudCombat.Modifier(agility),
            toHit = QudCombat.Modifier(agility),
            heal = mental + intelligence / 2,
        };
    }

    /// <summary>
    /// 装備の補正合計。
    /// ただし<b>左手に握った武器の補正は乗せない</b>。乗せてしまうと短剣を2本持つだけで
    /// 命中+6になり、二刀流の発動率を待たずに得をしてしまう。左手の武器は攻撃にだけ使い、
    /// 数値の恩恵は右手の得物から受ける、という取り決めにしてある。
    /// 盾は防具なので通常どおり補正を供給する（装甲だけは受けに成功したときのみ）。
    /// 重さは装備している以上かかるので、積載の計算からは除外しない。
    /// </summary>
    public StatBlock GetEquipmentBonus()
    {
        StatBlock b = default;
        foreach (var (slot, item) in equippedSlots)
        {
            if (slot == EquipSlot.LeftHand && item.type == EquipmentType.Weapon) continue;
            b += item.bonus;
        }
        return b;
    }

    public StatBlock GetFinalCombatStats()
    {
        var weapon = Weapon;
        var s = GetBaseCombatStats() + GetEquipmentBonus();

        float hCoef = weapon?.healPower ?? 0f;
        s.heal = hCoef > 0f ? (int)Math.Floor((s.heal + (weapon?.flatHeal ?? 0)) * hCoef) : 0;

        // 過積載は身のこなしを鈍らせる。装甲(AV)は担いでいる以上そのまま効くので削らない。
        if (OverweightAmount > 0)
        {
            s.dv -= OverweightDvPenalty;
            s.toHit -= OverweightToHitPenalty;
        }
        AdventurerConditionRules.ApplyModifiers(injuries, scars, ref s);
        return s;
    }

    // 過積載率1.0（積載上限を10超過）で受ける最大ペナルティ。ヘルプ画面がそのまま説明に使う。
    public const float OVERWEIGHT_DV_PENALTY = 6f;
    public const float OVERWEIGHT_TO_HIT_PENALTY = 4f;

    /// <summary>積載上限を1超えるごとに増える過積載率。</summary>
    public const float OVERWEIGHT_RATE_PER_POINT = 0.1f;

    // ---- 負傷・傷痕 ----

    public bool IsInjured => injuries.Count > 0;
    public string? ConditionTitle => scars.LastOrDefault()?.Title;

    public string ConditionSummary
    {
        get
        {
            if (!isAlive) return "死亡";
            if (isIncapacitated) return "戦闘不能（帰還判定待ち）";
            var parts = new List<string>();
            if (injuries.Count > 0)
                parts.Add($"負傷{injuries.Count}件・休養あと最大{injuries.Max(i => i.remainingRestTurns)}T");
            if (scars.Count > 0) parts.Add($"傷痕{scars.Count}件");
            return parts.Count == 0 ? "健康" : string.Join(" / ", parts);
        }
    }

    public void RegisterKnockout(int severity)
    {
        if (!isAlive) return;
        isIncapacitated = true;
        pendingInjurySeverity = Math.Max(pendingInjurySeverity, Math.Clamp(severity, 1, 3));
    }

    public const int MinorTraumaFatalityPercent = 10;
    public const int MajorTraumaFatalityPercent = 20;
    public const int CriticalTraumaFatalityPercent = 35;
    public const int PartyWipeFatalityBonusPercent = 25;

    /// <summary>
    /// 帰還時の死亡率。戦闘不能の重症度に壊滅補正を加え、医療院の救命補正を最後に差し引く。
    /// 乱数を含めず純粋に計算することで、画面説明と実処理が同じ数値を参照できるようにする。
    /// </summary>
    public static int CalculateFatalityPercent(
        int severity,
        bool partyWiped,
        int fatalityReductionPercent)
    {
        int clampedSeverity = Math.Clamp(severity, 1, 3);
        int fatality = clampedSeverity switch
        {
            1 => MinorTraumaFatalityPercent,
            2 => MajorTraumaFatalityPercent,
            _ => CriticalTraumaFatalityPercent,
        };
        if (partyWiped) fatality += PartyWipeFatalityBonusPercent;
        return Math.Clamp(fatality - Math.Max(0, fatalityReductionPercent), 0, 95);
    }

    /// <summary>帰還時に戦闘不能の結果を確定する。医療院の生存補正は死亡率から直接差し引く。</summary>
    public TraumaResolution ResolvePendingTrauma(bool partyWiped, int fatalityReductionPercent)
    {
        if (!isAlive || (!isIncapacitated && pendingInjurySeverity <= 0))
            return new TraumaResolution(false, null, null, "負傷判定なし");

        int severity = Math.Clamp(Math.Max(1, pendingInjurySeverity), 1, 3);
        int fatality = CalculateFatalityPercent(severity, partyWiped, fatalityReductionPercent);

        isIncapacitated = false;
        pendingInjurySeverity = 0;
        if (fatality > 0 && GameRandom.Range(1, 101) <= fatality)
        {
            isAlive = false;
            CombatHp = 0;
            CombatHpMax = 0;
            string death = $"{name} は負傷が致命傷となり死亡した（死亡率{fatality}%）";
            AddHistory(death);
            return new TraumaResolution(true, null, null, death);
        }

        InjuryType type = severity switch
        {
            1 => InjuryType.CutsAndBruises,
            2 => GameRandom.Range(0, 2) == 0 ? InjuryType.Fracture : InjuryType.Trauma,
            _ => InjuryType.DeepWound,
        };
        int restTurns = type switch
        {
            InjuryType.CutsAndBruises => 1,
            InjuryType.Fracture => 3,
            InjuryType.Trauma => 3,
            _ => 4,
        };
        int scarChance = type switch
        {
            InjuryType.CutsAndBruises => 5,
            InjuryType.Fracture => 35,
            InjuryType.Trauma => 45,
            _ => 70,
        };

        var injury = injuries.FirstOrDefault(i => i.type == type);
        if (injury == null)
        {
            injury = new AdventurerInjury
            {
                type = type,
                remainingRestTurns = restTurns,
                scarChancePercent = scarChance,
            };
            injuries.Add(injury);
        }
        else
        {
            injury.remainingRestTurns = Math.Max(injury.remainingRestTurns, restTurns);
            injury.scarChancePercent = Math.Max(injury.scarChancePercent, scarChance);
        }

        CombatHp = 0;
        CombatHpMax = 0;
        string survived = $"{name} は{injury.DisplayName}を負った（休養{injury.remainingRestTurns}T、{injury.EffectDescription}）";
        AddHistory(survived);
        return new TraumaResolution(false, injury, null, survived);
    }

    public RecoveryResolution AdvanceRecovery(int recoveryPoints, int scarPreventionPercent)
    {
        var healed = new List<AdventurerInjury>();
        var newScars = new List<AdventurerScar>();
        if (!isAlive || recoveryPoints <= 0) return new RecoveryResolution(healed, newScars);

        foreach (var injury in injuries.ToList())
        {
            injury.remainingRestTurns -= recoveryPoints;
            if (injury.remainingRestTurns > 0) continue;

            injuries.Remove(injury);
            healed.Add(injury);
            int scarChance = Math.Clamp(
                injury.scarChancePercent - Math.Max(0, scarPreventionPercent), 0, 100);
            if (scarChance <= 0 || GameRandom.Range(1, 101) > scarChance) continue;

            ScarType scarType = injury.type switch
            {
                InjuryType.Fracture => ScarType.StiffJoint,
                InjuryType.Trauma => GameRandom.Range(0, 2) == 0 ? ScarType.Nightmares : ScarType.Survivor,
                InjuryType.DeepWound => GameRandom.Range(0, 2) == 0 ? ScarType.BattleScar : ScarType.Survivor,
                _ => ScarType.BattleScar,
            };
            if (scars.Any(s => s.type == scarType)) continue;
            var scar = new AdventurerScar { type = scarType };
            scars.Add(scar);
            newScars.Add(scar);
        }

        foreach (var injury in healed)
            AddHistory($"{injury.DisplayName}が回復した");
        foreach (var scar in newScars)
            AddHistory($"傷痕「{scar.DisplayName}」が残り、称号「{scar.Title}」を得た");
        return new RecoveryResolution(healed, newScars);
    }

    // ---- セーブ/ロード ----
    public IReadOnlyList<(SkillMasterData skill, ClassMasterData? ownerClass)> ExportLearnedSkills()
        => learnedSkills.Select(x => (x.skill, x.ownerClass)).ToList();

    public IReadOnlyDictionary<string, int> ExportClassMasteryPoints() => classMasteryPoints;

    /// <summary>セーブデータからの復元専用。コンストラクタが自動付与したスキル・熟練度を、保存済みの内容で置き換える。</summary>
    public void RestoreProgress(
        IEnumerable<(SkillMasterData skill, ClassMasterData? ownerClass)> skills,
        IReadOnlyDictionary<string, int> masteryPoints)
    {
        learnedSkills.Clear();
        foreach (var (skill, ownerClass) in skills)
            learnedSkills.Add(new LearnedSkill { skill = skill, ownerClass = ownerClass });

        classMasteryPoints.Clear();
        foreach (var (classId, points) in masteryPoints)
            classMasteryPoints[classId] = points;

        MarkDirty();
    }

    /// <summary>特性の開花を冒険者の記録へ残す。傷痕と同じ扱いで、その人物の来歴になる。</summary>
    public void RecordTraitAwakening(MasterData.TraitMasterData trait)
    {
        string flavor = string.IsNullOrWhiteSpace(trait.flavorText) ? "" : $" {trait.flavorText}";
        AddHistory($"特性「{trait.traitName}」が開花した。{flavor}".TrimEnd());
        MarkDirty();
    }

    public void RecordExpedition(string questName, string result)
    {
        expeditionCount++;
        if (result == "成功") successfulExpeditionCount++;
        if (result == "撤退") retreatCount++;
        AddHistory($"{questName}: {result}");
    }

    void AddHistory(string entry)
    {
        adventureHistory.Add(entry);
        const int MaxHistoryEntries = 20;
        if (adventureHistory.Count > MaxHistoryEntries)
            adventureHistory.RemoveRange(0, adventureHistory.Count - MaxHistoryEntries);
    }

    public string ClassAndRace =>
        $"{currentClass?.className ?? "？"} / {race?.raceName ?? "？"}";
}
