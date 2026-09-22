using System.Windows;
using System.Windows.Controls;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Roles;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

public partial class CustomRoleEditDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly int? _roleId;
    private readonly Dictionary<Permission, CheckBox> _checkboxes = [];

    public CustomRoleEditDialog(ApiClient apiClient, CustomRoleDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _roleId = existing?.Id;

        var selected = existing?.Permissions.ToHashSet() ?? [];
        foreach (var permission in Enum.GetValues<Permission>())
        {
            var checkBox = new CheckBox
            {
                Content = permission.ToString(),
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = selected.Contains(permission),
            };
            _checkboxes[permission] = checkBox;
            PermissionsPanel.Items.Add(checkBox);
        }

        if (existing is not null)
        {
            Title = existing.Name;
            NameBox.Text = existing.Name;
            DescriptionBox.Text = existing.Description;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError((string)FindResource("CustomRoles.NameRequired"));
            return;
        }

        var request = new CustomRoleUpsertRequest
        {
            Name = NameBox.Text.Trim(),
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
            Permissions = _checkboxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList(),
        };

        try
        {
            if (_roleId.HasValue)
                await _apiClient.UpdateCustomRoleAsync(_roleId.Value, request);
            else
                await _apiClient.CreateCustomRoleAsync(request);

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
