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

namespace ContractFix.EliminateInvariantMethods
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class EliminateInvariantMethodsAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CR06_EliminateInvariantMethods";
        private const string Title = "Invariant methods can be removed from source code";
        private const string MessageFormat = "Invariant methods can be removed from source code";
        private const string Description = "Invariant methods can be removed from source code";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Info, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterCodeBlockAction(AnalyzeMethodDeclaration);
        }


        private static void AnalyzeMethodDeclaration(CodeBlockAnalysisContext context)
        {
            MethodDeclarationSyntax methodSyntax = context.CodeBlock as MethodDeclarationSyntax;
            IMethodSymbol method = context.OwningSymbol as IMethodSymbol;
            if (method == null || methodSyntax == null)
                return;
            if (!ContractStatementAnalyzer.IsInvariantMethod(method))
                return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, methodSyntax.Identifier.GetLocation()));
        }
    }
}
