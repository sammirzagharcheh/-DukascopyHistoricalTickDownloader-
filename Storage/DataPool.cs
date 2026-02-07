namespace HistoricalData.DataPool;

public sealed class DataPool
{
    public DataPool(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public string GetLocalPath(string instrument, int year, int month, int day, string fileName)
    {
        var directory = Path.Combine(RootPath, instrument, year.ToString("0000"), month.ToString("00"), day.ToString("00"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    public static bool HasValidFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length > 0;
    }
}
