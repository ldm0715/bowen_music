using Bowen.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Bowen.WinUI.Controls;

public sealed partial class AudioEffectsPanel : UserControl
{
    public AudioEffectsPanel() => InitializeComponent();

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(AudioEffectsViewModel),
        typeof(AudioEffectsPanel),
        new PropertyMetadata(null, (sender, _) => ((AudioEffectsPanel)sender).Bindings.Update()));

    public AudioEffectsViewModel ViewModel
    {
        get => (AudioEffectsViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void OnPresetClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AudioEffectPresetItem item)
        {
            ViewModel?.SelectPresetCommand.Execute(item);
        }
    }

    private void OnAmbientClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AmbientSoundItem item)
        {
            ViewModel?.SelectAmbientCommand.Execute(item);
            AmbientFlyout.Hide();
        }
    }
}
