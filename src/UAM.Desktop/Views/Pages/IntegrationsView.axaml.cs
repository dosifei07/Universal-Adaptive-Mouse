using Avalonia.Controls;
using Avalonia.Input;
using UAM.Desktop.ViewModels;

namespace UAM.Desktop.Views.Pages;

public partial class IntegrationsView : UserControl
{
    public IntegrationsView()
    {
        InitializeComponent();
    }

    private void OnLabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not IntegrationsViewModel viewModel ||
            sender is not Control surface)
        {
            return;
        }

        var point = e.GetPosition(surface);
        viewModel.Lab.RecordClick(point.X, point.Y);
        e.Handled = true;
    }
}
