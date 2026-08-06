using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace Licensr.Sdk.Tests;

/// <summary>
/// Known-answer tests against RFC 8032 §7.1 Ed25519 test vectors — mirrors
/// <c>sdks/cpp/test/test_ed25519.cpp</c>, but exercises the exact
/// BouncyCastle API surface <c>Offline/OfflineTokenVerifier.cs</c> depends
/// on (Init/BlockUpdate/VerifySignature) rather than a hand-rolled
/// implementation, since this SDK verifies EdDSA via BouncyCastle rather
/// than vendoring its own (see Licensr.Sdk.csproj).
/// </summary>
public class Ed25519SignatureTests
{
    private static bool Verify(string publicKeyHex, string signatureHex, byte[] message)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(Convert.FromHexString(publicKeyHex), 0));
        verifier.BlockUpdate(message, 0, message.Length);
        return verifier.VerifySignature(Convert.FromHexString(signatureHex));
    }

    [Fact]
    public void Rfc8032Vector1_EmptyMessage()
    {
        Assert.True(Verify(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a",
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b",
            Array.Empty<byte>()));
    }

    [Fact]
    public void Rfc8032Vector2_OneByteMessage()
    {
        Assert.True(Verify(
            "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c",
            "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00",
            [0x72]));
    }

    [Fact]
    public void Rfc8032Vector3_TwoByteMessage()
    {
        Assert.True(Verify(
            "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025",
            "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a",
            [0xaf, 0x82]));
    }

    [Fact]
    public void RejectsFlippedMessageByte()
    {
        Assert.False(Verify(
            "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c",
            "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00",
            [0x73])); // Flip the message byte that was signed.
    }
}
