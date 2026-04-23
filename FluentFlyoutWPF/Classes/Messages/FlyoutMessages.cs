using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FluentFlyoutWPF.Classes.Messages;

public class ShowMediaFlyoutMessage
{
    public bool ToggleMode { get; }
    public bool ForceShow { get; }

    public ShowMediaFlyoutMessage(bool toggleMode = false, bool forceShow = false)
    {
        ToggleMode = toggleMode;
        ForceShow = forceShow;
    }
}

public class RecreateTaskbarWindowMessage { }

public class RecreateTrayIconMessage { }

public class ToggleBlurMessage { }

public class UpdateTaskbarMessage { }

public class UpdateUILayoutMessage { }

public class ReorderTaskbarWidgetControlsMessage { }

public class TrayIconStateMessage
{
    public bool Register { get; }
    public TrayIconStateMessage(bool register) => Register = register;
}

public class ApplyTaskbarWidgetThemeMessage { }

public class UpdateAccentColorMessage { }
