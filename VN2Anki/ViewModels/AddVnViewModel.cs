using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using VN2Anki.Data;
using VN2Anki.Models;
using VN2Anki.Models.Entities;
using VN2Anki.Services;

namespace VN2Anki.ViewModels.Hub
{
    public partial class AddVnViewModel : ObservableObject
    {
        private readonly AppDbContext _db;
        private readonly VideoEngine _videoEngine;
        private readonly VndbService _vndbService;

        [ObservableProperty]
        private ObservableCollection<VideoEngine.VideoWindowItem> _openWindows = new();

        [ObservableProperty]
        private VideoEngine.VideoWindowItem _selectedWindow;

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

        public AddVnViewModel(AppDbContext db, VideoEngine videoEngine, VndbService vndbService)
        {
            _db = db;
            _videoEngine = videoEngine;
            _vndbService = vndbService;
            LoadWindows();
        }

        public void LoadWindows()
        {
            OpenWindows.Clear();
            var windows = _videoEngine.GetWindows();
            foreach (var win in windows) OpenWindows.Add(win);
        }

        partial void OnSelectedWindowChanged(VideoEngine.VideoWindowItem value)
        {
            if (value == null) return;

            var imgBytes = _videoEngine.CaptureWindow(value.ProcessName, 400);
            if (imgBytes != null && imgBytes.Length > 0)
            {
                try
                {
                    var image = new BitmapImage();
                    using (var mem = new MemoryStream(imgBytes))
                    {
                        mem.Position = 0;
                        image.BeginInit();
                        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = mem;
                        image.EndInit();
                    }
                    image.Freeze();
                    WindowThumbnail = image;
                }
                catch { WindowThumbnail = null; }
            }
            else
            {
                WindowThumbnail = null;
            }

            if (!string.IsNullOrWhiteSpace(value.Title))
            {
                SearchQuery = value.Title;
                _ = SearchVndbAsync();
            }
        }

        [RelayCommand]
        private async Task SearchVndbAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            IsLoading = true;
            SearchResults.Clear();

            var results = await _vndbService.SearchVisualNovelAsync(SearchQuery);
            foreach (var r in results) SearchResults.Add(r);

            if (SearchResults.Count > 0) SelectedVndbResult = SearchResults[0];

            IsLoading = false;
        }

        [RelayCommand]
        private async Task SaveAndCloseAsync(Window window)
        {
            if (SelectedWindow == null || SelectedVndbResult == null)
            {
                MessageBox.Show("Por favor, selecione a janela do jogo e um resultado do VNDB!", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool alreadyExists = _db.VisualNovels.Any(v => v.VndbId == SelectedVndbResult.Id);
            if (alreadyExists)
            {
                MessageBox.Show("Esta Visual Novel já está adicionada à sua Library!", "Duplicata", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsLoading = true;

            string coverPath = await _vndbService.DownloadCoverAsync(SelectedVndbResult.Image?.Url, SelectedVndbResult.Id);

            var vn = new VisualNovel
            {
                Title = SelectedVndbResult.Title,
                ProcessName = SelectedWindow.ProcessName,
                ExecutablePath = SelectedWindow.ExecutablePath,
                VndbId = SelectedVndbResult.Id,
                CoverImagePath = coverPath
            };

            _db.VisualNovels.Add(vn);
            await _db.SaveChangesAsync();

            IsLoading = false;
            window.DialogResult = true;
            window.Close();
        }
    }
}