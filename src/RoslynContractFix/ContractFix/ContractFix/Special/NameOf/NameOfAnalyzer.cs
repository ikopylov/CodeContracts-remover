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

namespace ContractFix.Special.NameOf
{
    //[DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class NameOfAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CR01_ToNameOfCS";
        private const string Title = "Argument string can be replaced with nameof";
        private const string MessageFormat = "Can be replaced with nameof({0})";
        private const string Description = "Replace with nameof";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeArgument, SyntaxKind.Argument);
        }

        private static void AnalyzeArgument(SyntaxNodeAnalysisContext context)
        {
            var node = (ArgumentSyntax)context.Node;
            if (!node.Expression.IsKind(SyntaxKind.StringLiteralExpression))
                return;

            var expression = (LiteralExpressionSyntax)node.Expression;
            var text = expression.Token.ValueText;
            
            if (GetParametersInScope(context).Any(o => o == text))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, expression.Token.GetLocation(), expression.Token.ValueText));
            }
        }

        private static IEnumerable<string> GetParametersInScope(SyntaxNodeAnalysisContext context)
        {
            // get the parameters for the containing method
            if (context.ContainingSymbol.Kind != SymbolKind.Method)
                yield break;

            IMethodSymbol methodSymbol = (IMethodSymbol)context.ContainingSymbol;

            foreach (var parameter in methodSymbol.Parameters)
            {
                yield return parameter.Name;
            }
        }
    }
}
