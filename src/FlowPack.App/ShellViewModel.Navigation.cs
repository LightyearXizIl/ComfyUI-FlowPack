namespace FlowPack.App;

public sealed partial class ShellViewModel
{
    private int _libraryTabIndex;
    private int _installTabIndex;
    private int _settingsTabIndex;
    private string _appearancePreferenceNotice = string.Empty;
    private string _repositoryNotice = string.Empty;
    private string _diagnosticsNotice = string.Empty;
    public string AppearancePreferenceNotice
    {
        get => _appearancePreferenceNotice;
        private set { _appearancePreferenceNotice = value; OnPropertyChanged(); }
    }
    public string RepositoryNotice
    {
        get => _repositoryNotice;
        private set { _repositoryNotice = value; OnPropertyChanged(); }
    }
    public string DiagnosticsNotice
    {
        get => _diagnosticsNotice;
        private set { _diagnosticsNotice = value; OnPropertyChanged(); }
    }
    public int SettingsTabIndex
    {
        get => _settingsTabIndex;
        set
        {
            if (value is < 0 or > 4 || value == _settingsTabIndex) return;
            _settingsTabIndex = value;
            OnPropertyChanged();
        }
    }
    public int LibraryTabIndex
    {
        get => _libraryTabIndex;
        set
        {
            if (value is < 0 or > 2 || value == _libraryTabIndex) return;
            _libraryTabIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPackagingContext));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public int InstallTabIndex
    {
        get => _installTabIndex;
        set
        {
            if (value is < 0 or > 1 || value == _installTabIndex) return;
            _installTabIndex = value;
            OnPropertyChanged();
        }
    }

    private void NavigateTo(string? route)
    {
        _applicationLog.Write(FlowPack.Infrastructure.LogSeverity.Detail, "ui.navigate", route);
        switch (route)
        {
            case "Tasks": return;
            case "Workflows": LibraryTabIndex = 0; CurrentPage = FlowPage.Library; return;
            case "Models": LibraryTabIndex = 1; CurrentPage = FlowPage.Library; return;
            case "Nodes": LibraryTabIndex = 2; CurrentPage = FlowPage.Library; return;
            case "Packaging":
            case "PackageWizard": BeginExportCommand.Execute(null); return;
            case "Packages": SelectedPackage = null; InstallTabIndex = 0; CurrentPage = FlowPage.Install; return;
            case "InstallPreview": InstallTabIndex = 0; CurrentPage = FlowPage.Install; return;
            case "Install": InstallTabIndex = 0; CurrentPage = FlowPage.Install; return;
            case "Appearance": CurrentPage = FlowPage.Settings; return;
        }
        if (Enum.TryParse<FlowPage>(route, out var page) && Enum.IsDefined(page)) CurrentPage = page;
    }
}
