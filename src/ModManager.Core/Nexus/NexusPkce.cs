using System;
using System.Security.Cryptography;
using System.Text;

namespace ModManager.Core.Nexus;

/// <summary>PKCE (RFC 7636, S256) + CSRF state for the OAuth authorization-code flow.</summary>
public static class NexusPkce
{
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>A high-entropy code_verifier (96 random bytes -> 128 base64url chars).</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(96));

    /// <summary>code_challenge = base64url(SHA256(ASCII(verifier))).</summary>
    public static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>An opaque CSRF state token.</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Ordinal, length-safe comparison of the returned state to the expected one.</summary>
    public static bool StateMatches(string expected, string actual) =>
        !string.IsNullOrEmpty(expected) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual ?? string.Empty));
}
