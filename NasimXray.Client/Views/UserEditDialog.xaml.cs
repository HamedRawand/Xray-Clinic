using System.Windows;
using NasimXray.Client.Services;
using NasimXray.Shared.Dtos.Roles;
using NasimXray.Shared.Dtos.Users;
using NasimXray.Shared.Enums;

namespace NasimXray.Client.Views;

public partial class UserEditDialog : Window
{
    private readonly ApiClient _apiClient;
    private readonly int? _userId;

    private record RoleOption(UserRole Value, string Label);
    private record CustomRoleOption(int? Id, string Name);

    public UserEditDialog(ApiClient apiClient, UserDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _userId = existing?.Id;

        RoleBox.ItemsSource = Enum.GetValues<UserRole>()
            .Select(r => new RoleOption(r, (string)FindResource($"Role.{r}")))
            .ToList();
        RoleBox.DisplayMemberPath = nameof(RoleOption.Label);

        _ = LoadCustomRolesAsync(existing?.CustomRoleId);

        if (existing is not null)
        {
            TitleText.Text = (string)FindResource("UserEdit.EditTitle");
            Title = TitleText.Text;
            UsernameBox.Text = existing.Username;
            UsernameBox.IsEnabled = false;
            PasswordPanel.Visibility = Visibility.Collapsed;
            FullNameBox.Text = existing.FullName;
            RoleBox.SelectedItem = ((List<RoleOption>)RoleBox.ItemsSource).First(r => r.Value == existing.Role);
            ActiveBox.IsChecked = existing.IsActive;
        }
        else
        {
            TitleText.Text = (string)FindResource("UserEdit.NewTitle");
            Title = TitleText.Text;
            RoleBox.SelectedIndex = 0;
            ActiveBox.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadCustomRolesAsync(int? selectedId)
    {
        try
        {
            var roles = await _apiClient.GetCustomRolesAsync();
            var options = new List<CustomRoleOption> { new(null, (string)FindResource("Users.None")) };
            options.AddRange(roles.Select(r => new CustomRoleOption(r.Id, r.Name)));
            CustomRoleBox.ItemsSource = options;
            CustomRoleBox.DisplayMemberPath = nameof(CustomRoleOption.Name);
            CustomRoleBox.SelectedItem = options.FirstOrDefault(o => o.Id == selectedId) ?? options[0];
        }
        catch (ApiException ex)
        {
            DialogHelper.ShowApiError(this, ex);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(FullNameBox.Text))
        {
            ShowError((string)FindResource("UserEdit.NameRequired"));
            return;
        }

        var role = ((RoleOption)RoleBox.SelectedItem).Value;
        var customRoleId = ((CustomRoleOption)CustomRoleBox.SelectedItem).Id;

        try
        {
            if (_userId.HasValue)
            {
                await _apiClient.UpdateUserAsync(_userId.Value, new UserUpdateRequest
                {
                    FullName = FullNameBox.Text.Trim(),
                    Role = role,
                    CustomRoleId = customRoleId,
                    IsActive = ActiveBox.IsChecked == true,
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(UsernameBox.Text))
                {
                    ShowError((string)FindResource("UserEdit.UsernameRequired"));
                    return;
                }
                if (PasswordBox.Password.Length < 8)
                {
                    ShowError((string)FindResource("UserEdit.PasswordTooShort"));
                    return;
                }

                await _apiClient.CreateUserAsync(new UserCreateRequest
                {
                    Username = UsernameBox.Text.Trim(),
                    Password = PasswordBox.Password,
                    FullName = FullNameBox.Text.Trim(),
                    Role = role,
                    CustomRoleId = customRoleId,
                });
            }

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
