using System.Security.Cryptography;

namespace RTSharp.Shared.Utils
{
    public static class StringUtils
    {
        private const string CHARS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890-_";

        public static string GetRandomString(int size) => RandomNumberGenerator.GetString(CHARS, size);
    }
}
