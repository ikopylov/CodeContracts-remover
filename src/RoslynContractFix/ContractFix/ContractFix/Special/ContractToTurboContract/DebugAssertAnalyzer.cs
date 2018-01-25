using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace ContractFix.Special.ContractToTurboContract
{
    //[DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class DebugAssertAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CRS10_DebugAssertToTurboContractReplace";
        private const string Title = "Debug.Assert can be replaced with TurboContract";
        private const string MessageFormat = "Debug.Assert can be replaced with TurboContract";
        private const string Description = "Debug.Assert can be replaced with TurboContract";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeInvocationOp, OperationKind.Invocation);
        }


        private static bool IsDebugAssertToReplace(Compilation compilation, IInvocationOperation invocation)
        {
            if (!compilation.IsEqualTypes(invocation.TargetMethod.ContainingType, typeof(System.Diagnostics.Debug)))
                return false;
            if (invocation.TargetMethod.Name != nameof(System.Diagnostics.Debug.Assert))
                return false;

            return true;
        }

        private static void AnalyzeInvocationOp(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (invocation.TargetMethod.Kind != SymbolKind.Method || !invocation.TargetMethod.IsStatic)
                return;

            if (context.ContainingSymbol is IMethodSymbol method && method.ContainingType.Name == ContractStatementAnalyzer.SpecialContractClass)
                return;

            if (!IsDebugAssertToReplace(context.Compilation, invocation))
                return;

            var invocationSyntax = (InvocationExpressionSyntax)invocation.Syntax;
            if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
                context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Expression.GetLocation()));
        }
    }
}
