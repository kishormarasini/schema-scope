using System.IO;

namespace SchemaScope.Sql;

public sealed record VersionScript(int Number, FileInfo File, string Label);
