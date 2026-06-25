using System.IO;

namespace SchemaScope.TestDb;

public sealed class TestDbScriptLocator
{
    public string FolderPath { get; }

    public TestDbScriptLocator(string folderPath)
    {
        FolderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
    }

    public bool FolderExists => Directory.Exists(FolderPath);

    public IReadOnlyList<FileInfo> List()
    {
        if (!FolderExists)
        {
            return Array.Empty<FileInfo>();
        }

        return new DirectoryInfo(FolderPath)
            .EnumerateFiles("*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
