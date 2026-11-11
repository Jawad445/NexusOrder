using MassTransit;
using Microsoft.EntityFrameworkCore;
using NexusOrder.Ordering.Domain;

namespace NexusOrder.Ordering.Infrastructure.Data;

public sealed class OrderingDbContext : DbContext
{
    public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.TotalAmount).HasPrecision(18, 2);
            b.Property(o => o.Status).HasConversion<string>();

            b.HasMany(o => o.Items)
             .WithOne()
             .HasForeignKey("OrderId")
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.HasKey(i => i.Id);
            b.Property(i => i.UnitPrice).HasPrecision(18, 2);
        });

        // Configures MassTransit Transactional Outbox & Inbox tables in PostgreSQL
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}