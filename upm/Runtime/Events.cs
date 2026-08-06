namespace Licensr.Sdk;

/// <summary>Fired on every retried request, before the retry delay.</summary>
public sealed class RetryEventArgs : EventArgs
{
    public required string Method { get; init; }

    public required int Attempt { get; init; }

    public required int DelayMs { get; init; }
}

/// <summary>Fired whenever a <see cref="LicensrClient"/> method throws, right before the exception propagates.</summary>
public sealed class LicensrErrorEventArgs : EventArgs
{
    public required string Method { get; init; }

    public required Exception Exception { get; init; }
}
