using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace FluentFlyoutWPF.Models;

public partial class Song : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _artist = string.Empty;

    [ObservableProperty]
    private string _album = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private ImageSource? _coverArt;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private int _trackNumber;

    [ObservableProperty]
    private string _genre = string.Empty;
}

public partial class Album : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _artist = string.Empty;

    [ObservableProperty]
    private ImageSource? _coverArt;

    [ObservableProperty]
    private int _year;

    public List<Song> Songs { get; set; } = new();
}
