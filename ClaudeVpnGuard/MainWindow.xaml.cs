using ClaudeVpnGuard.ViewModels;

namespace ClaudeVpnGuard;

public partial class MainWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
