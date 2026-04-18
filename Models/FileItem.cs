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

    public string TypeIcon => IsDirectory ? "FOLDER" : "FILE";
}
