using Payment.Api.Data;

namespace Payment.Api.Services;

public enum CreatePaymentResultStatus
{
    Created,
    Replayed,
    AlreadyProcessing,
    Conflict
}


public class CreatePaymentResult
{
    public CreatePaymentResultStatus Status { get; init; }
    
    public PaymentRecord? Payment { get; init; }
}