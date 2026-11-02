using System.Windows;
using System.Windows.Controls;
using NasimXray.Shared.Dtos;

namespace NasimXray.Client.Views.Shared;

// Previous / Next with "Page 2 of 7 · 312 records". The host view loads a page and calls Update;
// when the user clicks a button the control raises PageRequested and the host loads that page.
public partial class PagerControl : UserControl
{
    private int _page = 1;
    private int _totalPages = 1;

    public event Action<int>? PageRequested;

    public PagerControl()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public void Update<T>(PagedResult<T> result) => Update(result.Page, result.TotalPages, result.TotalCount);

    public void Update(int page, int totalPages, int totalCount)
    {
        _page = page;
        _totalPages = Math.Max(1, totalPages);
        PageText.Text = string.Format((string)FindResource("Pager.Format"), _page, _totalPages, totalCount);
        PrevButton.IsEnabled = _page > 1;
        NextButton.IsEnabled = _page < _totalPages;

        // Nothing to page through when everything fits on one page.
        Visibility = _totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 1) PageRequested?.Invoke(_page - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page < _totalPages) PageRequested?.Invoke(_page + 1);
    }
}
