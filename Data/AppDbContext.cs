using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MovieDate.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    // Data Protection keys live in the database so auth cookies stay valid across Fly machines and restarts.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
