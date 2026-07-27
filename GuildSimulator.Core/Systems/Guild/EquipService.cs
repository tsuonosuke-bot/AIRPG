using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Guild;

public static class EquipService
{
    public static readonly IReadOnlyList<EquipSlot> AllSlots = Enum.GetValues<EquipSlot>();

    public static string SlotDisplayName(EquipSlot slot) => slot switch
    {
        EquipSlot.RightHand => "右手",
        EquipSlot.LeftHand => "左手",
        EquipSlot.Head => "頭",
        EquipSlot.Body => "体",
        EquipSlot.Accessory => "アクセサリー",
        _ => slot.ToString(),
    };

    public static bool TryEquip(AdventurerData adv, EquipmentMasterData item, EquipSlot slot, GuildManager guild, out string reason)
    {
        reason = "";
        if (!adv.isAlive) { reason = "死亡者には装備できません"; return false; }
        if (!guild.Has(item)) { reason = $"在庫がありません: {item.displayName}"; return false; }
        if (!item.CanEquipTo(slot)) { reason = $"{item.displayName}は{SlotDisplayName(slot)}に装備できません"; return false; }

        var current = adv.GetEquipped(slot);
        if (current != null) guild.AddEquipment(current, 1);
        if (!guild.TryConsumeEquipment(item)) { reason = "在庫消費失敗"; return false; }
        adv.SetEquipped(slot, item);
        return true;
    }

    public static bool TryEquip(AdventurerData adv, EquipmentMasterData item, GuildManager guild, out string reason)
    {
        var slots = item.GetAllowedSlots();
        if (slots.Count == 0) { reason = "装備可能なスロットがありません"; return false; }
        return TryEquip(adv, item, slots[0], guild, out reason);
    }

    public static void Unequip(AdventurerData adv, EquipSlot slot, GuildManager guild)
    {
        var current = adv.GetEquipped(slot);
        if (current == null) return;
        guild.AddEquipment(current, 1);
        adv.SetEquipped(slot, null);
    }

    public static void UnequipWeapon(AdventurerData adv, GuildManager guild) =>
        Unequip(adv, EquipSlot.RightHand, guild);

    public static void UnequipArmor(AdventurerData adv, GuildManager guild) =>
        Unequip(adv, EquipSlot.Body, guild);

    public static void UnequipAll(AdventurerData adv, GuildManager guild)
    {
        foreach (var slot in AllSlots)
            Unequip(adv, slot, guild);
    }
}
