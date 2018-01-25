using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ContractFix
{
    public static class Helpers
    {
        public static void AddUsing(DocumentEditor editor, string namespaceName, int position)
        {
            var compUnit = editor.OriginalRoot as Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax;
            if (compUnit == null)
                return;

            if (compUnit.Usings.Any(u => u.Name.GetText().ToString() == namespaceName))
                return;

            if (position >= 0)
            {
                var enclosingSymbol = editor.SemanticModel.GetEnclosingSymbol(position);
                if (enclosingSymbol != null)
                {
                    var nsEnclName = enclosingSymbol.ContainingNamespace.ToString();
                    if (nsEnclName.StartsWith(namespaceName + ".") || nsEnclName == namespaceName)
                        return;
                }
            }


            var usingDirective = editor.Generator.NamespaceImportDeclaration(namespaceName);
            editor.InsertAfter(compUnit.Usings.Last(), usingDirective);
        }

        public static INamedTypeSymbol GetKnownType(this Compilation compilation, Type type)
        {
            return compilation.GetTypeByMetadataName(type.FullName);
        }

        public static bool IsEqualTypes(this Compilation compilation, ITypeSymbol typeSymbol, Type type)
        {
            return typeSymbol.ToString() == type.FullName;

            //var typeSymbolKnown = compilation.GetTypeByMetadataName(type.FullName);
            //return typeSymbol.Equals(typeSymbolKnown);
        }

        public static bool IsTypeOrSubtype(ITypeSymbol typeSymbol, ITypeSymbol baseType)
        {
            do
            {
                if (typeSymbol.Equals(baseType))
                    return true;

                typeSymbol = typeSymbol.BaseType;
            }
            while (typeSymbol != null);

            return false;
        }
    }
}
