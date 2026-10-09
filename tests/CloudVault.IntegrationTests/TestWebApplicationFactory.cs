using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CloudVault.IntegrationTests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:UseInMemoryDatabase"] = "true",
                ["Storage:Provider"] = "Local",
                ["Storage:LocalStoragePath"] = "test_data/storage"
            });
        });

        builder.UseEnvironment("Development");
    }
}
