using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace ContractFix.EliminateCallsToContract
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class EliminateCallsToContractAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CR08_EliminateCallsToContractMethods";
        private const string Title = "Contract should be removed from source code";
        private const string MessageFormat = "Should be removed from source code";
        private const string Description = "Contract should be removed from source code";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        internal static HashSet<string> MethodNamesToRemove { get; } = new HashSet<string>()
        {
            nameof(System.Diagnostics.Contracts.Contract.EndContractBlock),
            nameof(System.Diagnostics.Contracts.Contract.Ensures),
            nameof(System.Diagnostics.Contracts.Contract.EnsuresOnThrow),
            nameof(System.Diagnostics.Contracts.Contract.Invariant)
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeInvocationOp, OperationKind.ExpressionStatement);
        }


        private static bool IsCodeContractToRemove(Compilation compilation, IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.Kind != SymbolKind.Method || !invocation.TargetMethod.IsStatic)
                return false;

            if (!compilation.IsEqualTypes(invocation.TargetMethod.ContainingType, typeof(System.Diagnostics.Contracts.Contract)))
                return false;

            if (invocation.TargetMethod.IsGenericMethod)
                return false;

            return MethodNamesToRemove.Contains(invocation.TargetMethod.Name);
        }

        private static bool IsInvariantMethod(ISymbol containingSymbol)
        {
            if (containingSymbol is IMethodSymbol method)
            {
                var attrib = method.GetAttributes();
                return attrib.Any(o => o.AttributeClass.Name == nameof(System.Diagnostics.Contracts.ContractInvariantMethodAttribute));
            }
            return false;
        }
        private static bool IsContractClass(ISymbol containingSymbol)
        {
            if (containingSymbol is IMethodSymbol method &&
                method.ContainingType is INamedTypeSymbol typeSmb)
            {
                var attrib = typeSmb.GetAttributes();
                return attrib.Any(o => o.AttributeClass.Name == nameof(System.Diagnostics.Contracts.ContractClassForAttribute));
            }
            return false;
        }


        private static void AnalyzeInvocationOp(OperationAnalysisContext context)
        {
            var statement = (IExpressionStatementOperation)context.Operation;
            var invocation = statement.Operation as IInvocationOperation;
            if (invocation == null)
                return;

            if (!IsCodeContractToRemove(context.Compilation, invocation))
                return;
            if (IsInvariantMethod(context.ContainingSymbol) || IsContractClass(context.ContainingSymbol))
                return;


            var invocationSyntax = (InvocationExpressionSyntax)invocation.Syntax;
            if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
                context.ReportDiagnostic(Diagnostic.Create(Rule, statement.Syntax.GetLocation()));
        }
    }
}
