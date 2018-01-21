using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Editing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ContractFix
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NameOfCodeFixProvider)), Shared]
    public class NameOfCodeFixProvider: CodeFixProvider
    {
        private const string title = "Replace with nameof()";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(NameOfAnalyzer.DiagnosticId);
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
                    createChangedDocument: c => ReplaceWithNameOf(context.Document, nodeToReplace, stringText, c), 
                    equivalenceKey: title),
                context.Diagnostics);
        }

        private async Task<Document> ReplaceWithNameOf(Document document, SyntaxNode nodeToReplace,
            string stringText, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            var generator = editor.Generator;

            var trailingTrivia = nodeToReplace.GetTrailingTrivia();
            var leadingTrivia = nodeToReplace.GetLeadingTrivia();
            var nameOfExpression = generator.NameOfExpression(generator.IdentifierName(stringText))
                .WithTrailingTrivia(trailingTrivia)
                .WithLeadingTrivia(leadingTrivia);

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var newRoot = root.ReplaceNode(nodeToReplace, nameOfExpression);

            return document.WithSyntaxRoot(newRoot);
        }
    }
}
