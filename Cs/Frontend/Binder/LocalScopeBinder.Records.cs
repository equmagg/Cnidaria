using System;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Cs;

internal sealed partial class LocalScopeBinder
{
    private BoundExpression BindWithExpression(WithExpressionSyntax syntax, BindingContext context, DiagnosticBag diagnostics)
    {
        var receiver = BindExpression(syntax.Expression, context, diagnostics);
        if (receiver.HasErrors)
            return receiver;
        BoundExpression copy = receiver;
        if (!receiver.Type.IsValueType)
        {
            NamedTypeSymbol? type = receiver.Type as NamedTypeSymbol;
            if (receiver.Type is TypeParameterSymbol parameter)
                type = EnumerateConstraintTypes(parameter).FirstOrDefault(t => t.IsRecord);
            var clone = type?.IsRecord == true ? RecordBinder.FindMethod(type, "<Clone>$") : null;
            if (clone is null || clone.IsStatic || clone.DeclaredAccessibility != Accessibility.Public)
            {
                diagnostics.Add(new Diagnostic("CS8858", DiagnosticSeverity.Error,
                    "The receiver of a with expression must be a record or a struct.", new Location(context.SemanticModel.SyntaxTree, syntax.Expression.Span)));
                return new BoundBadExpression(syntax);
            }
            copy = new BoundCallExpression(syntax, receiver, clone, ImmutableArray<BoundExpression>.Empty);
            if (!AreSameType(copy.Type, receiver.Type))
                copy = new BoundConversionExpression(syntax, receiver.Type, copy, new Conversion(ConversionKind.ExplicitReference), false);
        }
        return BindObjectCreationInitializer(syntax, copy, syntax.Initializer, context, diagnostics);
    }

    private static bool CanAssignInitOnlyProperty(BoundExpression? receiver, BindingContext context)
        => receiver is BoundThisExpression or BoundBaseExpression &&
           context.ContainingSymbol is MethodSymbol { IsStatic: false } method && (method.IsConstructor || method.IsInitOnly);
}
