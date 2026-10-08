using FakeItEasy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payment.Api.Data;
using Payment.Api.Models;
using Payment.Api.Models.Provider;
using Payment.Api.Services;

namespace Payment.Tests.Services;

public class PaymentServiceTests
{
    private const string IdempotencyKey = "key-123";
    private const string RequestHash = "HASH-ABC";

    private readonly IPaymentStore _paymentStore = A.Fake<IPaymentStore>();
    private readonly IIdempotencyStore _idempotencyStore = A.Fake<IIdempotencyStore>();
    private readonly IRequestHasher _requestHasher = A.Fake<IRequestHasher>();
    private readonly IPaymentProviderClient _providerClient = A.Fake<IPaymentProviderClient>();
    private readonly ILogger<PaymentService> _logger = A.Fake<ILogger<PaymentService>>();
    private readonly PaymentService _paymentService;

    private readonly CreatePaymentRequest _request = new()
    {
        OrderId = "order-1",
        Amount = 49.99m,
        Currency = "usd"
    };

    public PaymentServiceTests()
    {
        A.CallTo(() => _requestHasher.ComputeHash(_request)).Returns(RequestHash);

        _paymentService = new PaymentService(_paymentStore, _idempotencyStore, _requestHasher, _providerClient, _logger);
    }

    // ---------- CreateAsync: existing idempotency request ----------

    [Fact]
    public async Task CreateAsync_WhenExistingRequestHasDifferentHash_ReturnsConflict()
    {
        GivenExistingIdempotencyRequest(IdempotencyStatus.Completed, requestHash: "OTHER-HASH");

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.Conflict);
        result.Payment.Should().BeNull();
        AssertNoPaymentCreated();
    }

    [Fact]
    public async Task CreateAsync_WhenExistingRequestIsProcessing_ReturnsAlreadyProcessing()
    {
        GivenExistingIdempotencyRequest(IdempotencyStatus.Processing);

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.AlreadyProcessing);
        result.Payment.Should().BeNull();
        AssertNoPaymentCreated();
    }

    [Fact]
    public async Task CreateAsync_WhenExistingRequestIsCompleted_ReplaysOriginalPayment()
    {
        var originalPayment = new PaymentRecord { Id = Guid.NewGuid(), OrderId = "order-1", Status = PaymentStatus.Succeeded };
        GivenExistingIdempotencyRequest(IdempotencyStatus.Completed, paymentId: originalPayment.Id);
        A.CallTo(() => _paymentStore.GetByIdAsync(originalPayment.Id, A<CancellationToken>._))
            .Returns(originalPayment);

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.Replayed);
        result.Payment.Should().BeSameAs(originalPayment);
        AssertNoPaymentCreated();
    }

    [Fact]
    public async Task CreateAsync_WhenCompletedRequestHasNoPaymentId_Throws()
    {
        GivenExistingIdempotencyRequest(IdempotencyStatus.Completed, paymentId: null);

        var act = () => _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*has no paymentId*");
    }

    [Fact]
    public async Task CreateAsync_WhenCompletedRequestReferencesMissingPayment_Throws()
    {
        var paymentId = Guid.NewGuid();
        GivenExistingIdempotencyRequest(IdempotencyStatus.Completed, paymentId: paymentId);
        A.CallTo(() => _paymentStore.GetByIdAsync(paymentId, A<CancellationToken>._))
            .Returns((PaymentRecord?)null);

        var act = () => _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateAsync_WhenExistingRequestFailed_ProcessesPaymentAgainAndCompletesRequest()
    {
        var existing = GivenExistingIdempotencyRequest(IdempotencyStatus.Failed);
        GivenProviderReturns(ChargeStatus.Approved, "prov-ref");

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.Created);
        A.CallTo(() => _idempotencyStore.AddProcessingAsync(A<IdempotencyRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        existing.Status.Should().Be(IdempotencyStatus.Completed);
        existing.PaymentId.Should().Be(result.Payment!.Id);
    }

    // ---------- CreateAsync: concurrent insert (DbUpdateException) ----------

    [Fact]
    public async Task CreateAsync_WhenConcurrentInsertWinsAndIsProcessing_ReturnsAlreadyProcessing()
    {
        GivenConcurrentInsertOf(NewIdempotencyRequest(IdempotencyStatus.Processing));

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.AlreadyProcessing);
        AssertNoPaymentCreated();
    }

    [Fact]
    public async Task CreateAsync_WhenConcurrentInsertWinsWithDifferentHash_ReturnsConflict()
    {
        GivenConcurrentInsertOf(NewIdempotencyRequest(IdempotencyStatus.Processing, requestHash: "OTHER-HASH"));

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.Conflict);
        AssertNoPaymentCreated();
    }

    [Fact]
    public async Task CreateAsync_WhenConcurrentInsertWinsAndIsCompleted_ReplaysOriginalPayment()
    {
        var originalPayment = new PaymentRecord { Id = Guid.NewGuid() };
        GivenConcurrentInsertOf(NewIdempotencyRequest(IdempotencyStatus.Completed, paymentId: originalPayment.Id));
        A.CallTo(() => _paymentStore.GetByIdAsync(originalPayment.Id, A<CancellationToken>._))
            .Returns(originalPayment);

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.Replayed);
        result.Payment.Should().BeSameAs(originalPayment);
    }

    // ---------- CreateAsync: new request ----------

    [Fact]
    public async Task CreateAsync_WhenNewRequestAndProviderApproves_CreatesSucceededPayment()
    {
        GivenNoExistingIdempotencyRequest();
        GivenProviderReturns(ChargeStatus.Approved, "prov-ref-1");

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.Created);
        result.Payment.Should().NotBeNull();
        result.Payment!.Id.Should().NotBeEmpty();
        result.Payment.OrderId.Should().Be("order-1");
        result.Payment.Amount.Should().Be(49.99m);
        result.Payment.Currency.Should().Be("USD");
        result.Payment.Status.Should().Be(PaymentStatus.Succeeded);
        result.Payment.ProviderReference.Should().Be("prov-ref-1");
    }

    [Fact]
    public async Task CreateAsync_WhenNewRequest_PersistsProcessingIdempotencyRequestThenCompletesIt()
    {
        GivenNoExistingIdempotencyRequest();
        GivenProviderReturns(ChargeStatus.Approved, "prov-ref-1");
        IdempotencyStatus? statusWhenAdded = null;
        IdempotencyRequest? added = null;
        A.CallTo(() => _idempotencyStore.AddProcessingAsync(A<IdempotencyRequest>._, A<CancellationToken>._))
            .Invokes((IdempotencyRequest r, CancellationToken _) =>
            {
                added = r;
                statusWhenAdded = r.Status;
            });

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        statusWhenAdded.Should().Be(IdempotencyStatus.Processing);
        added.Should().NotBeNull();
        added!.IdempotencyKey.Should().Be(IdempotencyKey);
        added.RequestHash.Should().Be(RequestHash);
        added.Status.Should().Be(IdempotencyStatus.Completed);
        added.PaymentId.Should().Be(result.Payment!.Id);
        added.ResponseCode.Should().Be(201);
        added.ResponseBody.Should().Contain(result.Payment.Id.ToString());
        added.UpdatedAt.Should().NotBeNull();
        A.CallTo(() => _idempotencyStore.UpdateAsync(added, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CreateAsync_WhenNewRequest_AddsPendingPaymentChargesProviderThenUpdatesPayment()
    {
        GivenNoExistingIdempotencyRequest();
        GivenProviderReturns(ChargeStatus.Approved, "prov-ref-1");
        PaymentStatus? statusWhenAdded = null;
        A.CallTo(() => _paymentStore.AddAsync(A<PaymentRecord>._, A<CancellationToken>._))
            .Invokes((PaymentRecord p, CancellationToken _) => statusWhenAdded = p.Status);

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        statusWhenAdded.Should().Be(PaymentStatus.Pending);
        A.CallTo(() => _paymentStore.AddAsync(result.Payment!, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly()
            .Then(A.CallTo(() => _providerClient.ChargeAsync(
                    A<ProviderChargeRequest>.That.Matches(r =>
                        r.OrderId == "order-1" && r.Amount == 49.99m && r.Currency == "USD"),
                    A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _paymentStore.UpdateAsync(result.Payment!, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly())
            .Then(A.CallTo(() => _idempotencyStore.UpdateAsync(A<IdempotencyRequest>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());
    }

    [Fact]
    public async Task CreateAsync_WhenProviderDeclines_CreatesDeclinedPayment()
    {
        GivenNoExistingIdempotencyRequest();
        GivenProviderReturns(ChargeStatus.Declined, "prov-ref-2");

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.Created);
        result.Payment!.Status.Should().Be(PaymentStatus.Declined);
        result.Payment.ProviderReference.Should().Be("prov-ref-2");
    }

    [Fact]
    public async Task CreateAsync_WhenProviderReturnsUnknownStatus_CreatesUnknownPayment()
    {
        GivenNoExistingIdempotencyRequest();
        GivenProviderReturns((ChargeStatus)999, "prov-ref-3");

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Payment!.Status.Should().Be(PaymentStatus.Unknown);
    }

    public static TheoryData<Exception> HandledProviderExceptions => new()
    {
        new HttpRequestException("network down"),
        new TaskCanceledException("timeout"),
        new InvalidOperationException("empty response")
    };

    [Theory]
    [MemberData(nameof(HandledProviderExceptions))]
    public async Task CreateAsync_WhenProviderFailsWithHandledException_CreatesFailedPaymentAndLogsError(Exception exception)
    {
        GivenNoExistingIdempotencyRequest();
        A.CallTo(() => _providerClient.ChargeAsync(A<ProviderChargeRequest>._, A<CancellationToken>._))
            .ThrowsAsync(exception);

        var result = await _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        result.Status.Should().Be(CreatePaymentResultStatus.Created);
        result.Payment!.Status.Should().Be(PaymentStatus.Failed);
        result.Payment.ProviderReference.Should().BeNull();
        A.CallTo(() => _paymentStore.UpdateAsync(result.Payment, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(_logger)
            .Where(call => call.Method.Name == nameof(ILogger.Log)
                           && call.GetArgument<LogLevel>(0) == LogLevel.Error
                           && call.GetArgument<Exception>(3) == exception)
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CreateAsync_WhenProviderFailsWithUnhandledException_Propagates()
    {
        GivenNoExistingIdempotencyRequest();
        A.CallTo(() => _providerClient.ChargeAsync(A<ProviderChargeRequest>._, A<CancellationToken>._))
            .ThrowsAsync(new ArgumentException("boom"));

        var act = () => _paymentService.CreateAsync(_request, IdempotencyKey, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        A.CallTo(() => _paymentStore.UpdateAsync(A<PaymentRecord>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _idempotencyStore.UpdateAsync(A<IdempotencyRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // ---------- GetByIdAsync ----------

    [Fact]
    public async Task GetByIdAsync_WhenPaymentExists_ReturnsPaymentFromStore()
    {
        var payment = new PaymentRecord { Id = Guid.NewGuid() };
        A.CallTo(() => _paymentStore.GetByIdAsync(payment.Id, A<CancellationToken>._)).Returns(payment);

        var result = await _paymentService.GetByIdAsync(payment.Id, CancellationToken.None);

        result.Should().BeSameAs(payment);
    }

    [Fact]
    public async Task GetByIdAsync_WhenPaymentDoesNotExist_ReturnsNull()
    {
        A.CallTo(() => _paymentStore.GetByIdAsync(A<Guid>._, A<CancellationToken>._))
            .Returns((PaymentRecord?)null);

        var result = await _paymentService.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_PassesCancellationTokenToStore()
    {
        using var cts = new CancellationTokenSource();
        var id = Guid.NewGuid();

        await _paymentService.GetByIdAsync(id, cts.Token);

        A.CallTo(() => _paymentStore.GetByIdAsync(id, cts.Token)).MustHaveHappenedOnceExactly();
    }

    // ---------- GetByOrderIdAsync ----------

    [Fact]
    public async Task GetByOrderIdAsync_ReturnsPaymentsFromStore()
    {
        IReadOnlyList<PaymentRecord> payments =
        [
            new PaymentRecord { Id = Guid.NewGuid(), OrderId = "order-1" },
            new PaymentRecord { Id = Guid.NewGuid(), OrderId = "order-1" }
        ];
        A.CallTo(() => _paymentStore.GetByOrderIdAsync("order-1", A<CancellationToken>._)).Returns(payments);

        var result = await _paymentService.GetByOrderIdAsync("order-1", CancellationToken.None);

        result.Should().BeEquivalentTo(payments, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task GetByOrderIdAsync_WhenNoPayments_ReturnsEmptyList()
    {
        A.CallTo(() => _paymentStore.GetByOrderIdAsync(A<string>._, A<CancellationToken>._))
            .Returns(Array.Empty<PaymentRecord>());

        var result = await _paymentService.GetByOrderIdAsync("unknown", CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ---------- helpers ----------

    private static IdempotencyRequest NewIdempotencyRequest(
        IdempotencyStatus status,
        string requestHash = RequestHash,
        Guid? paymentId = null) => new()
    {
        Id = Guid.NewGuid(),
        IdempotencyKey = IdempotencyKey,
        RequestHash = requestHash,
        Status = status,
        PaymentId = paymentId,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private IdempotencyRequest GivenExistingIdempotencyRequest(
        IdempotencyStatus status,
        string requestHash = RequestHash,
        Guid? paymentId = null)
    {
        var existing = NewIdempotencyRequest(status, requestHash, paymentId);
        A.CallTo(() => _idempotencyStore.GetByKeyAsync(IdempotencyKey, A<CancellationToken>._)).Returns(existing);
        return existing;
    }

    private void GivenNoExistingIdempotencyRequest() =>
        A.CallTo(() => _idempotencyStore.GetByKeyAsync(IdempotencyKey, A<CancellationToken>._))
            .Returns((IdempotencyRequest?)null);

    private void GivenConcurrentInsertOf(IdempotencyRequest winner)
    {
        A.CallTo(() => _idempotencyStore.GetByKeyAsync(IdempotencyKey, A<CancellationToken>._))
            .ReturnsNextFromSequence(null, winner);
        A.CallTo(() => _idempotencyStore.AddProcessingAsync(A<IdempotencyRequest>._, A<CancellationToken>._))
            .ThrowsAsync(new DbUpdateException("duplicate key"));
    }

    private void GivenProviderReturns(ChargeStatus status, string providerReference) =>
        A.CallTo(() => _providerClient.ChargeAsync(A<ProviderChargeRequest>._, A<CancellationToken>._))
            .Returns(new ProviderChargeResult { Status = status, ProviderReference = providerReference });

    private void AssertNoPaymentCreated()
    {
        A.CallTo(() => _paymentStore.AddAsync(A<PaymentRecord>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _providerClient.ChargeAsync(A<ProviderChargeRequest>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
