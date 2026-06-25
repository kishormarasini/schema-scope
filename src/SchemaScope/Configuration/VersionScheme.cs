using System.Globalization;
using System.Text.RegularExpressions;

namespace SchemaScope.Configuration;

public sealed class VersionScheme
{
    public string FilePattern { get; set; } = @"^1\.0\.0\.(\d+)\.sql$";

    public string FileNameFormat { get; set; } = "1.0.0.{0}.sql";

    public string LabelFormat { get; set; } = "1.0.0.{0}";

    public Regex CompilePattern() =>
        new(FilePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Label(int number) =>
        string.Format(CultureInfo.InvariantCulture, LabelFormat, number);

    public string FileName(int number) =>
        string.Format(CultureInfo.InvariantCulture, FileNameFormat, number);
}
