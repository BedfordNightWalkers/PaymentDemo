using System.Security.Cryptography;
using System.Text;
using Payment.Api.Models;

namespace Payment.Api.Services;

public class RequestHasher : IRequestHasher
{
    public string ComputeHash(CreatePaymentRequest request)
    {
        var canonical =
            $"{request.OrderId.Trim()}|{request.Amount:0.00}|{request.Currency.Trim().ToUpperInvariant()}";
        
        var bytes = Encoding.UTF8.GetBytes(canonical);
        
        var hash = SHA256.HashData(bytes);
        
        return Convert.ToHexString(hash);
    }
}