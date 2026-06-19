namespace FluentFlyoutWPF.Classes.Coordinators;

public static class LockKeyStateResolver
{
    public const int CapsLockVirtualKey = 0x14;
    public const int NumLockVirtualKey = 0x90;
    public const int ScrollLockVirtualKey = 0x91;
    public const int InsertVirtualKey = 0x2D;

    public static LockKeyFlyoutRequest? Resolve(
        int vkCode,
        bool insertEnabled,
        Func<bool> isCapsLockOn,
        Func<bool> isNumLockOn,
        Func<bool> isScrollLockOn)
    {
        return vkCode switch
        {
            CapsLockVirtualKey => new("LockWindow_CapsLock", "Caps Lock", isCapsLockOn()),
            NumLockVirtualKey => new("LockWindow_NumLock", "Num Lock", isNumLockOn()),
            ScrollLockVirtualKey => new("LockWindow_ScrollLock", "Scroll Lock", isScrollLockOn()),
            InsertVirtualKey when insertEnabled => new("LockWindow_InsertPressed", "Insert pressed", IsOn: true, UsePressedText: true),
            _ => null
        };
    }
}
