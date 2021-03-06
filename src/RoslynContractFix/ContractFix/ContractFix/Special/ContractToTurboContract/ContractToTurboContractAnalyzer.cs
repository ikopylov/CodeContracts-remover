﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace ContractFix.Special.ContractToTurboContract
{
    //[DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ContractToTurboContractAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "CRS08_ContractToTurboContractReplace";
        public const string DiagnosticIdWithinCC = "CRS09_ContractToTurboContractReplaceWitihinContractClass";
        private const string Title = "Contract should be replaced with TurboContract";
        private const string MessageFormat = "Contract should be replaced with TurboContract";
        private const string Description = "Contract should be replaced with TurboContract";
        private const string Category = "Usage";

        private static DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);
        private static DiagnosticDescriptor RuleWithinCC = new DiagnosticDescriptor(DiagnosticIdWithinCC, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);


        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule, RuleWithinCC); } }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            context.RegisterOperationAction(AnalyzeInvocationOp, OperationKind.Invocation);
        }


        private static bool IsCodeContractToReplace(Compilation compilation, IInvocationOperation invocation)
        {
            if (!compilation.IsEqualTypes(invocation.TargetMethod.ContainingType, typeof(System.Diagnostics.Contracts.Contract)))
                return false;
            if (invocation.TargetMethod.IsGenericMethod && invocation.TargetMethod.Name == nameof(System.Diagnostics.Contracts.Contract.Requires))
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

            DiagnosticDescriptor rule = Rule;
            if (ContractStatementAnalyzer.IsContractClass(context.ContainingSymbol))
                rule = RuleWithinCC;


            var invocationSyntax = (InvocationExpressionSyntax)invocation.Syntax;
            if (invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess)
                context.ReportDiagnostic(Diagnostic.Create(rule, memberAccess.Expression.GetLocation()));
        }
    }
}
