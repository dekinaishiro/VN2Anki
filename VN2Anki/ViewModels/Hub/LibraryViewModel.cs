using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Linq;
using VN2Anki.Data;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using Microsoft.EntityFrameworkCore;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;

namespace VN2Anki.ViewModels.Hub
{
    public partial class LibraryViewModel : ObservableObject, IRecipient<SessionSavedMessage>
    {
        private readonly AppDbContext _db;

        [ObservableProperty]
        private ObservableCollection<VisualNovel> _visualNovels = new();

        [ObservableProperty]
        private ObservableCollection<SessionRecord> _sessionHistory = new();

        [ObservableProperty]
        private string _newVnTitle = "";

        public LibraryViewModel(AppDbContext dbContext)
        {
            _db = dbContext;
            LoadLibrary();
            LoadHistory();
            WeakReferenceMessenger.Default.Register(this);
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

        [RelayCommand]
        private void PlayVn(VisualNovel vn)
        {
            if (vn == null) return;

            WeakReferenceMessenger.Default.Send(new PlayVnMessage(vn));
        }

        // sessions history related methods

        public void LoadHistory()
        {
            SessionHistory.Clear();
            var sessions = _db.Sessions.Include(s => s.VisualNovel).OrderByDescending(s => s.EndTime).ToList();
            foreach (var s in sessions) SessionHistory.Add(s);
        }
        public void Receive(SessionSavedMessage message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => LoadHistory());
        }

        [RelayCommand]
        private void DeleteSession(SessionRecord session)
        {
            if (session == null) return;
            _db.Sessions.Remove(session);
            _db.SaveChanges();
            SessionHistory.Remove(session);
        }
    }
}