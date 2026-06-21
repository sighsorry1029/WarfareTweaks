namespace WarfareTweaks;

internal static class WarfareTweaksConfigExtensions
{
    internal static bool IsOn(this WarfareTweaksPlugin.Toggle value)
    {
        return value == WarfareTweaksPlugin.Toggle.On;
    }
}
