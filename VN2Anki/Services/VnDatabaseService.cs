using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VN2Anki.Data;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class VnDatabaseService : IVnDatabaseService
    {
        private readonly IServiceProvider _serviceProvider;

        public VnDatabaseService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        private async Task<T> ExecuteWithDbAsync<T>(Func<AppDbContext, Task<T>> action)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await action(db);
        }

        private async Task ExecuteWithDbAsync(Func<AppDbContext, Task> action)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await action(db);
        }

        public Task<List<VisualNovel>> GetAllVisualNovelsAsync() =>
            ExecuteWithDbAsync(db => db.VisualNovels.ToListAsync());

        public Task AddVisualNovelAsync(VisualNovel vn) =>
            ExecuteWithDbAsync(async db =>
            {
                db.VisualNovels.Add(vn);
                await db.SaveChangesAsync();
            });

        public Task UpdateVisualNovelAsync(VisualNovel vn) =>
            ExecuteWithDbAsync(async db =>
            {
                db.VisualNovels.Update(vn);
                await db.SaveChangesAsync();
            });

        public Task DeleteVisualNovelAsync(VisualNovel vn) =>
            ExecuteWithDbAsync(async db =>
            {
                var relatedSessions = await db.Sessions.Where(s => s.VisualNovelId == vn.Id).ToListAsync();
                if (relatedSessions.Any()) db.Sessions.RemoveRange(relatedSessions);

                db.VisualNovels.Remove(vn);
                await db.SaveChangesAsync();

                if (!string.IsNullOrEmpty(vn.CoverImagePath) && System.IO.File.Exists(vn.CoverImagePath))
                {
                    try { System.IO.File.Delete(vn.CoverImagePath); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Cleanup Error]: {ex.Message}"); }
                }
            });

        public Task<bool> ExistsByVndbIdAsync(string vndbId) =>
            ExecuteWithDbAsync(db => db.VisualNovels.AnyAsync(v => v.VndbId == vndbId));

        public Task<List<SessionRecord>> GetAllSessionsAsync() =>
            ExecuteWithDbAsync(db => db.Sessions.Include(s => s.VisualNovel).OrderByDescending(s => s.EndTime).ToListAsync());

        public Task AddSessionAsync(SessionRecord session) =>
            ExecuteWithDbAsync(async db =>
            {
                db.Sessions.Add(session);
                await db.SaveChangesAsync();
                
                if (session.VisualNovelId.HasValue)
                {
                    await RecalculateVnStatsAsync(db, session.VisualNovelId.Value);
                }
            });

        public Task UpdateSessionAsync(SessionRecord session) =>
            ExecuteWithDbAsync(async db =>
            {
                db.Sessions.Update(session);
                await db.SaveChangesAsync();
                
                if (session.VisualNovelId.HasValue)
                {
                    await RecalculateVnStatsAsync(db, session.VisualNovelId.Value);
                }
            });

        public Task DeleteSessionAsync(SessionRecord session) =>
            ExecuteWithDbAsync(async db =>
            {
                db.Sessions.Remove(session);
                await db.SaveChangesAsync();
                
                if (session.VisualNovelId.HasValue)
                {
                    await RecalculateVnStatsAsync(db, session.VisualNovelId.Value);
                }
            });

        private async Task RecalculateVnStatsAsync(AppDbContext db, int vnId)
        {
            var vn = await db.VisualNovels.Include(v => v.Sessions).FirstOrDefaultAsync(v => v.Id == vnId);
            if (vn != null)
            {
                vn.TotalTimePlayedSeconds = vn.Sessions.Sum(s => s.DurationSeconds);
                vn.EffectiveTimePlayedSeconds = vn.Sessions.Sum(s => s.EffectiveDurationSeconds);
                vn.TotalCharactersRead = vn.Sessions.Sum(s => s.CharactersRead);
                await db.SaveChangesAsync();
            }
        }
    }
}