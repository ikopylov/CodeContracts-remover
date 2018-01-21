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
            return WellKnownFixAllProviders.BatchFixer;
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
                    createChangedDocument: c => ReplaceWithTurboContract(context.Document, nodeToReplace, stringText, c),
                    equivalenceKey: title),
                context.Diagnostics);
        }



        private static ContractCallInfo AnalyzeContractCallStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is InvocationExpressionSyntax invocation &&
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

        private static async Task<Document> ReplaceWithTurboContract(Document document, SyntaxNode nodeToReplace, string stringText, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            var generator = editor.Generator;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            var contractCallInfo = AnalyzeContractCallStatement(nodeToReplace.Parent as ExpressionStatementSyntax);
            if (contractCallInfo == null)
                return document;

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

            var annotation = new SyntaxAnnotation();

            Helpers.AddUsing(editor, "System.Diagnostics", nodeToReplace.SpanStart);
            editor.ReplaceNode(nodeToReplace, debugAssertCallNode);
            return editor.GetChangedDocument();
        }
    }
}
