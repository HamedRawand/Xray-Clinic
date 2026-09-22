using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Patients;

namespace NasimXray.Client.Views;

public partial class PatientsView : UserControl
{
    private readonly ApiClient _apiClient;
    private readonly DispatcherTimer _searchDebounce;

    public PatientsView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) => { _searchDebounce.Stop(); await LoadAsync(); };

        _ = LoadAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async Task LoadAsync()
    {
        try
        {
            var patients = await _apiClient.GetPatientsAsync(SearchBox.Text);
            Grid.ItemsSource = patients;
            EmptyText.Visibility = patients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(Window.GetWindow(this), ex);
        }
    }

    private async void NewPatient_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PatientEditDialog(_apiClient) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            await LoadAsync();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is PatientDto patient)
            OpenPatientDetail(patient);
    }

    private void OpenPatient_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PatientDto patient)
            OpenPatientDetail(patient);
    }

    private void OpenPatientDetail(PatientDto patient)
    {
        var window = new PatientDetailWindow(_apiClient, patient.Id) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        _ = LoadAsync();
    }
}
