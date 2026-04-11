namespace Alexandria.DataAccess.Externals.Interfaces;

public interface IKepubifyConverter
{
    Task Convert(string inputPath, string outputPath);
}
