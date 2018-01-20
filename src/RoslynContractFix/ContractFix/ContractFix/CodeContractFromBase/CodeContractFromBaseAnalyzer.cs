using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Linq;
using System.Threading;

namespace ContractFix.CodeContractFromBase
{

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CodeContractFromBaseAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CodeContractFromBaseRetrieve";
        private const string Title = "Contract should be retrieved from base type";
        private const string MessageFormat = "Requires should be retrieved from base type: \n {0}";
        private const string Description = "should be retrieved from base type";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterCodeBlockAction(AnalyzeMethodDeclaration);
        }


        private static IEnumerable<IMethodSymbol> GetInterfaceImplementation(IMethodSymbol method)
        {
            return method.ContainingType.AllInterfaces.SelectMany(@interface => @interface.GetMembers().OfType<IMethodSymbol>()).
                Where(interfaceMethod => method.ContainingType.FindImplementationForInterfaceMember(interfaceMethod).Equals(method));
        }

        private static IEnumerable<AttributeData> GetContractClassAttribute(ITypeSymbol type)
        {
            return type.GetAttributes().Where(o => o.AttributeClass.Name == "ContractClassAttribute");
        }
        private static ITypeSymbol GetContractClass(AttributeData attrib)
        {
            if (attrib.AttributeClass.Name != "ContractClassAttribute")
                return null;
            var argument = attrib.ConstructorArguments[0];
            if (argument.Value is INamedTypeSymbol namedType && namedType.IsUnboundGenericType)
                return namedType.ConstructedFrom;
            return argument.Value as ITypeSymbol;
        }
        private static IMethodSymbol GetContractClassMethod(ITypeSymbol contractClassType, IMethodSymbol baseTypeMethod)
        {
            if (baseTypeMethod.ContainingType.TypeKind == TypeKind.Interface)
            {
                if (contractClassType is INamedTypeSymbol namedcontractClassType && namedcontractClassType.IsGenericType)
                {
                    if (baseTypeMethod.ContainingType is INamedTypeSymbol baseNamed && baseNamed.IsGenericType)
                    {
                        var constructed = baseNamed.ConstructedFrom.Construct(namedcontractClassType.TypeArguments.ToArray());
                        baseTypeMethod = constructed.GetMembers().OfType<IMethodSymbol>().First(o => o.OriginalDefinition.Equals(baseTypeMethod.OriginalDefinition));              
                    }

                    return contractClassType.FindImplementationForInterfaceMember(baseTypeMethod) as IMethodSymbol;
                }
 
                return contractClassType.FindImplementationForInterfaceMember(baseTypeMethod) as IMethodSymbol;
            }
            else if (baseTypeMethod.ContainingType.TypeKind == TypeKind.Class)
            {
                if (contractClassType is INamedTypeSymbol namedcontractClassType && namedcontractClassType.IsGenericType)
                    return contractClassType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(o => o.IsOverride && o.OverriddenMethod.OriginalDefinition.Equals(baseTypeMethod.OriginalDefinition));

                return contractClassType.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(o => o.IsOverride && o.OverriddenMethod.Equals(baseTypeMethod));
            }

            return null;
        }

        /// <summary>
        /// Возвращает методы из класса котнтрактов
        /// </summary>
        /// <param name="baseTypeMethod"></param>
        /// <returns></returns>
        public static IEnumerable<IMethodSymbol> GetContractTypeMethods(IMethodSymbol baseTypeMethod, IMethodSymbol originalMethod)
        {
            var attributes = GetContractClassAttribute(baseTypeMethod.ContainingType).ToList();
            if (attributes.Count == 0)
                return Array.Empty<IMethodSymbol>();
            var contractClasses = attributes.Select(o => GetContractClass(o)).Where(o => o != null && !o.Equals(originalMethod.ContainingType)).ToList();
            if (contractClasses.Count == 0)
                return Array.Empty<IMethodSymbol>();

            return contractClasses.Select(o => GetContractClassMethod(o, baseTypeMethod)).Where(o => o != null);
        }

        /// <summary>
        /// Возвращает метод, если он виртуальный
        /// </summary>
        /// <param name="baseTypeMethod"></param>
        /// <param name="originalMethod"></param>
        /// <returns></returns>
        public static IEnumerable<IMethodSymbol> GetVirtualMethodsWithContracts(IMethodSymbol baseTypeMethod, IMethodSymbol originalMethod)
        {
            if (baseTypeMethod.IsVirtual)
                return new IMethodSymbol[] { baseTypeMethod };

            return Array.Empty<IMethodSymbol>();
        }

        /// <summary>
        /// Возвращает  методы базового типа, которые переопределяются данным методом
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public static List<IMethodSymbol> GetBaseTypeOverridingMethod(IMethodSymbol method)
        {
            List<IMethodSymbol> result = new List<IMethodSymbol>();

            if (method.IsOverride)
                result.Add(method.OverriddenMethod);

            result.AddRange(GetInterfaceImplementation(method));

            return result;
        }


        private static bool IsRequiresStatement(StatementSyntax statement)
        {
            if (statement is ExpressionStatementSyntax exprStatement &&
                exprStatement.Expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is SimpleNameSyntax nameSyntax &&
                nameSyntax.Identifier.ValueText == nameof(System.Diagnostics.Contracts.Contract.Requires))
            {
                var memberAccessLeftSideStr = memberAccess.Expression.ToString();
                if (memberAccessLeftSideStr == "Contract" || memberAccessLeftSideStr.EndsWith(".Contract"))
                    return true;
            }

            return false;
        }

        public static IEnumerable<StatementSyntax> ExtractRequires(MethodDeclarationSyntax method)
        {
            return method.Body.Statements.Where(o => IsRequiresStatement(o));
        }
        public static IEnumerable<StatementSyntax> ExtractRequires(IMethodSymbol method, CancellationToken token)
        {
            var syntax = method.DeclaringSyntaxReferences;
            if (syntax.Length != 1)
                return Array.Empty<StatementSyntax>();

            var methodSyntax = syntax[0].GetSyntax(token) as MethodDeclarationSyntax;
            if (methodSyntax == null)
                return Array.Empty<StatementSyntax>();

            return ExtractRequires(methodSyntax);
        }

        private static bool HasRequires(IMethodSymbol method, CancellationToken token)
        {
            return ExtractRequires(method, token).Any();
        }

        private static void RemoveDuplicatedStatements(List<StatementSyntax> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (list[j].IsEquivalentTo(list[i]))
                    {
                        list.RemoveAt(j);
                        j--;
                    }
                }
            }
        }
        private static void RemoveOverlappedStatements(List<StatementSyntax> list, List<StatementSyntax> other)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < other.Count; j++)
                {
                    if (list[i].IsEquivalentTo(other[j]))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }
        public static List<StatementSyntax> DeduplicateRequires(List<StatementSyntax> aggregatedRequires, List<StatementSyntax> presentedRequires)
        {
            if (aggregatedRequires.Count == 0)
                return aggregatedRequires;
            List<StatementSyntax> result = new List<StatementSyntax>(aggregatedRequires);
            RemoveOverlappedStatements(result, presentedRequires);
            RemoveDuplicatedStatements(result);
            return result;
        }



        private static void AnalyzeMethodDeclaration(CodeBlockAnalysisContext context)
        {
            MethodDeclarationSyntax methodSyntax = context.CodeBlock as MethodDeclarationSyntax;
            IMethodSymbol method = context.OwningSymbol as IMethodSymbol;
            if (method == null || methodSyntax == null)
                return;

            var baseTypeOverridingMethods = GetBaseTypeOverridingMethod(method);
            var contractMethods = baseTypeOverridingMethods.SelectMany(o => GetContractTypeMethods(o, method)).ToList();
            contractMethods.AddRange(baseTypeOverridingMethods.SelectMany(o => GetVirtualMethodsWithContracts(o, method)));

            if (contractMethods.Count > 0)
            {
                var requireStatements = contractMethods.SelectMany(o => ExtractRequires(o, context.CancellationToken)).ToList();
                requireStatements = DeduplicateRequires(requireStatements, ExtractRequires(methodSyntax).ToList());
                if (requireStatements.Count > 0)
                {
                    StringBuilder bldr = new StringBuilder();
                    foreach (var st in requireStatements)
                        bldr.AppendLine(st.ToString());

                    context.ReportDiagnostic(Diagnostic.Create(Rule, methodSyntax.Identifier.GetLocation(), bldr.ToString()));
                }
            }
        }
    }
}
