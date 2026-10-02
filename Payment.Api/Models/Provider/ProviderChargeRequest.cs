namespace Payment.Api.Models.Provider;

/// <summary>
/// Request sent to payment provider
/// </summary>
public class ProviderChargeRequest
{
    public string OrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
}
