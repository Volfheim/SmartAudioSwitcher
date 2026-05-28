using System.Windows;

namespace SmartAudioSwitcher.UI;

public static class AppDialog
{
    public static void ShowInfo(Window? owner, string title, string message)
    {
        Show(owner, title, message, DialogVisualStyle.Info);
    }

    public static void ShowWarning(Window? owner, string title, string message)
    {
        Show(owner, title, message, DialogVisualStyle.Warning);
    }

    public static void ShowError(Window? owner, string title, string message)
    {
        Show(owner, title, message, DialogVisualStyle.Error);
    }

    private static void Show(Window? owner, string title, string message, DialogVisualStyle style)
    {
        var dialog = new StyledMessageDialog(title, message, style);

        if (owner != null)
        {
            dialog.Owner = owner;
        }

        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
    }
}
