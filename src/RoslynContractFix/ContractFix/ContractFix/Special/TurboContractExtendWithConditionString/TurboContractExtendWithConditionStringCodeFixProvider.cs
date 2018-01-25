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

namespace ContractFix.Special.TurboContractExtendWithConditionString
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TurboContractExtendWithConditionStringCodeFixProvider)), Shared]
    public class TurboContractExtendWithConditionStringCodeFixProvider : CodeFixProvider
    {
        private const string title = "Extend with condition message";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(TurboContractExtendWithConditionStringAnalyzer.DiagnosticId);
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


        private static async Task<Document> ReplaceWithTurboContract(Document document, SyntaxNode nodeToReplace, string stringText, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            var generator = editor.Generator;

            ContractInvocationInfo contractCallInfo = null;
            if (!ContractStatementAnalyzer.ParseInvocation(nodeToReplace.Parent as ExpressionStatementSyntax, out contractCallInfo) ||
                !contractCallInfo.IsSpecialContractType || !TurboContractExtendWithConditionStringAnalyzer.MethodNamesToFix.Contains(contractCallInfo.MethodNameAsString))
            {
                return document;
            }

            var trailingTrivia = nodeToReplace.GetTrailingTrivia();
            var leadingTrivia = nodeToReplace.GetLeadingTrivia();

            SyntaxNode specialContractCallNode = null;

            if (contractCallInfo.Message == null)
            {
                specialContractCallNode = generator.InvocationExpression(
                    generator.MemberAccessExpression(generator.IdentifierName(ContractStatementAnalyzer.SpecialContractClass), contractCallInfo.MethodName),
                    contractCallInfo.Condition,
                    generator.Argument("conditionString", RefKind.None, generator.LiteralExpression(contractCallInfo.Condition.ToString())));
            }
            else
            {
                specialContractCallNode = generator.InvocationExpression(
                    generator.MemberAccessExpression(generator.IdentifierName(ContractStatementAnalyzer.SpecialContractClass), contractCallInfo.MethodName),
                    contractCallInfo.Condition,
                    contractCallInfo.Message,
                    generator.LiteralExpression(contractCallInfo.Condition.ToString()));
            }

            specialContractCallNode = specialContractCallNode.WithTrailingTrivia(trailingTrivia).WithLeadingTrivia(leadingTrivia);

            editor.ReplaceNode(nodeToReplace, specialContractCallNode);
            return editor.GetChangedDocument();
        }
    }
}
