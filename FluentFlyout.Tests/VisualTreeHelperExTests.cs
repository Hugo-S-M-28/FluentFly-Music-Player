using FluentFlyoutWPF.Classes.Utils;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Xunit;

namespace FluentFlyout.Tests;

public class VisualTreeHelperExTests
{
    [Fact]
    public void FindAncestor_FromRunInsideScrollViewer_ReturnsScrollViewer()
    {
        RunInSta(() =>
        {
            var run = new Run("Settings link");
            var textBlock = new TextBlock();
            textBlock.Inlines.Add(run);
            var scrollViewer = new ScrollViewer
            {
                Content = textBlock
            };

            scrollViewer.ApplyTemplate();
            textBlock.ApplyTemplate();

            var ancestor = VisualTreeHelperEx.FindAncestor<ScrollViewer>(run);

            Assert.Same(scrollViewer, ancestor);
        });
    }

    [Fact]
    public void FindAncestor_FromVisualChild_ReturnsVisualAncestor()
    {
        RunInSta(() =>
        {
            var textBlock = new TextBlock();
            var button = new Button
            {
                Content = textBlock
            };

            button.ApplyTemplate();
            textBlock.ApplyTemplate();

            var ancestor = VisualTreeHelperEx.FindAncestor<Button>(textBlock);

            Assert.Same(button, ancestor);
        });
    }

    [Fact]
    public void FindAncestor_Null_ReturnsNull()
    {
        Assert.Null(VisualTreeHelperEx.FindAncestor<Grid>(null));
    }

    [Fact]
    public void FindAncestor_DetachedRun_ReturnsNull()
    {
        RunInSta(() =>
        {
            var run = new Run("Detached");

            var ancestor = VisualTreeHelperEx.FindAncestor<ScrollViewer>(run);

            Assert.Null(ancestor);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw exception;
        }
    }
}
