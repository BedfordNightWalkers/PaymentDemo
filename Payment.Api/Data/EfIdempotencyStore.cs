using Microsoft.EntityFrameworkCore;

namespace Payment.Api.Data;

public class EfIdempotencyStore : IIdempotencyStore
{
    private readonly PaymentDbContext _db;
    
    public EfIdempotencyStore(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<IdempotencyRequest?> GetByKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await _db.IdempotencyRequests
            .SingleOrDefaultAsync(
                x => 
                    x.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public async Task AddProcessingAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken)
    {
        await _db.IdempotencyRequests.AddAsync(
            request,
            cancellationToken);
        
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}