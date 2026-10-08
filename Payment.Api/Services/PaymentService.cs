using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Payment.Api.Data;
using Payment.Api.Models;
using Payment.Api.Models.Provider;
using ProviderChargeStatus = Payment.Api.Models.Provider.ChargeStatus;

namespace Payment.Api.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentStore _paymentStore;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IRequestHasher _requestHasher;
    private readonly IPaymentProviderClient _paymentProviderClient;
    private readonly ILogger<PaymentService> _logger;


    public PaymentService(
        IPaymentStore paymentStore,
        IIdempotencyStore idempotencyStore,
        IRequestHasher requestHasher,
        IPaymentProviderClient paymentProviderClient,
        ILogger<PaymentService> logger)
    {
        _paymentStore = paymentStore;
        _idempotencyStore  = idempotencyStore;
        _requestHasher = requestHasher;
        _paymentProviderClient = paymentProviderClient;
        _logger = logger;
    }

    public async Task<CreatePaymentResult> CreateAsync(
        CreatePaymentRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var ownsIdempotencyRequest = false;
        var requestHash = _requestHasher.ComputeHash(request);


        var idempotencyRequest = await _idempotencyStore.GetByKeyAsync(idempotencyKey, cancellationToken);

        if (idempotencyRequest == null)
        {
            idempotencyRequest = new IdempotencyRequest
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash,
                Status = IdempotencyStatus.Processing,
                CreatedAt = DateTimeOffset.UtcNow
            };

            try
            {
                await _idempotencyStore.AddProcessingAsync(idempotencyRequest, cancellationToken);
                ownsIdempotencyRequest = true;
                
                await Task.Delay(5000, cancellationToken); //Testing concurrent requests
            }
            catch (DbUpdateException)
            {
                idempotencyRequest = 
                    await _idempotencyStore.GetByKeyAsync(
                        idempotencyKey,
                        cancellationToken);
            }
        }

        if (idempotencyRequest != null)
        {
            if(idempotencyRequest.RequestHash != requestHash)
            {
                return new CreatePaymentResult
                {
                    Status =  CreatePaymentResultStatus.Conflict
                };
            }

            if (idempotencyRequest.Status == IdempotencyStatus.Processing &&
                !ownsIdempotencyRequest)
            {
                return new CreatePaymentResult
                {
                    Status = CreatePaymentResultStatus.AlreadyProcessing
                };
            }

            if (idempotencyRequest.Status == IdempotencyStatus.Completed)
            {
                if (idempotencyRequest.PaymentId is null)
                {
                    throw new InvalidOperationException(
                        "Completed idempotency request has no paymentId");
                }
                
                var originalPayment = await _paymentStore.GetByIdAsync(
                    idempotencyRequest.PaymentId.Value,
                    cancellationToken);

                if (originalPayment is null)
                {
                    throw new InvalidOperationException(
                        "Payment referenced by idempotency request not found");
                }

                return new CreatePaymentResult
                {
                    Status = CreatePaymentResultStatus.Replayed,
                    Payment = originalPayment
                };
            }

        }
        
        var payment = new PaymentRecord
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Status = PaymentStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _paymentStore.AddAsync(payment, cancellationToken);

        try
        {
            var chargeResult = await _paymentProviderClient.ChargeAsync(
                new ProviderChargeRequest
                {
                    OrderId = payment.OrderId,
                    Amount = payment.Amount,
                    Currency = payment.Currency
                },
                cancellationToken);

            payment.ProviderReference = chargeResult.ProviderReference;
            payment.Status = chargeResult.Status switch
            {
                ProviderChargeStatus.Approved => PaymentStatus.Succeeded,
                ProviderChargeStatus.Declined => PaymentStatus.Declined,
                _ => PaymentStatus.Unknown
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Payment provider charge failed for payment {PaymentId}", payment.Id);
            payment.Status = PaymentStatus.Failed;
        }
        
        await _paymentStore.UpdateAsync(payment, cancellationToken);

        idempotencyRequest.Status = IdempotencyStatus.Completed;
        idempotencyRequest.PaymentId = payment.Id;
        idempotencyRequest.ResponseCode = 201;
        idempotencyRequest.ResponseBody = JsonSerializer.Serialize(payment);
        idempotencyRequest.UpdatedAt = DateTimeOffset.UtcNow;

        await _idempotencyStore.UpdateAsync(
            idempotencyRequest,
            cancellationToken);
        
        return new CreatePaymentResult
        {
            Status = CreatePaymentResultStatus.Created,
            Payment = payment
        };
    }


    public async Task<PaymentRecord?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
    {
        return await _paymentStore.GetByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentRecord>> GetByOrderIdAsync(
        string orderId,
        CancellationToken cancellationToken)
    {
        return await _paymentStore.GetByOrderIdAsync(orderId, cancellationToken);
    }
}
