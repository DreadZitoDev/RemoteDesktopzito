using System.Security.Cryptography;
using System.Text;

namespace RemoteClanker.Shared;

public static class Auth
{
    public const int NONCE_SIZE = 32;

    public static byte[] CreateNonce() => RandomNumberGenerator.GetBytes(NONCE_SIZE);

    public static byte[] ComputeProof(string password, byte[] nonce) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(password), nonce);

    public static bool Verify(string password, byte[] nonce, byte[] proof) =>
        CryptographicOperations.FixedTimeEquals(ComputeProof(password, nonce), proof);
}
