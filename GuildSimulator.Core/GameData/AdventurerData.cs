using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.GameData;

public class AdventurerData : IUnitMember
{
    public string id = Guid.NewGuid().ToString("N");
    public AdventurerMasterData master;
    public string name;
    public int level;
    public int experience;
    public bool isAlive = true;
    public int rank;
    public int rankPoint;
    public int expeditionCount;
    public int successfulExpeditionCount;
    public int retreatCount;
    public List<string> adventureHistory = new();

    public RaceMasterData? race;
    public ClassMasterData? currentClass;

    readonly Dictionary<EquipSlot, EquipmentMasterData> equippedSlots = new();

    public int vitality, mental, strength, agility, intelligence, constitution, appearance;

    bool IUnitMember.IsAlive { get => isAlive; set => isAlive = value; }
    public int Level => level;
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
    public string DamageDice => Weapon?.damageDice ?? UNARMED_DAMAGE_DICE;
    public bool IsMagicAttack => Weapon != null && Weapon.IsMagicWeapon;

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

    // class clear counts
    readonly Dictionary<string, int> classClearCounts = new();

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
        activeSkillCache.Clear();
        foreach (var ls in learnedSkills)
        {
            if (!activeSkillCache.Contains(ls.skill))
                activeSkillCache.Add(ls.skill);
        }
        activeSkillDirty = false;
        return activeSkillCache;
    }

    bool HasLearnedAny(SkillMasterData skill)
        => learnedSkills.Any(x => x.skill == skill);

    void LearnSkill(SkillMasterData skill, ClassMasterData? ownerClass)
    {
        if (HasLearnedAny(skill)) return;
        learnedSkills.Add(new LearnedSkill { skill = skill, ownerClass = ownerClass });
        MarkDirty();
    }

    public bool LearnPermanentSkill(SkillMasterData skill)
    {
        if (HasLearnedAny(skill)) return false;
        LearnSkill(skill, null);
        return true;
    }

    void GrantEntryClassSkills()
    {
        if (currentClass == null) return;
        foreach (var e in currentClass.classSkills)
            if (e.requiredClearCount <= 0 && e.Skill != null)
                LearnSkill(e.Skill, currentClass);
    }

    void CheckClassSkillUnlock()
    {
        if (currentClass == null) return;
        int clears = GetClassClearCount(currentClass.id);
        foreach (var e in currentClass.classSkills)
            if (e.Skill != null && clears >= e.requiredClearCount)
                LearnSkill(e.Skill, currentClass);
    }

    int GetClassClearCount(string classId)
        => classClearCounts.TryGetValue(classId, out var v) ? v : 0;

    public int CurrentClassClearCount => currentClass != null ? GetClassClearCount(currentClass.id) : 0;

    public void ChangeClass(ClassMasterData next)
    {
        if (currentClass == next) return;
        currentClass = next;
        GrantEntryClassSkills();
        CheckClassSkillUnlock();
        MarkDirty();
    }

    /// <summary>
    /// クラス習熟度は「適正ランクのクエストを正規クリアした回数」で増える。
    /// 格下では学ぶものがなく、格上すぎるクエストは連れ回されているだけなので、どちらも数えない。
    /// </summary>
    public void OnClearQuest(int questRank)
    {
        if (!isAlive || !Rank.IsSuitable(questRank, rank)) return;
        if (currentClass == null) return;
        classClearCounts.TryGetValue(currentClass.id, out var c);
        classClearCounts[currentClass.id] = c + 1;
        CheckClassSkillUnlock();
    }

    /// <summary>今の自分にとって適正ランクのクエストか。冒険者詳細やクエストボードの目印に使う。</summary>
    public bool IsSuitableQuestRank(int questRank) => Rank.IsSuitable(questRank, rank);

    /// <summary>適正帯の表記（例: "D〜B"）。</summary>
    public string SuitableRankRangeLabel => Rank.SuitableRangeLabel(rank);

    // ---- Adventurer rank ----
    public string RankLabel => Rank.Label(rank);

    public bool IsMaxRank => Rank.IsMax(rank);

    public int RequiredRankPointForNextRank => 10 * Math.Max(1, rank);

    public void AddRankPoints(int amount, out int rankUps)
    {
        rankUps = 0;
        if (!isAlive || amount <= 0) return;
        // Sに達したらランクポイントも溜めない。溜めても行き場がないうえ、
        // 上限に張り付いた冒険者のRP表示が伸び続けると昇格できるように見えてしまう。
        if (IsMaxRank) return;
        rankPoint += amount;
        while (rankPoint >= RequiredRankPointForNextRank)
        {
            rankPoint -= RequiredRankPointForNextRank;
            rank = Rank.Clamp(rank + 1);
            rankUps++;
            if (IsMaxRank) { rankPoint = 0; break; }
        }
    }

    public int CalcRankPointGain(int questRank) => questRank < rank ? 0 : Math.Max(1, questRank);

    // ---- Exp / Level ----
    public int RequiredExpForNextLevel => 100 + (level - 1) * 50;

    public bool AddExperience(int amount, out int levelUps)
    {
        levelUps = 0;
        if (!isAlive || amount <= 0) return false;
        experience += amount;
        while (experience >= RequiredExpForNextLevel)
        {
            experience -= RequiredExpForNextLevel;
            LevelUp();
            levelUps++;
        }
        return true;
    }

    /// <summary>
    /// レベルアップで伸びる能力の候補。体格(CON)は含まない。
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

    void LevelUp()
    {
        level++;
        for (int i = 0; i < StatPointsPerLevel; i++) GrowOneStat();

        // 施設の成長支援は「たまにもう1点」という形で効かせる。
        // 全能力に薄く配ると、1レベル1点という枠組みそのものが崩れてしまう。
        float bonus = FacilitySystem.GetGrowthRateBonus();
        if (bonus > 0f && GameRandom.NextFloat() < bonus) GrowOneStat();
    }

    /// <summary>
    /// 種族とクラスの重みで1能力だけ選んで+1する。
    /// どこが伸びるかはプレイヤーには選べない。特定の能力だけを狙って伸ばせてしまうと、
    /// 過積載や命中のような釣り合いを一方向に崩せてしまうため。
    /// </summary>
    void GrowOneStat()
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

        switch (GrowableStats[picked])
        {
            case StatType.Vitality: vitality++; break;
            case StatType.Mental: mental++; break;
            case StatType.Strength: strength++; break;
            case StatType.Agility: agility++; break;
            case StatType.Intelligence: intelligence++; break;
        }
    }

    /// <summary>全能力に共通の下駄。得意でない能力もときどき伸びるようにするための底上げ。</summary>
    public const float BaseGrowthWeight = 0.2f;

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
    int TotalWeight => equippedSlots.Values.Sum(e => e.weight);
    int CarryLimit => constitution + (strength + vitality) / 2;
    float OverweightRate
    {
        get
        {
            int over = TotalWeight - CarryLimit;
            if (over <= 0) return 0f;
            return Math.Clamp(over * OVERWEIGHT_RATE_PER_POINT, 0f, 1f);
        }
    }

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
        float r = OverweightRate;
        if (r > 0f)
        {
            s.dv -= (int)Math.Ceiling(OVERWEIGHT_DV_PENALTY * r);
            s.toHit -= (int)Math.Ceiling(OVERWEIGHT_TO_HIT_PENALTY * r);
        }
        return s;
    }

    // 過積載率1.0（積載上限を10超過）で受ける最大ペナルティ。ヘルプ画面がそのまま説明に使う。
    public const float OVERWEIGHT_DV_PENALTY = 6f;
    public const float OVERWEIGHT_TO_HIT_PENALTY = 4f;

    /// <summary>積載上限を1超えるごとに増える過積載率。</summary>
    public const float OVERWEIGHT_RATE_PER_POINT = 0.1f;

    // ---- セーブ/ロード ----
    public IReadOnlyList<(SkillMasterData skill, ClassMasterData? ownerClass)> ExportLearnedSkills()
        => learnedSkills.Select(x => (x.skill, x.ownerClass)).ToList();

    public IReadOnlyDictionary<string, int> ExportClassClearCounts() => classClearCounts;

    /// <summary>セーブデータからの復元専用。コンストラクタが自動付与したスキル・熟練度を、保存済みの内容で置き換える。</summary>
    public void RestoreProgress(
        IEnumerable<(SkillMasterData skill, ClassMasterData? ownerClass)> skills,
        IReadOnlyDictionary<string, int> clearCounts)
    {
        learnedSkills.Clear();
        foreach (var (skill, ownerClass) in skills)
            learnedSkills.Add(new LearnedSkill { skill = skill, ownerClass = ownerClass });

        classClearCounts.Clear();
        foreach (var (classId, count) in clearCounts)
            classClearCounts[classId] = count;

        MarkDirty();
    }

    public void RecordExpedition(string questName, string result)
    {
        expeditionCount++;
        if (result == "成功") successfulExpeditionCount++;
        if (result == "撤退") retreatCount++;
        adventureHistory.Add($"{questName}: {result}");
        const int MaxHistoryEntries = 20;
        if (adventureHistory.Count > MaxHistoryEntries)
            adventureHistory.RemoveRange(0, adventureHistory.Count - MaxHistoryEntries);
    }

    public string ClassAndRace =>
        $"{currentClass?.className ?? "？"} / {race?.raceName ?? "？"}";
}
