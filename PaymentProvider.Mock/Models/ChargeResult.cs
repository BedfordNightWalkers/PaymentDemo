namespace PaymentProvider.Mock.Models;

public class ChargeResult
{
    public string ProviderReference { get; init; } = string.Empty;
    public ChargeStatus Status { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; init; }
}
