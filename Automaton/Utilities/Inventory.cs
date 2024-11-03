using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.Interop;
using Lumina.Excel.GeneratedSheets;

namespace Automaton.Utilities;
public class Inventory
{
    public static readonly InventoryType[] PlayerInventory =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.KeyItems,
    ];

    public static readonly InventoryType[] MainOffHand =
    [
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand
    ];

    public static readonly InventoryType[] LeftSideArmory =
    [
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets
    ];

    public static readonly InventoryType[] RightSideArmory =
    [
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings
    ];

    public static readonly InventoryType[] Armory = [.. MainOffHand, .. LeftSideArmory, .. RightSideArmory, InventoryType.ArmorySoulCrystal];
    public static readonly InventoryType[] Equippable = [.. PlayerInventory, .. Armory];

    public static unsafe (InventoryType inv, int slot)? GetItemLocationInInventory(uint itemId, IEnumerable<InventoryType> inventories)
    {
        foreach (var inv in inventories)
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
            for (var i = 0; i < cont->Size; ++i)
                if (cont->GetInventorySlot(i)->ItemId == itemId)
                    return (inv, i);
        }
        return null;
    }

    public static unsafe bool HasItem(uint itemId) => GetItemInInventory(itemId, Equippable) != null;
    public static unsafe bool HasItemEquipped(uint itemId)
    {
        var cont = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        for (var i = 0; i < cont->Size; ++i)
            if (cont->GetInventorySlot(i)->ItemId == itemId)
                return true;
        return false;
    }

    public static unsafe InventoryItem* GetItemInInventory(uint itemId, IEnumerable<InventoryType> inventories, bool mustBeHQ = false)
    {
        foreach (var inv in inventories)
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
            for (var i = 0; i < cont->Size; ++i)
                if (cont->GetInventorySlot(i)->ItemId == itemId && (!mustBeHQ || cont->GetInventorySlot(i)->Flags == InventoryItem.ItemFlags.HighQuality))
                    return cont->GetInventorySlot(i);
        }
        return null;
    }

    public static unsafe List<Pointer<InventoryItem>> GetHQItems(IEnumerable<InventoryType> inventories)
    {
        List<Pointer<InventoryItem>> items = [];
        foreach (var inv in inventories)
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
            for (var i = 0; i < cont->Size; ++i)
                if (cont->GetInventorySlot(i)->Flags == InventoryItem.ItemFlags.HighQuality)
                    items.Add(cont->GetInventorySlot(i));
        }
        return items;
    }

    public static unsafe List<Pointer<InventoryItem>> GetDesynthableItems(IEnumerable<InventoryType> inventories)
    {
        List<Pointer<InventoryItem>> items = [];
        foreach (var inv in inventories)
        {
            var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
            for (var i = 0; i < cont->Size; ++i)
                if (GetRow<Item>(cont->GetInventorySlot(i)->ItemId)?.Desynth > 0)
                    items.Add(cont->GetInventorySlot(i));
        }
        return items;
    }

    public static unsafe uint GetEmptySlots(IEnumerable<InventoryType> inventories = null)
    {
        if (inventories == null)
            return InventoryManager.Instance()->GetEmptySlotsInBag();
        else
        {
            uint count = 0;
            foreach (var inv in inventories)
            {
                var cont = InventoryManager.Instance()->GetInventoryContainer(inv);
                for (var i = 0; i < cont->Size; ++i)
                    if (cont->GetInventorySlot(i)->ItemId == 0)
                        count++;
            }
            return count;
        }
    }
}
