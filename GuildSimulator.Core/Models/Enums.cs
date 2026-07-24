namespace GuildSimulator.Core.Models;

public enum StatType { Vitality, Mental, Strength, Agility, Intelligence, Constitution, Appearance }
public enum EquipmentType { Weapon, Armor }
public enum WeaponType { Sword, Axe, Spear, Bow, Fire, Wind, Water, Earth, Dark, Light, Null }
public enum ArmorType { Cloth, Leather, Plate, Null }
public enum SkillScope { Self, UnitAura }
public enum RelicEffectType { Unit_AddFlat, Unit_Multiply, GoldReward_Multiply, Upkeep_Multiply, RestHeal_Multiply }
public enum DungeonEventType { EnemyEncounter, Heal, Trap, Treasure, Nothing, Gather }
public enum QuestEventType { None, ForceEnemyEncounter, ForceBossEncounter, ForceTreasure, ForceTrap, ForceHeal, ForceGather }
public enum RewardType { Relic, Equipment, Gold, Skill, Consumable }
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
}
