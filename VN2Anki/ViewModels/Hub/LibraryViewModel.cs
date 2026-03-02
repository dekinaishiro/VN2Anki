using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VN2Anki.Data;
using VN2Anki.Models.Entities;

namespace VN2Anki.ViewModels.Hub
{
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly AppDbContext _db;

        [ObservableProperty]
        private ObservableCollection<VisualNovel> _visualNovels = new();

        [ObservableProperty]
        private string _newVnTitle = "";

        public LibraryViewModel(AppDbContext dbContext)
        {
            _db = dbContext;
            LoadLibrary();
        }

        public void LoadLibrary()
        {
            VisualNovels.Clear();
            var vns = _db.VisualNovels.ToList();
            foreach (var vn in vns) VisualNovels.Add(vn);
        }

        [RelayCommand]
        private void AddVn()
        {
            if (string.IsNullOrWhiteSpace(NewVnTitle)) return;

            var vn = new VisualNovel { Title = NewVnTitle, ProcessName = "Executável" };
            _db.VisualNovels.Add(vn);
            _db.SaveChanges();

            VisualNovels.Add(vn);
            NewVnTitle = "";
        }

        [RelayCommand]
        private void DeleteVn(VisualNovel vn)
        {
            if (vn == null) return;
            _db.VisualNovels.Remove(vn);
            _db.SaveChanges();
            VisualNovels.Remove(vn);
        }
    }
}