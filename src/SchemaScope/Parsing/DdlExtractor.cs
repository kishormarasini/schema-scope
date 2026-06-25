using System.IO;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SchemaScope.Parsing;

public sealed class DdlExtractionResult
{
    public IReadOnlyList<DdlObject> Objects { get; init; } = Array.Empty<DdlObject>();
    public IReadOnlyList<string> ParseErrors { get; init; } = Array.Empty<string>();
}

public sealed class DdlExtractor
{
    public DdlExtractionResult Extract(string scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
        {
            return new DdlExtractionResult();
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);

        TSqlFragment fragment;
        IList<ParseError> parseErrors;
        using (var reader = new StringReader(scriptText))
        {
            fragment = parser.Parse(reader, out parseErrors);
        }

        var errors = parseErrors
            .Select(e => $"Line {e.Line}, Col {e.Column}: {e.Message}")
            .ToList();

        var visitor = new DdlVisitor(scriptText);
        fragment.Accept(visitor);

        var objects = visitor.Objects
            .GroupBy(o => (o.Kind, o.Schema, o.Parent, o.Name))
            .Select(g => g.First())
            .OrderBy(o => o.Kind)
            .ThenBy(o => o.Parent ?? string.Empty)
            .ThenBy(o => o.Name)
            .ToList();

        return new DdlExtractionResult
        {
            Objects = objects,
            ParseErrors = errors
        };
    }

    private sealed class DdlVisitor : TSqlFragmentVisitor
    {
        private readonly string _source;

        public List<DdlObject> Objects { get; } = new();

        public DdlVisitor(string source)
        {
            _source = source;
        }

        public override void ExplicitVisit(CreateTableStatement node)
        {
            var name = GetLastIdentifier(node.SchemaObjectName);
            if (string.IsNullOrWhiteSpace(name) || node.Definition is null)
            {
                return;
            }

            var schema = GetSchemaName(node.SchemaObjectName);
            var defText = SliceSource(node);

            Objects.Add(new DdlObject
            {
                Kind = DdlObjectKind.Table,
                Name = name,
                Schema = schema,
                DefinitionText = defText
            });

            EmitTableElements(name, schema, node.Definition, defText);
        }

        public override void ExplicitVisit(AlterTableAddTableElementStatement node)
        {
            var tableName = GetLastIdentifier(node.SchemaObjectName);
            if (string.IsNullOrWhiteSpace(tableName) || node.Definition is null)
            {
                return;
            }

            EmitTableElements(tableName, GetSchemaName(node.SchemaObjectName), node.Definition, SliceSource(node));
        }

        private void EmitTableElements(string tableName, string? schema, TableDefinition definition, string defText)
        {
            foreach (var col in definition.ColumnDefinitions)
            {
                if (col.ColumnIdentifier is null || string.IsNullOrWhiteSpace(col.ColumnIdentifier.Value))
                {
                    continue;
                }

                Objects.Add(new DdlObject
                {
                    Kind = DdlObjectKind.Column,
                    Name = col.ColumnIdentifier.Value,
                    Parent = tableName,
                    Schema = schema,
                    DefinitionText = defText,
                    ColumnType = BuildColumnTypeSpec(col)
                });

                foreach (var colConstraint in col.Constraints)
                {
                    AddConstraint(colConstraint.ConstraintIdentifier?.Value, tableName, schema, defText);
                }
            }

            foreach (var constraint in definition.TableConstraints)
            {
                AddConstraint(constraint.ConstraintIdentifier?.Value, tableName, schema, defText);
            }

            foreach (var index in definition.Indexes)
            {
                AddInlineIndex(index, tableName, schema, defText);
            }
        }

        private void AddConstraint(string? constraintName, string tableName, string? schema, string defText)
        {
            if (string.IsNullOrWhiteSpace(constraintName))
            {
                return;
            }

            Objects.Add(new DdlObject
            {
                Kind = DdlObjectKind.Constraint,
                Name = constraintName,
                Parent = tableName,
                Schema = schema,
                DefinitionText = defText
            });
        }

        private void AddInlineIndex(IndexDefinition index, string tableName, string? schema, string defText)
        {
            var indexName = index.Name?.Value;
            if (string.IsNullOrWhiteSpace(indexName))
            {
                return;
            }

            var keyCols = index.Columns
                .Select(GetColumnName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            var includeCols = index.IncludeColumns
                .Select(c => c.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Objects.Add(new DdlObject
            {
                Kind = DdlObjectKind.Index,
                Name = indexName!,
                Parent = tableName,
                Schema = schema,
                DefinitionText = defText,
                IndexKeys = string.Join(",", keyCols),
                IndexIncludes = string.Join(",", includeCols)
            });
        }

        public override void ExplicitVisit(CreateIndexStatement node)
        {
            var indexName = node.Name?.Value;
            var tableName = GetLastIdentifier(node.OnName);
            if (string.IsNullOrWhiteSpace(indexName) || string.IsNullOrWhiteSpace(tableName))
            {
                return;
            }

            var keyCols = node.Columns
                .Select(GetColumnName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            var includeCols = node.IncludeColumns
                .Select(c => c.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Objects.Add(new DdlObject
            {
                Kind = DdlObjectKind.Index,
                Name = indexName!,
                Parent = tableName,
                Schema = GetSchemaName(node.OnName),
                DefinitionText = SliceSource(node),
                IndexKeys = string.Join(",", keyCols),
                IndexIncludes = string.Join(",", includeCols)
            });
        }

        public override void ExplicitVisit(CreateProcedureStatement node) =>
            AddModule(DdlObjectKind.Procedure, node.ProcedureReference?.Name, node);

        public override void ExplicitVisit(AlterProcedureStatement node) =>
            AddModule(DdlObjectKind.Procedure, node.ProcedureReference?.Name, node);

        public override void ExplicitVisit(CreateOrAlterProcedureStatement node) =>
            AddModule(DdlObjectKind.Procedure, node.ProcedureReference?.Name, node);

        public override void ExplicitVisit(CreateViewStatement node) =>
            AddModule(DdlObjectKind.View, node.SchemaObjectName, node);

        public override void ExplicitVisit(AlterViewStatement node) =>
            AddModule(DdlObjectKind.View, node.SchemaObjectName, node);

        public override void ExplicitVisit(CreateOrAlterViewStatement node) =>
            AddModule(DdlObjectKind.View, node.SchemaObjectName, node);

        public override void ExplicitVisit(CreateTriggerStatement node) =>
            AddModule(DdlObjectKind.Trigger, node.Name, node);

        public override void ExplicitVisit(AlterTriggerStatement node) =>
            AddModule(DdlObjectKind.Trigger, node.Name, node);

        public override void ExplicitVisit(CreateOrAlterTriggerStatement node) =>
            AddModule(DdlObjectKind.Trigger, node.Name, node);

        public override void ExplicitVisit(CreateFunctionStatement node) =>
            AddModule(DdlObjectKind.Function, node.Name, node);

        public override void ExplicitVisit(AlterFunctionStatement node) =>
            AddModule(DdlObjectKind.Function, node.Name, node);

        public override void ExplicitVisit(CreateOrAlterFunctionStatement node) =>
            AddModule(DdlObjectKind.Function, node.Name, node);

        private void AddModule(DdlObjectKind kind, SchemaObjectName? schemaName, TSqlFragment node)
        {
            var name = GetLastIdentifier(schemaName);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            Objects.Add(new DdlObject
            {
                Kind = kind,
                Name = name,
                Schema = GetSchemaName(schemaName),
                DefinitionText = SliceSource(node)
            });
        }

        private string SliceSource(TSqlFragment node)
        {
            var tokens = node.ScriptTokenStream;
            if (tokens is null || node.FirstTokenIndex < 0 || node.LastTokenIndex < 0)
            {
                return string.Empty;
            }

            if (node.FirstTokenIndex >= tokens.Count || node.LastTokenIndex >= tokens.Count)
            {
                return string.Empty;
            }

            var first = tokens[node.FirstTokenIndex];
            var last = tokens[node.LastTokenIndex];
            var start = first.Offset;
            var end = last.Offset + (last.Text?.Length ?? 0);

            if (start < 0 || end <= start || end > _source.Length)
            {
                return string.Empty;
            }

            return _source.Substring(start, end - start);
        }

        private static string GetLastIdentifier(SchemaObjectName? name)
        {
            if (name is null)
            {
                return string.Empty;
            }
            var last = name.Identifiers.LastOrDefault();
            return last?.Value ?? string.Empty;
        }

        private static string? GetSchemaName(SchemaObjectName? name)
        {
            if (name is null || name.Identifiers.Count < 2)
            {
                return null;
            }
            var schema = name.Identifiers[^2]?.Value;
            return string.IsNullOrWhiteSpace(schema) ? null : schema;
        }

        private static string GetColumnName(ColumnWithSortOrder col)
        {
            var parts = col.Column?.MultiPartIdentifier?.Identifiers;
            var last = parts?.LastOrDefault();
            return last?.Value ?? string.Empty;
        }

        private static string BuildColumnTypeSpec(ColumnDefinition column)
        {
            if (column.DataType is not SqlDataTypeReference typeRef)
            {
                return BuildFallbackTypeSpec(column);
            }

            var typeName = typeRef.Name?.BaseIdentifier?.Value?.ToLowerInvariant() ?? string.Empty;
            var size = BuildSizeSuffix(typeName, typeRef);
            var nullability = BuildNullability(column);

            return $"{typeName}{size}|{nullability}";
        }

        private static string BuildFallbackTypeSpec(ColumnDefinition column)
        {
            var typeName = column.DataType switch
            {
                UserDataTypeReference udt => udt.Name?.BaseIdentifier?.Value?.ToLowerInvariant() ?? string.Empty,
                _ => string.Empty
            };
            return $"{typeName}|{BuildNullability(column)}";
        }

        private static string BuildSizeSuffix(string typeName, SqlDataTypeReference typeRef)
        {
            var sized = typeName is "varchar" or "char" or "varbinary" or "binary" or "nvarchar" or "nchar";
            var scaled = typeName is "decimal" or "numeric";

            if (!sized && !scaled)
            {
                return string.Empty;
            }

            var parameters = typeRef.Parameters;
            if (parameters is null || parameters.Count == 0)
            {
                return string.Empty;
            }

            if (sized)
            {
                var first = parameters[0];
                // (max) compares equal to the DB-side spec, which also reports (max)
                // for max_length = -1, for both n- and non-n string/binary types.
                if (first is MaxLiteral)
                {
                    return "(max)";
                }
                if (first is Literal lit && int.TryParse(lit.Value, out var length))
                {
                    return $"({length})";
                }
                return string.Empty;
            }

            var sb = new StringBuilder("(");
            var precision = parameters[0] is Literal p ? p.Value : "0";
            sb.Append(precision);
            if (parameters.Count > 1 && parameters[1] is Literal s)
            {
                sb.Append(',').Append(s.Value);
            }
            else
            {
                sb.Append(",0");
            }
            sb.Append(')');
            return sb.ToString();
        }

        private static string BuildNullability(ColumnDefinition column)
        {
            foreach (var constraint in column.Constraints)
            {
                if (constraint is NullableConstraintDefinition nullable)
                {
                    return nullable.Nullable ? "NULL" : "NOT NULL";
                }
            }
            return "NULL";
        }
    }
}
