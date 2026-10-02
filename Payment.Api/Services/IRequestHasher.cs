using Payment.Api.Models;

namespace Payment.Api.Services;

public interface IRequestHasher
{
    string ComputeHash(CreatePaymentRequest request);
}