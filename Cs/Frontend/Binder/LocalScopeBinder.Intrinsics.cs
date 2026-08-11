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
            var operandType = BindType(node.Type, context, diagnostics);

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
        private static bool TryGetCompileTimeSizeOf(TypeSymbol type, TargetInfo target, out int size)
        {
            var visiting = new HashSet<TypeSymbol>();
            return TryGetStorageSizeAlign(type, target, visiting, out size, out _);
        }
        // Recursive value type layout uses declared instance fields and target pointer size
        private static bool TryGetStorageSizeAlign(
            TypeSymbol type,
            TargetInfo target,
            HashSet<TypeSymbol> visiting,
            out int size,
            out int align)
        {
            size = 0;
            align = 1;

            if (type is null || type is ErrorTypeSymbol)
                return false;

            if (type.IsReferenceType || type is ArrayTypeSymbol || type is PointerTypeSymbol || type is ByRefTypeSymbol || type is FunctionPointerTypeSymbol)
            {
                size = target.PointerSize;
                align = target.PointerSize;
                return true;
            }

            if (type is TypeParameterSymbol)
                return false;

            if (TryGetKnownPrimitiveOrBuiltinSizeAlign(type, target.PointerSize, out size, out align))
                return true;

            if (type is NamedTypeSymbol nt)
            {
                if (nt.TypeKind == TypeKind.Enum)
                {
                    var ut = nt.EnumUnderlyingType;
                    if (ut is null)
                        return false;

                    return TryGetStorageSizeAlign(ut, target, visiting, out size, out align);
                }

                if (nt.TypeKind == TypeKind.Struct)
                {
                    if (!visiting.Add(nt))
                        return false; // Reject recursive value layouts

                    try
                    {
                        if (InlineArrayFacts.TryGetInfo(nt, out var inlineArray))
                        {
                            if (!TryGetStorageSizeAlign(inlineArray.ElementType, target, visiting, out int es, out int ea))
                                return false;

                            int inlineSize = checked(es * inlineArray.Length);
                            size = AlignUp(inlineSize, ea);
                            if (size == 0)
                                size = 1;

                            align = ea;
                            return true;
                        }

                        int offset = 0;
                        int maxAlign = 1;

                        var members = nt.GetMembers();
                        for (int i = 0; i < members.Length; i++)
                        {
                            if (members[i] is not FieldSymbol f || f.IsStatic)
                                continue;

                            if (!TryGetStorageSizeAlign(f.Type, target, visiting, out int fs, out int fa))
                                return false;

                            offset = AlignUp(offset, fa);
                            offset += fs;
                            if (fa > maxAlign)
                                maxAlign = fa;
                        }

                        size = AlignUp(offset, maxAlign);
                        if (size == 0)
                            size = 1; // Empty structs occupy one byte

                        align = maxAlign;
                        return true;
                    }
                    finally
                    {
                        visiting.Remove(nt);
                    }
                }
            }
            return false;
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
        private static int AlignUp(int value, int align)
        {
            int mask = align - 1;
            return (value + mask) & ~mask;
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
