using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
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

namespace ContractFix.ContractToDebugAssert
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ContractToDebugAssertCodeFixProvider)), Shared]
    public class ContractToDebugAssertCodeFixProvider : CodeFixProvider
    {
        private const string title = "Replace with Debug.Assert";

        private class ContractCallInfo
        {
            public ContractCallInfo(ExpressionSyntax condition, ExpressionSyntax message)
            {
                Condition = condition;
                Message = message;
            }

            public ExpressionSyntax Condition { get; }
            public ExpressionSyntax Message { get; }
        }


        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(ContractToDebugAssertAnalyzer.DiagnosticId);
            }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return new ContractToDebugAssertFixAllProvider();
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
                    createChangedDocument: c => ReplaceWithDebugAssert(context.Document, nodeToReplace, stringText, c),
                    equivalenceKey: title),
                context.Diagnostics);
        }


        private static ContractCallInfo AnalyzeContractCallStatement(ExpressionStatementSyntax node)
        {
            if (node != null &&
                node.Expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is SimpleNameSyntax simpleName &&
                ContractToDebugAssertAnalyzer.MethodNamesToFix.Contains(simpleName.Identifier.ValueText) &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                invocation.ArgumentList.Arguments[0].Expression is ExpressionSyntax conditionExression)
            {
                ExpressionSyntax userDesc = null;
                if (invocation.ArgumentList.Arguments.Count > 1 &&
                    invocation.ArgumentList.Arguments[1].Expression is ExpressionSyntax userDescExpr)
                {
                    userDesc = userDescExpr;
                }

                return new ContractCallInfo(conditionExression, userDesc);
            }


            return null;
        }

        internal static void ReplaceWithDebugAssert(DocumentEditor editor, SyntaxNode nodeToReplace)
        {
            var contractCallInfo = AnalyzeContractCallStatement(nodeToReplace.Parent as ExpressionStatementSyntax);
            if (contractCallInfo == null)
                return;

            var generator = editor.Generator;

            var trailingTrivia = nodeToReplace.GetTrailingTrivia();
            var leadingTrivia = nodeToReplace.GetLeadingTrivia();

            SyntaxNode debugAssertCallNode = null;

            if (contractCallInfo.Message == null)
            {
                debugAssertCallNode = generator.InvocationExpression(
                    generator.MemberAccessExpression(generator.IdentifierName(nameof(Debug)), nameof(Debug.Assert)),
                    contractCallInfo.Condition,
                    generator.LiteralExpression(contractCallInfo.Condition.ToString()));
            }
            else
            {
                debugAssertCallNode = generator.InvocationExpression(
                    generator.MemberAccessExpression(generator.IdentifierName(nameof(Debug)), nameof(Debug.Assert)),
                    contractCallInfo.Condition,
                    generator.LiteralExpression(contractCallInfo.Condition.ToString()),
                    contractCallInfo.Message);
            }

            debugAssertCallNode = debugAssertCallNode.WithTrailingTrivia(trailingTrivia).WithLeadingTrivia(leadingTrivia);

            editor.ReplaceNode(nodeToReplace, debugAssertCallNode);
        }

        private static async Task<Document> ReplaceWithDebugAssert(Document document, SyntaxNode nodeToReplace, string stringText, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);


            Helpers.AddUsing(editor, "System.Diagnostics", nodeToReplace.SpanStart);
            ReplaceWithDebugAssert(editor, nodeToReplace);
            return editor.GetChangedDocument();
        }
    }


    public class ContractToDebugAssertFixAllProvider : FixAllProvider
    {
        private const string title = "Replace with Debug.Assert All";

        public override async Task<CodeAction> GetFixAsync(FixAllContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostics = await context.GetDocumentDiagnosticsAsync(context.Document);
            var nodesToReplace = diagnostics.Select(o => root.FindNode(o.Location.SourceSpan, getInnermostNodeForTie: true)).ToList();

            return CodeAction.Create(
                    title: title,
                    createChangedDocument: c => ReplaceAllWithDebugAssert(context.Document, nodesToReplace, c),
                    equivalenceKey: title);
        }

        private static async Task<Document> ReplaceAllWithDebugAssert(Document document, List<SyntaxNode> nodesToReplace, CancellationToken cancellationToken)
        {
            if (nodesToReplace.Count == 0)
                return document;

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            Helpers.AddUsing(editor, "System.Diagnostics", nodesToReplace[0].SpanStart);

            foreach (var node in nodesToReplace)
                ContractToDebugAssertCodeFixProvider.ReplaceWithDebugAssert(editor, node);

            return editor.GetChangedDocument();
        }
    }
}
