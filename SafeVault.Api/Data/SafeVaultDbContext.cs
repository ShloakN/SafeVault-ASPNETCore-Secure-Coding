using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SafeVault.Api.Models;

namespace SafeVault.Api.Data;

public class SafeVaultDbContext(DbContextOptions<SafeVaultDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Note>(entity =>
        {
            entity.Property(n => n.Content).HasMaxLength(2000).IsRequired();
            entity.Property(n => n.OwnerId).IsRequired();
            entity.HasIndex(n => n.OwnerId);
        });
    }
}
