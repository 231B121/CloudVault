using CloudVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CloudVault.UnitTests.Helpers;

public static class TestDbContextFactory
{
    public static ApplicationDbContext CreateInMemoryDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
