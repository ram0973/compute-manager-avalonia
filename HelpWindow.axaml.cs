using Avalonia.Controls;
using Avalonia.Interactivity;
using ComputerManager.Services;

namespace ComputerManager;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WindowsTitleBar.Apply(this);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
