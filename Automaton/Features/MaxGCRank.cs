namespace Automaton.Features;

[Tweak]
internal class MaxGCRank : Tweak
{
    public override string Name => "Enforce Expert Delivery";
    public override string Description => "Automatically maxes your GC rank to force the expert delivery window to show. Does not bypass anything else rank-restricted.";

    public override void Enable() => P.Memory.GCRankHook.Enable();
    public override void Disable() => P.Memory.GCRankHook.Disable();
}
