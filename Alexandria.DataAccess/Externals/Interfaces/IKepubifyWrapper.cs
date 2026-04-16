namespace Alexandria.DataAccess.Externals.Interfaces;

public interface IKepubifyWrapper
{
    Task Convert(string inputPath, string outputPath);
}
