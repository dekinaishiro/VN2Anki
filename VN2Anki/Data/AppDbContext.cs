using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using VN2Anki.Models.Entities;

namespace VN2Anki.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<VisualNovel> VisualNovels { get; set; }
        public DbSet<SessionRecord> Sessions { get; set; }

        public string DbPath { get; }

        public AppDbContext()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VN2Anki");
            Directory.CreateDirectory(folder);
            DbPath = Path.Combine(folder, "library.db");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={DbPath}");
    }
}