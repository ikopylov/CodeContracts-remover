using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
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

namespace ContractFix.ContractToTurboContract
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ContractToTurboContractCodeFixProvider)), Shared]
    public class ContractToTurboContractCodeFixProvider: CodeFixProvider
    {
        private const string title = "Replace with TurboContract";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(ContractToTurboContractAnalyzer.DiagnosticId, ContractToTurboContractAnalyzer.DiagnosticIdWithinCC, DebugAssertAnalyzer.DiagnosticId);
            }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return new ContractToTurboContractFixAllProvider();
            //return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostics = context.Diagnostics;
            var diagnosticSpan = context.Span;
            var nodeToReplace = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);

            Debug.Assert(nodeToReplace != null);
            var stringText = nodeToReplace.FindToken(diagnosticSpan.Start).ValueText;
            context.RegisterCodeFix(CodeAction.Create(
                    title: title,
                    createChangedDocument: c => ReplaceWithTurboContract(context.Document, nodeToReplace, c),
                    equivalenceKey: title),
                context.Diagnostics);
        }

        internal static void ReplaceWithTurboContract(DocumentEditor editor, SyntaxNode nodeToReplace)
        {
            var trailingTrivia = nodeToReplace.GetTrailingTrivia();
            var leadingTrivia = nodeToReplace.GetLeadingTrivia();

            var turboIdentifier = editor.Generator.IdentifierName("TurboContract")
                .WithTrailingTrivia(trailingTrivia)
                .WithLeadingTrivia(leadingTrivia);

            editor.ReplaceNode(nodeToReplace, turboIdentifier);
        }

        private static async Task<Document> ReplaceWithTurboContract(Document document, SyntaxNode nodeToReplace, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            Helpers.AddUsing(editor, "Qoollo.Turbo", nodeToReplace.SpanStart);
            ReplaceWithTurboContract(editor, nodeToReplace);
            return editor.GetChangedDocument();
        }
    }

    public class ContractToTurboContractFixAllProvider : FixAllProvider
    {
        private const string title = "Replace with TurboContract All";

        public override async Task<CodeAction> GetFixAsync(FixAllContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostics = await context.GetDocumentDiagnosticsAsync(context.Document);
            var nodesToReplace = diagnostics.Select(o => root.FindNode(o.Location.SourceSpan, getInnermostNodeForTie: true)).ToList();

            return CodeAction.Create(
                    title: title,
                    createChangedDocument: c => ReplaceAllWithTurboContract(context.Document, nodesToReplace, c),
                    equivalenceKey: title);
        }

        private static async Task<Document> ReplaceAllWithTurboContract(Document document, List<SyntaxNode> nodesToReplace, CancellationToken cancellationToken)
        {
            if (nodesToReplace.Count == 0)
                return document;

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            var generator = editor.Generator;

            Helpers.AddUsing(editor, "Qoollo.Turbo", nodesToReplace[0].SpanStart);

            foreach (var node in nodesToReplace)
                ContractToTurboContractCodeFixProvider.ReplaceWithTurboContract(editor, node);

            return editor.GetChangedDocument();
        }
    }
}
