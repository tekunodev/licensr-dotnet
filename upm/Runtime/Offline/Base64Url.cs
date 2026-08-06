using System.Text;

namespace Licensr.Sdk.Offline;

/// <summary>RFC 4648 §5 base64url (no padding) — used throughout JOSE (JWT/JWKS).</summary>
internal static class Base64Url
{
    internal static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (padded.Length % 4)) % 4;
        return Convert.FromBase64String(padded + new string('=', padding));
    }

    internal static string DecodeToString(string value) => Encoding.UTF8.GetString(Decode(value));

    internal static string Encode(byte[] value) =>
        Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
