using System.ComponentModel.DataAnnotations;

namespace Payment.Api.Models;

/// <summary>
/// Request coming into your API
/// </summary>
public class CreatePaymentRequest
{
    [Required]
    public string OrderId { get; init; } = string.Empty;

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-letter ISO code.")]
    public string Currency { get; init; } = string.Empty;
}
