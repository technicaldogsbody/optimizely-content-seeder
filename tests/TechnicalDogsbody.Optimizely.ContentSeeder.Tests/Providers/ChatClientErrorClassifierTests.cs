using System.Net;
using System.Net.Sockets;
using TechnicalDogsbody.Optimizely.ContentSeeder.AI;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class ChatClientErrorClassifierTests
{
    // Shapes of the exceptions real SDKs throw, without referencing those SDKs.
    private sealed class StatusCodeEnumException(HttpStatusCode code, string message = "x") : Exception(message)
    {
        public HttpStatusCode StatusCode { get; } = code;
    }

    private sealed class StatusIntException(int status, string message = "x") : Exception(message)
    {
        public int Status { get; } = status;
    }

    private sealed class NullableStatusException(HttpStatusCode? code) : Exception("x")
    {
        public HttpStatusCode? StatusCode { get; } = code;
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(403)]
    [InlineData(404)]
    public void HttpRequestException_FatalStatus_IsConfigurationError(int status)
    {
        var ex = new HttpRequestException("x", null, (HttpStatusCode)status);

        Assert.True(ChatClientErrorClassifier.IsConfigurationError(ex));
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(529)]
    public void HttpRequestException_TransientStatus_IsNotConfigurationError(int status)
    {
        var ex = new HttpRequestException("x", null, (HttpStatusCode)status);

        Assert.False(ChatClientErrorClassifier.IsConfigurationError(ex));
    }

    [Fact]
    public void ServiceNotRunning_IsConfigurationError()
    {
        var ex = new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(ChatClientErrorClassifier.IsConfigurationError(ex));
    }

    [Fact]
    public void StatusCodeProperty_IsRead_LikeTheAnthropicSdk()
    {
        Assert.True(ChatClientErrorClassifier.IsConfigurationError(new StatusCodeEnumException(HttpStatusCode.PaymentRequired)));
        Assert.False(ChatClientErrorClassifier.IsConfigurationError(new StatusCodeEnumException((HttpStatusCode)529)));
    }

    [Fact]
    public void StatusIntProperty_IsRead_LikeTheOpenAiAndAzureSdks()
    {
        Assert.True(ChatClientErrorClassifier.IsConfigurationError(new StatusIntException(401)));
        Assert.False(ChatClientErrorClassifier.IsConfigurationError(new StatusIntException(503)));
    }

    [Fact]
    public void NullableStatusCode_WithNoValue_IsNotConfigurationError()
    {
        Assert.False(ChatClientErrorClassifier.IsConfigurationError(new NullableStatusException(null)));
    }

    [Theory]
    [InlineData("HTTP 429 (insufficient_quota: insufficient_quota)", true)]
    [InlineData("Your credit balance is too low to access the API", true)]
    [InlineData("HTTP 429 (requests: rate_limit_exceeded)", false)]
    [InlineData("Too many requests, slow down", false)]
    public void TooManyRequests_IsFatalOnlyWhenOutOfCredit(string message, bool expected)
    {
        Assert.Equal(expected, ChatClientErrorClassifier.IsConfigurationError(new StatusIntException(429, message)));
    }

    [Fact]
    public void StatusOnAnInnerException_IsFound()
    {
        var ex = new InvalidOperationException("wrapped", new StatusIntException(401));

        Assert.True(ChatClientErrorClassifier.IsConfigurationError(ex));
    }

    [Fact]
    public void UnrelatedExceptions_AreNotConfigurationErrors()
    {
        Assert.False(ChatClientErrorClassifier.IsConfigurationError(new InvalidOperationException("boom")));
        Assert.False(ChatClientErrorClassifier.IsConfigurationError(new TaskCanceledException()));
    }
}
