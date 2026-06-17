using System.Windows;

namespace FastDelivery;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            var login = LoginBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                ErrorText.Text = "Введите логин и пароль.";
                return;
            }

            var user = FastDeliveryStore.Instance.Authenticate(login, password);

            if (user is null)
            {
                ErrorText.Text = "Неверный логин или пароль.";
                return;
            }

            Session.CurrentUser = user;
            Session.CurrentRoleName = user.Role.ToString();

            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка входа: {ex.Message}", "FastDelivery", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
