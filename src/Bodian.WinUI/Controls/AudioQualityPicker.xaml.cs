using Bodian.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Bodian.WinUI.Controls;

public sealed partial class AudioQualityPicker : UserControl
{
    public AudioQualityPicker() => InitializeComponent();

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(PlayerViewModel), typeof(AudioQualityPicker),
        new PropertyMetadata(null, (sender, _) => ((AudioQualityPicker)sender).Bindings.Update()));

    public PlayerViewModel ViewModel
    {
        get => (PlayerViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
    public event EventHandler? SelectionRequested;

    private void OnQualityContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs e)
    {
        if (!e.InRecycleQueue && e.Item is AudioQualityOption option)
        {
            e.ItemContainer.IsEnabled = option.IsEnabled;
            AutomationProperties.SetName(e.ItemContainer, option.Name);
        }
    }

    private async void OnQualityClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AudioQualityOption { IsEnabled: true } option)
        {
            SelectionRequested?.Invoke(this, EventArgs.Empty);
            await ViewModel.SwitchQualityAsync(option.Quality);
        }
    }
}
