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

namespace ContractFix.TurboContractConditionMessageArg
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TurboContractConditionMessageArgCodeFixProvider)), Shared]
    public class TurboContractConditionMessageArgCodeFixProvider : CodeFixProvider
    {
        private const string title = "Place condition string in correct argument";

        private class ContractCallInfo
        {
            public ContractCallInfo(NameSyntax methodName, ExpressionSyntax condition, ExpressionSyntax message)
            {
                MethodName = methodName;
                Condition = condition;
                Message = message;

                if (Message != null && 
                    Message is LiteralExpressionSyntax literalMsg &&
                    literalMsg.IsKind(SyntaxKind.StringLiteralExpression) &&
                    literalMsg.Token.ValueText == condition.ToString())
                {
                    MessageIsLikeCondition = true;
                }
            }

            public NameSyntax MethodName { get; }
            public ExpressionSyntax Condition { get; }
            public ExpressionSyntax Message { get; }
            public bool MessageIsLikeCondition { get; }
        }


        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(TurboContractConditionMessageArgAnalyzer.DiagnosticId);
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
            if (node != null &&
                node.Expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is SimpleNameSyntax simpleName &&
                TurboContractConditionMessageArgAnalyzer.MethodNamesToFix.Contains(simpleName.Identifier.ValueText) &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                invocation.ArgumentList.Arguments[0].Expression is ExpressionSyntax conditionExression)
            {
                ExpressionSyntax userDesc = null;
                if (invocation.ArgumentList.Arguments.Count > 1 &&
                    invocation.ArgumentList.Arguments[1].Expression is ExpressionSyntax userDescExpr)
                {
                    userDesc = userDescExpr;
                }

                return new ContractCallInfo(simpleName, conditionExression, userDesc);
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
     

            if (contractCallInfo.Message == null || contractCallInfo.MessageIsLikeCondition)
            {
                debugAssertCallNode = generator.InvocationExpression(
                    generator.MemberAccessExpression(generator.IdentifierName("TurboContract"), contractCallInfo.MethodName),
                    contractCallInfo.Condition,
                    generator.Argument("conditionString", RefKind.None, generator.LiteralExpression(contractCallInfo.Condition.ToString())));
            }
            else
            {
                debugAssertCallNode = generator.InvocationExpression(
                    generator.MemberAccessExpression(generator.IdentifierName("TurboContract"), contractCallInfo.MethodName),
                    contractCallInfo.Condition,
                    contractCallInfo.Message,
                    generator.LiteralExpression(contractCallInfo.Condition.ToString()));
            }

            debugAssertCallNode = debugAssertCallNode.WithTrailingTrivia(trailingTrivia).WithLeadingTrivia(leadingTrivia);

            editor.ReplaceNode(nodeToReplace, debugAssertCallNode);
            return editor.GetChangedDocument();
        }
    }
}
