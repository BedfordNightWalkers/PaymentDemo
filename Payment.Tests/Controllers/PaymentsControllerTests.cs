using FakeItEasy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Payment.Api.Controllers;
using Payment.Api.Data;
using Payment.Api.Models;
using Payment.Api.Services;

namespace Payment.Tests.Controllers;

public class PaymentsControllerTests
{
    private readonly IPaymentService _paymentService = A.Fake<IPaymentService>();
    private readonly PaymentsController _sut;

    private readonly CreatePaymentRequest _request = new()
    {
        OrderId = "order-1",
        Amount = 10m,
        Currency = "USD"
    };

    public PaymentsControllerTests()
    {
        _sut = new PaymentsController(_paymentService);
    }

    // ---------- Create ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_WhenIdempotencyKeyMissing_ReturnsBadRequest(string? key)
    {
        var result = await _sut.Create(_request, key!, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Idempotency-Key header is required");
        A.CallTo(_paymentService).MustNotHaveHappened();
    }

    [Fact]
    public async Task Create_WhenIdempotencyKeyTooLong_ReturnsBadRequest()
    {
        var result = await _sut.Create(_request, new string('k', 101), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().Be("Idempotency-Key length cannot exceed 100 characters");
        A.CallTo(_paymentService).MustNotHaveHappened();
    }

    [Fact]
    public async Task Create_WhenIdempotencyKeyIsExactly100Chars_CallsService()
    {
        var key = new string('k', 100);
        GivenCreateReturns(CreatePaymentResultStatus.Created, new PaymentRecord { Id = Guid.NewGuid() });

        await _sut.Create(_request, key, CancellationToken.None);

        A.CallTo(() => _paymentService.CreateAsync(_request, key, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Create_WhenCreated_ReturnsCreatedAtGetById()
    {
        var payment = new PaymentRecord { Id = Guid.NewGuid() };
        GivenCreateReturns(CreatePaymentResultStatus.Created, payment);

        var result = await _sut.Create(_request, "key", CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(PaymentsController.GetById));
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(payment.Id);
        created.Value.Should().BeSameAs(payment);
    }

    [Fact]
    public async Task Create_WhenReplayed_ReturnsOkWithOriginalPayment()
    {
        var payment = new PaymentRecord { Id = Guid.NewGuid() };
        GivenCreateReturns(CreatePaymentResultStatus.Replayed, payment);

        var result = await _sut.Create(_request, "key", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(payment);
    }

    [Theory]
    [InlineData(CreatePaymentResultStatus.AlreadyProcessing, "already being processed")]
    [InlineData(CreatePaymentResultStatus.Conflict, "different request")]
    public async Task Create_WhenConflictingStatus_ReturnsConflict(CreatePaymentResultStatus status, string messagePart)
    {
        GivenCreateReturns(status, payment: null);

        var result = await _sut.Create(_request, "key", CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.Value!.GetType().GetProperty("message")!.GetValue(conflict.Value)
            .Should().BeOfType<string>().Which.Should().Contain(messagePart);
    }

    [Fact]
    public async Task Create_WhenUnknownStatus_Throws()
    {
        GivenCreateReturns((CreatePaymentResultStatus)999, payment: null);

        var act = () => _sut.Create(_request, "key", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------- GetById ----------

    [Fact]
    public async Task GetById_WhenFound_ReturnsOk()
    {
        var payment = new PaymentRecord { Id = Guid.NewGuid() };
        A.CallTo(() => _paymentService.GetByIdAsync(payment.Id, A<CancellationToken>._)).Returns(payment);

        var result = await _sut.GetById(payment.Id, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(payment);
    }

    [Fact]
    public async Task GetById_WhenNotFound_ReturnsNotFound()
    {
        A.CallTo(() => _paymentService.GetByIdAsync(A<Guid>._, A<CancellationToken>._))
            .Returns((PaymentRecord?)null);

        var result = await _sut.GetById(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ---------- GetByOrderId ----------

    [Fact]
    public async Task GetByOrderId_ReturnsOkWithPayments()
    {
        IReadOnlyList<PaymentRecord> payments = [new PaymentRecord { Id = Guid.NewGuid(), OrderId = "order-1" }];
        A.CallTo(() => _paymentService.GetByOrderIdAsync("order-1", A<CancellationToken>._)).Returns(payments);

        var result = await _sut.GetByOrderId("order-1", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeSameAs(payments);
    }

    [Fact]
    public async Task GetByOrderId_WhenNoPayments_ReturnsOkWithEmptyList()
    {
        A.CallTo(() => _paymentService.GetByOrderIdAsync(A<string>._, A<CancellationToken>._))
            .Returns(Array.Empty<PaymentRecord>());

        var result = await _sut.GetByOrderId("unknown", CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.As<IReadOnlyList<PaymentRecord>>().Should().BeEmpty();
    }

    private void GivenCreateReturns(CreatePaymentResultStatus status, PaymentRecord? payment) =>
        A.CallTo(() => _paymentService.CreateAsync(A<CreatePaymentRequest>._, A<string>._, A<CancellationToken>._))
            .Returns(new CreatePaymentResult { Status = status, Payment = payment });
}
