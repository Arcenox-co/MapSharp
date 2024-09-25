using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace MapSharp;

internal class MappingInfo
{
    public INamedTypeSymbol SourceType { get; set; }
    public INamedTypeSymbol DestinationType { get; set; }
    public List<PropertyMapping> PropertyMappings { get; set; } = new();
    public bool HasReverseMap { get; set; } = false;
    public List<string> FileUsings { get; set; } = new();
}
internal class PropertyMapping
{
    public string DestinationPropertyName { get; set; }
    public string MappingExpression { get; set; }
    public bool IsAsync { get; set; }
}
