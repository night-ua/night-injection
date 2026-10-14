using System.Reflection;
using System.Text;
using NSec.Cryptography;

namespace NightInjection.Infrastructure.Networking;

public sealed class PackageSignatureVerifier
{
    private readonly PublicKey? _key;

    public PackageSignatureVerifier(string? rawPublicKeyBase64 = null)
    {
        var configured = rawPublicKeyBase64 ?? typeof(PackageSignatureVerifier).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static a => a.Key == "DarkDevil.Ed25519PublicKey")?.Value;
        if (!string.IsNullOrEmpty(configured))
        {
            _key = PublicKey.Import(SignatureAlgorithm.Ed25519,
                Convert.FromBase64String(configured), KeyBlobFormat.RawPublicKey);
        }
    }

    public bool RequiresSignature => _key is not null;

    public bool Verify(HttpResponseMessage response, string appId, ReadOnlySpan<byte> digest)
    {
        if (_key is null) return false;
        if (!response.Headers.TryGetValues("X-DarkDevil-AppId", out var ids)
            || ids.Count() != 1 || ids.First() != appId
            || !response.Headers.TryGetValues("X-DarkDevil-Signature", out var values)) return false;
        var signatures = values.ToArray();
        if (signatures.Length != 1 || signatures[0].Length != 88) return false;
        try
        {
            var signature = Convert.FromBase64String(signatures[0]);
            return signature.Length == 64 && SignatureAlgorithm.Ed25519.Verify(_key,
                Message(appId, digest), signature);
        }
        catch (FormatException) { return false; }
    }

    public static byte[] Message(string appId, ReadOnlySpan<byte> digest) =>
        Encoding.UTF8.GetBytes($"DarkDevil-Package-v1\n{appId}\n{Convert.ToHexString(digest).ToLowerInvariant()}\n");
}
