using System.IO;
using System.Text.RegularExpressions;
using SchemaScope.Configuration;

namespace SchemaScope.Sql;

public sealed class VersionScriptLocator
{
    private readonly DirectoryInfo? _folder;
    private readonly VersionScheme _scheme;
    private readonly Regex _pattern;

    public VersionScriptLocator(string versionFolder, VersionScheme scheme)
    {
        _folder = string.IsNullOrWhiteSpace(versionFolder) ? null : new DirectoryInfo(versionFolder);
        _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
        _pattern = scheme.CompilePattern();
    }

    public bool FolderExists => _folder?.Exists ?? false;

    public string FolderPath => _folder?.FullName ?? string.Empty;

    public string Label(int number) => _scheme.Label(number);

    public IReadOnlyList<VersionScript> GetInRange(int from, int to)
    {
        if (_folder is null || !_folder.Exists)
        {
            return Array.Empty<VersionScript>();
        }

        var results = new List<VersionScript>();
        foreach (var file in _folder.EnumerateFiles("*.sql"))
        {
            var match = _pattern.Match(file.Name);
            if (!match.Success || match.Groups.Count < 2)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[1].Value, out var number))
            {
                continue;
            }

            if (from > 0 && number < from)
            {
                continue;
            }
            if (to > 0 && number > to)
            {
                continue;
            }

            results.Add(new VersionScript(number, file, _scheme.Label(number)));
        }

        return results.OrderBy(x => x.Number).ToList();
    }

    public VersionScript? GetSingle(int number)
    {
        if (_folder is null || !_folder.Exists)
        {
            return null;
        }

        var path = Path.Combine(_folder.FullName, _scheme.FileName(number));
        var info = new FileInfo(path);
        return info.Exists ? new VersionScript(number, info, _scheme.Label(number)) : null;
    }
}
