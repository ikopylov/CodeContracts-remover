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

namespace ContractFix.ContractForAllToEnumerable
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ContractForAllToEnumerableAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CR07_ContractForAllToEnumerableReplace";
        private const string Title = "Contract.ForAll and Contract.Exists can be replaced with Enumerable.All and Enumerable.Any";
        private const string MessageFormat = "Contract.ForAll and Contract.Exists can be replaced with Enumerable.All and Enumerable.Any";
        private const string Description = "Contract.ForAll and Contract.Exists can be replaced with Enumerable.All and Enumerable.Any";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        internal static HashSet<string> MethodNamesToFix { get; } = new HashSet<string>()
        {
            nameof(System.Diagnostics.Contracts.Contract.ForAll),
            nameof(System.Diagnostics.Contracts.Contract.Exists)
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeInvocationOp, OperationKind.Invocation);
        }


        private static bool IsCodeContractToReplace(Compilation compilation, IInvocationOperation invocation)
        {
            if (!compilation.IsEqualTypes(invocation.TargetMethod.ContainingType, typeof(System.Diagnostics.Contracts.Contract)))
                return false;

            if (!invocation.TargetMethod.IsGenericMethod)
                return false;

            if (invocation.Arguments.Length != 2)
                return false;

            return MethodNamesToFix.Contains(invocation.TargetMethod.Name);
        }

        private static void AnalyzeInvocationOp(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (invocation.TargetMethod.Kind != SymbolKind.Method || !invocation.TargetMethod.IsStatic)
                return;
            if (!IsCodeContractToReplace(context.Compilation, invocation))
                return;
            if (ContractStatementAnalyzer.IsContractClass(context.ContainingSymbol))
                return;

            var invocationSyntax = (InvocationExpressionSyntax)invocation.Syntax;
            if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocationSyntax.GetLocation()));
        }
    }
}
