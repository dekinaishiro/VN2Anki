using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.DirectoryServices;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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

        [RelayCommand]
        private async Task SearchVndbAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            IsLoading = true;
            SearchResults.Clear();

            var results = await _vndbService.SearchVisualNovelAsync(SearchQuery);
            foreach (var r in results) SearchResults.Add(r);

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

            IsLoading = true;

            // dls cover image and get local path
            string coverPath = await _vndbService.DownloadCoverAsync(SelectedVndbResult.Image?.Url, SelectedVndbResult.Id);

            var vn = new VisualNovel
            {
                Title = SelectedVndbResult.Title,
                ProcessName = SelectedWindow.ProcessName,
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