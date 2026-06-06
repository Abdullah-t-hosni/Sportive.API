using Microsoft.Extensions.Configuration;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Sportive.API.Utils;

public class EncryptionHelper
{
    private readonly byte[] _keyV1;
    private readonly byte[] _searchSecretBytes;

    public EncryptionHelper(IConfiguration config)
    {
        // 1. Load Encryption Key
        var keyStr = config["Security:EncryptionKeyV1"];
        if (string.IsNullOrEmpty(keyStr) || keyStr == "${ENCRYPTION_KEY_V1}")
        {
            keyStr = Environment.GetEnvironmentVariable("ENCRYPTION_KEY_V1");
        }
        if (string.IsNullOrEmpty(keyStr))
        {
            throw new InvalidOperationException("Security:EncryptionKeyV1 is not configured.");
        }

        try
        {
            _keyV1 = Convert.FromBase64String(keyStr);
        }
        catch
        {
            var bytes = Encoding.UTF8.GetBytes(keyStr);
            _keyV1 = new byte[32];
            Array.Copy(bytes, _keyV1, Math.Min(bytes.Length, 32));
        }

        if (_keyV1.Length != 32)
        {
            throw new InvalidOperationException("EncryptionKeyV1 must be 32 bytes long.");
        }

        // 2. Load Search Secret
        var searchSecret = config["Security:SearchSecret"];
        if (string.IsNullOrEmpty(searchSecret) || searchSecret == "${SEARCH_SECRET}")
        {
            searchSecret = Environment.GetEnvironmentVariable("SEARCH_SECRET");
        }
        if (string.IsNullOrEmpty(searchSecret))
        {
            throw new InvalidOperationException("Security:SearchSecret is not configured.");
        }
        _searchSecretBytes = Encoding.UTF8.GetBytes(searchSecret);
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        var tag = new byte[16];
        var cipherBytes = new byte[plainBytes.Length];

        using (var aesGcm = new AesGcm(_keyV1, 16))
        {
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var versionBytes = BitConverter.GetBytes((int)1); // Version 1

        var result = new byte[4 + 12 + 16 + cipherBytes.Length];
        Buffer.BlockCopy(versionBytes, 0, result, 0, 4);
        Buffer.BlockCopy(nonce, 0, result, 4, 12);
        Buffer.BlockCopy(tag, 0, result, 16, 16);
        Buffer.BlockCopy(cipherBytes, 0, result, 32, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        try
        {
            var cipherData = Convert.FromBase64String(cipherText);
            if (cipherData.Length < 32) return cipherText; // Not a valid cipher block

            var version = BitConverter.ToInt32(cipherData, 0);
            if (version != 1)
            {
                throw new NotSupportedException($"Encryption key version {version} is not supported.");
            }

            var nonce = new byte[12];
            var tag = new byte[16];
            var cipherBytes = new byte[cipherData.Length - 32];

            Buffer.BlockCopy(cipherData, 4, nonce, 0, 12);
            Buffer.BlockCopy(cipherData, 16, tag, 0, 16);
            Buffer.BlockCopy(cipherData, 32, cipherBytes, 0, cipherBytes.Length);

            var plainBytes = new byte[cipherBytes.Length];

            using (var aesGcm = new AesGcm(_keyV1, 16))
            {
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            // Decryption fallback if string was plain text
            return cipherText;
        }
    }

    public string ComputeSearchHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var normalized = value.Trim().ToLowerInvariant();

        using var hmac = new HMACSHA256(_searchSecretBytes);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
