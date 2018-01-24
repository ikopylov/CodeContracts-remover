using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace ContractFix.TurboContractConditionMessageText
{
    //[DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class TurboContractConditionMessageTextAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CR13_TurboContractConditionTextSync";
        private const string Title = "TurboContract call has different condition and condition string";
        private const string MessageFormat = "has different condition and condition string";
        private const string Description = "TurboContract call has different condition and condition string";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        internal static HashSet<string> MethodNamesToFix { get; } = new HashSet<string>()
        {
            nameof(System.Diagnostics.Contracts.Contract.Requires),
            nameof(System.Diagnostics.Contracts.Contract.Assert),
            nameof(System.Diagnostics.Contracts.Contract.Assume)
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeInvocationOp, OperationKind.Invocation);
        }


        private static bool IsCodeContractToReplace(Compilation compilation, IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.ContainingType.Name != "TurboContract")
                return false;

            if (invocation.TargetMethod.IsGenericMethod)
                return false;

            if (!MethodNamesToFix.Contains(invocation.TargetMethod.Name))
                return false;

            if (invocation.Arguments.Length == 3)
            {
                if (invocation.Arguments[2].Parameter.Name == "conditionString" &&
                    invocation.Arguments[2].ArgumentKind != ArgumentKind.DefaultValue &&
                    invocation.Arguments[2].Syntax is ArgumentSyntax argSynt &&
                    argSynt.Expression is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    if (literal.Token.ValueText != invocation.Arguments[0].Syntax.ToString())
                        return true;
                }
                if (invocation.Arguments[1].Parameter.Name == "conditionString" &&
                    invocation.Arguments[1].ArgumentKind != ArgumentKind.DefaultValue &&
                    invocation.Arguments[1].Syntax is ArgumentSyntax argSynt2 &&
                    argSynt2.Expression is LiteralExpressionSyntax literal2 &&
                    literal2.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    if (literal2.Token.ValueText != invocation.Arguments[0].Syntax.ToString())
                        return true;
                }
            }

            if (invocation.Arguments.Length == 2)
            {
                if (invocation.Arguments[1].Syntax is ArgumentSyntax argSynt &&
                    (argSynt.NameColon != null && argSynt.NameColon.Name.Identifier.ValueText == "conditionString") &&
                    argSynt.Expression is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    if (literal.Token.ValueText != invocation.Arguments[0].Syntax.ToString())
                        return true;
                }
            }

            return false;
        }

        private static void AnalyzeInvocationOp(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (invocation.TargetMethod.Kind != SymbolKind.Method || !invocation.TargetMethod.IsStatic)
                return;
            if (!IsCodeContractToReplace(context.Compilation, invocation))
                return;
            

            var invocationSyntax = (InvocationExpressionSyntax)invocation.Syntax;
            if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocationSyntax.GetLocation()));
        }
    }
}
