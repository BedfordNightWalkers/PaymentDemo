namespace Payment.Api.Data;

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Declined,
    Failed,
    Unknown,
}

/// <summary>
/// Payment persisted in your database
/// </summary>
public class PaymentRecord
{
    public Guid Id { get; init; }
    public string OrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public PaymentStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}
