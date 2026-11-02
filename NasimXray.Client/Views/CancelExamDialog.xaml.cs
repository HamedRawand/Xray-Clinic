using System.Windows;

namespace NasimXray.Client.Views;

// Collects the (required) reason for cancelling an exam; the caller sends it to the API.
public partial class CancelExamDialog : Window
{
    public string Reason { get; private set; } = string.Empty;

    public CancelExamDialog(string examDescription)
    {
        InitializeComponent();
        ExamText.Text = examDescription;
        Loaded += (_, _) => ReasonBox.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        var reason = ReasonBox.Text.Trim();
        if (reason.Length == 0)
        {
            ErrorText.Text = (string)FindResource("CancelExam.ReasonRequired");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Reason = reason;
        DialogResult = true;
        Close();
    }
}
