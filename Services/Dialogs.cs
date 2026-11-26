using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ComputerManager.Services;

public static class Dialogs
{
    public static async Task<bool> ConfirmAsync(Window owner, string title, string text,
        string yes = "Да", string no = "Отмена")
    {
        bool result = false;
        var dlg = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        dlg.Opened += (_, _) => WindowsTitleBar.Apply(dlg);

        var yesBtn = new Button { Content = yes, IsDefault = true, MinWidth = 90 };
        var noBtn = new Button { Content = no, IsCancel = true, MinWidth = 90 };
        yesBtn.Click += (_, _) => { result = true; dlg.Close(); };
        noBtn.Click += (_, _) => dlg.Close();

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { yesBtn, noBtn },
                },
            },
        };

        await dlg.ShowDialog(owner);
        return result;
    }
}
