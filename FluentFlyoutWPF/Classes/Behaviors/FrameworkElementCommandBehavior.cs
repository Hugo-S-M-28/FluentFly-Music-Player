using System.Windows;
using System.Windows.Input;

namespace FluentFlyoutWPF.Classes.Behaviors;

public static class FrameworkElementCommandBehavior
{
    public static readonly DependencyProperty LoadedCommandProperty =
        DependencyProperty.RegisterAttached(
            "LoadedCommand",
            typeof(ICommand),
            typeof(FrameworkElementCommandBehavior),
            new PropertyMetadata(null, OnLoadedCommandChanged));

    public static readonly DependencyProperty UnloadedCommandProperty =
        DependencyProperty.RegisterAttached(
            "UnloadedCommand",
            typeof(ICommand),
            typeof(FrameworkElementCommandBehavior),
            new PropertyMetadata(null, OnUnloadedCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.RegisterAttached(
            "CommandParameter",
            typeof(object),
            typeof(FrameworkElementCommandBehavior),
            new PropertyMetadata(null));

    public static ICommand? GetLoadedCommand(DependencyObject obj) => (ICommand?)obj.GetValue(LoadedCommandProperty);
    public static void SetLoadedCommand(DependencyObject obj, ICommand? value) => obj.SetValue(LoadedCommandProperty, value);
    public static ICommand? GetUnloadedCommand(DependencyObject obj) => (ICommand?)obj.GetValue(UnloadedCommandProperty);
    public static void SetUnloadedCommand(DependencyObject obj, ICommand? value) => obj.SetValue(UnloadedCommandProperty, value);
    public static object? GetCommandParameter(DependencyObject obj) => obj.GetValue(CommandParameterProperty);
    public static void SetCommandParameter(DependencyObject obj, object? value) => obj.SetValue(CommandParameterProperty, value);

    private static void OnLoadedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.Loaded -= OnLoaded;
            if (e.NewValue is ICommand)
            {
                element.Loaded += OnLoaded;
            }
        }
    }

    private static void OnUnloadedCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.Unloaded -= OnUnloaded;
            if (e.NewValue is ICommand)
            {
                element.Unloaded += OnUnloaded;
            }
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        ExecuteCommand(sender as DependencyObject, GetLoadedCommand);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ExecuteCommand(sender as DependencyObject, GetUnloadedCommand);
    }

    private static void ExecuteCommand(DependencyObject? target, Func<DependencyObject, ICommand?> resolver)
    {
        if (target == null)
        {
            return;
        }

        var command = resolver(target);
        var parameter = GetCommandParameter(target);
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }
}
