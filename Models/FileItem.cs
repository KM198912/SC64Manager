using CommunityToolkit.Mvvm.ComponentModel;

namespace NetGui.Models;

public partial class FileItem : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SizeDisplay { get; set; } = string.Empty;

    public long Size { get; set; }
    public bool IsDirectory { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string? RomId { get; set; }

    [ObservableProperty]
    public partial string? BoxArtPath { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial string? DescriptionPath { get; set; }

    [ObservableProperty]
    public partial string? ScrapedTitle { get; set; }

    public string TypeIcon => IsDirectory ? "FOLDER" : "FILE";
}
