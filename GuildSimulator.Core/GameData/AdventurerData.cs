using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
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
    public string DamageDice => Weapon?.damageDice ?? "";
    public int MaxAtkBonus => Weapon?.maxAtkBonus ?? 0;
    public bool IsMagicAttack => Weapon != null && Weapon.magicCoeff > 0f;

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
        rank = Math.Max(1, master.defaultRank);
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

    public void OnClearQuest(int questRank)
    {
        if (!isAlive || questRank < rank) return;
        if (currentClass == null) return;
        classClearCounts.TryGetValue(currentClass.id, out var c);
        classClearCounts[currentClass.id] = c + 1;
        CheckClassSkillUnlock();
    }

    // ---- Adventurer rank ----
    public int RequiredRankPointForNextRank => 10 * Math.Max(1, rank);

    public void AddRankPoints(int amount, out int rankUps)
    {
        rankUps = 0;
        if (!isAlive || amount <= 0) return;
        rankPoint += amount;
        while (rankPoint >= RequiredRankPointForNextRank)
        {
            rankPoint -= RequiredRankPointForNextRank;
            rank++;
            rankUps++;
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

    void LevelUp()
    {
        level++;
        TryGrow(StatType.Vitality, ref vitality);
        TryGrow(StatType.Mental, ref mental);
        TryGrow(StatType.Strength, ref strength);
        TryGrow(StatType.Agility, ref agility);
        TryGrow(StatType.Intelligence, ref intelligence);
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

    void TryGrow(StatType type, ref int stat)
    {
        float chance = 0.2f + GetRaceGrowth(type) + GetClassGrowth(type) + FacilitySystem.GetGrowthRateBonus();
        if (chance <= 0f) return;
        int loops = (int)Math.Ceiling(chance);
        for (int i = 0; i < loops; i++)
        {
            if (chance >= 1.0f) { stat++; chance -= 1.0f; }
            else if (GameRandom.NextFloat() < chance) stat++;
        }
    }

    // ---- Combat ----
    int TotalWeight => equippedSlots.Values.Sum(e => e.weight);
    int CarryLimit => constitution + (strength + vitality) / 2;
    float OverweightRate
    {
        get
        {
            int over = TotalWeight - CarryLimit;
            if (over <= 0) return 0f;
            return Math.Clamp(over * 0.1f, 0f, 1f);
        }
    }

    public StatBlock GetBaseCombatStats()
    {
        return new StatBlock
        {
            hp = (vitality * 10 + constitution * 5) / 2,
            san = mental * 10,
            pAtk = (strength * 2 + constitution / 2) / 4,
            pDef = constitution / 8,
            mAtk = (intelligence * 2) / 4,
            mDef = (mental * 2) / 8,
            hit = agility,
            evade = agility - constitution / 2,
            heal = mental + intelligence / 2,
        };
    }

    public StatBlock GetEquipmentBonus()
    {
        StatBlock b = default;
        foreach (var item in equippedSlots.Values)
            b += item.bonus;
        return b;
    }

    public StatBlock GetFinalCombatStats()
    {
        var weapon = Weapon;
        var s = GetBaseCombatStats() + GetEquipmentBonus();
        float pCoef = weapon != null ? weapon.physicalCoeff : 1f;
        float mCoef = weapon?.magicCoeff ?? 0f;
        float hCoef = weapon?.healCoeff ?? 0f;
        int flatP = weapon?.flatPhysicalAtk ?? 0;
        int flatM = weapon?.flatMagicAtk ?? 0;
        int flatH = weapon?.flatHeal ?? 0;

        s.pAtk = (int)Math.Floor((s.pAtk + flatP) * pCoef);
        s.mAtk = (int)Math.Floor((s.mAtk + flatM) * mCoef);
        s.heal = (int)Math.Floor((s.heal + flatH) * hCoef);
        if (pCoef <= 0f) s.pAtk = 0;
        if (mCoef <= 0f) s.mAtk = 0;
        if (hCoef <= 0f) s.heal = 0;

        float r = OverweightRate;
        if (r > 0f)
        {
            s.pAtk = (int)Math.Floor(s.pAtk * (1f - 0.5f * r));
            s.hit = (int)Math.Floor(s.hit * (1f - 0.8f * r));
            s.evade = (int)Math.Floor(s.evade * (1f - 0.8f * r));
        }
        return s;
    }

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
