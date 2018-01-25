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

namespace ContractFix.RequiresGenericToIfThrow
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class RequiresGenericToIfThrowAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CR02_RequiresGenericToIfThrow";
        private const string Title = "Contract.Requires should be replaced with if..throw";
        private const string MessageFormat = "Contract.Requires should be replaced with if..throw";
        private const string Description = "Contract.Requires should be replaced with if..throw";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeInvocationOp, OperationKind.Invocation);
        }


        private static bool IsCodeContractToReplace(IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.ContainingType.ToString() != typeof(System.Diagnostics.Contracts.Contract).FullName)
                return false;
            if (!invocation.TargetMethod.IsGenericMethod || invocation.TargetMethod.Name != nameof(System.Diagnostics.Contracts.Contract.Requires))
                return false;

            return true;
        }

        private static void AnalyzeInvocationOp(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (invocation.TargetMethod.Kind != SymbolKind.Method || !invocation.TargetMethod.IsStatic)
                return;
            if (invocation.Parent.Kind != OperationKind.ExpressionStatement)
                return;
            if (!IsCodeContractToReplace(invocation))
                return;
            if (ContractStatementAnalyzer.IsContractClass(context.ContainingSymbol))
                return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Parent.Syntax.GetLocation()));
        }
    }
}
