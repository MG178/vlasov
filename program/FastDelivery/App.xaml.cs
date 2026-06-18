using System.Windows;

namespace FastDelivery;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var login = new LoginWindow();
        MainWindow = login;
        login.Show();
    }
}
