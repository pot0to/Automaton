﻿using Dalamud.Interface;
using Dalamud.Interface.Components;
using ECommons;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using ImGuiNET;

namespace Automaton.Features;

public class EnhancedDutyStartEndConfiguration
{
    [NetworkWarning]
    public string StartMsg = string.Empty;
    public HashSet<string> Players = [];
    public bool CheckForAll;
    public bool CheckForAny;

    [NetworkWarning]
    public string EndMsg = string.Empty;
    public bool AutoLeaveOnEnd;
    public int TimeToWait;
    public bool WaitForLoot;
}

[Tweak]
public class EnhancedDutyStartEnd : Tweak<EnhancedDutyStartEndConfiguration>
{
    public override string Name => "Enhanced Duty Start/End";
    public override string Description => "Automatically execute certain actions when the duty starts or ends.";

    public override void Enable()
    {
        Svc.DutyState.DutyStarted += OnDutyStart;
        Svc.DutyState.DutyCompleted += OnDutyComplete;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public override void Disable()
    {
        Svc.DutyState.DutyStarted -= OnDutyStart;
        Svc.DutyState.DutyCompleted -= OnDutyComplete;
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
    }

    private string _name = string.Empty;
    public override void DrawConfig()
    {
        ImGuiX.DrawSection("Duty Start Options");

        ImGui.InputText($"##{nameof(Config.StartMsg)}", ref Config.StartMsg, 50);
        ImGuiHelpers.SafeTextColoredWrapped(Colors.Grey, "Sends a party chat message when the duty starts.");

        if (ImGui.InputText($"##AddPlayers", ref _name, 50, ImGuiInputTextFlags.EnterReturnsTrue))
            Config.Players.Add(_name);
        ImGuiHelpers.SafeTextColoredWrapped(Colors.Grey, "Leave if specific players are not present.");

        if (Config.Players.Count > 0)
        {
            ImGuiX.DrawSection("Players to Check For");
            if (ImGui.Checkbox("Check for All", ref Config.CheckForAll))
                if (Config.CheckForAll)
                    Config.CheckForAny = !Config.CheckForAll;
            ImGui.SameLine();
            if (ImGui.Checkbox("Check for Any", ref Config.CheckForAny))
                if (Config.CheckForAny)
                    Config.CheckForAll = !Config.CheckForAny;
        }
        foreach (var person in Config.Players)
        {
            ImGuiEx.TextV(person);
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(person, FontAwesomeIcon.Trash))
                Config.Players.Remove(person);
        }

        ImGuiX.DrawSection("Duty End Options");

        ImGui.InputText($"##{nameof(Config.EndMsg)}", ref Config.EndMsg, 50);
        ImGuiHelpers.SafeTextColoredWrapped(Colors.Grey, "Sends a party chat message when the duty ends.");

        ImGui.Checkbox("Auto Leave##End", ref Config.AutoLeaveOnEnd);
        if (Config.AutoLeaveOnEnd)
        {
            ImGui.Indent();
            ImGui.Checkbox("Wait for loot", ref Config.WaitForLoot);
            ImGui.SliderInt("Leave after (s)", ref Config.TimeToWait, 0, 100);
        }
    }

    private void OnDutyStart(object? sender, ushort e)
    {
        if (!Config.StartMsg.IsNullOrEmpty())
        {
            if (Config.StartMsg.StartsWith('/'))
                ECommons.Automation.Chat.Instance.SendMessage(Config.StartMsg);
            else
                ECommons.Automation.Chat.Instance.SendMessage($"/p {Config.StartMsg}");
        }

        var allPlayersInParty = Config.Players.Count > 0 && Config.Players.IsSubsetOf(Svc.Party.Select(p => p.Name.TextValue));
        var noPlayersInParty = Config.Players.Count > 0 && !Config.Players.Any(p => Svc.Party.Any(pm => pm.Name.TextValue == p));
        if (Config.CheckForAll && !allPlayersInParty || Config.CheckForAny && noPlayersInParty)
            P.Memory.AbandonDuty(false);
    }

    private static uint _territoryID;
    private unsafe void OnDutyComplete(object? sender, ushort e)
    {
        _territoryID = Player.Territory;
        if (!Config.EndMsg.IsNullOrEmpty())
        {
            if (Config.EndMsg.StartsWith('/'))
                ECommons.Automation.Chat.Instance.SendMessage(Config.EndMsg);
            else
                ECommons.Automation.Chat.Instance.SendMessage($"/p {Config.EndMsg}");
        }

        if (Config.AutoLeaveOnEnd)
        {
            if (!Config.WaitForLoot || !CheckUnawaredLoot())
            {
                TaskManager.EnqueueDelay(Config.TimeToWait.Ms());
                TaskManager.Enqueue(() => P.Memory.AbandonDuty(false));
            }
        }
    }

    private void OnTerritoryChanged(ushort id)
    {
        // cancel queue if we changed zones via other means to prevent autoleave from triggering in the next duty
        if (id != _territoryID && TaskManager.Tasks.Count > 0)
            TaskManager.Abort();
    }

    private static unsafe bool CheckUnawaredLoot()
    {
        var unawardedLootCount = 0;
        var span = Loot.Instance()->Items;

        for (var i = 0; i < span.Length; i++)
        {
            var loot = span[i];
            Svc.Log.Info("Chest index: " + loot.ChestItemIndex);
            if (loot.RollResult == RollResult.UnAwarded)
            {
                unawardedLootCount++;
            }
        }

        return unawardedLootCount > 0;
    }
}
