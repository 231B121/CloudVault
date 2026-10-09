namespace CloudVault.Domain.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string entityName, object key) 
        : base($"{entityName} with id '{key}' was not found.") { }
}

public class QuotaExceededException : DomainException
{
    public long StorageLimitBytes { get; }
    public long StorageUsedBytes { get; }
    public long AttemptedSizeBytes { get; }

    public QuotaExceededException(long storageLimitBytes, long storageUsedBytes, long attemptedSizeBytes)
        : base($"Storage limit exceeded. Limit: {storageLimitBytes} bytes, Current used: {storageUsedBytes} bytes, Attempted: {attemptedSizeBytes} bytes.")
    {
        StorageLimitBytes = storageLimitBytes;
        StorageUsedBytes = storageUsedBytes;
        AttemptedSizeBytes = attemptedSizeBytes;
    }
}

public class DuplicateResourceException : DomainException
{
    public DuplicateResourceException(string message) : base(message) { }
}

public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message) : base(message) { }
}

public class ValidationException : DomainException
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(string message) : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IDictionary<string, string[]> errors) 
        : base("One or more validation failures have occurred.")
    {
        Errors = errors;
    }

    public ValidationException(string propertyName, string errorMessage)
        : base(errorMessage)
    {
        Errors = new Dictionary<string, string[]>
        {
            { propertyName, new[] { errorMessage } }
        };
    }
}
