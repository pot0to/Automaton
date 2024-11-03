namespace Automaton.Features;

[Tweak(debug: true)]
public unsafe class InstantReturn : Tweak
{
    public override string Name => "Quick Return";
    public override string Description => "Calls the return function directly";

    public override void Enable()
    {
        P.Memory.ReturnHook.Enable();
        P.Memory.ExecuteCommandHook.Enable();
    }

    public override void Disable()
    {
        P.Memory.ReturnHook.Disable();
        P.Memory.ExecuteCommandHook.Disable();
    }
}
