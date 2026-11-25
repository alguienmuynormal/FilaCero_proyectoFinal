using System.Windows;

namespace FilaCero;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        btnEntrar.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            try
            {
                await Db.InicializarAsync();   // código de conexión original, intacto
                await Auth.AsegurarAsync();
                btnEntrar.IsEnabled = true;
                txtUsuario.Focus();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error de MariaDB", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
    }

    private async void btnEntrar_Click(object sender, RoutedEventArgs e)
    {
        lblError.Text = "";
        try
        {
            if (await Auth.ValidarAsync(txtUsuario.Text.Trim(), txtClave.Password))
            {
                var main = new MainWindow();
                Application.Current.MainWindow = main;
                main.Show();
                Close();
            }
            else { lblError.Text = "Usuario o contraseña incorrectos."; txtClave.Clear(); }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Error de MariaDB", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
