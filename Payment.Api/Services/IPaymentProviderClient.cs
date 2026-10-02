using Payment.Api.Models.Provider;

namespace Payment.Api.Services;

public interface IPaymentProviderClient
{
    Task<ProviderChargeResult> ChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken);
}
