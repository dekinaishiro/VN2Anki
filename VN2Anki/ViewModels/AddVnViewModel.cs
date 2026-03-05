using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using VN2Anki.Data;
using VN2Anki.Models;
using VN2Anki.Models.Entities;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public class WindowDisplayItem
    {
        public VideoEngine.VideoWindowItem BaseItem { get; set; }
        public bool IsRegistered { get; set; }
        public VisualNovel MatchedVn { get; set; }
        public string DisplayName => IsRegistered ? $"📚 {BaseItem.DisplayName}" : BaseItem.DisplayName;
    }

    public partial class AddVnViewModel : ObservableObject
    {
        private readonly IVnDatabaseService _dbService;
        private readonly VideoEngine _videoEngine;
        private readonly VndbService _vndbService;

        public bool IsOpenedFromLibrary { get; set; }

        [ObservableProperty]
        private ObservableCollection<WindowDisplayItem> _openWindows = new();

        [ObservableProperty]
        private WindowDisplayItem _selectedWindow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasThumbnail))]
        private BitmapImage _windowThumbnail;
        public bool HasThumbnail => WindowThumbnail != null;

        [ObservableProperty]
        private string _searchQuery = "";

        [ObservableProperty]
        private ObservableCollection<VndbResult> _searchResults = new();

        [ObservableProperty]
        private VndbResult _selectedVndbResult;

        [ObservableProperty]
        private bool _isLoading = false;

        // Propriedades Dinâmicas de UI
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVndbSearchEnabled))]
        private bool _isProcessAlreadyRegistered;

        public bool IsVndbSearchEnabled => !IsProcessAlreadyRegistered;

        [ObservableProperty]
        private string _actionButtonText = "Confirmar e Guardar";

        [ObservableProperty]
        private Visibility _registeredWarningVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private string _registeredWarningText = "";

        private readonly IWindowService _windowService;

        public AddVnViewModel(IVnDatabaseService dbService, VideoEngine videoEngine, VndbService vndbService, IWindowService windowService)
        {
            _dbService = dbService;
            _videoEngine = videoEngine;
            _vndbService = vndbService;
            _windowService = windowService;

            _ = LoadWindowsAsync();
        }

        public async Task LoadWindowsAsync()
        {
            var windows = _videoEngine.GetWindows();
            var vns = await _dbService.GetAllVisualNovelsAsync();

            var displayItems = new System.Collections.Generic.List<WindowDisplayItem>();

            foreach (var win in windows)
            {
                var match = vns.FirstOrDefault(v =>
                    (!string.IsNullOrEmpty(v.ExecutablePath) && !string.IsNullOrEmpty(win.ExecutablePath) && v.ExecutablePath == win.ExecutablePath) ||
                    (!string.IsNullOrEmpty(v.ProcessName) && v.ProcessName == win.ProcessName));

                displayItems.Add(new WindowDisplayItem
                {
                    BaseItem = win,
                    IsRegistered = match != null,
                    MatchedVn = match
                });
            }

            // sort by registration status first, then alphabetically
            var sortedItems = displayItems.OrderByDescending(x => x.IsRegistered).ThenBy(x => x.BaseItem.DisplayName);

            System.Windows.Application.Current.Dispatcher.Invoke(() => 
            {
                OpenWindows.Clear();
                foreach (var item in sortedItems) OpenWindows.Add(item);
            });
        }

        partial void OnSelectedWindowChanged(WindowDisplayItem value)
        {
            if (value == null) return;

            IsProcessAlreadyRegistered = value.IsRegistered;

            if (IsProcessAlreadyRegistered)
            {
                RegisteredWarningVisibility = Visibility.Visible;
                if (IsOpenedFromLibrary)
                {
                    RegisteredWarningText = $"⚠️ Este executável já está registrado como '{value.MatchedVn?.Title}'.";
                    ActionButtonText = "Processo Já Registrado";
                }
                else
                {
                    RegisteredWarningText = $"📚 Jogo identificado: {value.MatchedVn?.Title}";
                    ActionButtonText = "Vincular Sessão Atual";
                }
                SearchQuery = string.Empty;
                SearchResults.Clear();
            }
            else
            {
                RegisteredWarningVisibility = Visibility.Collapsed;
                ActionButtonText = "Confirmar e Guardar";
                if (!string.IsNullOrWhiteSpace(value.BaseItem.Title))
                {
                    SearchQuery = value.BaseItem.Title;
                    _ = SearchVndbAsync();
                }
            }

            // Thumbnail
            var imgBytes = _videoEngine.CaptureWindow(value.BaseItem.ProcessName, 400);
            WindowThumbnail = VN2Anki.Helpers.ImageHelper.BytesToBitmap(imgBytes);
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
            if (SelectedWindow == null) return;

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
                ProcessName = SelectedWindow.BaseItem.ProcessName,
                ExecutablePath = SelectedWindow.BaseItem.ExecutablePath,
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