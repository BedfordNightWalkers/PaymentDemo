using Payment.Api.Models;

namespace Payment.Api.Data;

public enum IdempotencyStatus
{
    Processing,
    Completed,
    Failed
}

/// <summary>
/// Idempotency record persisted in database
/// </summary>
public class IdempotencyRequest
{
    public Guid Id { get; init; }

    public string IdempotencyKey { get; init; } = string.Empty;
    
    public string RequestHash { get; init; } = string.Empty;
    
    public Guid? PaymentId { get; set; }
    
    public IdempotencyStatus Status { get; set; }
    
    public int? ResponseCode { get; set; }
    
    public string? ResponseBody { get; set; }
    
    public DateTimeOffset CreatedAt { get; init; }
    
    public DateTimeOffset? UpdatedAt { get; set; }
}