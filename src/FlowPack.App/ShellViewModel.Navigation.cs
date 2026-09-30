namespace FlowPack.App;

public sealed partial class ShellViewModel
{
    private int _libraryTabIndex;
    public int LibraryTabIndex
    {
        get => _libraryTabIndex;
        set
        {
            if (value is < 0 or > 2 || value == _libraryTabIndex) return;
            _libraryTabIndex = value;
            OnPropertyChanged();
        }
    }
}
