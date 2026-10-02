using Microsoft.EntityFrameworkCore;
using Payment.Api.Models;

namespace Payment.Api.Data;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : base(options)
    {
        
    }

    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    
    public DbSet<IdempotencyRequest> IdempotencyRequests  => Set<IdempotencyRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentRecord>(entity =>
        {
            entity.Property(p => p.Amount)
                .HasPrecision(18, 2);
        });

        modelBuilder.Entity<IdempotencyRequest>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.IdempotencyKey)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(x => x.IdempotencyKey)
                .IsUnique();

            entity.Property(x => x.RequestHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(x => x.Status)
                .IsRequired();
            
            entity.Property(x => x.ResponseCode)
                .IsRequired(false);

            entity.Property(x => x.ResponseBody)
                .IsRequired(false);
            
            entity.Property(x => x.PaymentId)
                .IsRequired(false);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasOne<PaymentRecord>()
                .WithMany()
                .HasForeignKey(x => x.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}