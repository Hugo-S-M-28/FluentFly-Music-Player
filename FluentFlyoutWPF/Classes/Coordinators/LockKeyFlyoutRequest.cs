namespace FluentFlyoutWPF.Classes.Coordinators;

public sealed record LockKeyFlyoutRequest(
    string ResourceKey,
    string FallbackText,
    bool IsOn,
    bool UsePressedText = false);
