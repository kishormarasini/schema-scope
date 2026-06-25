namespace SchemaScope.Parsing;

public sealed record DdlObject
{
    public required DdlObjectKind Kind { get; init; }
    public required string Name { get; init; }
    public string? Parent { get; init; }

    public string? Schema { get; init; }

    public string DefinitionText { get; init; } = string.Empty;

    public string? ColumnType { get; init; }
    public string IndexKeys     { get; init; } = string.Empty;
    public string IndexIncludes { get; init; } = string.Empty;
}
