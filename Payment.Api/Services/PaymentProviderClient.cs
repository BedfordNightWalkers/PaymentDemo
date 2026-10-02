using System.Net.Http.Json;
using Payment.Api.Models.Provider;

namespace Payment.Api.Services;

public class PaymentProviderClient : IPaymentProviderClient
{
    private readonly HttpClient _httpClient;

    public PaymentProviderClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProviderChargeResult> ChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/charges", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ProviderChargeResult>(cancellationToken);
        if (result is null)
        {
            throw new InvalidOperationException("Payment provider returned an empty response.");
        }

        return result;
    }
}
