namespace Alexandria.DataAccess.Externals;

public interface IKepubifyConverter
{
    Task Convert(string inputPath, string outputPath);
}
