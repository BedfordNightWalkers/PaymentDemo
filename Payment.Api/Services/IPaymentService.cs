using Payment.Api.Data;
using Payment.Api.Models;

namespace Payment.Api.Services;

public interface IPaymentService
{
    Task<CreatePaymentResult> CreateAsync(
        CreatePaymentRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
    
    Task<PaymentRecord?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);
    
    Task<IReadOnlyList<PaymentRecord>> GetByOrderIdAsync(
        string orderId,
        CancellationToken cancellationToken);
}