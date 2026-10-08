using FluentAssertions;
using Payment.Api.Models;
using Payment.Api.Services;

namespace Payment.Tests.Services;

public class RequestHasherTests
{
    private readonly RequestHasher _requestHasher = new();

    [Fact]
    public void ComputeHash_ReturnsUppercaseHexSha256()
    {
        var hash = _requestHasher.ComputeHash(Request("order-1", 10m, "USD"));

        hash.Should().HaveLength(64).And.MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void ComputeHash_ForSameRequest_IsDeterministic()
    {
        var first = _requestHasher.ComputeHash(Request("order-1", 10m, "USD"));
        var second = _requestHasher.ComputeHash(Request("order-1", 10m, "USD"));

        first.Should().Be(second);
    }

    [Fact]
    public void ComputeHash_MatchesKnownValue()
    {
        // SHA-256 of "order-1|10.00|USD"
        var hash = _requestHasher.ComputeHash(Request("order-1", 10m, "USD"));

        hash.Should().Be(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData("order-1|10.00|USD"u8.ToArray())));
    }

    [Theory]
    [InlineData("  order-1  ", 10, "USD")]
    [InlineData("order-1", 10, "usd")]
    [InlineData("order-1", 10, " Usd ")]
    [InlineData("order-1", 10.00, "USD")]
    [InlineData("order-1", 10.001, "USD")]
    public void ComputeHash_NormalizesEquivalentRequests(string orderId, decimal amount, string currency)
    {
        var expected = _requestHasher.ComputeHash(Request("order-1", 10m, "USD"));

        _requestHasher.ComputeHash(Request(orderId, amount, currency)).Should().Be(expected);
    }

    [Theory]
    [InlineData("order-2", 10, "USD")]
    [InlineData("order-1", 10.01, "USD")]
    [InlineData("order-1", 10, "EUR")]
    [InlineData("ORDER-1", 10, "USD")]
    public void ComputeHash_ForDifferentRequests_ProducesDifferentHash(string orderId, decimal amount, string currency)
    {
        var baseline = _requestHasher.ComputeHash(Request("order-1", 10m, "USD"));

        _requestHasher.ComputeHash(Request(orderId, amount, currency)).Should().NotBe(baseline);
    }

    private static CreatePaymentRequest Request(string orderId, decimal amount, string currency) =>
        new() { OrderId = orderId, Amount = amount, Currency = currency };
}
