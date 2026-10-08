using System.Net;
using System.Text;
using System.Text.Json;
using FakeItEasy;
using FluentAssertions;
using Payment.Api.Models.Provider;
using Payment.Api.Services;

namespace Payment.Tests.Services;

public class PaymentProviderClientTests
{
    private readonly HttpMessageHandler _handler = A.Fake<HttpMessageHandler>();
    private readonly PaymentProviderClient _providerClient;

    private readonly ProviderChargeRequest _request = new()
    {
        OrderId = "order-1",
        Amount = 25.50m,
        Currency = "USD"
    };

    public PaymentProviderClientTests()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://provider.test/") };
        _providerClient = new PaymentProviderClient(httpClient);
    }

    [Fact]
    public async Task ChargeAsync_WhenProviderSucceeds_ReturnsDeserializedResult()
    {
        var processedAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        GivenResponse(HttpStatusCode.OK, $$"""
            {
              "providerReference": "prov-123",
              "status": "Approved",
              "amount": 25.50,
              "currency": "USD",
              "processedAt": "{{processedAt:O}}"
            }
            """);

        var result = await _providerClient.ChargeAsync(_request, CancellationToken.None);

        result.Should().BeEquivalentTo(new ProviderChargeResult
        {
            ProviderReference = "prov-123",
            Status = ChargeStatus.Approved,
            Amount = 25.50m,
            Currency = "USD",
            ProcessedAt = processedAt
        });
    }

    [Fact]
    public async Task ChargeAsync_WhenProviderDeclines_ReturnsDeclinedStatus()
    {
        GivenResponse(HttpStatusCode.OK, """{ "providerReference": "prov-456", "status": "Declined" }""");

        var result = await _providerClient.ChargeAsync(_request, CancellationToken.None);

        result.Status.Should().Be(ChargeStatus.Declined);
        result.ProviderReference.Should().Be("prov-456");
    }

    [Fact]
    public async Task ChargeAsync_PostsRequestAsJsonToChargesEndpoint()
    {
        HttpRequestMessage? sent = null;
        string? sentBody = null;
        A.CallTo(_handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .ReturnsLazily(async call =>
            {
                sent = call.GetArgument<HttpRequestMessage>(0)!;
                sentBody = await sent.Content!.ReadAsStringAsync();
                return JsonResponse(HttpStatusCode.OK, """{ "providerReference": "p", "status": "Approved" }""");
            });

        await _providerClient.ChargeAsync(_request, CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri.Should().Be(new Uri("https://provider.test/api/charges"));

        using var json = JsonDocument.Parse(sentBody!);
        json.RootElement.GetProperty("orderId").GetString().Should().Be("order-1");
        json.RootElement.GetProperty("amount").GetDecimal().Should().Be(25.50m);
        json.RootElement.GetProperty("currency").GetString().Should().Be("USD");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ChargeAsync_WhenProviderReturnsErrorStatus_ThrowsHttpRequestException(HttpStatusCode statusCode)
    {
        GivenResponse(statusCode, "{}");

        var act = () => _providerClient.ChargeAsync(_request, CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public async Task ChargeAsync_WhenProviderReturnsNullBody_ThrowsInvalidOperationException()
    {
        GivenResponse(HttpStatusCode.OK, "null");

        var act = () => _providerClient.ChargeAsync(_request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty response*");
    }

    [Fact]
    public async Task ChargeAsync_WhenTransportFails_PropagatesHttpRequestException()
    {
        A.CallTo(_handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var act = () => _providerClient.ChargeAsync(_request, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("connection refused");
    }

    private void GivenResponse(HttpStatusCode statusCode, string body) =>
        A.CallTo(_handler)
            .Where(call => call.Method.Name == "SendAsync")
            .WithReturnType<Task<HttpResponseMessage>>()
            .ReturnsLazily(() => JsonResponse(statusCode, body));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
