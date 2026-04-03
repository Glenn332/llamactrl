using LlamaCtrl.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Instance> Instances => Set<Instance>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<BenchmarkResult> BenchmarkResults => Set<BenchmarkResult>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<LlamaServerBinary> LlamaServerBinaries => Set<LlamaServerBinary>();
    public DbSet<ModelDirectory> ModelDirectories => Set<ModelDirectory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Instance>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.Profile)
             .WithMany(p => p.Instances)
             .HasForeignKey(x => x.ProfileId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Profile>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ModelPath).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.ParametersJson).HasColumnType("TEXT");
            e.Property(x => x.CustomArgsJson).HasColumnType("TEXT");
        });

        modelBuilder.Entity<BenchmarkResult>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Profile)
             .WithMany(p => p.BenchmarkResults)
             .HasForeignKey(x => x.ProfileId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });
    }
}
