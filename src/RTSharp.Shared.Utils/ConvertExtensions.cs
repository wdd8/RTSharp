using System;
using System.Collections.Generic;
using System.Text;

namespace RTSharp.Shared.Utils
{
    public static class ConvertExtensions
    {
        public static bool TryFromHexString(string hexString, out byte[] result)
        {
            var buffer = new byte[hexString.Length / 2];
            var res = Convert.FromHexString(hexString, buffer, out _, out _);
            result = buffer;

            if (res == System.Buffers.OperationStatus.Done) {
                return true;
            }
            return false;
        }
    }
}
