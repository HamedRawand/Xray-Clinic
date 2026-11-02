using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Patients;

namespace NasimXray.Client.Views;

public partial class PatientsView : UserControl
{
    private const int PageSize = 50;

    private readonly ApiClient _apiClient;
    private int _page = 1;
    private int _requestSeq;
    private readonly DispatcherTimer _searchDebounce;

    public PatientsView(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        NewPatientButton.Visibility = SessionService.Can(NasimXray.Shared.Enums.Permission.ManagePatients) ? Visibility.Visible : Visibility.Collapsed;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += async (_, _) => { _searchDebounce.Stop(); _page = 1; await LoadAsync(); };

        Pager.PageRequested += async page => { _page = page; await LoadAsync(); };

        _ = LoadAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async Task LoadAsync()
    {
        var seq = ++_requestSeq;
        try
        {
            var result = await _apiClient.GetPatientsAsync(SearchBox.Text, _page, PageSize);
            if (seq != _requestSeq) return; // a newer request is already on its way

            _page = result.Page;
            Grid.ItemsSource = result.Items;
            EmptyText.Visibility = result.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Pager.Update(result);
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
        {
            _page = 1; // newest patients are listed first
            await LoadAsync();
        }
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
