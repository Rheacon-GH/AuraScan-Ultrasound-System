using AuraScan.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuraScan.Server.Data;

public class AuraScanDbContext : DbContext
{
    public AuraScanDbContext(DbContextOptions<AuraScanDbContext> options) : base(options) { }

    public DbSet<PatientEntity> Patients => Set<PatientEntity>();
    public DbSet<StudyEntity> Studies => Set<StudyEntity>();
    public DbSet<SeriesEntity> Series => Set<SeriesEntity>();
    public DbSet<ImageEntity> Images => Set<ImageEntity>();
    public DbSet<MeasurementEntity> Measurements => Set<MeasurementEntity>();
    public DbSet<SegmentationResultEntity> Segmentations => Set<SegmentationResultEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<DicomNodeEntity> DicomNodes => Set<DicomNodeEntity>();
    public DbSet<SystemConfigEntity> SystemConfigs => Set<SystemConfigEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Patient
        modelBuilder.Entity<PatientEntity>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.PatientId).IsUnique();
            e.Property(p => p.PatientName).HasMaxLength(256);
            e.Property(p => p.Sex).HasMaxLength(16);
        });

        // Study
        modelBuilder.Entity<StudyEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.StudyInstanceUid).IsUnique();
            e.HasOne(s => s.Patient)
                .WithMany(p => p.Studies)
                .HasForeignKey(s => s.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Series
        modelBuilder.Entity<SeriesEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.SeriesInstanceUid).IsUnique();
            e.HasOne(s => s.Study)
                .WithMany(st => st.Series)
                .HasForeignKey(s => s.StudyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Image
        modelBuilder.Entity<ImageEntity>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => i.SopInstanceUid).IsUnique();
            e.HasOne(i => i.Series)
                .WithMany(s => s.Images)
                .HasForeignKey(i => i.SeriesId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Measurement
        modelBuilder.Entity<MeasurementEntity>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasOne(m => m.Image)
                .WithMany(i => i.Measurements)
                .HasForeignKey(m => m.ImageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SegmentationResult
        modelBuilder.Entity<SegmentationResultEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Image)
                .WithMany(i => i.Segmentations)
                .HasForeignKey(s => s.ImageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuditLog
        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.TimestampUtc);
            e.HasIndex(a => a.Action);
        });

        // DicomNode
        modelBuilder.Entity<DicomNodeEntity>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => d.AeTitle);
        });

        // SystemConfig
        modelBuilder.Entity<SystemConfigEntity>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Key).IsUnique();
            e.Property(c => c.Key).HasMaxLength(256);
        });
    }
}
