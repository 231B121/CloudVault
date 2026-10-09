using CloudVault.Application.Common.Interfaces;
using CloudVault.Domain.Entities;
using CloudVault.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace CloudVault.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ApplicationDbContext dbContext, ILogger<AuditLogService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogAsync(
        Guid? userId,
        string action,
        string? details = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                Details = details,
                IpAddress = ipAddress,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.AuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Do not fail user requests if audit logging table insertion has transient issue, but log structured error
            _logger.LogError(ex, "Failed to persist audit log for Action: {Action}, User: {UserId}", action, userId);
        }
    }
}
