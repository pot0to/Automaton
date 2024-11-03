using Dalamud.Game.Network.Structures;
using Dalamud.Hooking;
using ECommons.Automation;
using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Common.Lua;
using System.Runtime.InteropServices;

namespace Automaton.Utilities;
#pragma warning disable CS0649
internal unsafe class Memory
{
    internal unsafe delegate void RidePillionDelegate(BattleChara* target, int seatIndex);
    internal RidePillionDelegate? RidePillion = null!;

    internal unsafe delegate void SalvageItemDelegate(AgentSalvage* thisPtr, InventoryItem* item, int addonId, byte a4);
    internal SalvageItemDelegate SalvageItem = null!;

    internal delegate void AbandonDutyDelegate(bool a1);
    internal AbandonDutyDelegate AbandonDuty = null!;

    internal delegate nint AgentWorldTravelReceiveEventDelegate(Structs.AgentWorldTravel* agent, nint a2, nint a3, nint a4, long eventCase);
    internal AgentWorldTravelReceiveEventDelegate WorldTravel = null!;

    internal delegate nint WorldTravelSetupInfoDelegate(nint worldTravel, ushort currentWorld, ushort targetWorld);
    internal WorldTravelSetupInfoDelegate WorldTravelSetupInfo = null!;

    public Memory()
    {
        EzSignatureHelper.Initialize(this);
        RidePillion = Marshal.GetDelegateForFunctionPointer<RidePillionDelegate>(Svc.SigScanner.ScanText("48 85 C9 0F 84 ?? ?? ?? ?? 48 89 6C 24 ?? 56 48 83 EC"));
        SalvageItem = Marshal.GetDelegateForFunctionPointer<SalvageItemDelegate>(Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? EB 46 48 8B 03")); // thanks veyn
        AbandonDuty = Marshal.GetDelegateForFunctionPointer<AbandonDutyDelegate>(Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8B 43 28 41 B2 01"));
        WorldTravel = Marshal.GetDelegateForFunctionPointer<AgentWorldTravelReceiveEventDelegate>(Svc.SigScanner.ScanText("40 55 53 56 57 41 54 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? B8"));
        WorldTravelSetupInfo = Marshal.GetDelegateForFunctionPointer<WorldTravelSetupInfoDelegate>(Svc.SigScanner.ScanText("48 8B CB E8 ?? ?? ?? ?? 48 8D 8B ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ??"));

        ProcessRequestResultHook = Svc.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.ProcessRequestResult>(InfoProxyItemSearch.MemberFunctionPointers.ProcessRequestResult, ProcessRequestResultDetour);
        EndRequestHook = Svc.Hook.HookFromAddress<InfoProxyItemSearch.Delegates.EndRequest>(InfoProxyItemSearch.StaticVirtualTablePointer->EndRequest, EndRequestDetour);
        ProcessRequestResultHook?.Enable();
        EndRequestHook?.Enable();
        Svc.MarketBoard.OfferingsReceived += OnOfferingsReceived;
    }

    public void Dispose()
    {
        Svc.MarketBoard.OfferingsReceived -= OnOfferingsReceived;
        ProcessRequestResultHook?.Dispose();
        EndRequestHook?.Dispose();
    }

    #region PacketDispatcher
    const string PacketDispatcher_OnReceivePacketHookSig = "40 53 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 8B F2";
    internal delegate void PacketDispatcher_OnReceivePacket(nint a1, uint a2, nint a3);
    [EzHook(PacketDispatcher_OnReceivePacketHookSig, false)]
    internal EzHook<PacketDispatcher_OnReceivePacket> PacketDispatcher_OnReceivePacketHook = null!;
    [EzHook(PacketDispatcher_OnReceivePacketHookSig, false)]
    internal EzHook<PacketDispatcher_OnReceivePacket> PacketDispatcher_OnReceivePacketMonitorHook = null!;

    internal delegate byte PacketDispatcher_OnSendPacket(nint a1, nint a2, nint a3, byte a4);
    [EzHook("48 89 5C 24 ?? 48 89 74 24 ?? 4C 89 64 24 ?? 55 41 56 41 57 48 8B EC 48 83 EC 70", false)]
    internal EzHook<PacketDispatcher_OnSendPacket> PacketDispatcher_OnSendPacketHook = null!;

    internal List<uint> DisallowedSentPackets = [];
    internal List<uint> DisallowedReceivedPackets = [];

    private byte PacketDispatcher_OnSendPacketDetour(nint a1, nint a2, nint a3, byte a4)
    {
        const byte DefaultReturnValue = 1;

        if (a2 == IntPtr.Zero)
        {
            PluginLog.Error("[HyperFirewall] Error: Opcode pointer is null.");
            return DefaultReturnValue;
        }

        try
        {
            Events.OnPacketSent(a1, a2, a3, a4);
            var opcode = *(ushort*)a2;

            if (DisallowedSentPackets.Contains(opcode))
            {
                PluginLog.Verbose($"[HyperFirewall] Suppressing outgoing packet with opcode {opcode}.");
            }
            else
            {
                PluginLog.Verbose($"[HyperFirewall] Passing outgoing packet with opcode {opcode} through.");
                return PacketDispatcher_OnSendPacketHook.Original(a1, a2, a3, a4);
            }
        }
        catch (Exception e)
        {
            PluginLog.Error($"[HyperFirewall] Exception caught while processing opcode: {e.Message}");
            e.Log();
            return DefaultReturnValue;
        }

        return DefaultReturnValue;
    }

    private void PacketDispatcher_OnReceivePacketDetour(nint a1, uint a2, nint a3)
    {
        if (a3 == IntPtr.Zero)
        {
            PluginLog.Error("[HyperFirewall] Error: Data pointer is null.");
            return;
        }

        try
        {
            Events.OnPacketRecieved(a1, a2, a3);
            var opcode = *(ushort*)(a3 + 2);

            if (DisallowedReceivedPackets.Contains(opcode))
            {
                PluginLog.Verbose($"[HyperFirewall] Suppressing incoming packet with opcode {opcode}.");
            }
            else
            {
                PluginLog.Verbose($"[HyperFirewall] Passing incoming packet with opcode {opcode} through.");
                PacketDispatcher_OnReceivePacketHook.Original(a1, a2, a3);
            }
        }
        catch (Exception e)
        {
            PluginLog.Error($"[HyperFirewall] Exception caught while processing opcode: {e.Message}");
            e.Log();
            return;
        }

        return;
    }
    #endregion

    #region Bewitch
    internal unsafe delegate nint NoBewitchActionDelegate(CSGameObject* gameObj, float x, float y, float z, int a5, nint a6);
    [EzHook("40 53 48 83 EC 50 45 33 C0", false)]
    internal readonly EzHook<NoBewitchActionDelegate>? BewitchHook;

    private unsafe nint BewitchDetour(CSGameObject* gameObj, float x, float y, float z, int a5, nint a6)
    {
        try
        {
            if (gameObj->IsCharacter())
            {
                var chara = gameObj->Character();
                if (chara->GetStatusManager()->HasStatus(3023) || chara->GetStatusManager()->HasStatus(3024))
                    return nint.Zero;
            }
            return BewitchHook!.Original(gameObj, x, y, z, a5, a6);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex.Message, ex);
            return BewitchHook!.Original(gameObj, x, y, z, a5, a6);
        }
    }
    #endregion

    #region Knockback
    internal delegate long kbprocDelegate(long gameobj, float rot, float length, long a4, char a5, int a6);
    [EzHook("E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? FF C6", false)]
    internal readonly EzHook<kbprocDelegate>? KBProcHook;

    internal long KBProcDetour(long gameobj, float rot, float length, long a4, char a5, int a6) => KBProcHook!.Original(gameobj, rot, 0f, a4, a5, a6);
    #endregion

    #region Achievements
    internal delegate void ReceiveAchievementProgressDelegate(Achievement* achievement, uint id, uint current, uint max);
    [EzHook("C7 81 ?? ?? ?? ?? ?? ?? ?? ?? 89 91 ?? ?? ?? ?? 44 89 81")]
    internal EzHook<ReceiveAchievementProgressDelegate> ReceiveAchievementProgressHook = null!;

    private void ReceiveAchievementProgressDetour(Achievement* achievement, uint id, uint current, uint max)
    {
        try
        {
            Svc.Log.Debug($"{nameof(ReceiveAchievementProgressDetour)}: [{id}] {current} / {max}");
            Events.OnAchievementProgressUpdate(id, current, max);
        }
        catch (Exception e)
        {
            Svc.Log.Error("Error receiving achievement progress: {e}", e);
        }

        ReceiveAchievementProgressHook.Original(achievement, id, current, max);
    }
    #endregion

    #region Marketboard
    private readonly Hook<InfoProxyItemSearch.Delegates.ProcessRequestResult>? ProcessRequestResultHook;
    private readonly Hook<InfoProxyItemSearch.Delegates.EndRequest>? EndRequestHook;
    private readonly List<IMarketBoardItemListing> Listings = [];

    public delegate void ListingsStartDelegate();
    public delegate void ListingsPageDelegate(IReadOnlyList<IMarketBoardItemListing> listings);
    public delegate void ListingsEndDelegate(IReadOnlyList<IMarketBoardItemListing> listings);

    public nint ProcessRequestResultDetour(InfoProxyItemSearch* infoProxy, nint a2, nint a3, nint a4, int a5, byte a6, int a7)
    {
        Listings.Clear();
        Events.OnListingsStart();
        return ProcessRequestResultHook!.Original(infoProxy, a2, a3, a4, a5, a6, a7);
    }

    public void EndRequestDetour(InfoProxyItemSearch* infoProxy)
    {
        EndRequestHook!.Original(infoProxy);
        Events.OnListingsEnd(Listings);
        Listings.Clear();
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings currentOfferings)
    {
        Listings.AddRange(currentOfferings.ItemListings);
        Events.OnListingsPage(currentOfferings.ItemListings);
    }
    #endregion

    #region Snipe Quest Sequences
    [EzHook("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 50 48 8B F1 48 8D 4C 24 ?? E8 ?? ?? ?? ?? 48 8B 4C 24 ??", false)]
    internal EzHook<EnqueueSnipeTaskDelegate> SnipeHook = null!;
    internal delegate ulong EnqueueSnipeTaskDelegate(EventSceneModuleImplBase* scene, lua_State* state);

    private ulong SnipeDetour(EventSceneModuleImplBase* scene, lua_State* state)
    {
        try
        {
            var val = state->top;
            val->tt = 3;
            val->value.n = 1;
            state->top += 1;
            return 1;
        }
        catch
        {
            return SnipeHook.Original.Invoke(scene, state);
        }
    }
    #endregion

    #region Flight Prohibited
    internal delegate nint IsFlightProhibited(nint a1);
    [EzHook("E8 ?? ?? ?? ?? 85 C0 74 07 32 C0 48 83 C4 38", false)]
    internal readonly EzHook<IsFlightProhibited> IsFlightProhibitedHook = null!;

    private unsafe nint IsFlightProhibitedDetour(nint a1)
    {
        try
        {
            if (GetRow<Lumina.Excel.GeneratedSheets.TerritoryType>(Player.Territory)?.Unknown32 == 0) // don't detour in zones where flight is impossible normally
                return IsFlightProhibitedHook.Original(a1);
            else if (PlayerState.Instance()->IsAetherCurrentZoneComplete(Svc.ClientState.TerritoryType)) // don't detour in zones where you can already fly
                return IsFlightProhibitedHook.Original(a1);
            else if (!Svc.Condition[ConditionFlag.Mounted]) // don't detour if you aren't mounted
                return IsFlightProhibitedHook.Original(a1);
            else
                return 0;
        }
        catch (Exception e)
        {
            e.Log();
        }
        return IsFlightProhibitedHook.Original(a1);
    }
    #endregion

    #region Return Receive Event
    internal delegate byte AgentReturnReceiveEventDelegate(AgentInterface* agent);
    [EzHook("E8 ?? ?? ?? ?? 41 8D 5E 0D", false)]
    internal readonly EzHook<AgentReturnReceiveEventDelegate> ReturnHook = null!;

    internal delegate nint ExecuteCommandDelegate(int command, int param1, int param2, int param3, int param4);
    [EzHook("E8 ?? ?? ?? ?? 8D 43 0A", false)]
    internal readonly EzHook<ExecuteCommandDelegate> ExecuteCommandHook = null!;

    private byte ReturnDetour(AgentInterface* agent)
    {
        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 6) != 0 || GameMain.IsInPvPInstance())
            return ReturnHook.Original(agent);

        ExecuteCommand(214);
        return 1;
    }

    private nint ExecuteCommand(int command, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0)
    {
        var result = ExecuteCommandHook.Original(command, param1, param2, param3, param4);
        return result;
    }

    private nint ExecuteCommandDetour(int command, int param1, int param2, int param3, int param4)
    {
        Svc.Log.Debug($"[{nameof(ExecuteCommandDetour)}]: cmd:({command}) | p1:{param1} | p2:{param2} | p3:{param3} | p4:{param4}");
        return ExecuteCommandHook.Original(command, param1, param2, param3, param4);
    }
    #endregion

    #region Get Grand Company Rank
    internal delegate byte GetGrandCompanyRankDelegate(nint a1);
    [EzHook("E8 ?? ?? ?? ?? 3A 43 01", false)]
    internal readonly EzHook<GetGrandCompanyRankDelegate> GCRankHook = null!;

    private byte GCRankDetour(nint a1) => 17;
    #endregion

    #region Interact Related
    internal delegate bool CameraObjectBlockedDelegate(nint a1, nint a2, nint a3);
    [EzHook("E8 ?? ?? ?? ?? 84 C0 75 ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? EB ?? 40 B7", false)]
    internal readonly EzHook<CameraObjectBlockedDelegate>? CameraObjectBlockedHook = null!;

    internal unsafe delegate bool IsObjectInViewRangeDelegate(TargetSystem* system, CSGameObject* gameObject);
    [EzHook("E8 ?? ?? ?? ?? 84 C0 75 ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B C8 48 8B 10 FF 52 ?? 48 8B C8 BA ?? ?? ?? ?? E8 ?? ?? ?? ?? E9", false)]
    internal readonly EzHook<IsObjectInViewRangeDelegate>? IsObjectInViewRangeHook = null!;

    internal delegate bool InteractCheck0Delegate(nint a1, nint a2, nint a3, nint a4, bool a5);
    [EzHook("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 49 8B 00 49 8B C8", false)]
    internal readonly EzHook<InteractCheck0Delegate>? InteractCheckHook = null!;

    internal delegate bool IsPlayerJumpingDelegate(nint a1);

    [EzHook("E8 ?? ?? ?? ?? EB D0 E8 ?? ?? ?? ??", autoEnable: false, detourName: nameof(IsPlayerJumpingDetour))]
    internal readonly EzHook<IsPlayerJumpingDelegate>? IsPlayerJumping0Hook = null!;

    //[EzHook("E8 ?? ?? ?? ?? 84 C0 0F 85 ?? ?? ?? ?? 48 8D 8D ?? ?? ?? ?? 48 89 9C 24", autoEnable: false, detourName: nameof(IsPlayerJumpingDetour))]
    //internal readonly EzHook<IsPlayerOnJumpingDelegate>? IsPlayerJumping1Hook = null!;

    //[EzHook("E8 ?? ?? ?? ?? 84 C0 74 ?? 48 85 DB 74 ?? 48 8B 03 48 8B CB FF 50", autoEnable: false, detourName: nameof(IsPlayerJumpingDetour))]
    //internal readonly Hook<IsPlayerOnJumpingDelegate>? IsPlayerJumping2Hook = null!;

    internal delegate bool CheckTargetPositionDelegate(nint a1, nint a2, nint a3, byte a4, byte a5);
    [EzHook("40 53 57 41 56 48 83 EC ?? 48 8B 02", false)]
    internal readonly EzHook<CheckTargetPositionDelegate>? CheckTargetPositionHook = null!;

    internal unsafe delegate bool EventCanceledDelegate(EventFramework* framework);
    [EzHook("E8 ?? ?? ?? ?? 84 C0 74 ?? 48 8B CB E8 ?? ?? ?? ?? 48 3B C7", false)]
    internal readonly EzHook<EventCanceledDelegate>? EventCancelledHook = null!;

    internal unsafe delegate float CheckTargetDistanceDelegate(CSGameObject* localPlayer, CSGameObject* target);
    [EzHook("E8 ?? ?? ?? ?? 0F 2F 05 ?? ?? ?? ?? 76 ?? 48 8B 03 48 8B CB FF 50 ?? 48 8B C8 BA ?? ?? ?? ?? E8 ?? ?? ?? ?? EB", false)]
    internal readonly EzHook<CheckTargetDistanceDelegate>? CheckTargetDistanceHook = null!;

    private bool CameraObjectBlockedDetour(nint a1, nint a2, nint a3) => true;
    private unsafe bool IsObjectInViewRangeDetour(TargetSystem* system, CSGameObject* gameObject) => true;
    private bool InteractCheckDetour(nint a1, nint a2, nint a3, nint a4, bool a5) => true;
    private bool IsPlayerJumpingDetour(nint a1) => false;
    private bool CheckTargetPositionDetour(nint a1, nint a2, nint a3, byte a4, byte a5) => true;
    private unsafe bool EventCancelledDetour(EventFramework* framework) => false;
    private unsafe float CheckTargetDistanceDetour(CSGameObject* localPlayer, CSGameObject* target) => 0f;
    #endregion
}
