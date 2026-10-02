using Microsoft.EntityFrameworkCore;
using Payment.Api.Models;
using Payment.Api.Services;

namespace Payment.Api.Data;

public class EfPaymentStore : IPaymentStore
{
    private readonly PaymentDbContext _db;

    public EfPaymentStore(PaymentDbContext db)
    {
        _db = db;
    }
    
    public async Task AddAsync(PaymentRecord payment, CancellationToken cancellationToken)
    {
        await _db.Payments.AddAsync(payment, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(PaymentRecord payment, CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PaymentRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _db.Payments
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentRecord>> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken)
    {
        return await _db.Payments
            .Where(p => p.OrderId == orderId)
            .ToListAsync(cancellationToken);
    }
}