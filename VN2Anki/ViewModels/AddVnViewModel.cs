using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using VN2Anki.Models;
using VN2Anki.Models.Entities;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public partial class AddVnViewModel : ObservableObject
    {
        private readonly IVnDatabaseService _dbService;
        private readonly VndbService _vndbService;
        private readonly IWindowService _windowService;
        private readonly IConfigurationService _configService;
        private readonly VideoEngine _videoEngine;

        public bool IsOpenedFromLibrary { get; set; }

        [ObservableProperty]
        private string _targetProcessName;

        [ObservableProperty]
        private string _targetExecutablePath;

        [ObservableProperty]
        private string _targetDisplayName;

        [ObservableProperty]
        private string _searchQuery = "";

        [ObservableProperty]
        private ObservableCollection<VndbResult> _searchResults = new();

        [ObservableProperty]
        private VndbResult _selectedVndbResult;

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVndbSearchEnabled))]
        private bool _isProcessAlreadyRegistered;

        public bool IsVndbSearchEnabled => !IsProcessAlreadyRegistered && !string.IsNullOrEmpty(TargetProcessName);

        [ObservableProperty]
        private string _actionButtonText = "Confirmar e Guardar";

        [ObservableProperty]
        private Visibility _registeredWarningVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private string _registeredWarningText = "";

        public AddVnViewModel(IVnDatabaseService dbService, VideoEngine videoEngine, VndbService vndbService, IWindowService windowService, IConfigurationService configService)
        {
            _dbService = dbService;
            _videoEngine = videoEngine;
            _vndbService = vndbService;
            _windowService = windowService;
            _configService = configService;

            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            TargetProcessName = _configService.CurrentConfig.Media.VideoWindow;
            
            if (string.IsNullOrEmpty(TargetProcessName))
            {
                TargetDisplayName = Locales.Strings.StatusVideoDisconnected; 
                ActionButtonText = Locales.Strings.BtnSaveClose;
                return;
            }

            // try to get more details
            var window = _videoEngine.GetWindows().FirstOrDefault(w => w.ProcessName == TargetProcessName);
            if (window != null)
            {
                TargetDisplayName = window.DisplayName;
                TargetExecutablePath = window.ExecutablePath;
                SearchQuery = window.Title;
            }
            else
            {
                TargetDisplayName = TargetProcessName;
                TargetExecutablePath = "";
            }

            var vns = await _dbService.GetAllVisualNovelsAsync();
            var match = vns.FirstOrDefault(v =>
                (!string.IsNullOrEmpty(v.ExecutablePath) && !string.IsNullOrEmpty(TargetExecutablePath) && v.ExecutablePath == TargetExecutablePath) ||
                (!string.IsNullOrEmpty(v.ProcessName) && v.ProcessName == TargetProcessName));

            IsProcessAlreadyRegistered = match != null;

            if (IsProcessAlreadyRegistered)
            {
                RegisteredWarningVisibility = Visibility.Visible;
                if (IsOpenedFromLibrary)
                {
                    RegisteredWarningText = string.Format(Locales.Strings.MsgExeAlreadyRegistered, match?.Title);
                    ActionButtonText = Locales.Strings.BtnProcessAlreadyRegistered;
                }
                else
                {
                    RegisteredWarningText = $"📚 Jogo identificado: {match?.Title}";
                    ActionButtonText = Locales.Strings.BtnBindCurrentSession;
                }
                SearchQuery = string.Empty;
                SearchResults.Clear();
            }
            else
            {
                RegisteredWarningVisibility = Visibility.Collapsed;
                ActionButtonText = Locales.Strings.BtnSaveClose;
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    _ = SearchVndbAsync();
                }
            }
        }

        [RelayCommand]
        private async Task SearchVndbAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery) || IsProcessAlreadyRegistered) return;

            IsLoading = true;
            SearchResults.Clear();
            var results = await _vndbService.SearchVisualNovelAsync(SearchQuery);
            foreach (var r in results) SearchResults.Add(r);
            if (SearchResults.Count > 0) SelectedVndbResult = SearchResults[0];
            IsLoading = false;
        }

        [RelayCommand]
        private async Task SaveAndCloseAsync()
        {
            if (string.IsNullOrEmpty(TargetProcessName))
            {
                 _windowService.CloseWindow(this, false); 
                 return;
            }

            // already in Library
            if (IsProcessAlreadyRegistered)
            {
                if (IsOpenedFromLibrary)
                {
                    _windowService.ShowWarning(Locales.Strings.MsgExeAlreadyInUse, Locales.Strings.MsgAttention);
                    return;
                }
                else
                {
                    // open through quick sync
                    _windowService.CloseWindow(this, true); 
                    return;
                }
            }

            // default behavior (new entry)
            if (SelectedVndbResult == null)
            {
                _windowService.ShowWarning(Locales.Strings.MsgSelectVndbResult, Locales.Strings.MsgAttention);
                return;
            }

            bool alreadyExists = await _dbService.ExistsByVndbIdAsync(SelectedVndbResult.Id);
            if (alreadyExists)
            {
                _windowService.ShowInformation(Locales.Strings.MsgVnAlreadyInLibrary, Locales.Strings.TitleDuplicate);
                return;
            }

            IsLoading = true;
            string coverPath = await _vndbService.DownloadCoverAsync(SelectedVndbResult.Image?.Url, SelectedVndbResult.Id);

            var vn = new VisualNovel
            {
                Title = SelectedVndbResult.Title,
                OriginalTitle = SelectedVndbResult.AltTitle,
                ProcessName = TargetProcessName,
                ExecutablePath = TargetExecutablePath,
                VndbId = SelectedVndbResult.Id,
                CoverImagePath = coverPath,
                CoverImageUrl = SelectedVndbResult.Image?.Url
            };

            await _dbService.AddVisualNovelAsync(vn);

            IsLoading = false;

            _windowService.CloseWindow(this, true); 
        }
    }
}
