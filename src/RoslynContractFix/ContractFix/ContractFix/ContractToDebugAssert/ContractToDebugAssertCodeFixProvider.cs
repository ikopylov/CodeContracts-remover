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

        internal static void ReplaceWithDebugAssert(DocumentEditor editor, SyntaxNode nodeToReplace)
        {
            ContractInvocationInfo contractCallInfo = null;
            if (!ContractStatementAnalyzer.ParseInvocation(nodeToReplace.Parent as ExpressionStatementSyntax, out contractCallInfo) ||
                !contractCallInfo.IsContractType || !ContractToDebugAssertAnalyzer.MethodNamesToFix.Contains(contractCallInfo.MethodNameAsString))
            {
                return;
            }

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
            if (context.Scope == FixAllScope.Project || context.Scope == FixAllScope.Solution)
            {
                Dictionary<Document, List<SyntaxNode>> nodesToReplace = null;
                if (context.Scope == FixAllScope.Project)
                    nodesToReplace = await CollectProjectDiagnostics(context.Project, context);
                else if (context.Scope == FixAllScope.Solution)
                    nodesToReplace = await CollectSolutionDiagnostics(context.Solution, context);

                return CodeAction.Create(
                    title: title,
                    createChangedSolution: c => ReplaceAllInSolutionWithDebugAssert(context.Solution, nodesToReplace, c),
                    equivalenceKey: title);
            }
            else
            {
                var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
                var diagnostics = await context.GetDocumentDiagnosticsAsync(context.Document);
                var nodesToReplace = diagnostics.Select(o => root.FindNode(o.Location.SourceSpan, getInnermostNodeForTie: true)).ToList();

                return CodeAction.Create(
                        title: title,
                        createChangedDocument: c => ReplaceAllWithDebugAssert(context.Document, nodesToReplace, c),
                        equivalenceKey: title);
            }
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


        private static async Task<Dictionary<Document, List<SyntaxNode>>> CollectProjectDiagnostics(Project project, FixAllContext context)
        {
            Dictionary<SyntaxTree, Document> syntaxTreeToDocMap = new Dictionary<SyntaxTree, Document>();
            foreach (var doc in project.Documents)
            {
                var tree = await doc.GetSyntaxTreeAsync(context.CancellationToken).ConfigureAwait(false);
                syntaxTreeToDocMap.Add(tree, doc);
            }

            Dictionary<Document, List<SyntaxNode>> result = new Dictionary<Document, List<SyntaxNode>>();

            var allProjectDiags = await context.GetAllDiagnosticsAsync(project).ConfigureAwait(false);
            foreach (var resDiag in allProjectDiags)
            {
                if (!syntaxTreeToDocMap.TryGetValue(resDiag.Location.SourceTree, out Document curDoc))
                    continue;

                var root = await resDiag.Location.SourceTree.GetRootAsync(context.CancellationToken);
                var nodeToReplace = root.FindNode(resDiag.Location.SourceSpan, getInnermostNodeForTie: true);

                if (!result.TryGetValue(curDoc, out List<SyntaxNode> curDocNodes))
                {
                    curDocNodes = new List<SyntaxNode>();
                    result.Add(curDoc, curDocNodes);
                }

                curDocNodes.Add(nodeToReplace);
            }

            return result;
        }

        private static async Task<Dictionary<Document, List<SyntaxNode>>> CollectSolutionDiagnostics(Solution solution, FixAllContext context)
        {
            Dictionary<Document, List<SyntaxNode>> result = new Dictionary<Document, List<SyntaxNode>>();

            foreach (var proj in solution.Projects)
            {
                var projDiag = await CollectProjectDiagnostics(proj, context);

                foreach (var diag in projDiag)
                    result.Add(diag.Key, diag.Value);
            }

            return result;
        }


        private static async Task<Solution> ReplaceAllInSolutionWithDebugAssert(Solution solution, Dictionary<Document, List<SyntaxNode>> nodesToReplace, CancellationToken cancellationToken)
        {
            foreach (var diagNodes in nodesToReplace)
            {
                var updatedDoc = await ReplaceAllWithDebugAssert(diagNodes.Key, diagNodes.Value, cancellationToken);
                solution = solution.WithDocumentSyntaxRoot(diagNodes.Key.Id, await updatedDoc.GetSyntaxRootAsync(cancellationToken));
            }

            return solution;
        }
    }
}
