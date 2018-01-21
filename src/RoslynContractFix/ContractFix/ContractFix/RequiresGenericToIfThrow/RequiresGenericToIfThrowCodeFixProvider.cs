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

namespace ContractFix.RequiresGenericToIfThrow
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequiresGenericToIfThrowCodeFixProvider)), Shared]
    public class RequiresGenericToIfThrowCodeFixProvider : CodeFixProvider
    {
        private class RequireInfo
        {
            public RequireInfo(IdentifierNameSyntax exception, ExpressionSyntax condition, ExpressionSyntax userDescription)
            {
                Exception = exception;
                Condition = condition;
                UserDescription = userDescription;
            }

            public IdentifierNameSyntax Exception { get; }
            public ExpressionSyntax Condition { get; }
            public ExpressionSyntax UserDescription { get; }
        }

        private const string title = "Replace with if..throw";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get
            {
                return ImmutableArray.Create(RequiresGenericToIfThrowAnalyzer.DiagnosticId);
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
                    createChangedDocument: c => ReplaceWithTurboContract(context.Document, nodeToReplace, stringText, c),
                    equivalenceKey: title),
                context.Diagnostics);
        }



        private static RequireInfo AnalyzeRequireStatement(SyntaxNode node)
        {
            var childNodes = node.ChildNodes().ToList();
            if (childNodes.Count != 1)
                return null;
            
            if (childNodes[0] is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is GenericNameSyntax genericName &&
                genericName.Identifier.ValueText == nameof(System.Diagnostics.Contracts.Contract.Requires) &&
                genericName.TypeArgumentList.Arguments.Count == 1 &&
                genericName.TypeArgumentList.Arguments[0] is IdentifierNameSyntax exceptionTypeIdentifier &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                invocation.ArgumentList.Arguments[0].Expression is ExpressionSyntax conditionExression)
            {
                ExpressionSyntax userDesc = null;
                if (invocation.ArgumentList.Arguments.Count > 1 &&
                    invocation.ArgumentList.Arguments[1].Expression is ExpressionSyntax userDescExpr)
                {
                    userDesc = userDescExpr;
                }

                return new RequireInfo(exceptionTypeIdentifier, conditionExression, userDesc);
            }


            return null;
        }

        private static ExpressionSyntax SmartNotExpression(ExpressionSyntax expr, SyntaxGenerator generator)
        {
            if (expr is BinaryExpressionSyntax binary)
            {
                switch (binary.Kind())
                {
                    case SyntaxKind.NotEqualsExpression:
                        return (ExpressionSyntax)generator.ReferenceEqualsExpression(binary.Left, binary.Right);
                    case SyntaxKind.EqualsExpression:
                        return (ExpressionSyntax)generator.ReferenceNotEqualsExpression(binary.Left, binary.Right);
                    case SyntaxKind.GreaterThanExpression:
                        return (ExpressionSyntax)generator.LessThanOrEqualExpression(binary.Left, binary.Right);
                    case SyntaxKind.GreaterThanOrEqualExpression:
                        return (ExpressionSyntax)generator.LessThanExpression(binary.Left, binary.Right);
                    case SyntaxKind.LessThanExpression:
                        return (ExpressionSyntax)generator.GreaterThanOrEqualExpression(binary.Left, binary.Right);
                    case SyntaxKind.LessThanOrEqualExpression:
                        return (ExpressionSyntax)generator.GreaterThanExpression(binary.Left, binary.Right);
                }
            }

            return (ExpressionSyntax)generator.LogicalNotExpression(expr);
        }


        private static ExpressionSyntax BuildThrowExpr(IdentifierNameSyntax exception, ExpressionSyntax condition, ExpressionSyntax message, IdentifierNameSyntax parameter, SemanticModel semanticModel, SyntaxGenerator generator, CancellationToken cancellationToken)
        {
            bool IsParamArg(IParameterSymbol symbol)
            {
                return symbol.Type.SpecialType == SpecialType.System_String && (symbol.Name == "param" || symbol.Name == "paramName");
            }
            bool IsMessageArg(IParameterSymbol symbol)
            {
                return symbol.Type.SpecialType == SpecialType.System_String && symbol.Name == "message";
            }
            bool IsInnerExceptionArg(IParameterSymbol symbol)
            {
                return symbol.Type.Name == typeof(Exception).Name;
            }

            bool IsMethodMatchedArgs(IMethodSymbol method, params Func<IParameterSymbol, bool>[] args)
            {
                if (args == null || args.Length == 0)
                    return method.Parameters.Length == 0;

                if (method.Parameters.Length != args.Length)
                    return false;

                for (int i = 0; i < args.Length; i++)
                {
                    if (!method.Parameters.Any(args[i]))
                        return false;
                }

                return true;
            }

            ExpressionSyntax GenerateConsturctionExpr(IMethodSymbol constructor, params (Func<IParameterSymbol, bool>, SyntaxNode)[] args)
            {
                if (constructor.Parameters.Length == 0)
                    return (ExpressionSyntax)generator.ObjectCreationExpression(constructor.ContainingType);

                SyntaxNode[] syntaxArgs = new SyntaxNode[constructor.Parameters.Length];

                for (int i = 0; i < constructor.Parameters.Length; i++)
                {
                    syntaxArgs[i] = args.Single(o => o.Item1(constructor.Parameters[i])).Item2;
                }

                return (ExpressionSyntax)generator.ObjectCreationExpression(constructor.ContainingType, syntaxArgs);
            }


            var typeInfo = semanticModel.GetTypeInfo(exception, cancellationToken).Type;
            var exceptionType = semanticModel.Compilation.GetKnownType(typeof(Exception));
            bool isArgumentException = Helpers.IsTypeOrSubtype(typeInfo, semanticModel.Compilation.GetKnownType(typeof(ArgumentException)));
            bool isArgumentNullException = typeInfo.Equals(semanticModel.Compilation.GetKnownType(typeof(ArgumentNullException)));
            var messagePromoted = message ?? generator.LiteralExpression(condition.ToString());
            var constructors = typeInfo.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Constructor).ToList();

            var constructorWithParamAndMsg = constructors.FirstOrDefault(o => IsMethodMatchedArgs(o, IsParamArg, IsMessageArg));
            var constructorWithParam = constructors.FirstOrDefault(o => IsMethodMatchedArgs(o, IsParamArg));
            var constructorWithMsg = constructors.FirstOrDefault(o => IsMethodMatchedArgs(o, IsMessageArg));
            var constructorWithMsgAndExc = constructors.FirstOrDefault(o => IsMethodMatchedArgs(o, IsInnerExceptionArg, IsMessageArg));

            if (isArgumentException && parameter != null)
            {
                if (isArgumentNullException && message == null && constructorWithParam != null)
                    return GenerateConsturctionExpr(constructorWithParam, (IsParamArg, generator.NameOfExpression(parameter)));

                if (constructorWithParamAndMsg != null)
                    return GenerateConsturctionExpr(constructorWithParamAndMsg, (IsParamArg, generator.NameOfExpression(parameter)), (IsMessageArg, messagePromoted));

                if (constructorWithMsg == null && constructorWithParam != null)
                    return GenerateConsturctionExpr(constructorWithParam, (IsParamArg, generator.NameOfExpression(parameter)));
            }


            if (constructorWithMsg != null)
                return GenerateConsturctionExpr(constructorWithMsg, (IsMessageArg, messagePromoted));

            if (constructorWithParamAndMsg != null && parameter != null)
                return GenerateConsturctionExpr(constructorWithParamAndMsg, (IsParamArg, generator.NameOfExpression(parameter)), (IsMessageArg, messagePromoted));


            if (constructorWithMsgAndExc != null)
                return GenerateConsturctionExpr(constructorWithParamAndMsg, (IsInnerExceptionArg, generator.CastExpression(exceptionType, generator.NullLiteralExpression())), (IsMessageArg, messagePromoted));


            return (ExpressionSyntax)generator.ObjectCreationExpression(typeInfo);
        }


        private class IdentifierWalker: CSharpSyntaxWalker
        {
            private ImmutableArray<IParameterSymbol> _parameters;
            private HashSet<string> _parametersSet;

            public IdentifierWalker(ImmutableArray<IParameterSymbol> parameters)
            {
                _parameters = parameters;
                _parametersSet = new HashSet<string>(parameters.Select(o => o.Name));
                Nodes = new List<IdentifierNameSyntax>();
                _nodesNames = new HashSet<string>();
            }

            private HashSet<string> _nodesNames;
            public List<IdentifierNameSyntax> Nodes { get; }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (_parametersSet.Contains(node.Identifier.ValueText))
                {
                    if (!_nodesNames.Contains(node.Identifier.ValueText))
                    {
                        Nodes.Add(node);
                        _nodesNames.Add(node.Identifier.ValueText);
                    }
                }

                base.VisitIdentifierName(node);
            }
        }

        private static IdentifierNameSyntax FindArgument(ExpressionSyntax expr, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            var method = (IMethodSymbol)semanticModel.GetEnclosingSymbol(expr.SpanStart, cancellationToken);
            IdentifierWalker walker = new IdentifierWalker(method.Parameters);
            walker.Visit(expr);

            if (walker.Nodes.Count == 1)
                return walker.Nodes[0];

            return null;
        }

        private static async Task<Document> ReplaceWithTurboContract(Document document, SyntaxNode nodeToReplace, string stringText, CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
            var generator = editor.Generator;

            var requireInfo = AnalyzeRequireStatement(nodeToReplace);
            if (requireInfo == null)
                return document;

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            var condition = SmartNotExpression(requireInfo.Condition, generator);
            var argument = FindArgument(requireInfo.Condition, semanticModel, cancellationToken);
            var exception = BuildThrowExpr(requireInfo.Exception, requireInfo.Condition, requireInfo.UserDescription, argument, semanticModel, generator, cancellationToken);


            var trailingTrivia = nodeToReplace.GetTrailingTrivia();
            var leadingTrivia = nodeToReplace.GetLeadingTrivia();

            var replacementIfThrow =
                SyntaxFactory.IfStatement(
                    condition,
                    (Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax)
                    generator.ThrowStatement(exception))
                .WithTrailingTrivia(trailingTrivia)
                .WithLeadingTrivia(leadingTrivia);

            var annotation = new SyntaxAnnotation();

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            root = root.ReplaceNode(nodeToReplace, replacementIfThrow);
            return document.WithSyntaxRoot(root);
        }
    }
}
