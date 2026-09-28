using Microsoft.EntityFrameworkCore;

namespace FalazaLodge.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<BookingRequest> BookingRequests => Set<BookingRequest>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<BookingRequest>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EnquiryType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.GuestName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RoomPreference).HasMaxLength(200);
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.CheckIn);
            entity.HasIndex(x => new { x.RoomId, x.Status, x.CheckIn, x.CheckOut });
            entity.HasOne(x => x.Room)
                .WithMany(x => x.Bookings)
                .HasForeignKey(x => x.RoomId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AdminEmail).HasMaxLength(200);
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.AdminUser)
                .WithMany()
                .HasForeignKey(x => x.AdminUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ErrorLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TraceId).HasMaxLength(100);
            entity.Property(x => x.Method).HasMaxLength(20);
            entity.Property(x => x.Path).HasMaxLength(500);
            entity.Property(x => x.ExceptionType).HasMaxLength(300);
            entity.Property(x => x.Message).HasMaxLength(2000);
            entity.HasIndex(x => x.CreatedAtUtc);
        });
    }
}
