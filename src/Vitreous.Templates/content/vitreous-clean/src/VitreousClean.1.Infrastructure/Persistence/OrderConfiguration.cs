using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VitreousClean._1.Domain.Orders;

namespace VitreousClean._1.Infrastructure.Persistence;

// How the aggregate is stored. The mapping lives here, so the domain classes need no EF attributes.
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> order)
    {
        order.HasKey(o => o.Id);
        order.Property(o => o.Id).HasConversion(static id => id.Value, static value => new OrderId(value)).ValueGeneratedNever();
        order.Property(o => o.CustomerEmail).HasMaxLength(Order.MaxCustomerEmailLength);
        order.Property(o => o.PlacedAt);

        // A concurrency token: an update fails if the status changed since the order was read.
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();

        order.Ignore(o => o.Total);

        // The lines are part of the aggregate: always loaded with the order, and deleted with it.
        order.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        order.Navigation(o => o.Lines).AutoInclude();

        order.HasIndex(o => new { o.Status, o.PlacedAt });
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> line)
    {
        line.ToTable("OrderLines");

        // The line has no identity outside its order, so its key exists in the database only.
        line.Property<int>("Id");
        line.HasKey("Id");
        line.Property(l => l.Product).HasMaxLength(OrderLine.MaxProductLength);
        line.Property(l => l.Quantity);
        line.Ignore(l => l.Total);

        // A value object has no table of its own: its properties are columns of the line.
        line.ComplexProperty(l => l.UnitPrice, static price =>
        {
            price.Property(p => p.Amount).HasColumnName("UnitPrice").HasPrecision(18, 2);
            price.Property(p => p.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
    }
}
