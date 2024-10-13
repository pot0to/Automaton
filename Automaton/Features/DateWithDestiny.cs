using Automaton.IPC;
using Automaton.UI;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using ECommons.Automation;
using ECommons.EzHookManager;
using ECommons.GameFunctions;
using ECommons.SimpleGui;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
//using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using IronPython.Compiler.Ast;
using Lumina.Excel.GeneratedSheets;
using System.Security.Permissions;
using static ECommons.GameFunctions.ObjectFunctions;

namespace Automaton.Features;

public class DateWithDestinyConfiguration
{
    public HashSet<uint> blacklist = [];
    public HashSet<uint> whitelist = [];
    public List<uint> zones = [];
    [BoolConfig] public bool YokaiMode;
    [BoolConfig] public bool StayInMeleeRange;
    [BoolConfig] public bool PrioritizeForlorns = true;
    [BoolConfig] public bool PrioritizeBonusFates = true;
    [BoolConfig] public bool PrioritizeStartedFates;
    [BoolConfig(DependsOn = nameof(PrioritizeBonusFates))] public bool BonusWhenTwist = false;
    [BoolConfig] public bool EquipWatch = true;
    [BoolConfig] public bool SwapMinions = true;
    [BoolConfig] public bool SwapZones = true;
    [BoolConfig] public bool ChangeInstances = true;

    [BoolConfig] public bool FullAuto = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoMount = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoFly = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool PathToFate = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoSync = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoTarget = true;
    [BoolConfig(DependsOn = nameof(FullAuto))] public bool AutoMoveToMobs = true;
    [IntConfig(DefaultValue = 900)] public int MaxDuration = 900;
    [IntConfig(DefaultValue = 120)] public int MinTimeRemaining = 120;
    [IntConfig(DefaultValue = 90)] public int MaxProgress = 90;

    [BoolConfig] public bool ShowFateTimeRemaining;
    [BoolConfig] public bool ShowFateBonusIndicator;

    [BoolConfig] public bool AbortTasksOnTimeout = true;
}

public enum DateWithDestinyState
{
    Unknown,
    Ready,
    Mounting,
    MovingToFate,
    InteractingWithNpc,
    InCombat,
    ChangingInstances,
    ExchangingVouchers,
    Dead,
    SummonChocobo
}

[Tweak, Requirement(NavmeshIPC.Name, NavmeshIPC.Repo)]
internal class DateWithDestiny : Tweak<DateWithDestinyConfiguration>
{
    public override string Name => "Date with Destiny";
    public override string Description => $"It's a FATE bot. Requires whatever you want for combat.";

    public bool active = false;
    private static Vector3 TargetPos;
    private readonly Throttle action = new();
    private Random random = null!;

    private DateWithDestinyState State { get; set; }
    private DateWithDestinyState PreviousState { get; set; }
    private uint ZoneToFarm { get; set; }

    public DateWithDestiny()
    {
        State = DateWithDestinyState.Ready;
        PreviousState = DateWithDestinyState.Ready;
        ZoneToFarm = Svc.ClientState.TerritoryType;
    }

    private enum Z
    {
        MiddleLaNoscea = 134,
        LowerLaNoscea = 135,
        EasternLaNoscea = 137,
        WesternLaNoscea = 138,
        UpperLaNoscea = 139,
        WesternThanalan = 140,
        CentralThanalan = 141,
        EasternThanalan = 145,
        SouthernThanalan = 146,
        NorthernThanalan = 147,
        CentralShroud = 148,
        EastShroud = 152,
        SouthShroud = 153,
        NorthShroud = 154,
        OuterLaNoscea = 180,
        CoerthasWesternHighlands = 397,
        TheDravanianForelands = 398,
        TheDravanianHinterlands = 399,
        TheChurningMists = 400,
        TheSeaofClouds = 401,
        AzysLla = 402,
        TheFringes = 612,
        TheRubySea = 613,
        Yanxia = 614,
        ThePeaks = 620,
        TheLochs = 621,
        TheAzimSteppe = 622,
    }

    private bool yokaiMode;
    private const uint YokaiWatch = 15222;
    private static readonly uint[] YokaiMinions = [200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 390, 391, 392, 393];
    private static readonly uint[] YokaiLegendaryMedals = [15168, 15169, 15170, 15171, 15172, 15173, 15174, 15175, 15176, 15177, 15178, 15179, 15180, 30805, 30804, 30803, 30806];
    private static readonly uint[] YokaiWeapons = [15210, 15216, 15212, 15217, 15213, 15219, 15218, 15220, 15211, 15221, 15214, 15215, 15209, 30809, 30808, 30807, 30810];
    private static readonly Z[][] YokaiZones =
    [
        [Z.CentralShroud, Z.LowerLaNoscea, Z.CentralThanalan], // Jibanyan
        [Z.EastShroud, Z.WesternLaNoscea, Z.EasternThanalan], // Komasan
        [Z.SouthShroud, Z.UpperLaNoscea, Z.SouthernThanalan], // Whisper
        [Z.NorthShroud, Z.OuterLaNoscea, Z.MiddleLaNoscea], // Blizzaria
        [Z.WesternThanalan, Z.CentralShroud, Z.LowerLaNoscea], // Kyubi
        [Z.CentralThanalan, Z.EastShroud, Z.WesternLaNoscea], // Komajiro
        [Z.EasternThanalan, Z.SouthShroud, Z.UpperLaNoscea], // Manjimutt
        [Z.SouthernThanalan, Z.NorthShroud, Z.OuterLaNoscea], // Noko
        [Z.MiddleLaNoscea, Z.WesternThanalan, Z.CentralShroud], // Venoct
        [Z.LowerLaNoscea, Z.CentralThanalan, Z.EastShroud], // Shogunyan
        [Z.WesternLaNoscea, Z.EasternThanalan, Z.SouthShroud], // Hovernyan
        [Z.UpperLaNoscea, Z.SouthernThanalan, Z.NorthShroud], // Robonyan
        [Z.OuterLaNoscea, Z.MiddleLaNoscea, Z.WesternThanalan], // USApyon
        [Z.TheFringes, Z.TheRubySea, Z.Yanxia, Z.ThePeaks, Z.TheLochs, Z.TheAzimSteppe], // Lord Enma
        [Z.CoerthasWesternHighlands, Z.TheDravanianForelands, Z.TheDravanianHinterlands, Z.TheChurningMists, Z.TheSeaofClouds, Z.AzysLla], // Lord Ananta
        [Z.CoerthasWesternHighlands, Z.TheDravanianForelands, Z.TheDravanianHinterlands, Z.TheChurningMists, Z.TheSeaofClouds, Z.AzysLla], // Zazel
        [Z.TheFringes, Z.TheRubySea, Z.Yanxia, Z.ThePeaks, Z.TheLochs, Z.TheAzimSteppe], // Damona
    ];
    private static readonly List<(uint Minion, uint Medal, uint Weapon, Z[] Zones)> Yokai = YokaiMinions
        .Zip(YokaiLegendaryMedals, (x, y) => (Minion: x, Medal: y))
        .Zip(YokaiWeapons, (xy, z) => (xy.Minion, xy.Medal, Weapon: z))
        .Zip(YokaiZones, (wxy, z) => (wxy.Minion, wxy.Medal, wxy.Weapon, z))
        .ToList();

    private static readonly uint[] ForlornIDs = [7586, 7587];
    private static readonly uint[] TwistOfFateStatusIDs = [1288, 1289];

    private ushort fateID;
    private ushort FateID
    {
        get => fateID; set
        {
            if (fateID != value)
            {
                SyncFate(value);
            }
            fateID = value;
        }
    }

    public override void DrawConfig()
    {
        ImGuiX.DrawSection("Configuration");
        ImGui.Checkbox("Yo-Kai Mode (Very Experimental)", ref yokaiMode);
        ImGui.Checkbox("Prioritize targeting Forlorns", ref Config.PrioritizeForlorns);
        ImGui.Checkbox("Prioritize Fates with EXP bonus", ref Config.PrioritizeBonusFates);
        ImGui.Indent();
        using (var _ = ImRaii.Disabled(!Config.PrioritizeBonusFates))
        {
            ImGui.Checkbox("Only with Twist of Fate", ref Config.BonusWhenTwist);
        }
        ImGui.Unindent();
        ImGui.Checkbox("Prioritize fates that have progress already (up to configured limit)", ref Config.PrioritizeStartedFates);
        ImGui.Checkbox("Always close to melee range of target", ref Config.StayInMeleeRange);
        ImGui.Checkbox("Full Auto Mode", ref Config.FullAuto);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"All the below options will be treated as true if this is enabled.");
        ImGui.Indent();
        using (var _ = ImRaii.Disabled(Config.FullAuto))
        {
            ImGui.Checkbox("Auto Mount", ref Config.AutoMount);
            ImGui.Checkbox("Auto Fly", ref Config.AutoFly);
            ImGui.Checkbox("Auto Sync", ref Config.AutoSync);
            ImGui.Checkbox("Auto Target Mobs", ref Config.AutoTarget);
            ImGui.Checkbox("Auto Move To Mobs", ref Config.AutoMoveToMobs);
            ImGui.Checkbox("Path To Next Fate", ref Config.PathToFate);
        }
        ImGui.Unindent();

        ImGuiX.DrawSection("Fate Options");
        ImGui.DragInt("Max Duration (s)", ref Config.MaxDuration);
        ImGui.SameLine();
        ImGuiX.ResetButton(ref Config.MaxDuration, 900);

        ImGui.DragInt("Min Time Remaining (s)", ref Config.MinTimeRemaining);
        ImGui.SameLine();
        ImGuiX.ResetButton(ref Config.MinTimeRemaining, 120);

        ImGui.DragInt("Max Progress (%)", ref Config.MaxProgress, 1, 0, 100);
        ImGui.SameLine();
        ImGuiX.ResetButton(ref Config.MaxProgress, 90);

        ImGuiX.DrawSection("Fate Window Options");
        ImGui.Checkbox("Show Time Remaining", ref Config.ShowFateTimeRemaining);
        ImGui.Checkbox("Show Bonus Indicator", ref Config.ShowFateBonusIndicator);
        ImGui.Checkbox("Change Instances (Requires Lifestream)", ref Config.ChangeInstances);
    }

    public override void Enable()
    {
        EzConfigGui.WindowSystem.AddWindow(new FateTrackerUI(this));
        random = new();
        Svc.Framework.Update += OnUpdate;
    }

    public override void Disable()
    {
        Utils.RemoveWindow<FateTrackerUI>();
        Svc.Framework.Update -= OnUpdate;
    }

    [CommandHandler("/vfate", "Opens the FATE tracker")]
    private void OnCommand(string command, string arguments) => Utils.GetWindow<FateTrackerUI>()!.IsOpen ^= true;

    private int _successiveInstanceChanges = 0;
    private readonly int _distanceToTargetAetheryte = 50; // object.IsTargetable has a larger range than actually clickable

    private unsafe void OnUpdate(IFramework framework)
    {
        if (State != PreviousState)
        {
            Svc.Log.Info("State Change: " + State.ToString());
            PreviousState = State;
        }

        P.TaskManager.AbortOnTimeout = Config.AbortTasksOnTimeout;

        if (!Player.Available || P.TaskManager.IsBusy) return;

        if (!active)
        {
            if (State != DateWithDestinyState.Ready)
                State = DateWithDestinyState.Ready;
            return;
        }

        if (Player.IsDead && State != DateWithDestinyState.Dead)
        {
            State = DateWithDestinyState.Dead;
        }

        var currentFate = FateManager.Instance()->CurrentFate;
        var nextFate = GetFates().FirstOrDefault();
        var bicolorGemstoneCount = GetItemCount(26807);
        switch (State)
        {
            case DateWithDestinyState.Dead:
                ExecuteRevive();
                return;
            case DateWithDestinyState.Ready:
                if (Svc.ClientState.TerritoryType != ZoneToFarm)
                    ExecuteTeleport(Coords.GetPrimaryAetheryte(ZoneToFarm) ?? 0);
                else if (UIState.Instance()->Buddy.CompanionInfo.TimeLeft == 0)
                    State = DateWithDestinyState.SummonChocobo;
                else if (currentFate != null)
                    State = DateWithDestinyState.InCombat;
                else if (bicolorGemstoneCount > 1400)
                    State = DateWithDestinyState.ExchangingVouchers;
                else if (nextFate == null)
                    State = DateWithDestinyState.ChangingInstances;
                else
                    State = DateWithDestinyState.MovingToFate;
                return;
            case DateWithDestinyState.SummonChocobo:
                ExecuteSummonChocobo();
                return;
            case DateWithDestinyState.Mounting:
                if ((Config.FullAuto || Config.AutoMount) && !Player.Occupied && !(Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.Mounted2]))
                    ExecuteMount();
                else if ((Config.FullAuto || Config.AutoFly) && !Player.Occupied && Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.InFlight])
                    ExecuteJump();
                else if (Svc.Condition[ConditionFlag.InFlight])
                    State = DateWithDestinyState.MovingToFate;
                return;
            case DateWithDestinyState.MovingToFate:
                _successiveInstanceChanges = 0;

                //var nextFateContext = 
                if (FateManager.Instance()->GetFateById(nextFate!.FateId) == null) // if the fate disappears on your way there
                {
                    State = DateWithDestinyState.Ready;
                    return;
                }

                unsafe { AgentMap.Instance()->SetFlagMapMarker(Svc.ClientState.TerritoryType, Svc.ClientState.MapId, FateManager.Instance()->GetFateById(nextFate!.FateId)->Location); }
                if (!Svc.Condition[ConditionFlag.InFlight])
                {
                    State = DateWithDestinyState.Mounting;
                    return;
                }

                if (!P.Navmesh.PathfindInProgress() && !P.Navmesh.IsRunning())
                {
                    if (currentFate is not null)
                    {
                        State = DateWithDestinyState.InCombat;
                    }
                    else
                        MoveToNextFate(nextFate!.FateId);
                }
                return;
            case DateWithDestinyState.InteractingWithNpc:
                // TODO: not implemented
                return;
            case DateWithDestinyState.InCombat:

                if (currentFate == null && !Svc.Condition[ConditionFlag.InCombat] && !Player.IsCasting)
                {
                    State = DateWithDestinyState.Ready;
                    FateID = 0;
                }
                else
                {
                    if (Svc.Condition[ConditionFlag.Mounted]) ExecuteDismount();

                    var target = Svc.Targets.Target;
                    if (P.Navmesh.IsRunning() && Svc.Targets.Target?.ObjectKind == ObjectKind.BattleNpc &&
                        (DistanceToTarget() < 2 || (target != null && DistanceToHitboxEdge(target.HitboxRadius) <= (Config.StayInMeleeRange ? 0 : 15))))
                    {
                        P.Navmesh.Stop();
                        return;
                    }

                    // target mobs targeting player (includes unexpected combat outside of fate)
                    if (target == null && Svc.Condition[ConditionFlag.InCombat])
                        target = GetMobTargetingPlayer();

                    // target fate mobs if you're in a fate
                    if (currentFate != null)
                    {
                        FateID = currentFate->FateId;
                        if (target == null || target.ObjectKind != ObjectKind.BattleNpc)
                            target = GetFateMob();
                    }

                    // if you found a target, go fight it
                    if (target != null)
                    {
                        TargetPos = target.Position;
                        if ((Config.FullAuto || Config.AutoMoveToMobs) && (Svc.Targets.Target == null || !IsInMeleeRange(target.HitboxRadius + (Config.StayInMeleeRange ? 0 : 15))))
                        {
                            TargetAndMoveToEnemy(target);
                            return;
                        }
                    }
                }
                return;
            case DateWithDestinyState.ChangingInstances:
                Svc.Log.Info("_successiveInstanceChanges: " + _successiveInstanceChanges);
                if (ChangeInstances())
                    State = DateWithDestinyState.Ready;
                return;
            case DateWithDestinyState.ExchangingVouchers:
                if (P.Navmesh.PathfindInProgress() || P.Navmesh.IsRunning())
                    return;

                Svc.Log.Info("navmesh not running");

                if (bicolorGemstoneCount < 1400)
                {
                    // close the shop after gems are spent
                    if (TryGetAddonByName<AtkUnitBase>("ShopExchangeCurrency", out var shopAddon) && IsAddonReady(shopAddon))
                    {
                        Callback.Fire(shopAddon, true, -1);
                    }

                    if (!Player.Occupied)
                    {
                        if (Svc.ClientState.TerritoryType == ZoneToFarm)
                        {
                            State = DateWithDestinyState.Ready;
                            Svc.Log.Info("State Change: " + State.ToString());
                        }
                        else
                            ExecuteTeleport(Coords.GetPrimaryAetheryte(ZoneToFarm) ?? 0);
                    }
                    return;
                }
                else
                {
                    uint oldSharlayanTerritoryId = 962;
                    if (Svc.ClientState.TerritoryType != oldSharlayanTerritoryId)
                    {
                        ExecuteTeleport(Coords.GetPrimaryAetheryte(oldSharlayanTerritoryId) ?? 0);
                        return;
                    }

                    var nearbyShopTarget = Svc.Objects.FirstOrDefault(x => x.DataId == 1037055); // search nearby object table for target
                    if (nearbyShopTarget == null || Vector3.Distance(Player.Position, nearbyShopTarget.Position) > 5) // if not found or not nearby, move to shop location
                    {
                        Svc.Log.Info("shop not found nearby");
                        P.Navmesh.PathfindAndMoveTo(new Vector3(74.17f, 5.15f, -37.44f), false);
                        return;
                    }

                    // if not targeting shopkeeper
                    if (Svc.Targets.Target?.DataId != nearbyShopTarget.DataId)
                    {
                        Svc.Targets.Target = nearbyShopTarget;
                    }

                    Svc.Log.Info("targeting shopkeeper");
                    // interact with shopkeeper
                    if (!Svc.Condition[ConditionFlag.Occupied])
                        TargetSystem.Instance()->InteractWithObject((CSGameObject*)Svc.Targets.Target.Address);

                    // purchase bicolor vouchers
                    if (TryGetAddonByName<AtkUnitBase>("ShopExchangeCurrency", out var shopAddon) && IsAddonReady(shopAddon))
                    {
                        if (TryGetAddonByName<AtkUnitBase>("SelectYesno", out var yesnoAddon) && IsAddonReady(yesnoAddon)) // confirm
                            Callback.Fire(yesnoAddon, true, 0);
                        else // purchase the gems
                            Callback.Fire(shopAddon, false, 0, 5, bicolorGemstoneCount / 100);
                    }

                    return;
                }
        };
    }

    private void YokaiMode()
    {
        if (YokaiMinions.Contains(CurrentCompanion))
        {
            if (Config.EquipWatch && HaveYokaiMinionsMissing() && !HasWatchEquipped() && GetItemCount(YokaiWatch) > 0)
                Player.Equip(15222);

            var medal = Yokai.FirstOrDefault(x => x.Minion == CurrentCompanion).Medal;
            if (GetItemCount(medal) >= 10)
            {
                Svc.Log.Debug("Have 10 of the relevant Legendary Medal. Swapping minions");
                var minion = Yokai.FirstOrDefault(x => CompanionUnlocked(x.Minion) && GetItemCount(x.Medal) < 10 && GetItemCount(x.Weapon) < 1).Minion;
                if (Config.SwapMinions && minion != default)
                {
                    ECommons.Automation.Chat.Instance.SendMessage($"/minion {GetRow<Companion>(minion)?.Singular}");
                    return;
                }
            }

            var zones = Yokai.FirstOrDefault(x => x.Minion == CurrentCompanion).Zones;
            if (Config.SwapZones && !zones.Contains((Z)Svc.ClientState.TerritoryType))
            {
                Svc.Log.Debug("Have Yokai minion equipped but not in appropiate zone. Teleporting");
                if (!Svc.Condition[ConditionFlag.Casting])
                    ExecuteTeleport((uint)Coords.GetPrimaryAetheryte((uint)zones.First())!);
                return;
            }
        }
    }

    private unsafe void MoveToNextFate(ushort nextFateID)
    {
        if (P.Navmesh.IsReady() &&
            !Svc.Condition[ConditionFlag.InCombat] && !Player.Occupied)
        {
            var targetPos = GetRandomPointInFate(nextFateID);

            var teleportTimePenalty = 100; // to account for how long teleport takes you
            var directTravelDistance = Vector3.Distance(Player.Position, targetPos);
            var closestAetheryte = Coords.GetNearestAetheryte(Svc.ClientState.TerritoryType, targetPos);

            if (closestAetheryte != 0)
            {
                var aetheryteTravelDistance = Coords.GetDistanceToAetheryte(closestAetheryte, targetPos) + teleportTimePenalty;
                if (aetheryteTravelDistance < directTravelDistance) // if the closest aetheryte is a shortcut, then teleport
                    ExecuteTeleport(closestAetheryte);
                else // if the closest aetheryte is too far away, just fly directly to the fate
                {
                    if (P.Navmesh.IsReady() && !P.Navmesh.IsRunning() && !P.Navmesh.PathfindInProgress())
                        P.Navmesh.PathfindAndMoveTo(targetPos, true);
                }
            }
            else
            {
                // if there is no closest aetheryte (i.e. dravanian hinterlands with no aetherytes on the map)
                // then fly directly to the fate
                if (P.Navmesh.IsReady() && !P.Navmesh.IsRunning() && !P.Navmesh.PathfindInProgress())
                    P.Navmesh.PathfindAndMoveTo(targetPos, true);
            }
        }
    }

    private void TargetAndMoveToEnemy(IGameObject target)
    {
        if (Svc.Condition[ConditionFlag.Mounted]) ExecuteDismount();
        TargetPos = target.Position;
        if ((Config.FullAuto || Config.AutoTarget) && Svc.Targets.Target?.GameObjectId != target.GameObjectId)
            Svc.Targets.Target = target;
        if ((Config.FullAuto || Config.AutoMoveToMobs) && !P.Navmesh.PathfindInProgress() && !IsInMeleeRange(target.HitboxRadius + (Config.StayInMeleeRange ? 0 : 15)))
            P.Navmesh.PathfindAndMoveTo(TargetPos, false);
    }

    private unsafe void ExecuteSummonChocobo()
    {
        if (UIState.Instance()->Buddy.CompanionInfo.TimeLeft > 0)
        {
            State = DateWithDestinyState.Ready;
            return;
        }

        var gysahlGreensCount = GetItemCount(4868);
        if (gysahlGreensCount > 0)
        {
            return;
        }
    }
    private unsafe void ExecuteRevive()
    {
        if (TryGetAddonByName<AtkUnitBase>("SelectYesno", out var yesnoAddon) && IsAddonReady(yesnoAddon))
        {
            Callback.Fire(yesnoAddon, true, 0);
        }
        P.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.BetweenAreas], "BetweenAreas=True");
        P.TaskManager.Enqueue(() => !Svc.Condition[ConditionFlag.BetweenAreas], "BetweenAreas=False");
        P.TaskManager.Enqueue(() => !Player.IsDead, "IsDead=False");
        State = DateWithDestinyState.Ready;
    }

    private unsafe void ExecuteTeleport(uint closestAetheryteDataId)
    {
        P.TaskManager.Enqueue(() => Telepo.Instance()->Teleport(closestAetheryteDataId, 0), $"Teleport to {closestAetheryteDataId}");
        P.TaskManager.Enqueue(() => Player.Object.IsCasting, "Casting=True");
        P.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.BetweenAreas], "BetweenAreas=True");
        P.TaskManager.Enqueue(() => !Svc.Condition[ConditionFlag.BetweenAreas], "BetweenAreas=False");
    }

    private unsafe bool ChangeInstances()
    {
        var numberOfInstances = P.Lifestream.GetNumberOfInstances();
        if (_successiveInstanceChanges >= numberOfInstances - 1)
        {
            P.TaskManager.Enqueue(() => EzThrottler.Throttle("SuccessiveInstanceChanges", 10000));
            P.TaskManager.Enqueue(() => EzThrottler.Check("SuccessiveInstanceChanges"));
            Svc.Log.Info("Cycled through all instances. Waiting 10s.");
            _successiveInstanceChanges = 0;
            return false;
        }

        if (P.Navmesh.PathfindInProgress() || P.Navmesh.IsRunning())
            return false;

        var closestAetheryteDataId = Coords.GetNearestAetheryte((int)Player.Territory, Player.Position);
        var closestAetheryteGameObject = Svc.Objects
            .Where(x => x is { ObjectKind: ObjectKind.Aetheryte })
            .FirstOrDefault(x => x.DataId == closestAetheryteDataId);
        if (Coords.GetDistanceToAetheryte(closestAetheryteDataId, Player.Position) >= _distanceToTargetAetheryte)
        {
            Svc.Log.Debug("Teleporting to nearby aetheryte: " + closestAetheryteDataId);
            ExecuteTeleport(closestAetheryteDataId);
            return false;
        }

        if (Svc.Targets.Target?.ObjectKind != ObjectKind.Aetheryte)
        {
            Svc.Targets.Target = closestAetheryteGameObject;
            return false;
        }

        // If too far away to target or "target is too far below you" error
        if (!P.Navmesh.PathfindInProgress() && !P.Navmesh.IsRunning() && (DistanceToTarget() > 10 || Player.Position.Y - Svc.Targets.Target.Position.Y > 2))
        {
            // interact distance is between 8 and 10. less than 8 and you will run into the base of the aetheryte
            var closerToAetheryte = Svc.Targets.Target.Position - (Vector3.Normalize(Svc.Targets.Target.Position - Player.Position) * 8);
            closerToAetheryte.Y = Math.Min(closerToAetheryte.Y, Svc.Targets.Target.Position.Y + 1);
            P.Navmesh.PathfindAndMoveTo(closerToAetheryte, false);
            return false;
        }

        if (!P.Lifestream.CanChangeInstance())
        {
            Svc.Log.Debug("Cannot change instances at this time.");
            return false;
        }

        Svc.Log.Debug("Lifestream not busy.");
        Svc.Log.Debug("Changing instances.");

        var nextInstance = ((P.Lifestream.GetCurrentInstance() + 1) % numberOfInstances) + 1; // instances are 1-indexed

        P.TaskManager.Enqueue(() => P.Lifestream.ChangeInstance(nextInstance)); // flying mount roulette
        P.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]);
        P.TaskManager.Enqueue(() => !(Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]));

        _successiveInstanceChanges += 1;

        return true;
    }

    private unsafe void ExecuteActionSafe(ActionType type, uint id) => action.Exec(() => ActionManager.Instance()->UseAction(type, id));
    private void ExecuteMount()
    {
        P.TaskManager.Enqueue(() => ExecuteActionSafe(ActionType.GeneralAction, 24), "Flying Mount Roulette"); // flying mount roulette
        P.TaskManager.Enqueue(() => Player.Object.IsCasting, "Casting=True");
        P.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.Mounting] || Svc.Condition[ConditionFlag.Mounting71] || Svc.Condition[ConditionFlag.Unknown57], "Mounting=True");
        P.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.Mounted2], "Mounted=True");
    }
    private void ExecuteDismount() => ExecuteActionSafe(ActionType.GeneralAction, 23);
    private void ExecuteJump()
    {
        P.TaskManager.Enqueue(() => ExecuteActionSafe(ActionType.GeneralAction, 2));
        P.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.Jumping] || Svc.Condition[ConditionFlag.Jumping61]);
        P.TaskManager.Enqueue(() => Svc.Condition[ConditionFlag.InFlight]);
    }

    private IOrderedEnumerable<IFate> GetFates() => Svc.Fates.Where(FateConditions)
        .OrderByDescending(x =>
        Config.PrioritizeBonusFates
        && x.HasExpBonus
        && (
        !Config.BonusWhenTwist
        || Player.Status.FirstOrDefault(x => TwistOfFateStatusIDs.Contains(x.StatusId)) != null)
        )
        .ThenByDescending(x => Config.PrioritizeStartedFates && x.Progress > 0)
        .ThenBy(f => Vector3.Distance(Player.Position, f.Position));
    public bool FateConditions(IFate f) => f.GameData.Rule == 1 && f.State != FateState.Preparation && f.Duration <= Config.MaxDuration && f.Progress <= Config.MaxProgress && f.TimeRemaining > Config.MinTimeRemaining && !Config.blacklist.Contains(f.FateId);

    private unsafe DGameObject? GetMobTargetingPlayer()
        => Svc.Objects
        .FirstOrDefault(x => x is ICharacter { MaxHp: > 0 }
        && !x.IsDead
        && x.IsTargetable
        && x.IsHostile()
        && x.ObjectKind == ObjectKind.BattleNpc
        && x.SubKind == (byte)BattleNpcSubKind.Enemy
        && x.IsTargetingPlayer());

    private unsafe DGameObject? GetFateMob()
        => Svc.Objects
        .Where(x => x is ICharacter { MaxHp: > 0 }
        && !x.IsDead
        && x.IsTargetable
        && x.IsHostile()
        && x.ObjectKind == ObjectKind.BattleNpc
        && x.SubKind == (byte)BattleNpcSubKind.Enemy
        && (x.Struct() != null && x.Struct()->FateId == FateID)
        && Math.Sqrt(Math.Pow(x.Position.X - CurrentFate->Location.X, 2) + Math.Pow(x.Position.Z - CurrentFate->Location.Z, 2)) < CurrentFate->Radius)
        // Prioritize Forlorns if configured
        .OrderByDescending(x => Config.PrioritizeForlorns && ForlornIDs.Contains(x.DataId))
        // Prioritize enemies targeting us
        .ThenByDescending(x => x.IsTargetingPlayer())
        // Deprioritize mobs in combat with other players (hopefully avoid botlike pingpong behavior in trash fates)
        .ThenBy(x => IsTaggedByOther(x) && !x.IsTargetingPlayer())
        // Prioritize closest enemy        
        .ThenBy(x => Math.Floor(Vector3.Distance(Player.Position, x.Position)))
        // Prioritize lowest HP enemy
        .FirstOrDefault();

    private unsafe uint CurrentCompanion => Svc.ClientState.LocalPlayer!.Character()->CompanionObject->Character.GameObject.BaseId;
    private unsafe bool CompanionUnlocked(uint id) => UIState.Instance()->IsCompanionUnlocked(id);
    private unsafe bool HasWatchEquipped() => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->GetInventorySlot(10)->ItemId == YokaiWatch;
    private unsafe bool HaveYokaiMinionsMissing() => Yokai.Any(x => CompanionUnlocked(x.Minion));
    private unsafe int GetItemCount(uint itemID) => InventoryManager.Instance()->GetInventoryItemCount(itemID);

    private unsafe FateContext* CurrentFate => FateManager.Instance()->CurrentFate;

    private unsafe float DistanceToFate() => Vector3.Distance(CurrentFate->Location, Svc.ClientState.LocalPlayer!.Position);
    private unsafe float DistanceToTarget() => (Svc.Targets.Target is not null) ? Vector3.Distance(Svc.Targets.Target.Position, Svc.ClientState.LocalPlayer!.Position) : 0;

    //Will be negative if inside hitbox
    private unsafe float DistanceToHitboxEdge(float hitboxRadius) => DistanceToTarget() - hitboxRadius;
    private unsafe bool IsInMeleeRange(float hitboxRadius)
        => DistanceToHitboxEdge(hitboxRadius) < 2;
    public unsafe Vector3 GetRandomPointInFate(ushort fateID)
    {
        var fate = FateManager.Instance()->GetFateById(fateID);
        var angle = random.NextDouble() * 2 * Math.PI;
        var randomPoint = new Vector3((float)(fate->Location.X + fate->Radius / 2 * Math.Cos(angle)), fate->Location.Y, (float)(fate->Location.Z + fate->Radius / 2 * Math.Sin(angle)));
        var point = P.Navmesh.NearestPoint(randomPoint, 5, 5);
        return (Vector3)(point == null ? fate->Location : point);
    }

    private unsafe void SyncFate(ushort value)
    {
        if (value != 0 && PlayerState.Instance()->IsLevelSynced == 0)
        {
            if (Player.Level > CurrentFate->MaxLevel)
                ECommons.Automation.Chat.Instance.SendMessage("/lsync");
        }
    }

    private static unsafe bool IsTaggedByOther(IGameObject a)
    {
        GetNameplateColor ??= EzDelegate.Get<GetNameplateColorDelegate>(GetNameplateColorSig);
        var plateType = GetNameplateColor(a.Address);
        return plateType == 10;
    }
}
