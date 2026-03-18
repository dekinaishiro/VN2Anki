using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace VN2Anki.Models.State
{
    public partial class VnConnectionState : ObservableObject
    {
        [ObservableProperty] private string _displayVnTitle = "No Video Source";
        [ObservableProperty] private Brush _vnTitleColor = Brushes.Crimson;
        
        [ObservableProperty] private string _videoIconKind = "MonitorOff";
        [ObservableProperty] private Brush _videoIconColor = Brushes.Crimson;
        
        [ObservableProperty] private string _audioIconKind = "VolumeOff";
        [ObservableProperty] private Brush _audioIconColor = Brushes.Crimson;
        
        [ObservableProperty] private string _linkIconKind = "LinkVariantOff";
        [ObservableProperty] private Brush _linkIconColor = Brushes.White;

    }
}
