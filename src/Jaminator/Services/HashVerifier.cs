using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Jaminator.Services
{
    public static class HashVerifier
    {
        public static string Sha256OfFile(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return ToHex(sha.ComputeHash(fs));
        }

        public static bool Matches(string filePath, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256)) return true; // skipped
            return string.Equals(Sha256OfFile(filePath), expectedSha256.Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
