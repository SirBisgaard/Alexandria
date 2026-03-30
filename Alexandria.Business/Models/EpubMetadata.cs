using System.Security.Cryptography;
using System.Text;

namespace Alexandria.Business.Models;

public class EpubMetadata
{
    private static readonly Guid Namespace = Guid.Parse("cd4631e2-04f2-4cc2-b938-b34c1e3c85a0");

    public Guid Uuid => GenerateUuidV5(Namespace, $"{Title.Trim().ToLowerInvariant()}:{string.Join(",", Authors.Select(a => a.Trim().ToLowerInvariant()))}");
    public string Title { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string? PublicationDate { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? SeriesName { get; set; }
    public int? SeriesIndex { get; set; }
    public bool CoverExtracted { get; set; }
    public string? CoverFilePath { get; set; }
    
    private static Guid GenerateUuidV5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);
        var result = new byte[16];
        Array.Copy(hash, result, 16);
        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);
        SwapGuidByteOrder(result);
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