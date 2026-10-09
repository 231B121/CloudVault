using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using CloudVault.Application.Common.Interfaces;
using CloudVault.Application.Common.Options;
using CloudVault.Domain.Entities;
using CloudVault.Infrastructure.Persistence;
using CloudVault.Infrastructure.Services;
using CloudVault.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CloudVault.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Configure Database (SQL Server, PostgreSQL, or In-Memory)
        string? connectionString = configuration.GetConnectionString("DefaultConnection");
        string dbProvider = configuration.GetValue<string>("Database:Provider") ?? "";

        bool isPostgres = string.Equals(dbProvider, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
            || (connectionString != null && (connectionString.Contains("5432") || connectionString.Contains("Host=") || connectionString.Contains("Username=postgres")));

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (configuration.GetValue<bool>("Database:UseInMemoryDatabase"))
            {
                options.UseInMemoryDatabase("CloudVaultDb");
            }
            else if (isPostgres)
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null);
                });
            }
            else
            {
                options.UseSqlServer(connectionString ?? "Server=(localdb)\\mssqllocaldb;Database=CloudVaultDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True",
                    sqlOptions =>
                    {
                        sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                        sqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null);
                    });
            }
        });

        // 2. Configure ASP.NET Core Identity
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            // Secure Password requirements
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;

            // User requirements
            options.User.RequireUniqueEmail = true;

            // Lockout settings
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // 3. Configure AWS S3 Client
        var awsOptions = configuration.GetSection(AwsOptions.SectionName).Get<AwsOptions>() ?? new AwsOptions();

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(awsOptions.Region)
            };

            if (!string.IsNullOrEmpty(awsOptions.ServiceUrl))
            {
                config.ServiceURL = awsOptions.ServiceUrl;
                config.ForcePathStyle = awsOptions.ForcePathStyle;
            }

            if (!string.IsNullOrEmpty(awsOptions.AccessKey) && !string.IsNullOrEmpty(awsOptions.SecretKey))
            {
                var credentials = new BasicAWSCredentials(awsOptions.AccessKey, awsOptions.SecretKey);
                return new AmazonS3Client(credentials, config);
            }

            // Fallback to standard AWS SDK credential resolution (IAM Role, ECS task role, env vars)
            return new AmazonS3Client(config);
        });

        // 4. Configure Storage Service Implementation
        var storageOptions = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

        services.AddScoped<S3FileStorageService>();
        services.AddScoped<LocalStorageService>();

        services.AddScoped<IFileStorageService>(sp =>
        {
            if (string.Equals(storageOptions.Provider, "Local", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<LocalStorageService>();
            }

            return sp.GetRequiredService<S3FileStorageService>();
        });

        // 5. Register Application Services
        services.AddScoped<IStorageQuotaService, StorageQuotaService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IFileService, FileService>();

        return services;
    }
}
