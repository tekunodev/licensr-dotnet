using System;

namespace Licensr.Sdk;

/// <summary>
/// The API responded with a non-2xx status and a parsed <c>detail.error</c> body.
/// </summary>
/// <remarks>
/// <see cref="Code"/> is one of the documented error codes in
/// <c>contract/conformance.yaml</c> (e.g. <c>license_inactive</c>,
/// <c>origin_not_allowed</c>, <c>rate_limit_exceeded</c>). Branch on
/// <see cref="Code"/>, not on <see cref="Exception.Message"/> — the message
/// is for humans and may change without notice.
/// </remarks>
public sealed class LicensrApiException : Exception
{
    public int Status { get; }

    public string Code { get; }

    /// <summary>Present on some 429 responses when the server sends <c>Retry-After</c>.</summary>
    public int? RetryAfterSeconds { get; }

    public LicensrApiException(int status, string code, string message, int? retryAfterSeconds = null)
        : base(message)
    {
        Status = status;
        Code = code;
        RetryAfterSeconds = retryAfterSeconds;
    }
}

/// <summary>The request never got a response — network failure, timeout, or retries were exhausted.</summary>
public sealed class LicensrNetworkException : Exception
{
    public LicensrNetworkException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>An offline token failed EdDSA verification, was malformed, or has expired.</summary>
public sealed class LicensrTokenVerificationException : Exception
{
    public LicensrTokenVerificationException(string message)
        : base(message)
    {
    }
}
