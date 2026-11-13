using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    // Intrinsic expressions, compile-time sizes, and checked contexts
    internal sealed partial class LocalScopeBinder : Binder
    {
        private BoundExpression BindTypeOf(TypeOfExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            TypeSymbol operandType = BindTypeOfOperand(node.Type, context, diagnostics);

            if (!DeclarationBuilder.TryFindSystemType(context.Compilation.GlobalNamespace, "Type", arity: 0, out var systemType))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TYPEOF000",
                    DiagnosticSeverity.Error,
                    "Required type 'System.Type' was not found.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(new ErrorTypeSymbol("System.Type", containing: null, ImmutableArray<Location>.Empty));
                return bad;
            }

            if (operandType is ByRefTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TYPEOF001",
                    DiagnosticSeverity.Error,
                    "The typeof operator cannot be applied to a by-ref type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(systemType);
                return bad;
            }

            return new BoundTypeOfExpression(node, systemType, operandType);
        }
        private BoundExpression BindSizeOf(SizeOfExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var operandType = BindType(node.Type, context, diagnostics);
            if (operandType is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            if (operandType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SIZEOF000",
                    DiagnosticSeverity.Error,
                    "The sizeof operator cannot be applied to 'void'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));

                return new BoundBadExpression(node);
            }

            if (operandType is ByRefTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SIZEOF001",
                    DiagnosticSeverity.Error,
                    "The sizeof operator cannot be applied to a by-ref type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));

                return new BoundBadExpression(node);
            }

            var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);

            if (TryGetCompileTimeSizeOf(operandType, context.Compilation.Target, out int size))
                return new BoundLiteralExpression(node, intType, size);

            return new BoundSizeOfExpression(node, intType, operandType);
        }
        // Only a primitive, an enum or a pointer folds, the runtime lays out every struct
        private static bool TryGetCompileTimeSizeOf(TypeSymbol type, TargetInfo target, out int size)
        {
            size = 0;
            if (type is null || type is ErrorTypeSymbol || type is TypeParameterSymbol)
                return false;

            if (type.IsReferenceType || type is ArrayTypeSymbol || type is PointerTypeSymbol || type is ByRefTypeSymbol || type is FunctionPointerTypeSymbol)
            {
                size = target.PointerSize;
                return true;
            }

            if (TryGetKnownPrimitiveOrBuiltinSizeAlign(type, target.PointerSize, out size, out _))
                return true;

            return type is NamedTypeSymbol named && named.TypeKind == TypeKind.Enum && named.EnumUnderlyingType is TypeSymbol underlying &&
                TryGetCompileTimeSizeOf(underlying, target, out size);
        }
        private static bool TryGetKnownPrimitiveOrBuiltinSizeAlign(TypeSymbol type, int pointerSize, out int size, out int align)
        {
            size = 0;
            align = 1;

            switch (type.SpecialType)
            {
                case SpecialType.System_Void:
                    size = 0; align = 1; return true;

                case SpecialType.System_Boolean:
                case SpecialType.System_Int8:
                case SpecialType.System_UInt8:
                    size = 1; align = 1; return true;

                case SpecialType.System_Char:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                    size = 2; align = 2; return true;

                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Single:
                    size = 4; align = 4; return true;

                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Double:
                    size = 8; align = 8; return true;

                case SpecialType.System_Decimal:
                    size = 16; align = 8; return true;
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    size = pointerSize;
                    align = pointerSize;
                    return true;
            }

            return false;
        }
        private BoundExpression BindThrowExpression(ThrowExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var exType = GetSystemExceptionTypeOrReport(node, context, diagnostics);
            if (exType.Kind == SymbolKind.Error)
                return new BoundBadExpression(node);

            var expr = BindExpression(node.Expression, context, diagnostics);
            expr = ApplyConversion(
                exprSyntax: node.Expression,
                expr: expr,
                targetType: exType,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            return new BoundThrowExpression(node, expr);
        }
        // Checked and unchecked expressions clone binder flags while sharing scope state
        private BoundExpression BindCheckedExpression(CheckedExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            bool isChecked = node.Keyword.Kind == SyntaxKind.CheckedKeyword;
            var flags = ApplyCheckedContext(Flags, isChecked);
            var binder = WithFlags(flags);

            var expr = binder.BindExpression(node.Expression, context, diagnostics);
            return isChecked
                ? new BoundCheckedExpression(node, expr)
                : new BoundUncheckedExpression(node, expr);
        }
    }
}
