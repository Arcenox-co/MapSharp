using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace MapSharp
{
    [Generator]
    public class MapSharpGenerator : ISourceGenerator
    {
        // Diagnostic Descriptors
        private static readonly DiagnosticDescriptor ProfileSymbolsNotFoundDiagnostic = new(
            "GEN001",
            "Profile symbols not found",
            "Could not find Profile or IProfile symbols. Ensure they are correctly defined and accessible.",
            "SourceGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
        
        private static readonly DiagnosticDescriptor CreateMapSymbolNotFoundDiagnostic = new(
            "GEN002",
            "CreateMap Symbol Not Found",
            "Could not retrieve symbol information for a CreateMap invocation.",
            "SourceGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DuplicateCreateMappingDiagnostic = new(
            "GEN003",
            "Duplicate CreateMapping detected",
            "Duplicate mapping detected: source {0} to destination {1} is defined twice.",
            "SourceGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MalformedCreateMapInvocationDiagnostic = new(
            "GEN004",
            "Malformed CreateMap Invocation",
            "A CreateMap invocation was found but could not extract source and destination types.",
            "SourceGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor FailedToExtractPropertyMappingDiagnostic = new(
            "GEN005",
            "Failed to Extract Property Mapping",
            "Could not extract property mapping from a ForMember invocation.",
            "SourceGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor IncorrectForMemberArgumentsDiagnostic = new(
            "GEN006",
            "Incorrect ForMember Arguments",
            "A ForMember invocation does not have exactly two arguments.",
            "SourceGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingLambdaExpressionDiagnostic = new(
            "GEN007",
            "Missing Lambda Expression",
            "A ForMember invocation is missing a lambda expression for mapping.",
            "SourceGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MissingLambdaBodyDiagnostic = new(
            "GEN008",
            "Missing Lambda Body",
            "A ForMember invocation's lambda expression is missing a body.",
            "SourceGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor CannotAccessNonPublicMethodDiagnostic = new(
            "GEN009",
            "Cannot Access Non-Public Method",
            "Lambda function cannot be executed because it is accessing a non-public method.",
            "SourceGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor MappingSkippedDiagnostic = new(
            "GEN010",
            "Mapping Skipped",
            "Mapping skipped: The member {0} of {1} has a different type and cannot be mapped with the member {2} of {3}.",
            "SourceGenerator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register the syntax receiver
            context.RegisterForSyntaxNotifications(() => new MappingSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // Retrieve the syntax receiver
            if (context.SyntaxReceiver is not MappingSyntaxReceiver receiver)
                return;

            var compilation = context.Compilation;

            // Attempt to get the symbols for Profile and IProfile
            var profileSymbol = compilation.GetTypeByMetadataName("MapSharp.Profile");
            var iProfileSymbol = compilation.GetTypeByMetadataName("MapSharp.IProfile");

            if (profileSymbol is null && iProfileSymbol is null)
            {
                // Report a diagnostic if Profile symbols are not found
                context.ReportDiagnostic(Diagnostic.Create(
                    ProfileSymbolsNotFoundDiagnostic,
                    Location.None));
                return;
            }

            var allMappings = new List<MappingInfo?>();

            foreach (var classDeclaration in receiver.CandidateClasses)
            {
                var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);

                var classSymbol = ModelExtensions.GetDeclaredSymbol(semanticModel, classDeclaration) as INamedTypeSymbol;

                if (classSymbol is null)
                    continue;

                // Check if the class inherits from Profile or implements IProfile
                if ((profileSymbol is not null && classSymbol.InheritsFrom(profileSymbol)) ||
                    (iProfileSymbol is not null && classSymbol.ImplementsInterface(iProfileSymbol)))
                {
                    var mappings = FindCreateMapCalls(classDeclaration, semanticModel, context);

                    foreach (var mapping in mappings)
                    {
                        if (allMappings.Any(x => 
                                x.SourceType == mapping.SourceType &&
                                x.DestinationType == x.DestinationType))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                DuplicateCreateMappingDiagnostic,
                                classDeclaration.GetLocation(),
                                mapping.SourceType.Name,
                                mapping.DestinationType.Name));
                        }
                        else
                        {
                            allMappings.Add(mapping);
                        }
                    }
                }
            }

            // Build mapping dictionary
            var mappingDictionary = allMappings.ToDictionary(
                m => (m.SourceType, m.DestinationType),
                m => m.DestinationType);

            // Generate extension methods
            foreach (var mappingInfo in allMappings)
            {
                var extensionCode = GenerateExtensionMethod(mappingInfo, mappingDictionary, context);
                if (!string.IsNullOrEmpty(extensionCode))
                {
                    var fileName = $"{mappingInfo.SourceType.Name}_To_{mappingInfo.DestinationType.Name}.g.cs";
                    context.AddSource(fileName, SourceText.From(extensionCode, Encoding.UTF8));
                }
            }
        }

        // Private Helper Methods

        /// <summary>
        /// Determines if an invocation is a CreateMap call originating from the Profile class.
        /// </summary>
        private static bool IsCreateMapInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel,
            GeneratorExecutionContext context)
        {
            var methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
                GenericNameSyntax genericName => genericName.Identifier.Text,
                _ => null
            };

            if (methodName is not "CreateMap") return false; // Not a CreateMap call
            
            // Retrieve the method symbol to ensure it's the correct method
            if (ModelExtensions.GetSymbolInfo(semanticModel, invocation).Symbol is IMethodSymbol methodSymbol)
            {
                var containingType = methodSymbol.ContainingType;
                
                // Traverse the inheritance chain to check if the containing type inherits from "Profile"
                while (containingType is not null)
                {
                    if (containingType.Name == "Profile")
                        return true;

                    containingType = containingType.BaseType;
                }
            }
            else
            {
                // Report diagnostic if method symbol is not found
                context.ReportDiagnostic(Diagnostic.Create(
                    CreateMapSymbolNotFoundDiagnostic,
                    invocation.GetLocation()));
            }

            return false; // Not a CreateMap call
        }

        /// <summary>
        /// Identifies and extracts all CreateMap invocations and their associated ForMember calls.
        /// </summary>
        private List<MappingInfo> FindCreateMapCalls(ClassDeclarationSyntax classDeclaration,
            SemanticModel semanticModel, GeneratorExecutionContext context)
        {
            var mappings = new List<MappingInfo>();

            // Locate the Configure method within the class
            var configureMethod = classDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "Configure");

            if (configureMethod is null)
                return mappings;

            // Identify all CreateMap invocations within the Configure method
            var createMapInvocations = configureMethod.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation => IsCreateMapInvocation(invocation, semanticModel, context));

            foreach (var createMapInvocation in createMapInvocations)
            {
                var mappingInfo = new MappingInfo
                {
                    FileUsings = FileUsingsRetriever.GetOriginalFileUsings(classDeclaration)
                };

                // Extract source and destination types from CreateMap<TSource, TDestination>()
                var methodSymbol = ModelExtensions.GetSymbolInfo(semanticModel, createMapInvocation).Symbol as IMethodSymbol;

                if (methodSymbol is not null && methodSymbol.TypeArguments.Length == 2)
                {
                    mappingInfo.SourceType = methodSymbol.TypeArguments[0] as INamedTypeSymbol;
                    mappingInfo.DestinationType = methodSymbol.TypeArguments[1] as INamedTypeSymbol;
                }
                else
                {
                    // Report a diagnostic if type arguments are not present
                    context.ReportDiagnostic(Diagnostic.Create(
                        MalformedCreateMapInvocationDiagnostic,
                        createMapInvocation.GetLocation()));
                    continue;
                }

                // Extract all ForMember invocations chained to this CreateMap
                var forMemberInvocations = GetForMemberInvocations(createMapInvocation);
                foreach (var forMemberInvocation in forMemberInvocations)
                {
                    var propertyMapping = ExtractPropertyMapping(forMemberInvocation, semanticModel, context);
                    if (propertyMapping is not null)
                    {
                        mappingInfo.PropertyMappings.Add(propertyMapping);
                    }
                    else
                    {
                        // Report a diagnostic if PropertyMapping extraction fails
                        context.ReportDiagnostic(Diagnostic.Create(
                            FailedToExtractPropertyMappingDiagnostic,
                            forMemberInvocation.GetLocation()));
                    }
                }

                // Check if ReverseMap is called
                if (HasSpecificMethodMap(createMapInvocation, "ReverseMap"))
                {
                    mappingInfo.HasReverseMap = true;
                }

                mappings.Add(mappingInfo);
            }

            return mappings;
        }

        /// <summary>
        /// Checks for a specific method call in the member access chain.
        /// </summary>
        private static bool HasSpecificMethodMap(InvocationExpressionSyntax createMapInvocation, string specificMethod)
        {
            var currentInvocation = createMapInvocation;

            while (true)
            {
                if (currentInvocation.Parent is MemberAccessExpressionSyntax memberAccess)
                {
                    if (memberAccess.Name.Identifier.Text == specificMethod)
                        return true;

                    if (memberAccess.Parent is InvocationExpressionSyntax nextInvocation)
                        currentInvocation = nextInvocation;
                    else
                        break;
                }
                else
                {
                    break;
                }
            }

            return false;
        }

        /// <summary>
        /// Extracts all ForMember invocations chained after a given CreateMap invocation.
        /// </summary>
        private static List<InvocationExpressionSyntax> GetForMemberInvocations(InvocationExpressionSyntax createMapInvocation)
        {
            var forMemberInvocations = new List<InvocationExpressionSyntax>();
            var currentInvocation = createMapInvocation;

            while (currentInvocation.Parent is MemberAccessExpressionSyntax memberAccess &&
                   memberAccess.Name.Identifier.Text == "ForMember")
            {
                if (memberAccess.Parent is InvocationExpressionSyntax forMemberInvocation)
                {
                    forMemberInvocations.Add(forMemberInvocation);
                    currentInvocation = forMemberInvocation;
                }
                else
                {
                    break;
                }
            }

            return forMemberInvocations;
        }

        /// <summary>
        /// Extracts property mapping details from a ForMember invocation.
        /// </summary>
        private PropertyMapping ExtractPropertyMapping(InvocationExpressionSyntax forMemberInvocation,
            SemanticModel semanticModel, GeneratorExecutionContext context)
        {
            var arguments = forMemberInvocation.ArgumentList.Arguments;
            if (arguments.Count == 2)
            {
                // Extract the destination property name
                if (arguments[0].Expression is not LambdaExpressionSyntax destLambda)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        FailedToExtractPropertyMappingDiagnostic,
                        forMemberInvocation.GetLocation()));
                    return null;
                }

                var destPropName = GetPropertyNameFromLambda(destLambda);

                // Extract the mapping expression
                if (arguments[1].Expression is not LambdaExpressionSyntax mapFromLambda)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        FailedToExtractPropertyMappingDiagnostic,
                        forMemberInvocation.GetLocation()));
                    return null;
                }

                var mappingExpression = GetMappingExpression(mapFromLambda, semanticModel, context);

                bool isAsync = IsAsyncExpression(mapFromLambda, semanticModel);

                if (destPropName is not null && mappingExpression is not null)
                {
                    return new PropertyMapping
                    {
                        DestinationPropertyName = destPropName,
                        MappingExpression = mappingExpression,
                        IsAsync = isAsync
                    };
                }
            }
            else
            {
                // Report diagnostic for incorrect number of arguments
                context.ReportDiagnostic(Diagnostic.Create(
                    IncorrectForMemberArgumentsDiagnostic,
                    forMemberInvocation.GetLocation()));
            }

            return null;
        }

        /// <summary>
        /// Extracts the property name from a lambda expression used in ForMember.
        /// </summary>
        private static string GetPropertyNameFromLambda(LambdaExpressionSyntax lambda)
        {
            return lambda.Body switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
                IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
                _ => null
            };
        }

        /// <summary>
        /// Extracts and adjusts the mapping expression from a lambda expression used in ForMember.
        /// </summary>
        private static string GetMappingExpression(LambdaExpressionSyntax lambdaExpression, SemanticModel semanticModel,
            GeneratorExecutionContext context)
        {
            if (lambdaExpression is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MissingLambdaExpressionDiagnostic,
                    lambdaExpression?.GetLocation() ?? Location.None));
                return null;
            }

            // Determine the lambda parameter
            IParameterSymbol parameterSymbol = null;
            string newParameterName = "source";

            if (lambdaExpression is SimpleLambdaExpressionSyntax simpleLambda)
            {
                parameterSymbol = semanticModel.GetDeclaredSymbol(simpleLambda.Parameter);
            }
            else if (lambdaExpression is ParenthesizedLambdaExpressionSyntax parenthesizedLambda &&
                     parenthesizedLambda.ParameterList.Parameters.Count > 0)
            {
                parameterSymbol = semanticModel.GetDeclaredSymbol(parenthesizedLambda.ParameterList.Parameters[0]);
            }

            // Get the body of the lambda expression
            var body = lambdaExpression.Body;
            if (body is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MissingLambdaBodyDiagnostic,
                    lambdaExpression.GetLocation()));
                return null;
            }

            string adjustedExpression = string.Empty;

            // Apply the renaming only if parameterSymbol is found
            if (parameterSymbol != null)
            {
                // Create an instance of the rewriter
                var rewriter = new ParameterRenamer(semanticModel, parameterSymbol, newParameterName);

                // Rewrite the lambda body
                var newBody = rewriter.Visit(body);

                // Depending on the body type, format the adjusted expression
                switch (newBody)
                {
                    case InvocationExpressionSyntax methodInvocation:
                        if (IsLocalMethod(semanticModel, (body as InvocationExpressionSyntax), out var methodAccessibility))
                        {
                            if (methodAccessibility != Accessibility.Public)
                            {
                                context.ReportDiagnostic(Diagnostic.Create(
                                    CannotAccessNonPublicMethodDiagnostic,
                                    lambdaExpression.GetLocation()));

                                return "default";
                            }

                            adjustedExpression = $"<destination> destination.{methodInvocation}";
                        }
                        else
                        {
                            adjustedExpression = $"{methodInvocation}";
                        }

                        break;

                    case AwaitExpressionSyntax awaitExpression:
                        var originalInvocation = body as AwaitExpressionSyntax;
                        var innerInvocation = originalInvocation.Expression as InvocationExpressionSyntax;
                        if (innerInvocation is not null && IsLocalMethod(semanticModel, innerInvocation,
                                out var awaitMethodAccessibility))
                        {
                            if (awaitMethodAccessibility != Accessibility.Public)
                            {
                                context.ReportDiagnostic(Diagnostic.Create(
                                    CannotAccessNonPublicMethodDiagnostic,
                                    lambdaExpression.GetLocation()));

                                return "default";
                            }

                            adjustedExpression =
                                $"<destination> {awaitExpression.ToString().Replace("await ", "await destination.")}";
                        }
                        else
                        {
                            adjustedExpression = awaitExpression.ToString();
                        }

                        break;

                    case BlockSyntax blockSyntax:
                        if (lambdaExpression is SimpleLambdaExpressionSyntax simpleLambdaExpression &&
                            simpleLambdaExpression.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword) ||
                            (lambdaExpression is ParenthesizedLambdaExpressionSyntax parenthesizedLambda &&
                             parenthesizedLambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)))
                        {
                            if (blockSyntax.ToString().Contains("await "))
                            {
                                if (blockSyntax.ToString().Contains(newParameterName))
                                    adjustedExpression = $"<extBlockMethod><containsSource><async>{blockSyntax.ToString()}";
                                else
                                    adjustedExpression = $"<extBlockMethod><async>{blockSyntax.ToString()}";
                            }

                            break;
                        }

                        if (blockSyntax.ToString().Contains(newParameterName))
                            adjustedExpression = $"<extBlockMethod><containsSource>{blockSyntax.ToString()}";
                        else
                            adjustedExpression = $"<extBlockMethod>{blockSyntax.ToString()}";
                        break;

                    default:
                        adjustedExpression = newBody.ToString();
                        break;
                }
            }
            else
            {
                // Fallback logic if parameterSymbol is not found
                adjustedExpression = body.ToString().Replace($"{newParameterName}.", "source.");
            }

            return adjustedExpression;
    }

        /// <summary>
        /// Determines if an invocation is a local method and retrieves its accessibility.
        /// </summary>
        private static bool IsLocalMethod(SemanticModel semanticModel, InvocationExpressionSyntax methodInvocation,
            out Accessibility methodAccessibility)
        {
            methodAccessibility = Accessibility.NotApplicable;

            var symbolInfo = semanticModel.GetSymbolInfo(methodInvocation.Expression);
            var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

            if (methodSymbol is null)
                return false;

            methodAccessibility = methodSymbol.DeclaredAccessibility;

            var invokedMethodContainingType = methodSymbol.ContainingType;

            var currentSymbol = semanticModel.GetEnclosingSymbol(methodInvocation.SpanStart);
            var currentContainingType = currentSymbol?.ContainingType;

            if (currentContainingType is null)
                return false;

            // Compare the containing types to determine if the method is part of the same class
            return SymbolEqualityComparer.Default.Equals(
                invokedMethodContainingType,
                currentContainingType
            );
        }

        /// <summary>
        /// Generates the extension method code based on the mapping information.
        /// </summary>
        private string GenerateExtensionMethod(
            MappingInfo mappingInfo,
            Dictionary<(INamedTypeSymbol, INamedTypeSymbol), INamedTypeSymbol> mappingDictionary,
            GeneratorExecutionContext context)
        {
            var sourceType = mappingInfo.SourceType;
            var destinationType = mappingInfo.DestinationType;

            if (sourceType is null || destinationType is null)
                return null;

            if (SymbolEqualityComparer.Default.Equals(sourceType, destinationType))
            {
                // Prevent mapping the same type to itself
                return null;
            }

            var sourceNamespace = sourceType.ContainingNamespace.ToDisplayString();
            var destinationNamespace = destinationType.ContainingNamespace.ToDisplayString();

            var usings = new HashSet<string>
            {
                "using System;",
                "using System.Collections.Generic;",
                "using System.Linq;"
            };

            foreach (var u in mappingInfo.FileUsings)
            {
                var isSameAsUsing = u
                    .Replace("using ", string.Empty)
                    .TrimStart('\n', '\r')
                    .TrimEnd(';', '\n', '\r');
                    
                if(isSameAsUsing != destinationNamespace)
                    usings.Add(u);
            }

            // Conditionally add the source namespace if it's different from the destination namespace
            if (!string.IsNullOrWhiteSpace(sourceNamespace) && sourceNamespace != destinationNamespace)
            {
                 usings.Add($"using {sourceNamespace};");
            }

            var usingsCode = string.Join(" ", usings);

            var assignments = new StringBuilder();
            var extAssignments = new StringBuilder();
            var extBlockMethods = new StringBuilder();
            // Add explicit ForMember mappings
            foreach (var destProp in GetPublicProperties(destinationType))
            {
                var propAssignment = GeneratePropertyAssignment(destProp, sourceType, mappingInfo, mappingDictionary, context);
                if (!string.IsNullOrEmpty(propAssignment) && !propAssignment.Contains("<destination>") && !propAssignment.Contains("<extBlockMethod>"))
                {
                    assignments.AppendLine($"{destProp.Name} = {propAssignment}");
                }
                else if (!string.IsNullOrEmpty(propAssignment) && propAssignment.Contains("<destination>"))
                {
                    propAssignment = propAssignment.Substring(0, propAssignment.Length - 1);
                    extAssignments.AppendLine($"destination.{destProp.Name} = {propAssignment.Replace("<destination>", string.Empty)};");
                }
                else if (!string.IsNullOrEmpty(propAssignment) && propAssignment.Contains("<extBlockMethod>"))
                {
                    var isAsync = propAssignment.Contains("<async>");
                    var containsSource = propAssignment.Contains("<containsSource>");
                    
                    assignments.AppendLine($"{destProp.Name} = {(isAsync ? "await " : "")}Get{destProp.Name}{(isAsync ? "Async" : "")}({(containsSource ? $"source" : "")}),");

                    extBlockMethods.AppendLine();
                    extBlockMethods.AppendLine($"private static {(isAsync ? $"async Task<{destProp.Type.ToDisplayString()}>" : destProp.Type.ToDisplayString())} Get{destProp.Name}{(isAsync ? "Async" : "")}({(containsSource ? $"{sourceType.ToDisplayString()} source" : "")})");
                    extBlockMethods.AppendLine($"{propAssignment.Replace("<extBlockMethod>", string.Empty).Replace("<containsSource>", string.Empty).Replace("<async>", string.Empty).TrimEnd(',')}");
                }
            }

            if (assignments.Length == 0)
                return null;

            // Remove the trailing comma from the last property assignment
            var trimmedAssignments = assignments.ToString().TrimEnd(',', '\n', '\r');
            var trimmedExtAssignments = extAssignments.ToString().TrimEnd('\n', '\r');
            var trimmedExtBlockMethods = extBlockMethods.ToString();

            bool hasExtAssignments = !string.IsNullOrWhiteSpace(trimmedExtAssignments);
            var methodIsAsync = mappingInfo.PropertyMappings.Any(x => x.IsAsync);

            // Construct the generated code with the conditional using directives and proper indentation
            var codeBuilder = new StringBuilder();

            if (methodIsAsync)
            {
                codeBuilder.AppendLine("using System.Threading.Tasks;");
            }

            codeBuilder.AppendLine(usingsCode);
            codeBuilder.AppendLine();
            codeBuilder.AppendLine($"namespace {destinationNamespace}");
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine($"public static class {sourceType.Name}_To_{destinationType.Name}_MappingExtension");
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine($"public static {(methodIsAsync ? "async Task<" : "")}{destinationType.ToDisplayString()}{(methodIsAsync ? ">" : "")} To{destinationType.Name}{(methodIsAsync ? "Async" : "")}(this {sourceType.ToDisplayString()} source)");
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine("if (source == null) throw new ArgumentNullException(nameof(source));");
            codeBuilder.AppendLine();
            codeBuilder.AppendLine(hasExtAssignments ? $"var destination = new {destinationType.ToDisplayString()}" : $"return new {destinationType.ToDisplayString()}");
            codeBuilder.AppendLine("{");
            codeBuilder.Append(trimmedAssignments);
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("};");
            if (hasExtAssignments)
            {
                codeBuilder.AppendLine();
                codeBuilder.AppendLine(trimmedExtAssignments);
                codeBuilder.AppendLine();
                codeBuilder.AppendLine("return destination;");
            }
            codeBuilder.AppendLine("}");
            if (!string.IsNullOrWhiteSpace(trimmedExtBlockMethods))
            {
                codeBuilder.Append(trimmedExtBlockMethods);
            }
            codeBuilder.AppendLine("}");
            codeBuilder.AppendLine("}");
            
            return FormatCode(codeBuilder.ToString());
        }

        /// <summary>
        /// Generates the assignment for a single property in the mapping.
        /// </summary>
        private static string GeneratePropertyAssignment(
            IPropertySymbol destProp,
            INamedTypeSymbol sourceType,
            MappingInfo mappingInfo,
            Dictionary<(INamedTypeSymbol, INamedTypeSymbol), INamedTypeSymbol> mappingDictionary,
            GeneratorExecutionContext context)
        {
            // Handle custom mappings from ForMember configurations
            var customMapping = mappingInfo.PropertyMappings.FirstOrDefault(pm => pm.DestinationPropertyName == destProp.Name);

            if (customMapping is not null)
            {
                return $"{customMapping.MappingExpression},";
            }

            if (!mappingInfo.HasReverseMap)
                return string.Empty;

            var sourceProp = GetPublicProperties(sourceType).FirstOrDefault(sp => sp.Name == destProp.Name);

            if (sourceProp is not null)
            {
                // Check if the property is a complex type with a mapping
                if (sourceProp.Type is INamedTypeSymbol sourcePropTypeSymbol &&
                    destProp.Type is INamedTypeSymbol destinationPropTypeSymbol &&
                    mappingDictionary.TryGetValue((sourcePropTypeSymbol, destinationPropTypeSymbol), out var nestedDestType))
                {
                    // Generate code to call the nested extension method
                    return $"source.{sourceProp.Name}?.To{nestedDestType.Name}(),";
                }

                if (IsCollectionType(sourceProp.Type) && IsCollectionType(destProp.Type))
                {
                    // Handle collections of complex types
                    var sourceItemType = GetItemType(sourceProp.Type);
                    var destItemType = GetItemType(destProp.Type);

                    if (SymbolEqualityComparer.Default.Equals(sourceItemType, destItemType))
                    {
                        return $"source.{sourceProp.Name},";
                    }

                    if (sourceItemType is INamedTypeSymbol sourceItemTypeSymbol &&
                        destItemType is INamedTypeSymbol destinationItemTypeSymbol &&
                        mappingDictionary.TryGetValue((sourceItemTypeSymbol, destinationItemTypeSymbol), out var nestedItemDestType))
                    {
                        string conversionMethod = GetConversionMethod(destProp.Type);
                        if (IsArray(destProp.Type))
                            return $"source.{sourceProp.Name}?.Select(item => item.To{nestedItemDestType.Name}()).ToArray(),";

                        return $"source.{sourceProp.Name}?.Select(item => item.To{nestedItemDestType.Name}()){conversionMethod},";
                    }

                    if (!SymbolEqualityComparer.Default.Equals(sourceProp.Type, destProp.Type))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            MappingSkippedDiagnostic,
                            Location.None,
                            sourceProp.Name,
                            mappingInfo.SourceType.Name,
                            destProp.Name,
                            mappingInfo.DestinationType.Name));
                        return null;
                    }

                    return $"source.{sourceProp.Name},";
                }

                // Direct assignment for simple types
                return $"source.{sourceProp.Name},";
            }

            // Property not found or cannot be mapped
            return null;
        }

        /// <summary>
        /// Determines the appropriate conversion method based on the destination type.
        /// Prioritizes specific collection types over general ones.
        /// </summary>
        private static string GetConversionMethod(ITypeSymbol destType)
        {
            return destType switch
            {
                var t when IsDirectlyList(t) => ".ToList()",
                var t when IsEnumerable(t) => "",
                _ => ".ToList()",
            };
        }

        /// <summary>
        /// Retrieves all public, instance properties of a given type.
        /// </summary>
        private static List<IPropertySymbol> GetPublicProperties(INamedTypeSymbol typeSymbol)
        {
            return typeSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic)
                .ToList();
        }

        /// <summary>
        /// Determines if a type symbol represents a collection type.
        /// </summary>
        private static bool IsCollectionType(ITypeSymbol type)
        {
            return (type.AllInterfaces.Any(i => i.ToDisplayString() == "System.Collections.IEnumerable") &&
                   type is INamedTypeSymbol { TypeArguments.Length: 1 }) || IsArray(type);
        }

        /// <summary>
        /// Checks if the given type is an array type.
        /// </summary>
        private static bool IsArray(ITypeSymbol typeSymbol)
        {
            return typeSymbol is IArrayTypeSymbol;
        }

        /// <summary>
        /// Checks if the given type directly implements IList<T> or ICollection<T>.
        /// </summary>
        private static bool IsDirectlyList(ITypeSymbol typeSymbol)
        {
            var originalDef = typeSymbol.OriginalDefinition.ToDisplayString();
            return originalDef.Contains("System.Collections.Generic.IList") || originalDef.Contains("System.Collections.Generic.ICollection");
        }

        /// <summary>
        /// Checks if the given type implements IEnumerable<T>.
        /// </summary>
        private static bool IsEnumerable(ITypeSymbol typeSymbol)
        {
            return typeSymbol.OriginalDefinition.ToDisplayString().Contains("System.Collections.Generic.IEnumerable");
        }

        /// <summary>
        /// Retrieves the item type from a collection type symbol.
        /// </summary>
        private static ITypeSymbol GetItemType(ITypeSymbol collectionType)
        {
            return collectionType switch
            {
                INamedTypeSymbol namedType when namedType.TypeArguments.Length == 1 => namedType.TypeArguments[0],
                IArrayTypeSymbol arrayType => arrayType.ElementType,
            };
        }

        /// <summary>
        /// Determines if a lambda expression is asynchronous.
        /// </summary>
        private static bool IsAsyncExpression(LambdaExpressionSyntax lambda, SemanticModel semanticModel)
        {
            // Check if the expression contains an 'await' keyword
            if (lambda.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
                return true;

            // Alternatively, check if the return type is Task or Task<T>
            var typeInfo = semanticModel.GetTypeInfo(lambda.Body);
            var returnType = typeInfo.ConvertedType;

            return returnType?.Name == "Task" &&
                   returnType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
        }

        private static string FormatCode(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            
            // Format the syntax tree
            var workspace = new AdhocWorkspace();
            var formattedRoot = Formatter.Format(root, workspace);

            return formattedRoot.ToFullString();
        }
    }
}