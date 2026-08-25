using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Interceptors;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>
/// Concrete DbContext used exclusively by the reference client.
/// The model deliberately avoids provider-specific schema features so the same model
/// can run unchanged on SQL Server, PostgreSQL and MySQL.
/// </summary>
public sealed class ClientDbContext(
    IOptions<KukulcanDatabaseOptions> options,
    Abstractions.ITenantContext tenantContext,
    KUKULCAN.SharedKernel.Abstractions.IClock clock,
    IDomainEventDispatcher domainEventDispatcher,
    SlowQueryInterceptor slowQueryInterceptor) :
    KukulcanDbContextBase(options, tenantContext, clock, domainEventDispatcher)
{
    public DbSet<ClientProduct> Products => Set<ClientProduct>();
    public DbSet<DemoAuditLog> AuditLogs => Set<DemoAuditLog>();
    public DbSet<ClientOrder> Orders => Set<ClientOrder>();
    public DbSet<DemoTenantDocument> TenantDocuments => Set<DemoTenantDocument>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.AddInterceptors(slowQueryInterceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Do not use HasDefaultSchema: MySQL has no independent schema namespace
        // equivalent to SQL Server/PostgreSQL. Table names are therefore kept neutral.
        modelBuilder.Entity<ClientProduct>(e =>
        {
            e.ToTable("Products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasConversion(id => id.Value, value => new ClientEntityId(value));
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.Property(p => p.Category).HasMaxLength(100);
        });

        modelBuilder.Entity<DemoAuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(a => a.Id);
            e.Property(a => a.Action).HasMaxLength(200).IsRequired();
            e.Property(a => a.PerformedBy).HasMaxLength(256).IsRequired();
            e.Property(a => a.Detail).HasMaxLength(1000);
        });

        modelBuilder.Entity<ClientOrder>(e =>
        {
            e.ToTable("Orders");
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasConversion(id => id.Value, value => new ClientEntityId(value));
            e.Property(o => o.OrderNumber).HasMaxLength(50).IsRequired();
            e.Property(o => o.TotalAmount).HasPrecision(18, 2);
            e.Property(o => o.Status).HasMaxLength(50);
            e.Ignore(o => o.DomainEvents);
        });

        modelBuilder.Entity<DemoTenantDocument>(e =>
        {
            e.ToTable("TenantDocuments");
            e.HasKey(d => d.Id);
            e.Property(d => d.Title).HasMaxLength(300).IsRequired();
            e.Property(d => d.Content).HasMaxLength(4000);
        });
    }
}
