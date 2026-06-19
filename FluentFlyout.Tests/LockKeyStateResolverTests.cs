using FluentFlyoutWPF.Classes.Coordinators;
using Xunit;

namespace FluentFlyout.Tests;

public class LockKeyStateResolverTests
{
    [Theory]
    [InlineData(LockKeyStateResolver.CapsLockVirtualKey, "LockWindow_CapsLock", "Caps Lock")]
    [InlineData(LockKeyStateResolver.NumLockVirtualKey, "LockWindow_NumLock", "Num Lock")]
    [InlineData(LockKeyStateResolver.ScrollLockVirtualKey, "LockWindow_ScrollLock", "Scroll Lock")]
    public void Resolve_ToggleLockKeys_ReturnsRequestWithToggleState(int vkCode, string expectedResourceKey, string expectedFallback)
    {
        var request = LockKeyStateResolver.Resolve(
            vkCode,
            insertEnabled: false,
            isCapsLockOn: () => true,
            isNumLockOn: () => true,
            isScrollLockOn: () => true);

        Assert.NotNull(request);
        Assert.Equal(expectedResourceKey, request.ResourceKey);
        Assert.Equal(expectedFallback, request.FallbackText);
        Assert.True(request.IsOn);
        Assert.False(request.UsePressedText);
    }

    [Fact]
    public void Resolve_InsertEnabled_ReturnsMomentaryPressedRequest()
    {
        var request = LockKeyStateResolver.Resolve(
            LockKeyStateResolver.InsertVirtualKey,
            insertEnabled: true,
            isCapsLockOn: () => false,
            isNumLockOn: () => false,
            isScrollLockOn: () => false);

        Assert.NotNull(request);
        Assert.Equal("LockWindow_InsertPressed", request.ResourceKey);
        Assert.Equal("Insert pressed", request.FallbackText);
        Assert.True(request.IsOn);
        Assert.True(request.UsePressedText);
    }

    [Fact]
    public void Resolve_InsertDisabled_ReturnsNull()
    {
        var request = LockKeyStateResolver.Resolve(
            LockKeyStateResolver.InsertVirtualKey,
            insertEnabled: false,
            isCapsLockOn: () => true,
            isNumLockOn: () => true,
            isScrollLockOn: () => true);

        Assert.Null(request);
    }

    [Fact]
    public void Resolve_UnknownKey_ReturnsNull()
    {
        var request = LockKeyStateResolver.Resolve(
            0xFF,
            insertEnabled: true,
            isCapsLockOn: () => true,
            isNumLockOn: () => true,
            isScrollLockOn: () => true);

        Assert.Null(request);
    }
}
