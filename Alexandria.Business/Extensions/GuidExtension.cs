using System.Security.Cryptography;
using System.Text;

namespace Alexandria.Business.Extensions;

public static class GuidExtensions
{
    private static readonly Guid Namespace = Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0");

    extension(Guid)
    {
        public static Guid GenerateUuidV5(string name)
        {
            var namespaceBytes = Namespace.ToByteArray();
            Guid.SwapGuidByteOrder(namespaceBytes);
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);
            var result = new byte[16];
            Array.Copy(hash, result, 16);
            result[6] = (byte)((result[6] & 0x0F) | 0x50);
            result[8] = (byte)((result[8] & 0x3F) | 0x80);
            Guid.SwapGuidByteOrder(result);
            return new Guid(result);
        }

        private static void SwapGuidByteOrder(byte[] guid)
        {
            (guid[3], guid[0]) = (guid[0], guid[3]);
            (guid[2], guid[1]) = (guid[1], guid[2]);
            (guid[5], guid[4]) = (guid[4], guid[5]);
            (guid[7], guid[6]) = (guid[6], guid[7]);
        }
    }
}