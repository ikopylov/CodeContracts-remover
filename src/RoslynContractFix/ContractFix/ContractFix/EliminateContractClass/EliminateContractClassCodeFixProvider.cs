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

namespace ContractFix.EliminateContractClass
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EliminateContractClassCodeFixProvider)), Shared]
    public class EliminateContractClassCodeFixProvider : CodeFixProvider
    {
        private const string title = "Remove Contract class";


        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(EliminateContractClassAnalyzer.DiagnosticId);
            }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return new EliminateContractClassFixAllProvider();
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
                    createChangedDocument: c => RemoveContractStatementAsync(context.Document, nodeToReplace, stringText, c),
                    equivalenceKey: title),
                context.Diagnostics);
        }


        public static AttributeSyntax GetAttributeSyntax(DocumentEditor editor, ClassDeclarationSyntax ccClass, CancellationToken cancellationToken)
        {
            var classSymbol = editor.SemanticModel.GetDeclaredSymbol(ccClass, cancellationToken);
            if (classSymbol == null)
                return null;

            var attrib = classSymbol.GetAttributes().FirstOrDefault(o => o.AttributeClass.Name == nameof(System.Diagnostics.Contracts.ContractClassForAttribute));
            if (attrib == null)
                return null;

            var argument = attrib.ConstructorArguments[0];
            INamedTypeSymbol targetType = null;
            if (argument.Value is INamedTypeSymbol namedType && namedType.IsUnboundGenericType)
                targetType = namedType.ConstructedFrom;
            else
                targetType = argument.Value as INamedTypeSymbol;

            if (targetType == null)
                return null;

            var attribReverse = targetType.GetAttributes().FirstOrDefault(o => o.AttributeClass.Name == nameof(System.Diagnostics.Contracts.ContractClassAttribute) && (o.ConstructorArguments[0].Value is INamedTypeSymbol ntLoc) && ntLoc.ConstructedFrom.Equals(classSymbol));
            if (attribReverse == null)
                return null;

            if (attribReverse.ApplicationSyntaxReference == null)
                return null;

            var resultSyntax = attribReverse.ApplicationSyntaxReference.GetSyntax(cancellationToken) as AttributeSyntax;
            return resultSyntax;
        }

        public static void RemoveClassWithAttrib(DocumentEditor editor, ClassDeclarationSyntax ccClass, AttributeSyntax reverseAttrib)
        {
            if (reverseAttrib != null)
            {
                if (editor.OriginalRoot.Contains(reverseAttrib))
                    editor.RemoveNode(reverseAttrib, SyntaxRemoveOptions.KeepExteriorTrivia);
            }

            editor.RemoveNode(ccClass, SyntaxRemoveOptions.KeepNoTrivia);
        }

        private static async Task<Document> RemoveContractStatementAsync(Document document, SyntaxNode nodeToReplace, string stringText, CancellationToken cancellationToken)
        {
            var classSyntax = nodeToReplace as ClassDeclarationSyntax;
            if (classSyntax == null)
                return document;

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            var attribSyntax = GetAttributeSyntax(editor, classSyntax, cancellationToken);

            RemoveClassWithAttrib(editor, classSyntax, attribSyntax);
            return editor.GetChangedDocument();
        }
    }



    public class EliminateContractClassFixAllProvider : FixAllProvider
    {
        private const string title = "Remove all Contract classes";

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
                    createChangedSolution: c => RemoveAllContractClassesInSolution(context.Solution, nodesToReplace, c),
                    equivalenceKey: title);
            }
            else
            {
                var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
                var diagnostics = await context.GetDocumentDiagnosticsAsync(context.Document);
                var nodesToReplace = diagnostics.Select(o => root.FindNode(o.Location.SourceSpan, getInnermostNodeForTie: true)).ToList();

                return CodeAction.Create(
                        title: title,
                        createChangedDocument: c => RemoveAllContractClasses(context.Document, nodesToReplace, c),
                        equivalenceKey: title);
            }
        }

        private static async Task<Document> RemoveAllContractClasses(Document document, List<SyntaxNode> nodesToReplace, CancellationToken cancellationToken)
        {
            if (nodesToReplace.Count == 0)
                return document;

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            List<(ClassDeclarationSyntax, AttributeSyntax)> combined = new List<(ClassDeclarationSyntax, AttributeSyntax)>(nodesToReplace.Count);
            foreach (var node in nodesToReplace)
            {
                if (node is ClassDeclarationSyntax ccClass)
                    combined.Add((ccClass, EliminateContractClassCodeFixProvider.GetAttributeSyntax(editor, ccClass, cancellationToken)));
            }

            foreach (var node in combined)
                EliminateContractClassCodeFixProvider.RemoveClassWithAttrib(editor, node.Item1, node.Item2);

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


        private static async Task<Solution> RemoveAllContractClassesInSolution(Solution solution, Dictionary<Document, List<SyntaxNode>> nodesToReplace, CancellationToken cancellationToken)
        {
            foreach (var diagNodes in nodesToReplace)
            {
                var updatedDoc = await RemoveAllContractClasses(diagNodes.Key, diagNodes.Value, cancellationToken);
                solution = solution.WithDocumentSyntaxRoot(diagNodes.Key.Id, await updatedDoc.GetSyntaxRootAsync(cancellationToken));
            }

            return solution;
        }
    }
}
