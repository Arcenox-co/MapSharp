using System.Linq;
using Microsoft.CodeAnalysis;

namespace MapSharp;


internal static class SymbolExtensions
{
    public static bool InheritsFrom(this INamedTypeSymbol symbol, INamedTypeSymbol baseType)
    {
        while (symbol != null && symbol.TypeKind != TypeKind.Error)
        {
            if (SymbolEqualityComparer.Default.Equals(symbol, baseType))
                return true;

            symbol = symbol.BaseType;
        }
        return false;
    }

    public static bool ImplementsInterface(this INamedTypeSymbol symbol, INamedTypeSymbol interfaceType)
    {
        return symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType));
    }
}
