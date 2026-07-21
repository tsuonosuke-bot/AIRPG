using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Guild;

public static class EquipService
{
    public static bool TryEquip(AdventurerData adv, EquipmentMasterData item, GuildManager guild, out string reason)
    {
        reason = "";
        if (!adv.isAlive) { reason = "死亡者には装備できません"; return false; }
        if (!guild.Has(item)) { reason = $"在庫がありません: {item.displayName}"; return false; }

        if (item.type == EquipmentType.Weapon)
        {
            if (adv.weapon != null) guild.AddEquipment(adv.weapon, 1);
            if (!guild.TryConsumeEquipment(item)) { reason = "在庫消費失敗"; return false; }
            adv.weapon = item; return true;
        }
        if (item.type == EquipmentType.Armor)
        {
            if (adv.armor != null) guild.AddEquipment(adv.armor, 1);
            if (!guild.TryConsumeEquipment(item)) { reason = "在庫消費失敗"; return false; }
            adv.armor = item; return true;
        }
        reason = "不明な装備タイプ"; return false;
    }

    public static void UnequipWeapon(AdventurerData adv, GuildManager guild)
    {
        if (adv.weapon == null) return;
        guild.AddEquipment(adv.weapon, 1);
        adv.weapon = null;
    }

    public static void UnequipArmor(AdventurerData adv, GuildManager guild)
    {
        if (adv.armor == null) return;
        guild.AddEquipment(adv.armor, 1);
        adv.armor = null;
    }
}
