using Microsoft.EntityFrameworkCore;

namespace FalazaLodge.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BookingRequest> BookingRequests => Set<BookingRequest>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
        });

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
        });
    }
}
