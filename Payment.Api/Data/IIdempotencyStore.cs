namespace Payment.Api.Data;

public interface IIdempotencyStore
{
    Task<IdempotencyRequest?> GetByKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task AddProcessingAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken);
    
    // Task CreateProcessingAsync(
    //     string idempotencyKey,
    //     string requestHash,
    //     CancellationToken cancellationToken);
}