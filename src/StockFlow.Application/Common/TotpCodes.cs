using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace StockFlow.Application.Common;

public static class TotpCodes
{
    // RFC 6238, HMAC-SHA1, 30-second steps, six decimal digits.
    public static string Generate(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        int offset = hash[^1] & 15;
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (value % 1000000).ToString("D6");
    }

    public static long? Verify(byte[] secret, string? code, long step, long? lastStep)
    {
        if (code is null || code.Length != 6 || code.Any(c => c < '0' || c > '9'))
        {
            return null;
        }

        for (long candidate = step - 1; candidate <= step + 1; candidate++)
        {
            if (candidate > (lastStep ?? -1) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Generate(secret, candidate)), Encoding.ASCII.GetBytes(code)))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string Base32(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Append(alphabet[(buffer >> bits) & 31]);
            }
        }

        if (bits > 0)
        {
            result.Append(alphabet[(buffer << (5 - bits)) & 31]);
        }

        return result.ToString();
    }
}
