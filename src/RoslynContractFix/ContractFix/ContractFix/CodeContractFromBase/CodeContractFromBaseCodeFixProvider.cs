using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ContractFix.CodeContractFromBase
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CodeContractFromBaseCodeFixProvider)), Shared]
    public class CodeContractFromBaseCodeFixProvider : CodeFixProvider
    {
        private const string title = "Retrieve contract from base type";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(CodeContractFromBaseAnalyzer.DiagnosticId);
            }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostics = context.Diagnostics;
            var diagnosticSpan = context.Span;
            // getInnerModeNodeForTie = true so we are replacing the string literal node and not the whole argument node
            var nodeToReplace = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);

            Debug.Assert(nodeToReplace != null);
            var stringText = nodeToReplace.FindToken(diagnosticSpan.Start).ValueText;
            context.RegisterCodeFix(CodeAction.Create(
                    title: title,
                    createChangedDocument: c => RetriveContractFromBaseType(context.Document, nodeToReplace, stringText, c),
                    equivalenceKey: title),
                context.Diagnostics);
        }


        private static async Task<Document> RetriveContractFromBaseType(Document document, SyntaxNode diagNode, string stringText, CancellationToken cancellationToken)
        {
            var methodSyntax = diagNode as MethodDeclarationSyntax;
            if (methodSyntax == null)
                return document;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            var methodSymbol = (IMethodSymbol)semanticModel.GetDeclaredSymbol(methodSyntax, cancellationToken);
            var baseTypeOverridingMethods = CodeContractFromBaseAnalyzer.GetBaseTypeOverridingMethod(methodSymbol);
            var contractMethods = baseTypeOverridingMethods.SelectMany(o => CodeContractFromBaseAnalyzer.GetContractTypeMethods(o, methodSymbol)).ToList();
            contractMethods.AddRange(baseTypeOverridingMethods.SelectMany(o => CodeContractFromBaseAnalyzer.GetVirtualMethodsWithContracts(o, methodSymbol)));

            if (contractMethods.Count == 0)
                return document;

            var requireStatements = contractMethods.SelectMany(o => CodeContractFromBaseAnalyzer.ExtractRequires(o, cancellationToken)).ToList();
            requireStatements = CodeContractFromBaseAnalyzer.DeduplicateRequires(requireStatements, CodeContractFromBaseAnalyzer.ExtractRequires(methodSyntax).ToList());
            if (requireStatements.Count == 0)
                return document;


            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            var newBody = methodSyntax.Body.WithStatements(methodSyntax.Body.Statements.InsertRange(0, requireStatements));
            root = root.ReplaceNode(methodSyntax.Body, newBody);
            return document.WithSyntaxRoot(root);
        }
    }
}
