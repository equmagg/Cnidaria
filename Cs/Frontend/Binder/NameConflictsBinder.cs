using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Cnidaria.Cs
{
    internal static class NameConflictBinder
    {
        public static void BindAll(Compilation compilation, DiagnosticBag diagnostics)
        {
            var visited = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            VisitNamespace(compilation.SourceGlobalNamespace, diagnostics, visited);
        }
        private static void VisitNamespace(NamespaceSymbol ns, DiagnosticBag diagnostics, HashSet<NamedTypeSymbol> visited)
        {
            var namespaces = ns.GetNamespaceMembers();
            for (int i = 0; i < namespaces.Length; i++)
                VisitNamespace(namespaces[i], diagnostics, visited);
            var types = ns.GetTypeMembers();
            for (int i = 0; i < types.Length; i++)
                VisitType(types[i], diagnostics, visited);
        }
        private static void VisitType(NamedTypeSymbol type, DiagnosticBag diagnostics, HashSet<NamedTypeSymbol> visited)
        {
            if (!visited.Add(type))
                return;
            ValidateDuplicateMethodSignatures(type, diagnostics);
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is NamedTypeSymbol nestedType)
                    VisitType(nestedType, diagnostics, visited);
            }
        }
        private static void ValidateDuplicateMethodSignatures(NamedTypeSymbol type, DiagnosticBag diagnostics)
        {
            var members = type.GetMembers();
            var seen = new List<MethodSymbol>();

            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is not MethodSymbol method)
                    continue;

                if (!IsSourceUserCallableMethod(method, out var methodReference))
                    continue;

                for (int j = 0; j < seen.Count; j++)
                {
                    var previous = seen[j];
                    if (!HasSameMemberSignature(previous, method))
                        continue;

                    diagnostics.Add(new Diagnostic(
                        "CS0111",
                        DiagnosticSeverity.Error,
                        $"Type '{type.Name}' already defines a member called '{method.Name}' with the same parameter types",
                        new Location(methodReference.SyntaxTree, GetDiagnosticSpan(methodReference.Node))));
                    break;
                }

                seen.Add(method);
            }
        }
        private static bool IsSourceUserCallableMethod(MethodSymbol method, out SyntaxReference declaration)
        {
            declaration = default;

            if (method is not SourceMethodSymbol)
                return false;

            var declarations = method.DeclaringSyntaxReferences;
            if (declarations.IsDefaultOrEmpty)
                return false;

            var first = declarations[0];
            if (first.Node is MethodDeclarationSyntax methodDeclaration)
            {
                if (methodDeclaration.ExplicitInterfaceSpecifier is not null)
                    return false;

                declaration = first;
                return true;
            }

            if (first.Node is ConstructorDeclarationSyntax)
            {
                declaration = first;
                return true;
            }

            return false;
        }
        private static TextSpan GetDiagnosticSpan(SyntaxNode node)
        {
            return node switch
            {
                MethodDeclarationSyntax md => md.Identifier.Span,
                ConstructorDeclarationSyntax cd => cd.Identifier.Span,
                _ => node.Span
            };
        }
        private static bool HasSameMemberSignature(MethodSymbol left, MethodSymbol right)
        {
            if (!StringComparer.Ordinal.Equals(left.Name, right.Name))
                return false;

            if (left.TypeParameters.Length != right.TypeParameters.Length)
                return false;

            var leftParameters = left.Parameters;
            var rightParameters = right.Parameters;
            if (leftParameters.Length != rightParameters.Length)
                return false;

            for (int i = 0; i < leftParameters.Length; i++)
            {
                if (leftParameters[i].RefKind != rightParameters[i].RefKind)
                    return false;

                if (!LocalScopeBinder.AreSameType(leftParameters[i].Type, rightParameters[i].Type))
                    return false;
            }
            return true;
        }
    }
}
