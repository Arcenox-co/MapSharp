using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MapSharp
{
    internal class MappingSyntaxReceiver : ISyntaxReceiver
    {
        public List<ClassDeclarationSyntax> CandidateClasses { get; } = [];
        public string[] CandidateConditions { get; } = ["IProfile", "Profile"];
        
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            // Collect all class declarations
            if (syntaxNode is ClassDeclarationSyntax classDeclaration)
            {
                // Check if the class inherits from Profile
                foreach (var baseType in classDeclaration.BaseList?.Types ?? new SeparatedSyntaxList<BaseTypeSyntax>())
                {
                    if (baseType.Type is IdentifierNameSyntax identifierName &&
                        CandidateConditions.Contains(identifierName.Identifier.Text))
                    {
                        CandidateClasses.Add(classDeclaration);
                        break;
                    }
                }
            }
        }
    }
}