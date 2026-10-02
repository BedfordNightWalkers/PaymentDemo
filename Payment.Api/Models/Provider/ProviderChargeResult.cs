using System.Text.Json.Serialization;

namespace Payment.Api.Models.Provider;

public enum ChargeStatus
{
    Approved,
    Declined
}

/// <summary>
/// Result returned by provider client
/// </summary>
public class ProviderChargeResult
{
    public string ProviderReference { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChargeStatus Status { get; init; }

    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; init; }
}
