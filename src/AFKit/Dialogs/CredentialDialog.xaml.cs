using System.Windows;
using AFKit.Services;

namespace AFKit.Dialogs;

public partial class CredentialDialog : Window
{
    public string UserName => UserBox.Text;
    public string Password => PassBox.Password;
    public CredentialDialog(string uri, string challenge)
    {
        InitializeComponent();
        SiteText.Text = Loc.T("Cred.Request", uri);
        ChallengeText.Text = challenge;
        Loaded += (_, _) => UserBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
