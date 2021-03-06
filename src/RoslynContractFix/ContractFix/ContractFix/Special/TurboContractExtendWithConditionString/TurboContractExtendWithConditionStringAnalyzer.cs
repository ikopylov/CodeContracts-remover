﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace ContractFix.Special.TurboContractExtendWithConditionString
{
    //[DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class TurboContractExtendWithConditionStringAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CRS11_TurboContractExtendWithConditionString";
        private const string Title = "TurboContract call can be extended with condition message";
        private const string MessageFormat = "TurboContract call can be extended with condition message";
        private const string Description = "TurboContract call can be extended with condition message";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Info, isEnabledByDefault: true, description: Description);

        internal static HashSet<string> MethodNamesToFix { get; } = new HashSet<string>()
        {
            nameof(System.Diagnostics.Contracts.Contract.Requires),
            nameof(System.Diagnostics.Contracts.Contract.Assert),
            nameof(System.Diagnostics.Contracts.Contract.Assume)
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeInvocationOp, OperationKind.Invocation);
        }


        private static bool IsCodeContractToReplace(Compilation compilation, IInvocationOperation invocation)
        {
            if (invocation.TargetMethod.ContainingType.Name != ContractStatementAnalyzer.SpecialContractClass)
                return false;

            if (invocation.TargetMethod.IsGenericMethod)
                return false;

            if (!MethodNamesToFix.Contains(invocation.TargetMethod.Name))
                return false;

            if (invocation.Arguments.Length >= 2)
                return false;

            return true;
        }

        private static void AnalyzeInvocationOp(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (invocation.TargetMethod.Kind != SymbolKind.Method || !invocation.TargetMethod.IsStatic)
                return;
            if (!IsCodeContractToReplace(context.Compilation, invocation))
                return;
            

            var invocationSyntax = (InvocationExpressionSyntax)invocation.Syntax;
            if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocationSyntax.GetLocation()));
        }
    }
}
