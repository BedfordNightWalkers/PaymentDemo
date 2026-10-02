using Microsoft.AspNetCore.Mvc;
using PaymentProvider.Mock.Models;

namespace PaymentProvider.Mock.Controllers;

[ApiController]
[Route("api/charges")]
public class ChargesController : ControllerBase
{
    // Demo rule: amounts over 10,000 simulate a declined charge (e.g. limit exceeded).
    private const decimal DeclineThreshold = 10_000m;

    [HttpPost]
    [ProducesResponseType(typeof(ChargeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<ChargeResult> Charge([FromBody] ChargeRequest request)
    {
        var status = request.Amount > DeclineThreshold ? ChargeStatus.Declined : ChargeStatus.Approved;

        var result = new ChargeResult
        {
            ProviderReference = $"prov_{Guid.NewGuid():N}",
            Status = status,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            ProcessedAt = DateTimeOffset.UtcNow
        };

        return Ok(result);
    }
}
