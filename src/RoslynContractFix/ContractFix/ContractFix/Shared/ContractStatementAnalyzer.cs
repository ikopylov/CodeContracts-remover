using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ContractFix
{
    public class ContractInvocationInfo
    {
        public ContractInvocationInfo(SimpleNameSyntax className, SimpleNameSyntax methodName, NameSyntax exceptionType,
            ExpressionSyntax condition, ExpressionSyntax message, ExpressionSyntax conditionString, ArgumentListSyntax allArguments)
        {
            ClassName = className;
            MethodName = methodName;
            ExceptionType = exceptionType;
            Condition = condition;
            Message = message;
            ConditionString = conditionString;
            AllArguments = allArguments;
        }

        public SimpleNameSyntax ClassName { get; }
        public SimpleNameSyntax MethodName { get; }
        public NameSyntax ExceptionType { get; }
        public ExpressionSyntax Condition { get; }
        public ExpressionSyntax Message { get; }
        public ExpressionSyntax ConditionString { get; }
        public ArgumentListSyntax AllArguments { get; }

        public string ClassNameAsString { get { return ClassName.Identifier.ValueText; } }
        public string MethodNameAsString { get { return MethodName.Identifier.ValueText; } }
        public string ConditionAsString { get { return Condition.ToString(); } }

        public bool IsConditionMatchConditionString
        {
            get
            {
                return ConditionString == null ||
                        (ConditionString is LiteralExpressionSyntax literal && literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression) && literal.Token.ValueText == ConditionAsString);
            }
        }

        public bool IsContractType { get { return ClassName.Identifier.ValueText == "Contract"; } }
        public bool IsDebugType { get { return ClassName.Identifier.ValueText == "Debug"; } }
        public bool IsSpecialContractType { get { return ClassName.Identifier.ValueText == ContractStatementAnalyzer.SpecialContractClass; } }
    }


    public static class ContractStatementAnalyzer
    {
        public const string SpecialContractClass = "TurboContract";
        public const string SpecialContractClassNamespace = "Qoollo.Turbo";

        public static readonly HashSet<string> ValidContractClasses = new HashSet<string>()
        {
            nameof(System.Diagnostics.Debug),
            nameof(System.Diagnostics.Contracts.Contract),
            SpecialContractClass
        };



        public static bool ParseInvocation(InvocationExpressionSyntax invocation, out ContractInvocationInfo invocationInfo)
        {
            if (invocation.ArgumentList.Arguments.Count >= 1 && invocation.ArgumentList.Arguments.Count <= 3 &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name is SimpleNameSyntax methodNameSyntax)
            {
                SimpleNameSyntax className = memberAccess.Expression as SimpleNameSyntax;
                if (className == null && memberAccess.Expression is MemberAccessExpressionSyntax classMemberAccess)
                    className = classMemberAccess.Name as SimpleNameSyntax;

                if (className != null && ValidContractClasses.Contains(className.Identifier.ValueText))
                {
                    SimpleNameSyntax methodName = methodNameSyntax;
                    NameSyntax genericExceptionType = null;
                    if (methodNameSyntax is GenericNameSyntax genericName && genericName.TypeArgumentList.Arguments.Count == 1)
                    {
                        genericExceptionType = genericName.TypeArgumentList.Arguments[0] as NameSyntax;
                    }


                    ExpressionSyntax condition = invocation.ArgumentList.Arguments[0].Expression;
                    ExpressionSyntax message = null;
                    ExpressionSyntax conditionString = null;
                    if (invocation.ArgumentList.Arguments.Count >= 2)
                    {
                        if (invocation.ArgumentList.Arguments[1].NameColon != null)
                        {
                            if (invocation.ArgumentList.Arguments[1].NameColon.Name.Identifier.ValueText == "conditionString")
                                conditionString = invocation.ArgumentList.Arguments[1].Expression;
                            else
                                message = invocation.ArgumentList.Arguments[1].Expression;
                        }
                        else
                        {
                            message = invocation.ArgumentList.Arguments[1].Expression;
                        }
                    }

                    if (invocation.ArgumentList.Arguments.Count == 3)
                    {
                        if (invocation.ArgumentList.Arguments[2].NameColon != null)
                        {
                            if (invocation.ArgumentList.Arguments[2].NameColon.Name.Identifier.ValueText == "message" ||
                                invocation.ArgumentList.Arguments[2].NameColon.Name.Identifier.ValueText == "userMessage")
                                message = invocation.ArgumentList.Arguments[2].Expression;
                            else
                                conditionString = invocation.ArgumentList.Arguments[2].Expression;
                        }
                        else
                        {
                            conditionString = invocation.ArgumentList.Arguments[2].Expression;
                        }
                    }

                    if (className != null && methodName != null && condition != null)
                    {
                        invocationInfo = new ContractInvocationInfo(className, methodName, genericExceptionType, condition, message, conditionString, invocation.ArgumentList);
                        return true;
                    }
                }
            }


            invocationInfo = null;
            return false;
        }

        public static bool ParseInvocation(StatementSyntax statement, out ContractInvocationInfo invocationInfo)
        {
            if (statement != null &&
                statement is ExpressionStatementSyntax exprSt &&
                exprSt.Expression is InvocationExpressionSyntax invocation)
            {
                return ParseInvocation(invocation, out invocationInfo);
            }

            invocationInfo = null;
            return false;
        }


        public static bool IsInvariantMethod(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
                return false;
            var attrib = methodSymbol.GetAttributes();
            return attrib.Any(o => o.AttributeClass.Name == nameof(System.Diagnostics.Contracts.ContractInvariantMethodAttribute));
        }
        public static bool IsInvariantMethod(ISymbol methodSymbol)
        {
            if (methodSymbol == null)
                return false;
            return (methodSymbol is IMethodSymbol smb) && IsInvariantMethod(smb);
        }
        public static bool IsContractClass(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
                return false;
            var attrib = typeSymbol.GetAttributes();
            return attrib.Any(o => o.AttributeClass.Name == nameof(System.Diagnostics.Contracts.ContractClassForAttribute));
        }
        public static bool IsContractClass(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
                return false;

            return IsContractClass(methodSymbol.ContainingType);
        }
        public static bool IsContractClass(ISymbol symbol)
        {
            if (symbol == null)
                return false;

            if (symbol is IMethodSymbol methodSymbol)
                return IsContractClass(methodSymbol);

            if (symbol is ITypeSymbol typeSymbol)
                return IsContractClass(typeSymbol);

            return false;
        }
    }
}
