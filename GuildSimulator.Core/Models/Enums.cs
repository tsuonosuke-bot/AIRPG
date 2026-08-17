namespace GuildSimulator.Core.Models;

public enum StatType { Vitality, Mental, Strength, Agility, Intelligence, Constitution, Appearance }
// 列挙順はマスタJSONの数値そのままなので、並べ替えず末尾に足すこと（Shield=3 はその追加分）。
public enum EquipmentType { Weapon, Armor, Accessory, Shield }
public enum EquipSlot { RightHand, LeftHand, Head, Body, Accessory }
// 列挙順はマスタJSONの数値そのままなので、並べ替えず末尾に足すこと（Dagger=11 はその追加分）。
public enum WeaponType { Sword, Axe, Spear, Bow, Fire, Wind, Water, Earth, Dark, Light, Null, Dagger }

/// <summary>武器が何を撃つか。物理か魔法かは能力値の大小ではなく武器そのもので決まる。</summary>
public enum AttackKind { Physical, Magic, Heal }
// 列挙順はマスタJSONの数値そのままなので、並べ替えず末尾に足すこと。
public enum ArmorType { Cloth, LightArmor, Plate, Null }
public enum SkillScope { Self, UnitAura }
public enum RelicEffectType { Unit_AddFlat, Unit_Multiply, GoldReward_Multiply, Upkeep_Multiply, RestHeal_Multiply }
// 採取はダンジョンイベントとは別枠で判定するので、ここには含めない（QuestProgressor参照）。
public enum DungeonEventType { EnemyEncounter, Heal, Trap, Treasure, Nothing }
public enum QuestEventType { None, ForceEnemyEncounter, ForceBossEncounter, ForceTreasure, ForceTrap, ForceHeal, ForceGather }
public enum RewardType { Relic, Equipment, Gold, Skill, Consumable }

/// <summary>宝箱の種別。ボスの宝箱だけは空っぽ抽選を受けない。</summary>
public enum TreasureChestKind { Dungeon, Boss }
public enum Rarity { Common, Uncommon, Rare, Unique, Legend }

/// <summary>冒険者の性別。未指定を既定(0)にして、既存マスタ・セーブデータとの互換を保つ。</summary>
public enum Gender { Unspecified, Male, Female }
public enum ConsumableEffectType
{
    MaxHpPercent,
    MoralePercent,
    GoldRewardPercent,
    ExpRewardPercent,
    TrapDamageReductionPercent,
    // セーブ済みマスタとの互換性を保つため、新しい値は必ず末尾へ追加する。
    RestHealPercent,
    TreasureFromNothingPercent,
    TargetPv,
    TargetMpv,
    GuaranteedNonEmptyChest,
    BattleHorn,
    EmergencyRetreatPercent,
}
public enum QuestChoiceEffectType
{
    None,
    Morale,
    HealPercent,
    DamagePercent,
    Experience,
    Gold,
    Equipment,
    Consumable,
    /// <summary>ダンジョンの宝箱テーブルから value 個ぶん抽選する。</summary>
    Treasure,

    // ここから下は「対象を1人選ぶ」効果。列挙順はJSONの数値そのものなので末尾に足すこと。

    /// <summary>選んだ1人の能力を恒久的に上げる。targetIdで能力を指定、空ならランダム。</summary>
    AdventurerStatUp,

    /// <summary>選んだ1人の能力を恒久的に下げる。下限は1。</summary>
    AdventurerStatDown,

    /// <summary>選んだ1人に targetId のスキルを習得させる。</summary>
    AdventurerSkill,

    /// <summary>選んだ1人に最大HPの value% のダメージ。1未満にはならない。</summary>
    AdventurerDamage,

    /// <summary>
    /// 道中の商人から targetId の装備または消耗品を購入する。
    /// value は提示価格。既存セーブとの互換性を保つため列挙値は末尾に追加する。
    /// </summary>
    Purchase,
}

/// <summary>ギルドマスターが出発前に与える遠征の優先方針。</summary>
public enum ExpeditionPolicy
{
    SurvivalFirst,
    ObjectiveFirst,
}

/// <summary>クエストから撤退した直接の理由。保存データとの互換性のため末尾に追加する。</summary>
public enum ExpeditionRetreatReason
{
    None,
    MoraleBroken,
    SurvivalPolicy,
    BattleStalemate,
    GatherTargetMissed,
    SmokeBomb,
}

/// <summary>数値戦闘ログとは別に、帰還報告へ載せる出来事の分類。</summary>
public enum ExpeditionEventKind
{
    Departure,
    Progress,
    Encounter,
    Rest,
    Trap,
    Treasure,
    Gather,
    Decision,
    Discovery,
    Completion,
    Retreat,
    Injury,
}

/// <summary>1回の戦闘の中だけ持続する状態効果。</summary>
public enum CombatStatusType
{
    Poisoned,
    Bleeding,
    Burning,
    Stunned,
    Regenerating,
    Empowered,
    Guarded,

    /// <summary>凍え。体が冷えて回避が鈍る（DVがpotencyぶん下がる）。</summary>
    Chilled,

    /// <summary>魔力減衰。魔法の威力が落ちる（mPVがpotencyぶん下がる）。</summary>
    ManaSapped,
}

/// <summary>スキル・装備から発生する状態効果の対象。</summary>
public enum CombatStatusTarget
{
    Self,
    Allies,
    Enemy,
}

/// <summary>遠征から帰還した後も残る治療可能な負傷。</summary>
public enum InjuryType
{
    CutsAndBruises,
    Fracture,
    DeepWound,
    Trauma,
}

/// <summary>重傷を乗り越えた際に残る恒久的な傷痕・後遺症。</summary>
public enum ScarType
{
    BattleScar,
    StiffJoint,
    Nightmares,
    Survivor,
}
