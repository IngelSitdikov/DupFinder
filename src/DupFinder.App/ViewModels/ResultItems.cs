using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DupFinder.Core;

namespace DupFinder.App.ViewModels;

public sealed partial class FileItem(FileEntry entry, GroupItem group) : ObservableObject
{
    public FileEntry Entry { get; } = entry;
    public GroupItem Group { get; } = group;

    public string Path => Entry.Path;
    public string RootLabel => Entry.RootIndex == 0 ? "A" : "B";
    public DateTime LastWrite => Entry.LastWriteUtc.ToLocalTime();

    /// <summary>Отмечен к удалению.</summary>
    [ObservableProperty]
    private bool _isMarked;

    partial void OnIsMarkedChanged(bool value) => Group.OnMarksChanged();
}

public sealed class GroupItem : ObservableObject
{
    private readonly Action _marksChanged;

    public GroupItem(DuplicateGroup group, Action marksChanged)
    {
        _marksChanged = marksChanged;
        Size = group.Size;
        Hash = group.Hash;
        Files = new ObservableCollection<FileItem>(group.Files.Select(f => new FileItem(f, this)));
        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WastedBytes));
            OnPropertyChanged(nameof(Header));
        };
    }

    public long Size { get; }
    public string Hash { get; }
    public ObservableCollection<FileItem> Files { get; }

    /// <summary>Группа в нынешнем виде — без уже удалённых и заменённых ссылками копий.</summary>
    public DuplicateGroup ToGroup() => new(Size, Hash, Files.Select(f => f.Entry).ToList());

    public long WastedBytes => Size * Math.Max(0, Files.Count - 1);

    public string Header => string.Format(Strings.GroupHeader,
        Format.Count(Files.Count, Strings.CopyForms), Format.Bytes(Size), Format.Bytes(WastedBytes));

    public void OnMarksChanged() => _marksChanged();
}
