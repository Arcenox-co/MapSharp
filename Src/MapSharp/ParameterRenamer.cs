

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MapSharp;
public class ParameterRenamer : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    private readonly IParameterSymbol _parameterSymbol;
    private readonly string _newName;

    public ParameterRenamer(SemanticModel semanticModel, IParameterSymbol parameterSymbol, string newName)
    {
        _semanticModel = semanticModel;
        _parameterSymbol = parameterSymbol;
        _newName = newName;
    }

    public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
    {
        // Get the symbol info for the identifier
        var symbolInfo = _semanticModel.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol;

        // Check if the symbol matches the parameter symbol
        if (SymbolEqualityComparer.Default.Equals(symbol, _parameterSymbol))
        {
            // Replace with the new identifier
            return SyntaxFactory.IdentifierName(_newName)
                .WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode VisitParameter(ParameterSyntax node)
    {
        if (SymbolEqualityComparer.Default.Equals(_semanticModel.GetDeclaredSymbol(node), _parameterSymbol))
        {
            // Rename the parameter
            return node.WithIdentifier(SyntaxFactory.Identifier(_newName))
                .WithTriviaFrom(node);
        }

        return base.VisitParameter(node);
    }
}