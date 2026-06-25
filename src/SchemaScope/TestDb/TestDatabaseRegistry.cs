using System.IO;
using System.Text.Json;

namespace SchemaScope.TestDb;

public sealed class TestDatabaseRegistry
{
    private readonly List<TestDatabase> _entries;

    public IReadOnlyList<TestDatabase> Entries => _entries;

    private TestDatabaseRegistry(List<TestDatabase> entries)
    {
        _entries = entries;
    }

    public static TestDatabaseRegistry Load()
    {
        try
        {
            if (!File.Exists(AppPaths.TestDbsRegistryFile))
            {
                return new TestDatabaseRegistry(new List<TestDatabase>());
            }

            var json = File.ReadAllText(AppPaths.TestDbsRegistryFile);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new TestDatabaseRegistry(new List<TestDatabase>());
            }

            var list = JsonSerializer.Deserialize<List<TestDatabase>>(json, SerializerOptions);
            return new TestDatabaseRegistry(list ?? new List<TestDatabase>());
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Failed to read test-database registry '{AppPaths.TestDbsRegistryFile}'", ex);
            return new TestDatabaseRegistry(new List<TestDatabase>());
        }
    }

    public bool Contains(string name) =>
        _entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool Add(TestDatabase entry)
    {
        if (Contains(entry.Name))
        {
            return false;
        }
        _entries.Add(entry);
        Save();
        return true;
    }

    public bool Remove(string name)
    {
        var idx = _entries.FindIndex(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            return false;
        }
        _entries.RemoveAt(idx);
        Save();
        return true;
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureDirectories();
            var json = JsonSerializer.Serialize(_entries, SerializerOptions);
            File.WriteAllText(AppPaths.TestDbsRegistryFile, json);
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Failed to save test-database registry '{AppPaths.TestDbsRegistryFile}'", ex);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
