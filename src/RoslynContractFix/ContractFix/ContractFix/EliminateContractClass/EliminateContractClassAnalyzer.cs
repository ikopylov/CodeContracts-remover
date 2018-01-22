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

namespace ContractFix.EliminateContractClass
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class EliminateContractClassAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CR10_EliminateContractClass";
        private const string Title = "Contract class can be removed from source code";
        private const string MessageFormat = "Can be removed from source code";
        private const string Description = "Contract class can be removed from source code";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Info, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterSymbolAction(AnalyzeClassDeclaration, SymbolKind.NamedType);
        }


        private static bool IsContractClass(INamedTypeSymbol typeSmb)
        {
            var attrib = typeSmb.GetAttributes();
            return attrib.Any(o => o.AttributeClass.Name == nameof(System.Diagnostics.Contracts.ContractClassForAttribute));
        }


        private static void AnalyzeClassDeclaration(SymbolAnalysisContext context)
        {
            var namedType = context.Symbol as INamedTypeSymbol;
            if (namedType == null)
                return;
            if (namedType.TypeKind != TypeKind.Class)
                return;
            if (!IsContractClass(namedType))
                return;

            foreach (var syntax in namedType.DeclaringSyntaxReferences)
            {
                var syntaxNode = syntax.GetSyntax(context.CancellationToken);
                if (syntaxNode is ClassDeclarationSyntax classDecl)
                    context.ReportDiagnostic(Diagnostic.Create(Rule, classDecl.Identifier.GetLocation()));
            }
        }
    }
}
