using Avalonia.Controls;
using DatabaseMigrator.ViewModels;
namespace DatabaseMigrator.Views;
public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); DataContext = new MainWindowViewModel(); }
}
