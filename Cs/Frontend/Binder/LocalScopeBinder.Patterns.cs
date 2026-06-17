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
    internal sealed partial class LocalScopeBinder : Binder
    {
        private BoundStatement BindCheckedStatement(CheckedStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            bool isChecked = node.Keyword.Kind == SyntaxKind.CheckedKeyword;
            var flags = ApplyCheckedContext(Flags, isChecked);
            var binder = WithFlags(flags);

            var statement = binder.BindStatement(node.Block, context, diagnostics);
            return isChecked
                ? new BoundCheckedStatement(node, statement)
                : new BoundUncheckedStatement(node, statement);
        }
        private BoundStatement BindFixedStatement(
            FixedStatementSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            EnsureUnsafe(node, context, diagnostics);

            var decl = node.Declaration;

            if (IsVar(decl.Type))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FIXED_VAR",
                    DiagnosticSeverity.Error,
                    "Implicitly typed locals cannot be fixed.",
                    new Location(context.SemanticModel.SyntaxTree, decl.Type.Span)));
                return new BoundBadStatement(node);
            }

            var declaredPtrType = BindType(decl.Type, context, diagnostics) as PointerTypeSymbol;
            if (declaredPtrType is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FIXED001",
                    DiagnosticSeverity.Error,
                    "A fixed local must have a pointer type.",
                    new Location(context.SemanticModel.SyntaxTree, decl.Type.Span)));
                return new BoundBadStatement(node);
            }

            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            var decls = ImmutableArray.CreateBuilder<BoundLocalDeclarationStatement>(decl.Variables.Count);

            for (int i = 0; i < decl.Variables.Count; i++)
            {
                var v = decl.Variables[i];
                var name = v.Identifier.ValueText ?? string.Empty;

                if (name.Length == 0)
                    continue;

                if (scope.IsNameDeclaredInEnclosingScopes(name))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_LOCAL001",
                        DiagnosticSeverity.Error,
                        $"A local named '{name}' is already declared in this scope.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));
                }

                if (v.Initializer is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FIXED002",
                        DiagnosticSeverity.Error,
                        "A fixed local must be initialized.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));

                    continue;
                }

                var local = new LocalSymbol(
                    name: name,
                    containing: _containing,
                    type: declaredPtrType,
                    locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, v.Span)),
                    isByRef: false,
                    isConst: false,
                    isReadOnly: true);

                scope.DeclareLocal(local, v, context);

                var init = scope.BindFixedInitializer(
                    exprSyntax: v.Initializer.Value,
                    declaredPointerType: declaredPtrType,
                    diagnosticNode: v,
                    context: context,
                    diagnostics: diagnostics);

                decls.Add(new BoundLocalDeclarationStatement(v, local, init));
            }

            var body = scope.BindStatement(node.Statement, context, diagnostics);
            return new BoundFixedStatement(node, decls.ToImmutable(), body);
        }
        private BoundExpression BindFixedInitializer(
            ExpressionSyntax exprSyntax,
            PointerTypeSymbol declaredPointerType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            BoundExpression result;

            if (exprSyntax is PrefixUnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.AmpersandToken } addr)
            {
                result = BindFixedAddressOfInitializer(addr, declaredPointerType, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, result);
                return result;
            }

            var expr = BindExpression(exprSyntax, context, diagnostics);

            // array
            if (expr.Type is ArrayTypeSymbol arrayType)
            {
                if (!GenericConstraintFacts.IsUnmanagedType(arrayType.ElementType))
                {
                    result = new BoundBadExpression(exprSyntax);
                    diagnostics.Add(new Diagnostic(
                        "CN_FIXED_INIT002",
                        DiagnosticSeverity.Error,
                        "fixed targed type must be unmanaged.",
                        new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));
                    context.Recorder.RecordBound(exprSyntax, result);
                    return result;
                }

                var elementPtr = context.Compilation.CreatePointerType(arrayType.ElementType);
                var conv = ClassifyConversion(new BoundTypeOnlyExpression(exprSyntax, elementPtr), declaredPointerType);
                if (!conv.IsImplicit)
                {
                    result = new BoundBadExpression(exprSyntax);
                    diagnostics.Add(new Diagnostic(
                        "CN_FIXED_INIT003",
                        DiagnosticSeverity.Error,
                        "fixed targed type has no implicit conversion from an element type.",
                        new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));
                    context.Recorder.RecordBound(exprSyntax, result);
                    return result;
                }

                result = new BoundFixedInitializerExpression(
                    exprSyntax,
                    declaredPointerType,
                    BoundFixedInitializerKind.Array,
                    expr,
                    arrayType.ElementType,
                    conv);

                context.Recorder.RecordBound(exprSyntax, result);
                return result;
            }

            // string
            if (expr.Type.SpecialType == SpecialType.System_String)
            {
                var charType = context.Compilation.GetSpecialType(SpecialType.System_Char);
                var charPtr = context.Compilation.CreatePointerType(charType);
                var conv = ClassifyConversion(new BoundTypeOnlyExpression(exprSyntax, charPtr), declaredPointerType);
                if (!conv.IsImplicit)
                {
                    result = new BoundBadExpression(exprSyntax);
                    diagnostics.Add(new Diagnostic(
                        "CN_FIXED_INIT003",
                        DiagnosticSeverity.Error,
                        "fixed targed type has no implicit conversion from an element type.",
                        new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));
                    context.Recorder.RecordBound(exprSyntax, result);
                    return result;
                }

                result = new BoundFixedInitializerExpression(
                    exprSyntax,
                    declaredPointerType,
                    BoundFixedInitializerKind.String,
                    expr,
                    charType,
                    conv);

                context.Recorder.RecordBound(exprSyntax, result);
                return result;
            }

            // GetPinnableReference
            if (TryBindFixedGetPinnableReference(exprSyntax, expr, declaredPointerType, context, diagnostics, out var fixedInit))
            {
                context.Recorder.RecordBound(exprSyntax, fixedInit);
                return fixedInit;
            }

            result = new BoundBadExpression(exprSyntax);
            diagnostics.Add(new Diagnostic(
                "CN_FIXED_INIT004",
                DiagnosticSeverity.Error,
                "Unknown fixed expression.",
                new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));
            context.Recorder.RecordBound(exprSyntax, result);
            return result;
        }
        private BoundExpression BindFixedAddressOfInitializer(
            PrefixUnaryExpressionSyntax node,
            PointerTypeSymbol declaredPointerType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            EnsureUnsafe(node, context, diagnostics);

            var operand = BindAssignableValue(node.Operand, context, diagnostics);
            if (operand.HasErrors)
                return operand;

            if (operand is BoundLocalExpression bl && bl.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ADDR001",
                    DiagnosticSeverity.Error,
                    "Cannot take the address of a const local.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(declaredPointerType);
                return bad;
            }

            if (operand.Type.IsReferenceType || operand.Type is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ADDR002",
                    DiagnosticSeverity.Error,
                    $"Cannot take the address of managed type '{operand.Type.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(declaredPointerType);
                return bad;
            }

            var actualPtrType = context.Compilation.CreatePointerType(operand.Type);
            var conv = ClassifyConversion(new BoundTypeOnlyExpression(node, actualPtrType), declaredPointerType);

            if (!conv.IsImplicit)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FIXED_INIT003",
                    DiagnosticSeverity.Error,
                    "fixed target type has no implicit conversion from an element type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(declaredPointerType);
                return bad;
            }

            var addr = new BoundAddressOfExpression(node, actualPtrType, operand);

            return new BoundFixedInitializerExpression(
                node,
                declaredPointerType,
                BoundFixedInitializerKind.AddressOf,
                addr,
                operand.Type,
                conv);
        }
        private bool TryBindFixedGetPinnableReference(
            ExpressionSyntax exprSyntax,
            BoundExpression receiver,
            PointerTypeSymbol declaredPointerType,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = null!;

            var receiverType = GetReceiverTypeForMemberLookup(receiver.Type);
            if (receiverType is null)
                return false;

            var instanceCandidates = ImmutableArray.CreateBuilder<MethodSymbol>();
            var extensionCandidates = ImmutableArray.CreateBuilder<MethodSymbol>();

            static bool IsSuitableFixedPinnable(
                MethodSymbol method,
                bool expectStatic,
                bool expectSingleReceiverParam,
                BindingContext context)
            {
                if (method.IsStatic != expectStatic)
                    return false;
                if (method.TypeParameters.Length != 0)
                    return false;

                if (expectSingleReceiverParam)
                {
                    if (method.Parameters.Length != 1)
                        return false;
                }
                else if (method.Parameters.Length != 0)
                {
                    return false;
                }

                if (!expectStatic && !AccessibilityHelper.IsAccessible(method, context))
                    return false;

                if (method.ReturnType is not ByRefTypeSymbol byRef)
                    return false;

                return GenericConstraintFacts.IsUnmanagedType(byRef.ElementType);
            }

            var methods = LookupMethods(receiverType, "GetPinnableReference");
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (IsSuitableFixedPinnable(m, expectStatic: false, expectSingleReceiverParam: false, context))
                    instanceCandidates.Add(m);
            }

            MethodSymbol? chosenMethod = null;

            if (instanceCandidates.Count != 0)
            {
                if (instanceCandidates.Count != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FIXED_INIT005",
                        DiagnosticSeverity.Error,
                        "Ambiguous GetPinnableReference() for fixed initializer.",
                        new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));

                    result = new BoundBadExpression(exprSyntax);
                    return true;
                }

                chosenMethod = instanceCandidates[0];
            }
            else
            {
                var ext = LookupExtensionMethods("GetPinnableReference", receiver, context);
                for (int i = 0; i < ext.Length; i++)
                {
                    var m = ext[i];
                    if (IsSuitableFixedPinnable(m, expectStatic: true, expectSingleReceiverParam: true, context))
                        extensionCandidates.Add(m);
                }

                if (extensionCandidates.Count == 0)
                    return false;

                if (extensionCandidates.Count != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FIXED_INIT005",
                        DiagnosticSeverity.Error,
                        "Ambiguous GetPinnableReference() for fixed initializer.",
                        new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));

                    result = new BoundBadExpression(exprSyntax);
                    return true;
                }

                chosenMethod = extensionCandidates[0];
            }

            var elementType = ((ByRefTypeSymbol)chosenMethod.ReturnType).ElementType;
            var elementPtr = context.Compilation.CreatePointerType(elementType);
            var conv = ClassifyConversion(new BoundTypeOnlyExpression(exprSyntax, elementPtr), declaredPointerType);

            if (!conv.IsImplicit)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FIXED_INIT003",
                    DiagnosticSeverity.Error,
                    "fixed target type has no implicit conversion from an element type.",
                    new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));

                result = new BoundBadExpression(exprSyntax);
                return true;
            }

            result = new BoundFixedInitializerExpression(
                exprSyntax,
                declaredPointerType,
                BoundFixedInitializerKind.GetPinnableReference,
                receiver,
                elementType,
                conv,
                getPinnableReferenceMethodOpt: chosenMethod);

            return true;
        }
        private BoundExpression BindIsPatternExpression(IsPatternExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var operand = BindExpression(node.Expression, context, diagnostics);
            if (operand.HasErrors)
                return new BoundBadExpression(node);

            if (PatternInputMayBeTestedMoreThanOnce(node.Pattern) &&
                operand.Type is not NullTypeSymbol &&
                operand.Type is not DefaultLiteralTypeSymbol)
            {
                return BindIsPatternExpressionWithInputTemp(node, operand, context, diagnostics);
            }

            return BindIsPatternCore(node, operand, node.Pattern, context, diagnostics);
        }
        private BoundExpression BindIsPatternExpressionWithInputTemp(
            IsPatternExpressionSyntax node,
            BoundExpression operand,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var temp = NewTemp("$patternInput", operand.Type);

            var tempStore = new BoundExpressionStatement(
                node.Expression,
                new BoundAssignmentExpression(
                    node.Expression,
                    new BoundLocalExpression(node.Expression, temp),
                    operand));

            var tempRead = new BoundLocalExpression(node.Expression, temp);
            var value = BindIsPatternCore(node, tempRead, node.Pattern, context, diagnostics);

            if (value.HasErrors)
                return value;

            return new BoundSequenceExpression(
                node,
                locals: ImmutableArray.Create(temp),
                sideEffects: ImmutableArray.Create<BoundStatement>(tempStore),
                value: value);
        }

        private static bool PatternInputMayBeTestedMoreThanOnce(PatternSyntax pattern)
        {
            return pattern switch
            {
                ParenthesizedPatternSyntax p =>
                    PatternInputMayBeTestedMoreThanOnce(p.Pattern),

                UnaryPatternSyntax u when u.Kind == SyntaxKind.NotPattern =>
                    PatternInputMayBeTestedMoreThanOnce(u.Pattern),

                BinaryPatternSyntax b when b.Kind is SyntaxKind.AndPattern or SyntaxKind.OrPattern =>
                    true,

                _ => false
            };
        }
        private BoundExpression BindIsPatternCore(
            SyntaxNode wholeSyntax,
            BoundExpression operand,
            PatternSyntax pattern,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            switch (pattern)
            {
                case ParenthesizedPatternSyntax paren:
                    return BindIsPatternCore(wholeSyntax, operand, paren.Pattern, context, diagnostics);

                case TypePatternSyntax typePattern:
                    {
                        var patternType = BindType(typePattern.Type, context, diagnostics);
                        return BindIsTypePattern(
                            wholeSyntax,
                            operand,
                            patternType,
                            declaredLocalOpt: null,
                            isDiscard: false,
                            diagnosticNode: typePattern,
                            context,
                            diagnostics);
                    }

                case DeclarationPatternSyntax declPattern:
                    {
                        var patternType = BindType(declPattern.Type, context, diagnostics);
                        if (patternType is ErrorTypeSymbol)
                            return new BoundBadExpression(wholeSyntax);

                        if (!TryBindPatternDesignation(
                            declPattern.Designation,
                            patternType,
                            context,
                            diagnostics,
                            out var local,
                            out var isDiscard))
                        {
                            return new BoundBadExpression(wholeSyntax);
                        }

                        return BindIsTypePattern(
                            wholeSyntax,
                            operand,
                            patternType,
                            local,
                            isDiscard,
                            declPattern,
                            context,
                            diagnostics);
                    }

                case ConstantPatternSyntax constantPattern:
                    return BindConstantPatternInIs(
                        wholeSyntax,
                        operand,
                        constantPattern,
                        context,
                        diagnostics);

                case BinaryPatternSyntax binaryPattern when binaryPattern.Kind is SyntaxKind.AndPattern or SyntaxKind.OrPattern:
                    {
                        var left = BindIsPatternCore(
                            wholeSyntax,
                            operand,
                            binaryPattern.Left,
                            context,
                            diagnostics);

                        LocalScopeBinder rightBinder = binaryPattern.Kind == SyntaxKind.AndPattern
                            ? CreateFlowScopeBinderForTrue(left)
                            : CreateFlowScopeBinderForFalse(left);

                        var right = rightBinder.BindIsPatternCore(
                            wholeSyntax,
                            operand,
                            binaryPattern.Right,
                            context,
                            diagnostics);

                        return binaryPattern.Kind == SyntaxKind.AndPattern
                            ? MakeLogicalAnd(binaryPattern, left, right, context, diagnostics)
                            : MakeLogicalOr(binaryPattern, left, right, context, diagnostics);
                    }

                case UnaryPatternSyntax unaryPattern when unaryPattern.Kind == SyntaxKind.NotPattern:
                    {
                        var inner = BindIsPatternCore(
                            wholeSyntax,
                            operand,
                            unaryPattern.Pattern,
                            context,
                            diagnostics);

                        if (inner is BoundIsPatternExpression ip)
                        {
                            return new BoundIsPatternExpression(
                                syntax: wholeSyntax,
                                operand: ip.Operand,
                                boolType: ip.Type,
                                patternKind: ip.PatternKind,
                                patternTypeOpt: ip.PatternTypeOpt,
                                constantOpt: ip.ConstantOpt,
                                comparisonTypeOpt: ip.ComparisonTypeOpt,
                                declaredLocalOpt: ip.DeclaredLocalOpt,
                                isDiscard: ip.IsDiscard,
                                isNegated: !ip.IsNegated);
                        }

                        return MakeLogicalNot(unaryPattern, inner, context, diagnostics);
                    }

                case VarPatternSyntax:
                    diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS002",
                    DiagnosticSeverity.Error,
                    "'var' patterns are not supported.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                    return new BoundBadExpression(wholeSyntax);

                case DiscardPatternSyntax:
                    diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS003",
                    DiagnosticSeverity.Error,
                    "Discard-only patterns in 'is' expressions are not supported.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                    return new BoundBadExpression(wholeSyntax);

                default:
                    diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS004",
                    DiagnosticSeverity.Error,
                    $"Pattern '{pattern.Kind}' is not supported in 'is' expressions.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                    return new BoundBadExpression(wholeSyntax);
            }
        }
        private BoundExpression BindConstantPatternInIs(
            SyntaxNode wholeSyntax,
            BoundExpression operand,
            ConstantPatternSyntax pattern,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var constant = BindExpression(pattern.Expression, context, diagnostics);
            if (constant.HasErrors)
                return new BoundBadExpression(wholeSyntax);

            if (IsNullConstantPattern(constant))
                return BindIsNullPattern(wholeSyntax, operand, pattern, context);

            if (!constant.ConstantValueOpt.HasValue)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS011",
                    DiagnosticSeverity.Error,
                    "Constant pattern expression must be a compile-time constant.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            var comparisonType = ChooseBasicConstantPatternComparisonType(operand, constant, context);
            if (comparisonType is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS012",
                    DiagnosticSeverity.Error,
                    $"Constant pattern of type '{constant.Type.Name}' is not supported for operand type '{operand.Type.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, pattern.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            BoundExpression left = operand;
            if (!ReferenceEquals(left.Type, comparisonType))
            {
                left = ApplyConversion(
                    exprSyntax: (ExpressionSyntax)left.Syntax,
                    expr: left,
                    targetType: comparisonType,
                    diagnosticNode: pattern,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (left.HasErrors)
                    return new BoundBadExpression(wholeSyntax);
            }

            BoundExpression right = constant;
            if (!ReferenceEquals(right.Type, comparisonType))
            {
                right = ApplyConversion(
                    exprSyntax: pattern.Expression,
                    expr: right,
                    targetType: comparisonType,
                    diagnosticNode: pattern,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (right.HasErrors || !right.ConstantValueOpt.HasValue)
                    return new BoundBadExpression(wholeSyntax);
            }

            if (comparisonType.SpecialType == SpecialType.System_String || IsEnumType(comparisonType))
            {
                return BindSynthesizedConstantPatternEquality(
                    wholeSyntax,
                    left,
                    right,
                    pattern.Expression,
                    context,
                    diagnostics);
            }

            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
            return new BoundIsPatternExpression(
                syntax: wholeSyntax,
                operand: left,
                boolType: boolType,
                patternKind: BoundIsPatternKind.Constant,
                constantOpt: right,
                comparisonTypeOpt: comparisonType);
        }
        private BoundExpression BindSynthesizedConstantPatternEquality(
            SyntaxNode wholeSyntax,
            BoundExpression left,
            BoundExpression right,
            ExpressionSyntax rightSyntax,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var leftSyntax = left.Syntax as ExpressionSyntax ?? rightSyntax;
            var eqToken = MakeToken(SyntaxKind.EqualsEqualsToken, wholeSyntax.Span);
            var eqSyntax = new BinaryExpressionSyntax(
                SyntaxKind.EqualsExpression,
                leftSyntax,
                eqToken,
                rightSyntax);

            return BindEqualityBinary(eqSyntax, left, right, context, diagnostics);
        }
        private TypeSymbol? ChooseBasicConstantPatternComparisonType(
            BoundExpression operand,
            BoundExpression constant,
            BindingContext context)
        {
            if (IsBasicConstantPatternType(operand.Type))
            {
                var constToOperand = ClassifyConversion(constant, operand.Type, context);
                if (constToOperand.IsImplicit)
                    return operand.Type;
            }

            if (IsBasicConstantPatternType(constant.Type))
            {
                var operandToConst = ClassifyConversion(operand, constant.Type, context);
                if (operandToConst.IsImplicit)
                    return constant.Type;
            }

            return null;
        }
        private static bool IsBasicConstantPatternType(TypeSymbol type)
        {
            if (IsEnumType(type))
                return true;

            return type.SpecialType is
                SpecialType.System_Boolean or
                SpecialType.System_Char or
                SpecialType.System_Int8 or
                SpecialType.System_UInt8 or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Single or
                SpecialType.System_Double or
                SpecialType.System_String;
        }
        private BoundExpression BindIsNullPattern(
            SyntaxNode wholeSyntax,
            BoundExpression operand,
            ConstantPatternSyntax pattern,
            BindingContext context)
        {
            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);

            BoundExpression left = operand;

            if (NeedsBoxingForNullTest(operand.Type))
            {
                var objectType = context.Compilation.GetSpecialType(SpecialType.System_Object);
                left = new BoundConversionExpression(
                    syntax: pattern.Expression,
                    type: objectType,
                    operand: operand,
                    conversion: new Conversion(ConversionKind.Boxing),
                    isChecked: false);
            }

            return new BoundIsPatternExpression(
                syntax: wholeSyntax,
                operand: left,
                boolType: boolType,
                patternKind: BoundIsPatternKind.Null,
                constantOpt: new BoundLiteralExpression(pattern.Expression, NullTypeSymbol.Instance, null));
        }
        private static bool NeedsBoxingForNullTest(TypeSymbol type)
        {
            if (type is TypeParameterSymbol tp)
                return (tp.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) == 0;

            return type.IsValueType;
        }
        private static bool IsNullConstantPattern(BoundExpression expr)
        {
            return expr.Type is NullTypeSymbol
                || (expr.ConstantValueOpt.HasValue && expr.ConstantValueOpt.Value is null);
        }
        private bool TryBindPatternDesignation(
            VariableDesignationSyntax designation,
            TypeSymbol localType,
            BindingContext context,
            DiagnosticBag diagnostics,
            out LocalSymbol? local,
            out bool isDiscard)
        {
            switch (designation)
            {
                case DiscardDesignationSyntax:
                    local = null;
                    isDiscard = true;
                    return true;

                case SingleVariableDesignationSyntax single:
                    {
                        var name = single.Identifier.ValueText ?? string.Empty;
                        if (name.Length == 0)
                        {
                            local = null;
                            isDiscard = false;
                            return false;
                        }

                        if (IsNameDeclaredInEnclosingScopes(name))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_PAT_IS005",
                                DiagnosticSeverity.Error,
                                $"A local named '{name}' is already declared in this scope.",
                                new Location(context.SemanticModel.SyntaxTree, single.Span)));
                        }

                        local = new LocalSymbol(
                            name: name,
                            containing: _containing,
                            type: localType,
                            locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, single.Span)),
                            isConst: false,
                            constantValueOpt: Optional<object>.None,
                            isByRef: false);

                        context.Recorder.RecordDeclared(single, local);
                        isDiscard = false;
                        return true;
                    }

                default:
                    diagnostics.Add(new Diagnostic(
                        "CN_PAT_IS006",
                        DiagnosticSeverity.Error,
                        "Only single-variable and discard designations are supported in patterns.",
                        new Location(context.SemanticModel.SyntaxTree, designation.Span)));
                    local = null;
                    isDiscard = false;
                    return false;
            }
        }
        private BoundExpression BindIsTypePattern(
            SyntaxNode wholeSyntax,
            BoundExpression operand,
            TypeSymbol patternType,
            LocalSymbol? declaredLocalOpt,
            bool isDiscard,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (patternType is ErrorTypeSymbol)
                return new BoundBadExpression(wholeSyntax);

            if (patternType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS007",
                    DiagnosticSeverity.Error,
                    "Pattern type cannot be 'void'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            if (patternType is PointerTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS008",
                    DiagnosticSeverity.Error,
                    "Pattern type cannot be a pointer type.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            if (patternType.IsRefLikeType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS009",
                    DiagnosticSeverity.Error,
                    "Pattern matching against ref-like types is not supported.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return new BoundBadExpression(wholeSyntax);
            }

            if (!(ClassifyConversion(operand, patternType, context).Kind is ConversionKind.Identity
                or ConversionKind.ImplicitReference
                or ConversionKind.ExplicitReference
                or ConversionKind.Boxing
                or ConversionKind.Unboxing
                or ConversionKind.NullLiteral))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PAT_IS010",
                    DiagnosticSeverity.Warning,
                    $"Expression of type '{operand.Type.Name}' is never of type '{patternType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
            }

            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
            return new BoundIsPatternExpression(
                syntax: wholeSyntax,
                operand: operand,
                boolType: boolType,
                patternKind: BoundIsPatternKind.Type,
                patternTypeOpt: patternType,
                declaredLocalOpt: declaredLocalOpt,
                isDiscard: isDiscard);

        }
    }
}
