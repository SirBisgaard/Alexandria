using System.Security.Cryptography;
using System.Text;

namespace Alexandria.Business.Extensions;

public static class GuidExtensions
{
    private static Guid seed;

    public static void SetNamespace(Guid ns) => seed = ns;

    extension(Guid)
    {
        /// <summary>
        /// Generates a deterministic UUID v5 from the given <paramref name="name"/> using SHA-1 and
        /// a fixed Alexandria-specific namespace (<c>cd4631e2-04f2-4cc2-b938-b34c1e3c85a0</c>).
        /// The same <paramref name="name"/> will always produce the same GUID, following RFC 9562 §5.5.
        /// Byte order is swapped before hashing and after construction to account for .NET's
        /// mixed-endian GUID layout.
        /// </summary>
        /// <param name="name">The input string to hash (e.g. a normalized "title:authors" key).</param>
        /// <returns>A deterministic UUID v5 with version nibble 0101 and variant bits 10.</returns>
        public static Guid GenerateUuidV5(string name)
        {
            if (seed == Guid.Empty)
                throw new InvalidOperationException("UUID namespace has not been initialized. Call SetNamespace() first.");

            var namespaceBytes = seed.ToByteArray();
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