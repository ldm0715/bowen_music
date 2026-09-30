using Bodian.Core.Models;
using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Bodian.WinUI.Views;

/// <summary>搜索页。</summary>
public sealed partial class SearchPage : Page
{
    public SearchPage(SearchViewModel viewModel)
    {
        ViewModel = viewModel;

        InitializeComponent();
    }

    public SearchViewModel ViewModel { get; }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            ViewModel.PlayCommand.Execute(track);
        }
    }

    private void OnKeywordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;

        if (ViewModel.SearchCommand.CanExecute(null))
        {
            ViewModel.SearchCommand.Execute(null);
        }
    }
}
