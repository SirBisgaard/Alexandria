namespace Alexandria.Business.Interfaces;

public interface IKepubifyConverter
{
    Task Convert(string inputPath, string outputPath);
}