using DominioDeLaSierra.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Wine> Wines => Set<Wine>();
    public DbSet<ProductComponent> ProductComponents => Set<ProductComponent>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
