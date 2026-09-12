using Microsoft.EntityFrameworkCore;

namespace WebApplication4.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Document> Documents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.Entity<Document>().ToTable("Documents");
        base.OnModelCreating(modelBuilder);
    }
}