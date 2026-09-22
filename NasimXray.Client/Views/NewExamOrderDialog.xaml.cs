using System.Windows;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.ExamOrders;
using NasimXray.Shared.Dtos.ExamTypes;
using NasimXray.Shared.Dtos.ReferringPhysicians;

namespace NasimXray.Client.Views;

public partial class NewExamOrderDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _patientId;

    private record PhysicianOption(int? Id, string Name);

    public NewExamOrderDialog(ApiClient apiClient, int patientId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _patientId = patientId;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            ExamTypeBox.ItemsSource = await _apiClient.GetExamTypesAsync();

            var options = new List<PhysicianOption> { new(null, (string)FindResource("NewExamOrder.None")) };
            options.AddRange((await _apiClient.GetReferringPhysiciansAsync()).Select(p => new PhysicianOption(p.Id, p.Name)));
            ReferringPhysicianBox.ItemsSource = options;
            ReferringPhysicianBox.DisplayMemberPath = nameof(PhysicianOption.Name);
            ReferringPhysicianBox.SelectedIndex = 0;
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (ExamTypeBox.SelectedItem is not ExamTypeDto examType)
        {
            ShowError((string)FindResource("NewExamOrder.SelectExamType"));
            return;
        }

        var referringPhysician = ReferringPhysicianBox.SelectedItem as PhysicianOption;

        try
        {
            await _apiClient.CreateExamOrderAsync(new ExamOrderCreateRequest
            {
                PatientId = _patientId,
                ExamTypeId = examType.Id,
                ReferringPhysicianId = referringPhysician?.Id,
            });

            DialogResult = true;
            Close();
        }
        catch (ApiException ex)
        {
            ShowError((string)FindResource($"Api.{ex.Reason}"));
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
