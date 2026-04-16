using System.Security.Cryptography;
using Alexandria.Business.Authentication;

namespace Alexandria.Tests.Auth;

[TestClass]
public sealed class TokenHelpersTests
{
    private static readonly byte[] Key = HMACSHA256.HashData("test-seed"u8.ToArray(), "test-purpose"u8.ToArray());

    [TestMethod]
    public void Mint_then_verify_round_trips_the_id()
    {
        var token = TokenHelpers.Mint("alice", Key, TimeSpan.FromMinutes(5));

        var ok = TokenHelpers.TryVerify(token, Key, out var id);

        Assert.IsTrue(ok);
        Assert.AreEqual("alice", id);
    }

    [TestMethod]
    public void Expired_token_fails_verification()
    {
        var token = TokenHelpers.Mint("alice", Key, TimeSpan.FromSeconds(-1));

        var ok = TokenHelpers.TryVerify(token, Key, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Tampered_signature_fails_verification()
    {
        var token = TokenHelpers.Mint("alice", Key, TimeSpan.FromMinutes(5));
        var dot = token.IndexOf('.');
        var sig = token[(dot + 1)..];
        var flipped = sig[0] == 'A' ? 'B' : 'A';
        var tampered = token[..(dot + 1)] + flipped + sig[1..];

        var ok = TokenHelpers.TryVerify(tampered, Key, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Tampered_payload_fails_verification()
    {
        var token = TokenHelpers.Mint("alice", Key, TimeSpan.FromMinutes(5));
        var dot = token.IndexOf('.');
        // Flip the first char of the payload — guaranteed to carry full 6 bits of data (no base64
        // padding bits at position 0), so decoded bytes change and HMAC verify fails.
        var first = token[0];
        var flipped = first == 'A' ? 'B' : 'A';
        var tampered = flipped + token[1..dot] + token[dot..];

        var ok = TokenHelpers.TryVerify(tampered, Key, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Wrong_key_fails_verification()
    {
        var token = TokenHelpers.Mint("alice", Key, TimeSpan.FromMinutes(5));
        var otherKey = HMACSHA256.HashData("test-seed"u8.ToArray(), "different-purpose"u8.ToArray());

        var ok = TokenHelpers.TryVerify(token, otherKey, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Missing_or_malformed_token_fails_verification()
    {
        Assert.IsFalse(TokenHelpers.TryVerify(null, Key, out _));
        Assert.IsFalse(TokenHelpers.TryVerify(string.Empty, Key, out _));
        Assert.IsFalse(TokenHelpers.TryVerify("no-dot-here", Key, out _));
        Assert.IsFalse(TokenHelpers.TryVerify(".leading-dot", Key, out _));
        Assert.IsFalse(TokenHelpers.TryVerify("trailing-dot.", Key, out _));
    }

    [TestMethod]
    public void Id_with_pipe_character_round_trips_correctly()
    {
        // The separator is the last '|' so ids containing '|' should survive — the payload parser
        // uses LastIndexOf to locate the expiry, not IndexOf.
        var token = TokenHelpers.Mint("alice|bob", Key, TimeSpan.FromMinutes(5));

        var ok = TokenHelpers.TryVerify(token, Key, out var id);

        Assert.IsTrue(ok);
        Assert.AreEqual("alice|bob", id);
    }
}
