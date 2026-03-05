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

        public async Task<List<VisualNovel>> GetAllVisualNovelsAsync()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.VisualNovels.ToListAsync();
            }
        }

        public async Task AddVisualNovelAsync(VisualNovel vn)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.VisualNovels.Add(vn);
                await db.SaveChangesAsync();
            }
        }

        public async Task UpdateVisualNovelAsync(VisualNovel vn)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.VisualNovels.Update(vn);
                await db.SaveChangesAsync();
            }
        }

        public async Task DeleteVisualNovelAsync(VisualNovel vn)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.VisualNovels.Remove(vn);
                await db.SaveChangesAsync();
            }
        }

        public async Task<bool> ExistsByVndbIdAsync(string vndbId)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.VisualNovels.AnyAsync(v => v.VndbId == vndbId);
            }
        }

        public async Task<List<SessionRecord>> GetAllSessionsAsync()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return await db.Sessions.Include(s => s.VisualNovel).OrderByDescending(s => s.EndTime).ToListAsync();
            }
        }

        public async Task AddSessionAsync(SessionRecord session)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Sessions.Add(session);
                await db.SaveChangesAsync();
                
                if (session.VisualNovelId.HasValue)
                {
                    await RecalculateVnStatsAsync(db, session.VisualNovelId.Value);
                }
            }
        }

        public async Task DeleteSessionAsync(SessionRecord session)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Sessions.Remove(session);
                await db.SaveChangesAsync();
                
                if (session.VisualNovelId.HasValue)
                {
                    await RecalculateVnStatsAsync(db, session.VisualNovelId.Value);
                }
            }
        }

        private async Task RecalculateVnStatsAsync(AppDbContext db, int vnId)
        {
            var vn = await db.VisualNovels.Include(v => v.Sessions).FirstOrDefaultAsync(v => v.Id == vnId);
            if (vn != null)
            {
                vn.TotalTimePlayedSeconds = vn.Sessions.Sum(s => s.DurationSeconds);
                vn.TotalCharactersRead = vn.Sessions.Sum(s => s.CharactersRead);
                await db.SaveChangesAsync();
            }
        }
    }
}