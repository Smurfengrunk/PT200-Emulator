using System.IO;
public interface IDataPathProvider
{
    string BasePath { get; }
    string CharTablesPath { get; }
}

public class DataPathProvider : IDataPathProvider
{
    public string BasePath { get; }
    public string CharTablesPath => Path.Combine(BasePath, "Data", "CharTables");

    public DataPathProvider(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentException("Base path is required.", nameof(basePath));
        BasePath = basePath;
    }
}