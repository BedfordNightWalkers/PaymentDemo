using System.Text.Json.Serialization;

namespace PaymentProvider.Mock.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChargeStatus
{
    Approved,
    Declined
}
