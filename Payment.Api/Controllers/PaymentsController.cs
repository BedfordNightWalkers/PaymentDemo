using Microsoft.AspNetCore.Mvc;
using Payment.Api.Data;
using Payment.Api.Models;
using Payment.Api.Services;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(PaymentRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaymentRecord>> Create(
        [FromBody] CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest("Idempotency-Key header is required");
        }

        if (idempotencyKey.Length > 100)
        {
            return BadRequest("Idempotency-Key length cannot exceed 100 characters");
        }
        
        var paymentResult = await _paymentService.CreateAsync(request, idempotencyKey, cancellationToken);

        return paymentResult.Status switch
        {
            CreatePaymentResultStatus.Created =>
                CreatedAtAction(nameof(GetById), new { id = paymentResult.Payment.Id }, paymentResult.Payment),

            CreatePaymentResultStatus.Replayed =>
                Ok(paymentResult.Payment),

            CreatePaymentResultStatus.AlreadyProcessing =>
                Conflict(new 
                {
                    message = "A request with this Idempotency-Key is already being processed."
                }),

            CreatePaymentResultStatus.Conflict =>
                Conflict(new 
                {
                    message = "The Idempotency-Key has already been used with a different request."
                }),

            _ => throw new InvalidOperationException()
        };
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentRecord>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var payment = await _paymentService.GetByIdAsync(id, cancellationToken);
        return payment is null ? NotFound() : Ok(payment);
    }

    [HttpGet("by-order/{orderId}")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentRecord>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentRecord>>> GetByOrderId(
        string orderId,
        CancellationToken cancellationToken)
    {
        var payments = await _paymentService.GetByOrderIdAsync(orderId, cancellationToken);
        return Ok(payments);
    }
}
