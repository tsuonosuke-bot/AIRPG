namespace GuildSimulator.Core.Models;

public enum StatType { Vitality, Mental, Strength, Agility, Intelligence, Constitution, Appearance }
public enum EquipmentType { Weapon, Armor, Accessory }
public enum EquipSlot { RightHand, LeftHand, Head, Body, Accessory }
public enum WeaponType { Sword, Axe, Spear, Bow, Fire, Wind, Water, Earth, Dark, Light, Null }

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
public enum ConsumableEffectType
{
    MaxHpPercent,
    MoralePercent,
    GoldRewardPercent,
    ExpRewardPercent,
    TrapDamageReductionPercent,
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
}
