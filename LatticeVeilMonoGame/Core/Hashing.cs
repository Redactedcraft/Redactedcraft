using System.Security.Cryptography;

namespace LatticeVeilMonoGame.Core;

public static class Hashing
{
    public static string Sha256File(string filePath)
    {
        filePath = (filePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath is required", nameof(filePath));

        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
