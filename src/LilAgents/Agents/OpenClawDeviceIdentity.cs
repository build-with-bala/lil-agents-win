using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace LilAgents.Agents;

/// <summary>
/// Ed25519 device identity for the OpenClaw gateway handshake.
///
/// The device id is the SHA-256 hex fingerprint of the raw public key, matching the
/// derivation the OpenClaw Node gateway performs — so a key generated here authenticates
/// exactly as the macOS build's CryptoKit Curve25519 key does.
///
/// BouncyCastle rather than NSec: .NET has no built-in Ed25519, and NSec would drag in a
/// native libsodium binary that complicates arm64 single-file publishing.
/// </summary>
public sealed class OpenClawDeviceIdentity
{
    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly Ed25519PublicKeyParameters _publicKey;

    public string DeviceId { get; }

    private OpenClawDeviceIdentity(Ed25519PrivateKeyParameters privateKey)
    {
        _privateKey = privateKey;
        _publicKey = privateKey.GeneratePublicKey();
        DeviceId = Convert.ToHexString(SHA256.HashData(_publicKey.GetEncoded())).ToLowerInvariant();
    }

    public string PublicKeyBase64Url => Base64UrlEncode(_publicKey.GetEncoded());

    public string Sign(string payload)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        var bytes = Encoding.UTF8.GetBytes(payload);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return Base64UrlEncode(signer.GenerateSignature());
    }

    /// <summary>
    /// Reproduces OpenClaw's <c>buildDeviceAuthPayload</c> string. v2 appends the
    /// server-issued nonce; v1 is used when the gateway did not send a challenge.
    /// </summary>
    public string BuildAuthPayload(string clientId, string clientMode, string role,
        IReadOnlyList<string> scopes, long signedAtMs, string token, string? nonce)
    {
        var version = nonce is not null ? "v2" : "v1";
        var parts = new List<string>
        {
            version, DeviceId, clientId, clientMode, role,
            string.Join(",", scopes), signedAtMs.ToString(), token
        };
        if (version == "v2") parts.Add(nonce ?? "");
        return string.Join("|", parts);
    }

    // MARK: - Persistence

    public static OpenClawDeviceIdentity LoadOrCreate()
    {
        var stored = Settings.Current.OpenClawDeviceKey;
        if (!string.IsNullOrEmpty(stored))
        {
            try
            {
                var bytes = Convert.FromBase64String(stored);
                if (bytes.Length == Ed25519PrivateKeyParameters.KeySize)
                {
                    return new OpenClawDeviceIdentity(new Ed25519PrivateKeyParameters(bytes, 0));
                }
            }
            catch (FormatException)
            {
                // Corrupt key material — fall through and mint a fresh identity.
            }
        }

        var seed = RandomNumberGenerator.GetBytes(Ed25519PrivateKeyParameters.KeySize);
        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);

        Settings.Current.OpenClawDeviceKey = Convert.ToBase64String(privateKey.GetEncoded());
        Settings.Current.Save();

        return new OpenClawDeviceIdentity(privateKey);
    }

    internal static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).Replace("+", "-").Replace("/", "_").TrimEnd('=');
}
