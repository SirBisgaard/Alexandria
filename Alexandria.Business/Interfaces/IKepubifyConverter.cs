namespace Alexandria.Business.Interfaces;

public interface IKepubifyConverter
{
    Task ConvertAsync(string inputPath, string outputPath);
}