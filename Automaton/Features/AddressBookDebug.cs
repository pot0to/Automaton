﻿using Dalamud.Interface;
using Dalamud.Interface.Components;
using ECommons.ImGuiMethods;
using ImGuiNET;

namespace Automaton.Features;

public class AddresBookDebugConfiguration
{
    public List<(string Name, uint Territory, Vector3 Position)> Locations = [];
}

[Tweak(debug: true)]
public class AddressBookDebug : Tweak<AddresBookDebugConfiguration>
{
    public override string Name => "Address Book Debug";
    public override string Description => "Teleport to favourited locations.";

    private string _name = string.Empty;
    public override void DrawConfig()
    {
        if (!Player.Available || PlayerEx.InPvP) return;
        if (ImGui.InputText($"##NewLocation", ref _name, 50, ImGuiInputTextFlags.EnterReturnsTrue))
            Config.Locations.Add((_name, Player.Territory, PlayerEx.Position));
        ImGuiX.DrawSection("Locations in Zone");

        var locs = Config.Locations.Where(x => x.Territory == Player.Territory);
        foreach (var loc in locs)
        {
            ImGuiEx.TextV(Colors.White, $"{loc.Name}: {loc.Position}");
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(loc.Position.GetHashCode(), FontAwesomeIcon.FighterJet))
                PlayerEx.Position = loc.Position;
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(loc.Position.GetHashCode(), FontAwesomeIcon.Trash))
                Config.Locations.Remove(loc);
        }
    }
}
