using System.Windows;

namespace FlowPack.App;

public partial class NoticeDialog : Window
{
    public NoticeDialog(string message, string title)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        Loaded += (_, _) => CloseButton.Focus();
    }

    public static void ShowNotice(string message, string title)
    {
        var dialog = new NoticeDialog(message, title);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
        if (owner?.IsVisible == true)
        {
            dialog.Owner = owner;
            dialog.Width = Math.Min(dialog.Width, owner.ActualWidth - 48);
            dialog.MaxHeight = Math.Min(dialog.MaxHeight, owner.ActualHeight - 48);
        }
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
    }

    private void CloseNotice(object sender, RoutedEventArgs e) => Close();
}
