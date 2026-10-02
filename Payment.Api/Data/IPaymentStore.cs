using Payment.Api.Models;

namespace Payment.Api.Data;

public interface IPaymentStore
{
    Task AddAsync(
        PaymentRecord payment,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        PaymentRecord payment,
        CancellationToken cancellationToken);

    Task<PaymentRecord?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentRecord>> GetByOrderIdAsync(
        string orderId,
        CancellationToken cancellationToken);
}