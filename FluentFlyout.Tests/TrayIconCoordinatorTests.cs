using System.Windows;
using System.Windows.Media;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Coordinators;
using FluentFlyoutWPF.ViewModels;
using Xunit;

namespace FluentFlyout.Tests;

public class TrayIconCoordinatorTests
{
    [Fact]
    public async Task RecreateTrayIconAsync_HideSettingUnregistersAndClearsIcon()
    {
        SettingsManager.Current = new UserSettings { NIconHide = true };
        var icon = new FakeTrayNotifyIcon { IsRegistered = true, Icon = CreateImage() };
        var coordinator = new TrayIconCoordinator(_ => CreateImage());

        await coordinator.RecreateTrayIconAsync(icon);

        Assert.False(icon.IsRegistered);
        Assert.Null(icon.Icon);
        Assert.Equal(1, icon.UnregisterCalls);
        Assert.Equal(0, icon.RegisterCalls);
    }

    [Fact]
    public async Task RecreateTrayIconAsync_RetriesWithDefaultIconWhenSymbolRegistrationFails()
    {
        SettingsManager.Current = new UserSettings
        {
            NIconHide = false,
            NIconSymbol = true
        };

        var usedResources = new List<string>();
        var icon = new FakeTrayNotifyIcon
        {
            RegisterBehavior = fake =>
            {
                fake.IsRegistered = fake.RegisterCalls >= 2;
            }
        };
        var coordinator = new TrayIconCoordinator(resource =>
        {
            usedResources.Add(resource);
            return CreateImage();
        });

        await coordinator.RecreateTrayIconAsync(icon, maxAttempts: 2, delayMs: 0);

        Assert.True(icon.IsRegistered);
        Assert.Equal(2, icon.RegisterCalls);
        Assert.Equal(2, usedResources.Count);
        Assert.Contains("TrayIcons", usedResources[0]);
        Assert.EndsWith("Resources/FluentFlyout2.ico", usedResources[1]);
    }

    [Fact]
    public async Task RecreateTrayIconAsync_AlreadyRegisteredIconIsUnregisteredBeforeRegistering()
    {
        SettingsManager.Current = new UserSettings { NIconHide = false };
        var icon = new FakeTrayNotifyIcon { IsRegistered = true };
        var coordinator = new TrayIconCoordinator(_ => CreateImage());

        await coordinator.RecreateTrayIconAsync(icon, maxAttempts: 1, delayMs: 0);

        Assert.True(icon.IsRegistered);
        Assert.Equal(1, icon.UnregisterCalls);
        Assert.Equal(1, icon.RegisterCalls);
        Assert.Equal(Visibility.Visible, icon.Visibility);
        Assert.Equal("FluentFlyout", icon.TooltipText);
    }

    private static ImageSource CreateImage()
    {
        var image = new DrawingImage();
        image.Freeze();
        return image;
    }

    private sealed class FakeTrayNotifyIcon : ITrayNotifyIcon
    {
        public int RegisterCalls { get; private set; }
        public int UnregisterCalls { get; private set; }
        public Action<FakeTrayNotifyIcon>? RegisterBehavior { get; set; }

        public bool IsRegistered { get; set; }
        public ImageSource? Icon { get; set; }
        public string TooltipText { get; set; } = string.Empty;
        public Visibility Visibility { get; set; } = Visibility.Collapsed;

        public void Register()
        {
            RegisterCalls++;
            if (RegisterBehavior != null)
            {
                RegisterBehavior(this);
            }
            else
            {
                IsRegistered = true;
            }
        }

        public void Unregister()
        {
            UnregisterCalls++;
            IsRegistered = false;
        }
    }
}
