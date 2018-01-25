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
        [Flags]
        public enum ExtractStatements
        {
            Default = 0,
            CustomContractRequires = 1,
            DebugAssert = 2,
            All = CustomContractRequires | DebugAssert
        }

        public const ExtractStatements ExtractStatementsKind = ExtractStatements.All;

        public const string DiagnosticId = "CR01_RetrieveCodeContractFromBase";
        private const string Title = "Contract.Requires can be retrieved from base type/contract class";
        private const string MessageFormat = "Requires can be retrieved from base type: \n {0}";
        private const string Description = "Contract.Requires can be retrieved from base type/contract class";
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
        private static IEnumerable<IMethodSymbol> GetOverridenMethods(IMethodSymbol method)
        {
            var curMethod = method;
            while (curMethod.IsOverride && curMethod.OverriddenMethod != null)
            {
                curMethod = curMethod.OverriddenMethod;
                yield return curMethod;
            }
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
                result.AddRange(GetOverridenMethods(method));

            result.AddRange(GetInterfaceImplementation(method));

            return result;
        }


        private static bool IsRequiresStatement(StatementSyntax statement)
        {
            return ContractStatementAnalyzer.ParseInvocation(statement, out var invocationInfo) &&
                invocationInfo.MethodNameAsString == "Requires" &&
                invocationInfo.IsContractType;
        }
        private static bool IsTurboRequiresStatement(StatementSyntax statement)
        {
            return ContractStatementAnalyzer.ParseInvocation(statement, out var invocationInfo) &&
                invocationInfo.MethodNameAsString == "Requires" &&
                invocationInfo.IsSpecialContractType;
        }
        private static bool IsDebugAssertStatement(StatementSyntax statement)
        {
            return ContractStatementAnalyzer.ParseInvocation(statement, out var invocationInfo) &&
                invocationInfo.MethodNameAsString == "Assert" &&
                invocationInfo.IsDebugType;
        }

        public static IEnumerable<StatementSyntax> ExtractContractRequires(MethodDeclarationSyntax method)
        {
            return method.Body.Statements.Where(o => IsRequiresStatement(o));
        }
        public static IEnumerable<StatementSyntax> ExtractTurboRequires(MethodDeclarationSyntax method)
        {
            return method.Body.Statements.Where(o => IsTurboRequiresStatement(o));
        }
        public static IEnumerable<StatementSyntax> ExtractDebugAssert(MethodDeclarationSyntax method)
        {
            return method.Body.Statements.Where(o => IsDebugAssertStatement(o));
        }
        public static IEnumerable<StatementSyntax> ExtractRequires(MethodDeclarationSyntax method, ExtractStatements extractStatements)
        {
            var statementsToCheck = method.Body.Statements.TakeWhile(o => o is ExpressionStatementSyntax);

            switch (extractStatements)
            {
                case ExtractStatements.CustomContractRequires:
                    return statementsToCheck.Where(o => IsRequiresStatement(o) || IsTurboRequiresStatement(o));
                case ExtractStatements.DebugAssert:
                    return statementsToCheck.Where(o => IsRequiresStatement(o) || IsDebugAssertStatement(o));
                case ExtractStatements.All:
                    return statementsToCheck.Where(o => IsRequiresStatement(o) || IsDebugAssertStatement(o) || IsTurboRequiresStatement(o));
                case ExtractStatements.Default:
                default:
                    return statementsToCheck.Where(o => IsRequiresStatement(o));
            }
        }
        public static IEnumerable<StatementSyntax> ExtractRequires(IMethodSymbol method, ExtractStatements extractStatements, CancellationToken token)
        {
            var syntax = method.DeclaringSyntaxReferences;
            if (syntax.Length != 1)
                return Array.Empty<StatementSyntax>();

            var methodSyntax = syntax[0].GetSyntax(token) as MethodDeclarationSyntax;
            if (methodSyntax == null)
                return Array.Empty<StatementSyntax>();

            return ExtractRequires(methodSyntax, extractStatements);
        }
        public static IEnumerable<StatementSyntax> ExtractRequires(IMethodSymbol method, CancellationToken token)
        {
            return ExtractRequires(method, ExtractStatements.Default, token);
        }

        private static bool HasRequires(IMethodSymbol method, CancellationToken token)
        {
            return ExtractRequires(method, ExtractStatements.Default, token).Any();
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
        private static bool IsEquivalentRequireStatements(StatementSyntax a, StatementSyntax b, bool smart)
        {
            if (!smart)
                return a.IsEquivalentTo(b);

            var aCond = ContractStatementAnalyzer.ParseInvocation(a, out var aInv) ? aInv.Condition : null;
            var bCond = ContractStatementAnalyzer.ParseInvocation(b, out var bInv) ? bInv.Condition : null;
            if (aCond == null || bCond == null)
                return false;

            return aCond.IsEquivalentTo(bCond);
        }
        private static void RemoveOverlappedStatements(List<StatementSyntax> list, List<StatementSyntax> other, bool smart)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < other.Count; j++)
                {
                    if (IsEquivalentRequireStatements(list[i], other[j], smart))
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }
        public static List<StatementSyntax> DeduplicateRequires(List<StatementSyntax> aggregatedRequires, List<StatementSyntax> presentedRequires, bool smart)
        {
            if (aggregatedRequires.Count == 0)
                return aggregatedRequires;
            List<StatementSyntax> result = new List<StatementSyntax>(aggregatedRequires);
            RemoveOverlappedStatements(result, presentedRequires, smart);
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

            context.CancellationToken.ThrowIfCancellationRequested();

            if (contractMethods.Count > 0)
            {
                var requireStatements = contractMethods.SelectMany(o => ExtractRequires(o, context.CancellationToken)).ToList();
                requireStatements = DeduplicateRequires(requireStatements, ExtractRequires(methodSyntax, ExtractStatementsKind).ToList(), ExtractStatementsKind != ExtractStatements.Default);
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
