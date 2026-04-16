namespace Alexandria.Business.KoboStoreProxy.Models.Store;

public class KoboStoreAudiobookManifest
{
    public KoboStoreAudiobookDrm Drm { get; set; } = new();
    public List<KoboStoreAudiobookNavigation> Navigation { get; set; } = [];
    public List<KoboStoreAudiobookSpineEntry> Spine { get; set; } = [];
}

public class KoboStoreAudiobookDrm
{
    public string DrmType { get; set; } = "";
    public List<object> Keys { get; set; } = [];
}

public class KoboStoreAudiobookNavigation
{
    public double Offset { get; set; }
    public int PartId { get; set; }
    public string Title { get; set; } = "";
}

public class KoboStoreAudiobookSpineEntry
{
    public double Bitrate { get; set; }
    public double Duration { get; set; }
    public int Id { get; set; }
    public string MediaType { get; set; } = "";
    public string Url { get; set; } = "";
    public string FileExtension { get; set; } = "";
}
