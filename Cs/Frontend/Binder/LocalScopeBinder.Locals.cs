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
        private static bool IsExtensionMethod(MethodSymbol method)
        {
            if (method is null || !method.IsStatic || method.Parameters.Length == 0)
                return false;

            return method.IsExtensionMethod;
        }
        private BoundStatement BindLocalFunctionStatement(
            LocalFunctionStatementSyntax lf,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = lf.Identifier.ValueText ?? "";

            if (!_localFunctions.TryGetValue(name, out var sym))
                sym = DeclareLocalFunction(lf, context, diagnostics);

            var bodyFlags = Flags;
            if (HasModifier(lf.Modifiers, SyntaxKind.UnsafeKeyword))
                bodyFlags |= BinderFlags.UnsafeRegion;

            var bodyBinder = new LocalScopeBinder(
                parent: this,
                flags: bodyFlags,
                containing: sym,
                inheritFlowFromParent: false);

            var bodyCtx = new BindingContext(context.Compilation, context.SemanticModel, sym, context.Recorder);

            BoundStatement body;
            if (lf.Body != null)
            {
                body = bodyBinder.BindStatement(lf.Body, bodyCtx, diagnostics);
            }
            else if (lf.ExpressionBody != null)
            {
                var expr = bodyBinder.BindExpression(lf.ExpressionBody.Expression, bodyCtx, diagnostics);

                if (sym.ReturnType.SpecialType == SpecialType.System_Void)
                {
                    body = new BoundBlockStatement(lf,
                        ImmutableArray.Create<BoundStatement>(
                            new BoundExpressionStatement(lf.ExpressionBody, expr)));
                }
                else
                {
                    expr = bodyBinder.ApplyConversion(
                        exprSyntax: lf.ExpressionBody.Expression,
                        expr: expr,
                        targetType: sym.ReturnType,
                        diagnosticNode: lf,
                        context: bodyCtx,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    body = new BoundBlockStatement(lf,
                        ImmutableArray.Create<BoundStatement>(
                            new BoundReturnStatement(lf.ExpressionBody, expr)));
                }
            }
            else
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC007", DiagnosticSeverity.Error,
                    "Local function must have a body.",
                    new Location(context.SemanticModel.SyntaxTree, lf.Span)));
                body = new BoundBadStatement(lf);
            }

            bodyBinder.ReportControlFlowDiagnostics(bodyCtx, diagnostics, body, lf);

            return new BoundLocalFunctionStatement(lf, sym, body);
        }
        private BoundStatement BindLocalDeclaration(LocalDeclarationStatementSyntax ld, BindingContext context, DiagnosticBag diagnostics)
        {
            var decl = ld.Declaration;

            var isUsingDeclaration = ld.UsingKeyword.Span.Length != 0;
            if (ld.AwaitKeyword.Span.Length != 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING_AWAIT000",
                    DiagnosticSeverity.Error,
                    "await using is not supported.",
                    new Location(context.SemanticModel.SyntaxTree, ld.AwaitKeyword.Span)));
            }

            var isConst = HasModifier(ld.Modifiers, SyntaxKind.ConstKeyword);
            var isRefLocal = HasModifier(ld.Modifiers, SyntaxKind.RefKeyword);
            var isVar = IsVar(decl.Type);

            if (decl.Variables.Count == 0)
                return new BoundBadStatement(ld);

            if (isConst && isVar)
            {
                diagnostics.Add(new Diagnostic("CN_CONST000", DiagnosticSeverity.Error,
                    "A const local must have an explicit type (const var is not allowed).",
                    new Location(context.SemanticModel.SyntaxTree, decl.Type.Span)));
            }
            if (isConst && isRefLocal)
            {
                diagnostics.Add(new Diagnostic("CN_REFLOC000", DiagnosticSeverity.Error,
                    "A ref local cannot be const.",
                    new Location(context.SemanticModel.SyntaxTree, ld.Span)));
            }
            if (isUsingDeclaration && isConst)
            {
                diagnostics.Add(new Diagnostic("CN_USING_CONST000", DiagnosticSeverity.Error,
                    "A using local cannot be const.",
                    new Location(context.SemanticModel.SyntaxTree, ld.Span)));
            }
            if (isUsingDeclaration && isRefLocal)
            {
                diagnostics.Add(new Diagnostic("CN_USING_REF000", DiagnosticSeverity.Error,
                    "A using local cannot be a ref local.",
                    new Location(context.SemanticModel.SyntaxTree, ld.Span)));
            }
            if (isRefLocal && decl.Variables.Count != 1)
            {
                diagnostics.Add(new Diagnostic("CN_REFLOC001", DiagnosticSeverity.Error,
                    "A ref local declaration must declare exactly one variable.",
                    new Location(context.SemanticModel.SyntaxTree, decl.Span)));
            }

            TypeSymbol? explicitType = null;
            if (!isVar)
                explicitType = BindType(decl.Type, context, diagnostics);

            if (decl.Variables.Count == 1)
                return BindSingleDeclarator(
                    ld, decl.Variables[0], isVar, explicitType, isRefLocal, isConst, isUsingDeclaration, context, diagnostics);

            var list = ImmutableArray.CreateBuilder<BoundStatement>(decl.Variables.Count);
            for (int i = 0; i < decl.Variables.Count; i++)
                list.Add(BindSingleDeclarator(
                    ld, decl.Variables[i], isVar, explicitType, isRefLocal, isConst, isUsingDeclaration, context, diagnostics));

            return new BoundStatementList(ld, list.ToImmutable());
        }
        private BoundStatement BindSingleDeclarator(
            SyntaxNode ownerSyntax,
            VariableDeclaratorSyntax v,
            bool isVar,
            TypeSymbol? explicitType,
            bool isRefLocal,
            bool isConst,
            bool isUsing,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = v.Identifier.ValueText ?? "";

            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic("CN_LOCAL001", DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, v.Span)));
            }

            if (isConst && v.Initializer is null)
            {
                diagnostics.Add(new Diagnostic("CN_CONST001", DiagnosticSeverity.Error,
                    "A const local must be initialized.",
                    new Location(context.SemanticModel.SyntaxTree, v.Span)));
            }

            BoundExpression? init = null;
            TypeSymbol localType;
            if (isRefLocal)
            {
                if (v.Initializer is null)
                {
                    diagnostics.Add(new Diagnostic("CN_REFLOC002", DiagnosticSeverity.Error,
                        "A ref local must be initialized.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));

                    localType = explicitType ?? new ErrorTypeSymbol("ref", containing: null, ImmutableArray<Location>.Empty);
                    init = null;
                }
                else
                {
                    var rhs = BindExpression(v.Initializer.Value, context, diagnostics);

                    if (rhs is not BoundRefExpression br || br.Type is not ByRefTypeSymbol brType)
                    {
                        diagnostics.Add(new Diagnostic("CN_REFLOC003", DiagnosticSeverity.Error,
                            "A ref local initializer must be a ref expression.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = explicitType ?? new ErrorTypeSymbol("ref", containing: null, ImmutableArray<Location>.Empty);
                        init = rhs;
                    }
                    else if (isVar)
                    {
                        localType = brType.ElementType;
                        init = rhs;
                    }
                    else
                    {
                        localType = explicitType ?? new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);

                        if (!ReferenceEquals(brType.ElementType, localType))
                        {
                            diagnostics.Add(new Diagnostic("CN_REFLOC004", DiagnosticSeverity.Error,
                                $"Cannot initialize ref local of type '{localType.Name}' with ref value of type '{brType.ElementType.Name}'.",
                                new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));
                        }

                        init = rhs;
                    }
                }
                var refLocal = new LocalSymbol(
                    name: name,
                    containing: _containing,
                    type: localType,
                    locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, v.Span)),
                    isConst: false,
                    isReadOnly: isUsing,
                    constantValueOpt: Optional<object>.None,
                    isByRef: true);

                _locals[name] = refLocal;
                context.Recorder.RecordDeclared(v, refLocal);

                var result = new BoundLocalDeclarationStatement(ownerSyntax, refLocal, init, isUsing);
                if (isUsing)
                    ValidateUsingResource(refLocal.Type, v.Initializer?.Value, v, context, diagnostics);
                return result;
            }
            if (isVar)
            {
                if (v.Initializer is null)
                {
                    diagnostics.Add(new Diagnostic("CN_VAR001", DiagnosticSeverity.Error,
                        "Implicitly-typed local variable must be initialized.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));

                    localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                }
                else
                {
                    var rhs = BindExpression(v.Initializer.Value, context, diagnostics);

                    if (rhs is BoundUnboundLambdaExpression)
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR_LAMBDA000", DiagnosticSeverity.Error,
                            "Cannot infer the type of an implicitly-typed local variable from a lambda expression.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        var badInit = new BoundBadExpression(v.Initializer.Value);
                        badInit.SetType(localType);
                        init = badInit;
                    }
                    else if (rhs is BoundMethodGroupExpression)
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR_METHODGROUP000", DiagnosticSeverity.Error,
                            "Cannot infer the type of an implicitly-typed local variable from a method group.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        var badInit = new BoundBadExpression(v.Initializer.Value);
                        badInit.SetType(localType);
                        init = badInit;
                    }
                    else if (rhs.Type is NullTypeSymbol ||
                        (rhs.ConstantValueOpt.HasValue && rhs.ConstantValueOpt.Value is null))
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR002", DiagnosticSeverity.Error,
                            "Cannot assign <null> to an implicitly-typed local variable.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        init = rhs;
                    }
                    else if (rhs.Type.SpecialType == SpecialType.System_Void)
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR003", DiagnosticSeverity.Error,
                            "Cannot assign void to an implicitly-typed local variable.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        init = rhs;
                    }
                    else if (rhs is BoundUnboundCollectionExpression)
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR004", DiagnosticSeverity.Error,
                            "Cannot infer the type of an implicitly-typed local variable from a collection expression.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        var badInit = new BoundBadExpression(v.Initializer.Value);
                        badInit.SetType(localType);
                        init = badInit;
                    }
                    else if (rhs.Type is TupleTypeSymbol tupleType && TupleHasUninferableElements(tupleType))
                    {
                        diagnostics.Add(new Diagnostic("CN_VAR_TUPLE000", DiagnosticSeverity.Error,
                            "Cannot infer the type of an implicitly-typed local variable from a tuple " +
                            "containing elements with no type. Add explicit casts.",
                            new Location(context.SemanticModel.SyntaxTree, v.Initializer.Value.Span)));

                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                        init = rhs;
                    }
                    else
                    {

                        if (rhs is BoundStackAllocArrayCreationExpression sa && (Flags & BinderFlags.UnsafeRegion) == 0 &&
                            TryGetSystemSpanDefinition(context.Compilation, out var spanDef))
                        {
                            localType = context.Compilation.ConstructNamedType(
                                spanDef,
                                ImmutableArray.Create<TypeSymbol>(sa.ElementType));
                            init = ApplyConversion(
                                exprSyntax: v.Initializer.Value,
                                expr: rhs,
                                targetType: localType,
                                diagnosticNode: v.Initializer,
                                context: context,
                                diagnostics: diagnostics,
                                requireImplicit: true);
                        }
                        else
                        {
                            if (rhs is BoundStackAllocArrayCreationExpression)
                                EnsureUnsafe(v.Initializer.Value, context, diagnostics);
                            localType = rhs.Type;
                            init = rhs;
                        }
                    }
                }
            }
            else
            {
                localType = explicitType ?? new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);

                if (v.Initializer != null)
                {
                    init = BindExpressionWithTargetType(
                        exprSyntax: v.Initializer.Value,
                        targetType: localType,
                        diagnosticNode: v.Initializer,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);
                }
            }

            Optional<object> constValueOpt = Optional<object>.None;
            if (isConst)
            {
                if (init is null || !init.ConstantValueOpt.HasValue)
                {
                    diagnostics.Add(new Diagnostic("CN_CONST002", DiagnosticSeverity.Error,
                        "The expression assigned to a const local must be a constant expression.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));
                }
                else
                {
                    constValueOpt = init.ConstantValueOpt;
                }
            }

            var local = new LocalSymbol(
                name: name,
                containing: _containing,
                type: localType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, v.Span)),
                isConst: isConst,
                isReadOnly: isUsing,
                constantValueOpt: constValueOpt);

            _locals[name] = local;
            context.Recorder.RecordDeclared(v, local);

            {
                var result = new BoundLocalDeclarationStatement(ownerSyntax, local, init, isUsing);
                if (isUsing)
                    ValidateUsingResource(local.Type, v.Initializer?.Value, v, context, diagnostics);
                return result;
            }
        }
        private BoundExpression BindElementAccess(
            ElementAccessExpressionSyntax node,
            BindValueKind bindValueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var expr = BindExpression(node.Expression, context, diagnostics);
            if (expr.HasErrors)
                return new BoundBadExpression(node);
            if (node.ArgumentList.Arguments.Count == 1 &&
                node.ArgumentList.Arguments[0].Expression is RangeExpressionSyntax range)
            {
                if (bindValueKind == BindValueKind.LValue)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE000",
                        DiagnosticSeverity.Error,
                        "Cannot assign to a slice.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                if (InlineArrayFacts.TryGetInfo(expr.Type, out var inlineArrayRange))
                    return BindInlineArrayElementAccess(node, expr, node.ArgumentList, bindValueKind, context, diagnostics, inlineArrayRange);

                return BindSliceElementAccess(node, expr, range, context, diagnostics);
            }
            if (expr.Type is ArrayTypeSymbol at)
            {
                if (node.ArgumentList.Arguments.Count != at.Rank)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM002",
                        DiagnosticSeverity.Error,
                        at.Rank == 1
                            ? "Array element access expects exactly one index argument."
                            : $"Array element access expects exactly {at.Rank} index arguments.",
                        new Location(context.SemanticModel.SyntaxTree, node.ArgumentList.Span)));
                    return new BoundBadExpression(node);
                }

                var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);

                // a[^x] => a[a.Length - x]
                bool hasFromEndIndex = false;
                for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                {
                    if (node.ArgumentList.Arguments[i].Expression is PrefixUnaryExpressionSyntax pre &&
                        pre.Kind == SyntaxKind.IndexExpression)
                    {
                        hasFromEndIndex = true;
                        break;
                    }
                }

                if (!hasFromEndIndex)
                {
                    var indices = ImmutableArray.CreateBuilder<BoundExpression>(node.ArgumentList.Arguments.Count);

                    for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                    {
                        var arg = node.ArgumentList.Arguments[i];
                        var indexExpr = BindExpression(arg.Expression, context, diagnostics);
                        indexExpr = ApplyConversion(
                            exprSyntax: arg.Expression,
                            expr: indexExpr,
                            targetType: intType,
                            diagnosticNode: node,
                            context: context,
                            diagnostics: diagnostics,
                            requireImplicit: true);
                        indices.Add(indexExpr);
                    }

                    return new BoundArrayElementAccessExpression(node, at.ElementType, expr, indices.ToImmutable());
                }

                if (at.Rank != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM004",
                        DiagnosticSeverity.Error,
                        "Index-from-end (^) is only supported for single-dimensional arrays.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var recvTmp = NewTemp("$idx_recv", expr.Type);
                var recvDecl = new BoundLocalDeclarationStatement(node, recvTmp, expr);
                var recvExpr = new BoundLocalExpression(node, recvTmp);

                var receiverTypeForLookup = GetReceiverTypeForMemberLookup(expr.Type);
                if (receiverTypeForLookup is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM005",
                        DiagnosticSeverity.Error,
                        "Index-from-end (^) is not supported for this receiver type.",
                        new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                    return new BoundBadExpression(node);
                }

                Symbol? lengthMember = null;
                {
                    var members = LookupMembers(receiverTypeForLookup, "Length");
                    for (int i = 0; i < members.Length; i++)
                    {
                        var m = members[i];
                        if (m is PropertySymbol p && !p.IsStatic && ReferenceEquals(p.Type, intType))
                        {
                            lengthMember = p;
                            break;
                        }
                        if (m is FieldSymbol f && !f.IsStatic && ReferenceEquals(f.Type, intType))
                        {
                            lengthMember = f;
                            break;
                        }
                    }
                }
                if (lengthMember is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM006",
                        DiagnosticSeverity.Error,
                        "Receiver type does not have an accessible 'Length' member required for index-from-end (^).",
                        new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                    return new BoundBadExpression(node);
                }

                var lenTmp = NewTemp("$idx_len", intType);
                var lenAccess = new BoundMemberAccessExpression(
                    (ExpressionSyntax)node.Expression,
                    receiverOpt: recvExpr,
                    member: lengthMember,
                    type: intType,
                    isLValue: false);
                var lenDecl = new BoundLocalDeclarationStatement(node, lenTmp, lenAccess);
                var lenExpr = new BoundLocalExpression(node, lenTmp);

                var locals = ImmutableArray.CreateBuilder<LocalSymbol>(2 + node.ArgumentList.Arguments.Count);
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(2 + node.ArgumentList.Arguments.Count);
                var indices2 = ImmutableArray.CreateBuilder<BoundExpression>(node.ArgumentList.Arguments.Count);

                locals.Add(recvTmp);
                sideEffects.Add(recvDecl);

                // Capture each argument expression
                for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                {
                    var argSyntax = node.ArgumentList.Arguments[i].Expression;

                    bool fromEnd = argSyntax is PrefixUnaryExpressionSyntax pre && pre.Kind == SyntaxKind.IndexExpression;
                    var valueSyntax = fromEnd ? ((PrefixUnaryExpressionSyntax)argSyntax).Operand : argSyntax;

                    var boundValue = BindExpression(valueSyntax, context, diagnostics);
                    boundValue = ApplyConversion(
                        exprSyntax: valueSyntax,
                        expr: boundValue,
                        targetType: intType,
                        diagnosticNode: node,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    if (boundValue.HasErrors)
                        return new BoundBadExpression(node);

                    var valTmp = NewTemp($"$idx_arg{i}", intType);
                    locals.Add(valTmp);
                    sideEffects.Add(new BoundLocalDeclarationStatement(node, valTmp, boundValue));
                    var valExpr = new BoundLocalExpression(node, valTmp);

                    BoundExpression finalIndex = fromEnd
                        ? (BoundExpression)new BoundBinaryExpression(
                            node,
                            BoundBinaryOperatorKind.Subtract,
                            intType,
                            lenExpr,
                            valExpr,
                            Optional<object>.None,
                            isChecked: false)
                        : (BoundExpression)valExpr;

                    indices2.Add(finalIndex);
                }

                locals.Add(lenTmp);
                sideEffects.Add(lenDecl);

                var access = new BoundArrayElementAccessExpression(node, at.ElementType, recvExpr, indices2.ToImmutable());
                return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), access);
            }
            if (InlineArrayFacts.TryGetInfo(expr.Type, out var inlineArray))
                return BindInlineArrayElementAccess(node, expr, node.ArgumentList, bindValueKind, context, diagnostics, inlineArray);

            if (expr.Type is PointerTypeSymbol pt)
            {
                EnsureUnsafe(node, context, diagnostics);

                if (node.ArgumentList.Arguments.Count != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_PTRIDX000",
                        DiagnosticSeverity.Error,
                        "Pointer element access expects exactly one index argument.",
                        new Location(context.SemanticModel.SyntaxTree, node.ArgumentList.Span)));
                    return new BoundBadExpression(node);
                }

                var indexExpr = BindExpression(node.ArgumentList.Arguments[0].Expression, context, diagnostics);
                var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                indexExpr = ApplyConversion(
                    exprSyntax: node.ArgumentList.Arguments[0].Expression,
                    expr: indexExpr,
                    targetType: intType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                return new BoundPointerElementAccessExpression(node, pt.PointedAtType, expr, indexExpr);
            }

            var receiverType = GetReceiverTypeForMemberLookup(expr.Type);
            if (receiverType is not null)
            {
                var allIndexers = LookupIndexers(receiverType);
                if (!allIndexers.IsDefaultOrEmpty)
                {
                    var accessibleIndexers = FilterAccessibleIndexers(allIndexers, context);
                    if (accessibleIndexers.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ACC003",
                            DiagnosticSeverity.Error,
                            "Indexer is inaccessible due to its protection level.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }

                    var instanceIndexers = ImmutableArray.CreateBuilder<PropertySymbol>(accessibleIndexers.Length);
                    for (int i = 0; i < accessibleIndexers.Length; i++)
                    {
                        if (!accessibleIndexers[i].IsStatic)
                            instanceIndexers.Add(accessibleIndexers[i]);
                    }

                    if (instanceIndexers.Count == 0)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ELEM003",
                            DiagnosticSeverity.Error,
                            "A static indexer cannot be accessed with an instance reference.",
                            new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                        return new BoundBadExpression(node);
                    }

                    var rawArgs = ImmutableArray.CreateBuilder<BoundExpression>(node.ArgumentList.Arguments.Count);
                    bool hasArgErrors = false;
                    bool hasFromEndIndex = false;
                    for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                    {
                        if (node.ArgumentList.Arguments[i].Expression is PrefixUnaryExpressionSyntax pre &&
                            pre.Kind == SyntaxKind.IndexExpression)
                        {
                            hasFromEndIndex = true;
                            break;
                        }
                    }
                    BoundExpression indexerReceiver = expr;
                    ImmutableArray<LocalSymbol> seqLocals = default;
                    ImmutableArray<BoundStatement> seqSideEffects = default;

                    if (hasFromEndIndex)
                    {
                        var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);

                        Symbol? lengthMember = null;
                        {
                            var members = LookupMembers(receiverType, "Length");
                            for (int i = 0; i < members.Length; i++)
                            {
                                var m = members[i];
                                if (m is PropertySymbol p && !p.IsStatic && ReferenceEquals(p.Type, intType))
                                {
                                    lengthMember = p;
                                    break;
                                }
                                if (m is FieldSymbol f && !f.IsStatic && ReferenceEquals(f.Type, intType))
                                {
                                    lengthMember = f;
                                    break;
                                }
                            }
                        }

                        if (lengthMember is null)
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_ELEM006",
                                DiagnosticSeverity.Error,
                                "Receiver type does not have an accessible 'Length' member required for index-from-end (^).",
                                new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                            return new BoundBadExpression(node);
                        }

                        // Capture receiver
                        var recvTmp = NewTemp("$idx_recv", expr.Type);
                        var recvDecl = new BoundLocalDeclarationStatement(node, recvTmp, expr);
                        var recvExpr = new BoundLocalExpression(node, recvTmp);

                        // Capture length
                        var lenTmp = NewTemp("$idx_len", intType);
                        var lenAccess = new BoundMemberAccessExpression(
                            (ExpressionSyntax)node.Expression,
                            receiverOpt: recvExpr,
                            member: lengthMember,
                            type: intType,
                            isLValue: false);
                        var lenDecl = new BoundLocalDeclarationStatement(node, lenTmp, lenAccess);
                        var lenExpr = new BoundLocalExpression(node, lenTmp);

                        var locals = ImmutableArray.CreateBuilder<LocalSymbol>(2);
                        var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(2);

                        locals.Add(recvTmp);
                        sideEffects.Add(recvDecl);

                        locals.Add(lenTmp);
                        sideEffects.Add(lenDecl);

                        indexerReceiver = recvExpr;

                        for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                        {
                            var argSyntax = node.ArgumentList.Arguments[i].Expression;

                            if (argSyntax is PrefixUnaryExpressionSyntax pre &&
                                pre.Kind == SyntaxKind.IndexExpression)
                            {
                                var valueSyntax = pre.Operand;
                                var valueExpr = BindExpression(valueSyntax, context, diagnostics);
                                valueExpr = ApplyConversion(
                                    exprSyntax: valueSyntax,
                                    expr: valueExpr,
                                    targetType: intType,
                                    diagnosticNode: node,
                                    context: context,
                                    diagnostics: diagnostics,
                                    requireImplicit: true);

                                if (valueExpr.HasErrors)
                                {
                                    hasArgErrors = true;
                                }
                                else
                                {
                                    rawArgs.Add(new BoundBinaryExpression(
                                        node,
                                        BoundBinaryOperatorKind.Subtract,
                                        intType,
                                        lenExpr,
                                        valueExpr,
                                        Optional<object>.None,
                                        isChecked: false));
                                }
                            }
                            else
                            {
                                var arg = BindExpression(argSyntax, context, diagnostics);
                                rawArgs.Add(arg);
                                if (arg.HasErrors)
                                    hasArgErrors = true;
                            }
                        }

                        seqLocals = locals.ToImmutable();
                        seqSideEffects = sideEffects.ToImmutable();
                    }
                    else
                    {
                        for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
                        {
                            var arg = BindExpression(node.ArgumentList.Arguments[i].Expression, context, diagnostics);
                            rawArgs.Add(arg);
                            if (arg.HasErrors)
                                hasArgErrors = true;
                        }
                    }

                    if (hasArgErrors)
                        return new BoundBadExpression(node);

                    if (!TryResolveIndexerOverload(
                        candidates: instanceIndexers.ToImmutable(),
                        syntax: node,
                        rawArgs: rawArgs.ToImmutable(),
                        context: context,
                        diagnostics: diagnostics,
                        chosen: out var chosenIndexer,
                        convertedArgs: out var convertedArgs))
                    {
                        return new BoundBadExpression(node);
                    }

                    bool canReadIndexer =
                        chosenIndexer!.GetMethod is not null &&
                        AccessibilityHelper.IsAccessible(chosenIndexer.GetMethod, context);

                    bool canWriteIndexer =
                        chosenIndexer.SetMethod is not null &&
                        AccessibilityHelper.IsAccessible(chosenIndexer.SetMethod, context);

                    if (bindValueKind == BindValueKind.RValue && !canReadIndexer)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC024",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible getter.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }
                    bool isRefReturnIndexer =
                        chosenIndexer.GetMethod is MethodSymbol getMethodSymbol &&
                        getMethodSymbol.ReturnType is ByRefTypeSymbol;

                    if (bindValueKind == BindValueKind.LValue && isRefReturnIndexer && !canReadIndexer)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC024",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible getter.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }
                    if (bindValueKind == BindValueKind.LValue &&
                        !chosenIndexer.IsStatic &&
                        IsReadOnlyValueReceiver(expr, context))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_READONLY_THIS002",
                            DiagnosticSeverity.Error,
                            "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }

                    bool allowCtorAutoPropWrite =
                        bindValueKind == BindValueKind.LValue &&
                        !canWriteIndexer &&
                        CanAssignReadOnlyAutoPropertyInConstructor(chosenIndexer, expr, context);

                    if (bindValueKind == BindValueKind.LValue &&
                        !canWriteIndexer &&
                        !allowCtorAutoPropWrite &&
                        !(isRefReturnIndexer && canReadIndexer))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC025",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible setter.",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        return new BoundBadExpression(node);
                    }

                    bool hasErrors = expr.HasErrors;
                    for (int i = 0; i < convertedArgs.Length; i++)
                    {
                        if (convertedArgs[i].HasErrors)
                        {
                            hasErrors = true;
                            break;
                        }
                    }

                    var result = new BoundIndexerAccessExpression(
                        node,
                        indexerReceiver,
                        chosenIndexer,
                        convertedArgs,
                        isLValue: canWriteIndexer || allowCtorAutoPropWrite || (isRefReturnIndexer && canReadIndexer),
                        hasErrors: hasErrors);

                    if (hasFromEndIndex)
                        return new BoundSequenceExpression(node, seqLocals, seqSideEffects, result);

                    return result;
                }
            }

            diagnostics.Add(new Diagnostic(
                "CN_ELEM000",
                DiagnosticSeverity.Error,
                "Element access is not implemented for this type.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));

            return new BoundBadExpression(node);
        }
        private BoundExpression BindSliceElementAccess(
            ElementAccessExpressionSyntax node,
            BoundExpression receiver,
            RangeExpressionSyntax range,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            static bool IsFromEndIndex(ExpressionSyntax expr, out ExpressionSyntax valueExpression)
            {
                if (expr is PrefixUnaryExpressionSyntax pre && pre.Kind == SyntaxKind.IndexExpression)
                {
                    valueExpression = pre.Operand;
                    return true;
                }

                valueExpression = expr;
                return false;
            }

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);

            // Capture receiver
            var recvTmp = NewTemp("$slice_recv", receiver.Type);
            var recvDecl = new BoundLocalDeclarationStatement(node, recvTmp, receiver);
            var recvExpr = new BoundLocalExpression(node, recvTmp);

            BoundExpression? leftValueExpr = null;
            bool leftFromEnd = false;
            LocalSymbol? leftTmp = null;
            BoundStatement? leftDecl = null;

            if (range.LeftOperand is ExpressionSyntax leftSyntax)
            {
                leftFromEnd = IsFromEndIndex(leftSyntax, out var leftValueSyntax);
                var boundLeft = BindExpression(leftValueSyntax, context, diagnostics);
                boundLeft = ApplyConversion(
                    exprSyntax: leftValueSyntax,
                    expr: boundLeft,
                    targetType: int32,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (boundLeft.HasErrors)
                    return new BoundBadExpression(node);

                leftTmp = NewTemp("$slice_left", int32);
                leftDecl = new BoundLocalDeclarationStatement(node, leftTmp, boundLeft);
                leftValueExpr = new BoundLocalExpression(node, leftTmp);
            }

            BoundExpression? rightValueExpr = null;
            bool rightFromEnd = false;
            LocalSymbol? rightTmp = null;
            BoundStatement? rightDecl = null;

            if (range.RightOperand is ExpressionSyntax rightSyntax)
            {
                rightFromEnd = IsFromEndIndex(rightSyntax, out var rightValueSyntax);
                var boundRight = BindExpression(rightValueSyntax, context, diagnostics);
                boundRight = ApplyConversion(
                    exprSyntax: rightValueSyntax,
                    expr: boundRight,
                    targetType: int32,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (boundRight.HasErrors)
                    return new BoundBadExpression(node);

                rightTmp = NewTemp("$slice_right", int32);
                rightDecl = new BoundLocalDeclarationStatement(node, rightTmp, boundRight);
                rightValueExpr = new BoundLocalExpression(node, rightTmp);
            }

            // Determine slicing strategy
            var receiverTypeForLookup = GetReceiverTypeForMemberLookup(receiver.Type);
            if (receiverTypeForLookup is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SLICE001",
                    DiagnosticSeverity.Error,
                    "Slicing is not supported for this receiver type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            // Find instance Length
            Symbol? lengthMember = null;
            {
                var members = LookupMembers(receiverTypeForLookup, "Length");
                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i];
                    if (m is PropertySymbol p && !p.IsStatic && ReferenceEquals(p.Type, int32))
                    {
                        lengthMember = p;
                        break;
                    }
                    if (m is FieldSymbol f && !f.IsStatic && ReferenceEquals(f.Type, int32))
                    {
                        lengthMember = f;
                        break;
                    }
                }
            }
            if (lengthMember is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SLICE002",
                    DiagnosticSeverity.Error,
                    "Receiver type does not have an accessible 'Length' member required for slicing.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                return new BoundBadExpression(node);
            }

            // lenTmp = recv.Length
            var lenTmp = NewTemp("$slice_len", int32);
            var lenAccess = new BoundMemberAccessExpression(
                (ExpressionSyntax)node.Expression,
                receiverOpt: recvExpr,
                member: lengthMember,
                type: int32,
                isLValue: false);
            var lenDecl = new BoundLocalDeclarationStatement(node, lenTmp, lenAccess);
            var lenExpr = new BoundLocalExpression(node, lenTmp);

            // startTmp
            var startTmp = NewTemp("$slice_start", int32);
            BoundExpression startInit;
            if (leftValueExpr is null)
            {
                startInit = new BoundLiteralExpression(node, int32, 0);
            }
            else if (leftFromEnd)
            {
                startInit = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.Subtract,
                    int32,
                    lenExpr,
                    leftValueExpr,
                    Optional<object>.None,
                    isChecked: false);
            }
            else
            {
                startInit = leftValueExpr;
            }
            var startDecl = new BoundLocalDeclarationStatement(node, startTmp, startInit);
            var startExpr = new BoundLocalExpression(node, startTmp);

            // sliceLenTmp
            var sliceLenTmp = NewTemp("$slice_count", int32);
            BoundExpression sliceLenInit;
            if (rightValueExpr is null)
            {
                // len - start
                sliceLenInit = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.Subtract,
                    int32,
                    lenExpr,
                    startExpr,
                    Optional<object>.None,
                    isChecked: false);
            }
            else
            {
                BoundExpression endExpr = rightFromEnd
                    ? new BoundBinaryExpression(
                        node,
                        BoundBinaryOperatorKind.Subtract,
                        int32,
                        lenExpr,
                        rightValueExpr,
                        Optional<object>.None,
                        isChecked: false)
                    : rightValueExpr;

                sliceLenInit = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.Subtract,
                    int32,
                    endExpr,
                    startExpr,
                    Optional<object>.None,
                    isChecked: false);
            }
            var sliceLenDecl = new BoundLocalDeclarationStatement(node, sliceLenTmp, sliceLenInit);
            var sliceLenExpr = new BoundLocalExpression(node, sliceLenTmp);

            var locals = ImmutableArray.CreateBuilder<LocalSymbol>(8);
            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(8);

            locals.Add(recvTmp);
            sideEffects.Add(recvDecl);

            if (leftTmp is not null)
            {
                locals.Add(leftTmp);
                sideEffects.Add(leftDecl!);
            }
            if (rightTmp is not null)
            {
                locals.Add(rightTmp);
                sideEffects.Add(rightDecl!);
            }

            locals.Add(lenTmp);
            sideEffects.Add(lenDecl);
            locals.Add(startTmp);
            sideEffects.Add(startDecl);
            locals.Add(sliceLenTmp);
            sideEffects.Add(sliceLenDecl);

            // string slicing => string.Substring
            if (receiver.Type.SpecialType == SpecialType.System_String)
            {
                var stringType = (NamedTypeSymbol)context.Compilation.GetSpecialType(SpecialType.System_String);

                MethodSymbol? substring = null;
                bool useTwoArgs = rightValueExpr is not null;
                int desiredParamCount = useTwoArgs ? 2 : 1;

                var members = stringType.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol m) continue;
                    if (m.IsStatic || m.IsConstructor) continue;
                    if (!string.Equals(m.Name, "Substring", StringComparison.Ordinal)) continue;
                    if (m.Parameters.Length != desiredParamCount) continue;
                    if (!ReferenceEquals(m.ReturnType, stringType)) continue;
                    if (!ReferenceEquals(m.Parameters[0].Type, int32)) continue;
                    if (desiredParamCount == 2 && !ReferenceEquals(m.Parameters[1].Type, int32)) continue;
                    substring = m;
                    break;
                }

                if (substring is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE010",
                        DiagnosticSeverity.Error,
                        "Missing required 'string.Substring' overload for slicing.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var args = useTwoArgs
                    ? ImmutableArray.Create<BoundExpression>(startExpr, sliceLenExpr)
                    : ImmutableArray.Create<BoundExpression>(startExpr);

                var call = new BoundCallExpression(node, recvExpr, substring, args);
                return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), call);
            }

            // span slicing => Slice(start) / Slice(start, length)
            if (TryGetSpanLikeElementType(receiver.Type, out var spanLikeType, out _))
            {
                bool useTwoArgs = rightValueExpr is not null;
                int desiredParamCount = useTwoArgs ? 2 : 1;

                MethodSymbol? slice = null;
                var members = spanLikeType.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol m) continue;
                    if (m.IsStatic || m.IsConstructor) continue;
                    if (!string.Equals(m.Name, "Slice", StringComparison.Ordinal)) continue;
                    if (m.Parameters.Length != desiredParamCount) continue;
                    if (!ReferenceEquals(m.Parameters[0].Type, int32)) continue;
                    if (desiredParamCount == 2 && !ReferenceEquals(m.Parameters[1].Type, int32)) continue;
                    if (!ReferenceEquals(m.ReturnType, spanLikeType)) continue;
                    slice = m;
                    break;
                }

                if (slice is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE011",
                        DiagnosticSeverity.Error,
                        "Missing required 'Slice' method overload for span slicing.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var args = useTwoArgs
                    ? ImmutableArray.Create<BoundExpression>(startExpr, sliceLenExpr)
                    : ImmutableArray.Create<BoundExpression>(startExpr);

                var call = new BoundCallExpression(node, recvExpr, slice, args);
                return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), call);
            }

            // array slicing => allocate + Array.Copy
            if (receiver.Type is ArrayTypeSymbol at)
            {
                if (at.Rank != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE020",
                        DiagnosticSeverity.Error,
                        "Array slicing is only supported for single-dimensional arrays.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var resultArrTmp = NewTemp("$slice_arr", receiver.Type);
                locals.Add(resultArrTmp);

                var created = new BoundArrayCreationExpression(
                    syntax: node,
                    type: (ArrayTypeSymbol)receiver.Type,
                    elementType: at.ElementType,
                    count: sliceLenExpr,
                    initializerOpt: null);

                var resultArrDecl = new BoundLocalDeclarationStatement(node, resultArrTmp, created);
                sideEffects.Add(resultArrDecl);

                var resultArrExpr = new BoundLocalExpression(node, resultArrTmp);

                // Find Array.Copy(Array, int, Array, int, int)
                var arrayBase = context.Compilation.GetSpecialType(SpecialType.System_Array);
                if (arrayBase is not NamedTypeSymbol arrayType)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE021",
                        DiagnosticSeverity.Error,
                        "Missing special type System.Array required for array slicing.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                MethodSymbol? copy = null;
                var arrayMembers = arrayType.GetMembers();
                for (int i = 0; i < arrayMembers.Length; i++)
                {
                    if (arrayMembers[i] is not MethodSymbol m) continue;
                    if (!m.IsStatic || m.IsConstructor) continue;
                    if (!string.Equals(m.Name, "Copy", StringComparison.Ordinal)) continue;
                    if (m.Parameters.Length != 5) continue;
                    if (!ReferenceEquals(m.Parameters[0].Type, arrayType)) continue;
                    if (!ReferenceEquals(m.Parameters[1].Type, int32)) continue;
                    if (!ReferenceEquals(m.Parameters[2].Type, arrayType)) continue;
                    if (!ReferenceEquals(m.Parameters[3].Type, int32)) continue;
                    if (!ReferenceEquals(m.Parameters[4].Type, int32)) continue;
                    copy = m;
                    break;
                }

                if (copy is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE022",
                        DiagnosticSeverity.Error,
                        "Missing required 'Array.Copy(Array, int, Array, int, int)' overload for array slicing.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
                }

                var zero = new BoundLiteralExpression(node, int32, 0);

                var srcAsArray = ApplyConversion(
                    exprSyntax: node.Expression,
                    expr: recvExpr,
                    targetType: arrayType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                var dstAsArray = ApplyConversion(
                    exprSyntax: node.Expression,
                    expr: resultArrExpr,
                    targetType: arrayType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (srcAsArray.HasErrors || dstAsArray.HasErrors)
                    return new BoundBadExpression(node);

                var copyArgs = ImmutableArray.Create<BoundExpression>(
                    srcAsArray,
                    startExpr,
                    dstAsArray,
                    zero,
                    sliceLenExpr);

                var copyCall = new BoundCallExpression(node, receiverOpt: null, copy, copyArgs);
                sideEffects.Add(new BoundExpressionStatement(node, copyCall));

                return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), resultArrExpr);
            }

            diagnostics.Add(new Diagnostic(
                "CN_SLICE003",
                DiagnosticSeverity.Error,
                "Slicing is not supported for this receiver type.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));

            return new BoundBadExpression(node);
        }
        private static ImmutableArray<PropertySymbol> FilterAccessibleIndexers(
            ImmutableArray<PropertySymbol> candidates, BindingContext context)
        {
            if (candidates.IsDefaultOrEmpty)
                return candidates;

            var builder = ImmutableArray.CreateBuilder<PropertySymbol>(candidates.Length);
            for (int i = 0; i < candidates.Length; i++)
            {
                var p = candidates[i];
                if (AccessibilityHelper.IsAccessible(p, context))
                    builder.Add(p);
            }

            return builder.Count == candidates.Length ? candidates : builder.ToImmutable();
        }
        private bool TryResolveIndexerOverload(
            ImmutableArray<PropertySymbol> candidates,
            ElementAccessExpressionSyntax syntax,
            ImmutableArray<BoundExpression> rawArgs,
            BindingContext context,
            DiagnosticBag diagnostics,
            out PropertySymbol? chosen,
            out ImmutableArray<BoundExpression> convertedArgs)
        {
            chosen = null;
            convertedArgs = default;

            PropertySymbol? best = null;
            int bestScore = int.MaxValue;
            bool ambiguous = false;

            for (int i = 0; i < candidates.Length; i++)
            {
                var p = candidates[i];
                var parameters = p.Parameters;

                if (parameters.Length != rawArgs.Length)
                    continue;

                int score = 0;
                bool ok = true;

                for (int a = 0; a < rawArgs.Length; a++)
                {
                    var conv = ClassifyConversion(rawArgs[a], parameters[a].Type, context);
                    if (!conv.Exists || !conv.IsImplicit)
                    {
                        ok = false;
                        break;
                    }

                    score += conv.Kind switch
                    {
                        ConversionKind.Identity => 0,
                        ConversionKind.ImplicitNumeric => 1,
                        ConversionKind.ImplicitConstant => 1,
                        ConversionKind.ImplicitReference => 1,
                        ConversionKind.ImplicitTuple => 1,
                        ConversionKind.NullLiteral => 1,
                        ConversionKind.Boxing => 2,
                        ConversionKind.UserDefined => 3,
                        _ => 10
                    };
                }

                if (!ok)
                    continue;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                    ambiguous = false;
                }
                else if (score == bestScore)
                {
                    ambiguous = true;
                }
            }

            if (best is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ELEM001",
                    DiagnosticSeverity.Error,
                    "No indexer overload matches the argument list.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.ArgumentList.Span)));
                return false;
            }

            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ELEM004",
                    DiagnosticSeverity.Error,
                    "Indexer overload resolution is ambiguous.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.ArgumentList.Span)));
                return false;
            }

            var converted = ImmutableArray.CreateBuilder<BoundExpression>(rawArgs.Length);
            var bestParams = best.Parameters;
            for (int i = 0; i < rawArgs.Length; i++)
            {
                converted.Add(ApplyConversion(
                    exprSyntax: syntax.ArgumentList.Arguments[i].Expression,
                    expr: rawArgs[i],
                    targetType: bestParams[i].Type,
                    diagnosticNode: syntax,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true));
            }

            chosen = best;
            convertedArgs = converted.ToImmutable();
            return true;
        }
        private BoundExpression BindArrayCreation(
            ArrayCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var arrayTypeBound = BindType(node.Type, context, diagnostics);
            if (arrayTypeBound is not ArrayTypeSymbol arrayType)
                return new BoundBadExpression(node);

            var elemType = arrayType.ElementType;
            if (elemType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRNEW001",
                    DiagnosticSeverity.Error,
                    "Array element type cannot be void.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.ElementType.Span)));
                return new BoundBadExpression(node);
            }

            if (node.Type.RankSpecifiers.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRNEW000",
                    DiagnosticSeverity.Error,
                    "Array creation requires at least one rank specifier.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                return new BoundBadExpression(node);
            }

            var outerRank = node.Type.RankSpecifiers[0];

            for (int i = 1; i < node.Type.RankSpecifiers.Count; i++)
            {
                var rs = node.Type.RankSpecifiers[i];
                for (int j = 0; j < rs.Sizes.Count; j++)
                {
                    if (rs.Sizes[j] is OmittedArraySizeExpressionSyntax)
                        continue;

                    diagnostics.Add(new Diagnostic(
                        "CN_ARRNEW005",
                        DiagnosticSeverity.Error,
                        "Only the outermost array dimension may specify sizes in a jagged array creation.",
                        new Location(context.SemanticModel.SyntaxTree, rs.Span)));
                    return new BoundBadExpression(node);
                }
            }

            var initializerOpt = BindManagedArrayInitializer(node.Initializer, elemType, arrayType.Rank, context, diagnostics);
            var inferredInitShape = initializerOpt is null
                ? ImmutableArray<int>.Empty
                : InferRectangularInitializerShape(initializerOpt, arrayType.Rank, context, diagnostics);

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var dimensionSizes = ImmutableArray.CreateBuilder<BoundExpression>(outerRank.Sizes.Count);

            for (int i = 0; i < outerRank.Sizes.Count; i++)
            {
                var sizeExprSyntax = outerRank.Sizes[i];

                if (sizeExprSyntax is not OmittedArraySizeExpressionSyntax)
                {
                    var sizeExpr = BindExpression(sizeExprSyntax, context, diagnostics);
                    sizeExpr = ApplyConversion(
                        exprSyntax: sizeExprSyntax,
                        expr: sizeExpr,
                        targetType: int32,
                        diagnosticNode: node,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);
                    dimensionSizes.Add(sizeExpr);
                    continue;
                }

                if (initializerOpt is not null && i < inferredInitShape.Length)
                {
                    dimensionSizes.Add(new BoundLiteralExpression(sizeExprSyntax, int32, inferredInitShape[i]));
                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    "CN_ARRNEW003",
                    DiagnosticSeverity.Error,
                    arrayType.Rank == 1
                        ? "Array creation requires a size expression or an initializer."
                        : "Cannot infer all array dimensions from the initializer.",
                    new Location(context.SemanticModel.SyntaxTree, outerRank.Span)));
                return new BoundBadExpression(node);
            }

            if (initializerOpt != null)
            {
                for (int i = 0; i < dimensionSizes.Count && i < inferredInitShape.Length; i++)
                {
                    var dimExpr = dimensionSizes[i];
                    if (dimExpr.ConstantValueOpt.HasValue &&
                        dimExpr.ConstantValueOpt.Value is int n &&
                        n != inferredInitShape[i])
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ARRNEW004",
                            DiagnosticSeverity.Error,
                            $"Initializer dimension {i + 1} has length '{inferredInitShape[i]}', but '{n}' was specified.",
                            new Location(context.SemanticModel.SyntaxTree, node.Initializer!.Span)));
                    }
                }
            }

            return new BoundArrayCreationExpression(node, arrayType, elemType, dimensionSizes.ToImmutable(), initializerOpt);
        }
        private BoundExpression BindImplicitArrayCreation(ImplicitArrayCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Commas.Count != 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRIMP000",
                    DiagnosticSeverity.Error,
                    "Only single-dimensional implicit array creation is supported.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var exprs = node.Initializer.Expressions;
            if (exprs.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRIMP001",
                    DiagnosticSeverity.Error,
                    "Cannot infer array element type from an empty initializer.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
                return new BoundBadExpression(node);
            }

            var raw = ImmutableArray.CreateBuilder<BoundExpression>(exprs.Count);
            for (int i = 0; i < exprs.Count; i++)
                raw.Add(BindExpression(exprs[i], context, diagnostics));

            TypeSymbol? elemType = null;
            for (int i = 0; i < raw.Count; i++)
            {
                var t = raw[i].Type;
                if (t is ErrorTypeSymbol)
                    continue;

                if (elemType is null)
                {
                    elemType = t;
                    continue;
                }

                elemType = ClassifyImplicitArrayElementCommonType(elemType, t, node, context, diagnostics);
                if (elemType is null || elemType is ErrorTypeSymbol)
                    return new BoundBadExpression(node);
            }

            if (elemType is null || elemType is NullTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRIMP002",
                    DiagnosticSeverity.Error,
                    "Cannot infer array element type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
                return new BoundBadExpression(node);
            }

            if (elemType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARRIMP003",
                    DiagnosticSeverity.Error,
                    "Array element type cannot be void.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
                return new BoundBadExpression(node);
            }

            var converted = ImmutableArray.CreateBuilder<BoundExpression>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                converted.Add(ApplyConversion(
                    exprSyntax: exprs[i],
                    expr: raw[i],
                    targetType: elemType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true));
            }

            var init = new BoundArrayInitializerExpression(node.Initializer, elemType, converted.ToImmutable());
            context.Recorder.RecordBound(node.Initializer, init);

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var count = new BoundLiteralExpression(node, int32, converted.Count);
            var arrayType = context.Compilation.CreateArrayType(elemType, rank: 1);

            return new BoundArrayCreationExpression(node, arrayType, elemType, count, init);
        }
        internal BoundExpression BindExpressionWithTargetType(
            ExpressionSyntax exprSyntax,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool requireImplicit = true)
        {
            if (exprSyntax is InitializerExpressionSyntax initSyntax &&
                initSyntax.Kind == SyntaxKind.ArrayInitializerExpression &&
                targetType is ArrayTypeSymbol arrayType)
            {
                var boundInit = BindManagedArrayInitializer(
                    initSyntax,
                    arrayType.ElementType,
                    arrayType.Rank,
                    context,
                    diagnostics);

                if (boundInit is null)
                {
                    var bad = new BoundBadExpression(exprSyntax);
                    bad.SetType(targetType);
                    context.Recorder.RecordBound(exprSyntax, bad);
                    return bad;
                }

                var inferredShape = InferRectangularInitializerShape(
                    boundInit,
                    arrayType.Rank,
                    context,
                    diagnostics);

                if (arrayType.Rank > 1 && inferredShape.Length < arrayType.Rank)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ARRNEW003",
                        DiagnosticSeverity.Error,
                        "Cannot infer all array dimensions from the initializer.",
                        new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));

                    var bad = new BoundBadExpression(exprSyntax);
                    bad.SetType(targetType);
                    context.Recorder.RecordBound(exprSyntax, bad);
                    return bad;
                }

                var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                var sizes = ImmutableArray.CreateBuilder<BoundExpression>(arrayType.Rank);

                for (int i = 0; i < arrayType.Rank; i++)
                    sizes.Add(new BoundLiteralExpression(initSyntax, int32, inferredShape[i]));

                var arrayCreation = new BoundArrayCreationExpression(
                    exprSyntax,
                    arrayType,
                    arrayType.ElementType,
                    sizes.ToImmutable(),
                    boundInit);

                context.Recorder.RecordBound(exprSyntax, arrayCreation);
                return arrayCreation;
            }

            var bound = BindExpression(exprSyntax, context, diagnostics);
            return ApplyConversion(
                exprSyntax,
                bound,
                targetType,
                diagnosticNode,
                context,
                diagnostics,
                requireImplicit);
        }
        private BoundArrayInitializerExpression? BindManagedArrayInitializer(
            InitializerExpressionSyntax? init,
            TypeSymbol elemType,
            int rank,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (init is null)
                return null;

            var exprs = init.Expressions;
            var b = ImmutableArray.CreateBuilder<BoundExpression>(exprs.Count);

            for (int i = 0; i < exprs.Count; i++)
            {
                var exprSyntax = exprs[i];

                if (rank > 1)
                {
                    if (exprSyntax is not InitializerExpressionSyntax nested)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ARRNEW006",
                            DiagnosticSeverity.Error,
                            "A nested initializer is required for a multidimensional array.",
                            new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));

                        b.Add(new BoundBadExpression(exprSyntax));
                        continue;
                    }

                    var nestedInit = BindManagedArrayInitializer(nested, elemType, rank - 1, context, diagnostics)
                        ?? new BoundArrayInitializerExpression(nested, elemType, ImmutableArray<BoundExpression>.Empty);

                    b.Add(nestedInit);
                    continue;
                }

                if (exprSyntax is InitializerExpressionSyntax nestedArrayInit && elemType is ArrayTypeSymbol nestedArrayType)
                {
                    var nestedBoundInit = BindManagedArrayInitializer(
                        nestedArrayInit,
                        nestedArrayType.ElementType,
                        nestedArrayType.Rank,
                        context,
                        diagnostics);

                    if (nestedBoundInit is null)
                    {
                        b.Add(new BoundBadExpression(exprSyntax));
                        continue;
                    }

                    var shape = InferRectangularInitializerShape(nestedBoundInit, nestedArrayType.Rank, context, diagnostics);
                    if (shape.Length < nestedArrayType.Rank)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ARRNEW007",
                            DiagnosticSeverity.Error,
                            "Cannot infer all nested array dimensions from the initializer.",
                            new Location(context.SemanticModel.SyntaxTree, nestedArrayInit.Span)));

                        b.Add(new BoundBadExpression(exprSyntax));
                        continue;
                    }

                    var nestedSizes = ImmutableArray.CreateBuilder<BoundExpression>(nestedArrayType.Rank);
                    var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                    for (int d = 0; d < nestedArrayType.Rank; d++)
                        nestedSizes.Add(new BoundLiteralExpression(nestedArrayInit, int32, shape[d]));

                    b.Add(new BoundArrayCreationExpression(
                        nestedArrayInit,
                        nestedArrayType,
                        nestedArrayType.ElementType,
                        nestedSizes.ToImmutable(),
                        nestedBoundInit));
                    continue;
                }

                var e = BindExpression(exprSyntax, context, diagnostics);
                e = ApplyConversion(exprSyntax, e, elemType, init, context, diagnostics, requireImplicit: true);
                b.Add(e);
            }

            var boundInit = new BoundArrayInitializerExpression(init, elemType, b.ToImmutable());
            context.Recorder.RecordBound(init, boundInit);
            return boundInit;
        }
        private ImmutableArray<int> InferRectangularInitializerShape(
            BoundArrayInitializerExpression init,
            int rank,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (rank <= 1)
                return ImmutableArray.Create(init.Elements.Length);

            var topLen = init.Elements.Length;
            if (topLen == 0)
                return ImmutableArray.Create(0);

            ImmutableArray<int> subShape = default;
            bool haveSubShape = false;

            for (int i = 0; i < init.Elements.Length; i++)
            {
                if (init.Elements[i] is not BoundArrayInitializerExpression nested)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ARRNEW006",
                        DiagnosticSeverity.Error,
                        "A nested initializer is required for a multidimensional array.",
                        new Location(context.SemanticModel.SyntaxTree, init.Elements[i].Syntax.Span)));
                    return ImmutableArray.Create(topLen);
                }

                var current = InferRectangularInitializerShape(nested, rank - 1, context, diagnostics);
                if (!haveSubShape)
                {
                    subShape = current;
                    haveSubShape = true;
                    continue;
                }

                if (current.Length != subShape.Length)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ARRNEW008",
                        DiagnosticSeverity.Error,
                        "Multidimensional array initializer must be rectangular.",
                        new Location(context.SemanticModel.SyntaxTree, nested.Syntax.Span)));
                    continue;
                }

                for (int d = 0; d < subShape.Length; d++)
                {
                    if (current[d] != subShape[d])
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_ARRNEW008",
                            DiagnosticSeverity.Error,
                            "Multidimensional array initializer must be rectangular.",
                            new Location(context.SemanticModel.SyntaxTree, nested.Syntax.Span)));
                        break;
                    }
                }
            }

            var result = ImmutableArray.CreateBuilder<int>(1 + subShape.Length);
            result.Add(topLen);
            result.AddRange(subShape);
            return result.ToImmutable();
        }
        private TypeSymbol? ClassifyImplicitArrayElementCommonType(
            TypeSymbol t1,
            TypeSymbol t2,
            SyntaxNode diagNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (ReferenceEquals(t1, t2))
                return t1;

            if (t1 is NullTypeSymbol && t2.IsReferenceType)
                return t2;
            if (t2 is NullTypeSymbol && t1.IsReferenceType)
                return t1;

            if (IsNumeric(t1.SpecialType) && IsNumeric(t2.SpecialType))
                return GetBinaryNumericPromotionType(
                    context.Compilation,
                    context.SemanticModel.SyntaxTree,
                    t1,
                    t2,
                    diagNode,
                    diagnostics);

            if (t1.IsReferenceType && t2.IsReferenceType)
            {
                if (HasImplicitReferenceConversion(t2, t1)) return t1;
                if (HasImplicitReferenceConversion(t1, t2)) return t2;
            }

            diagnostics.Add(new Diagnostic(
                "CN_ARRIMP004",
                DiagnosticSeverity.Error,
                $"Cannot determine common array element type for '{t1.Name}' and '{t2.Name}'.",
                new Location(context.SemanticModel.SyntaxTree, diagNode.Span)));
            return null;
        }
        private BoundExpression BindStackAlloc(
            StackAllocArrayCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Type is not ArrayTypeSyntax at)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC000",
                    DiagnosticSeverity.Error,
                    "stackalloc requires an array type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (at.RankSpecifiers.Count != 1)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC001",
                    DiagnosticSeverity.Error,
                    "stackalloc only supports single-dimensional arrays.",
                    new Location(context.SemanticModel.SyntaxTree, at.Span)));
                return new BoundBadExpression(node);
            }

            var elemType = BindType(at.ElementType, context, diagnostics);
            if (elemType.IsReferenceType || elemType is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC002",
                    DiagnosticSeverity.Error,
                    $"Cannot stackalloc managed type '{elemType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, at.ElementType.Span)));
            }

            var rs = at.RankSpecifiers[0];
            BoundExpression? countExpr = null;

            if (rs.Sizes.Count == 1 && rs.Sizes[0] is not OmittedArraySizeExpressionSyntax)
                countExpr = BindExpression(rs.Sizes[0], context, diagnostics);

            var initializerOpt = BindStackAllocInitializer(node.Initializer, elemType, context, diagnostics);

            if (countExpr == null)
            {
                if (initializerOpt != null)
                {
                    var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                    countExpr = new BoundLiteralExpression(node, intType, initializerOpt.Elements.Length);
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_STACKALLOC003",
                        DiagnosticSeverity.Error,
                        "stackalloc requires a size expression or an initializer.",
                        new Location(context.SemanticModel.SyntaxTree, rs.Span)));
                    countExpr = new BoundBadExpression(node);
                }
            }

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            countExpr = ApplyConversion(
                exprSyntax: rs.Sizes.Count > 0 ? rs.Sizes[0] : node,
                expr: countExpr,
                targetType: int32,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            if (initializerOpt != null && countExpr.ConstantValueOpt.HasValue && countExpr.ConstantValueOpt.Value is int n)
            {
                if (n != initializerOpt.Elements.Length)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_STACKALLOC004",
                        DiagnosticSeverity.Error,
                        $"An initializer of length '{initializerOpt.Elements.Length}' is expected.",
                        new Location(context.SemanticModel.SyntaxTree, node.Initializer!.Span)));
                }
            }

            var ptrType = context.Compilation.CreatePointerType(elemType);
            return new BoundStackAllocArrayCreationExpression(node, ptrType, elemType, countExpr, initializerOpt);
        }
        private BoundExpression BindImplicitStackAlloc(ImplicitStackAllocArrayCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Initializer is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC_IMP000",
                    DiagnosticSeverity.Error,
                    "Implicit stackalloc requires an initializer.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var elems = node.Initializer.Expressions;
            if (elems.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC_IMP001",
                    DiagnosticSeverity.Error,
                    "Cannot infer stackalloc element type from an empty initializer.",
                    new Location(context.SemanticModel.SyntaxTree, node.Initializer.Span)));
                return new BoundBadExpression(node);
            }

            var first = BindExpression(elems[0], context, diagnostics);
            var elemType = first.Type;

            var builder = ImmutableArray.CreateBuilder<BoundExpression>(elems.Count);
            builder.Add(first);
            for (int i = 1; i < elems.Count; i++)
                builder.Add(BindExpression(elems[i], context, diagnostics));

            for (int i = 0; i < builder.Count; i++)
            {
                builder[i] = ApplyConversion(
                    exprSyntax: elems[i],
                    expr: builder[i],
                    targetType: elemType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            if (elemType.IsReferenceType || elemType is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC_IMP002",
                    DiagnosticSeverity.Error,
                    $"Cannot stackalloc managed type '{elemType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
            }

            var initializerOpt = new BoundArrayInitializerExpression(node.Initializer, elemType, builder.ToImmutable());
            context.Recorder.RecordBound(node.Initializer, initializerOpt);

            var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var countExpr = new BoundLiteralExpression(node, intType, initializerOpt.Elements.Length);

            var ptrType = context.Compilation.CreatePointerType(elemType);
            return new BoundStackAllocArrayCreationExpression(node, ptrType, elemType, countExpr, initializerOpt);
        }
        private BoundArrayInitializerExpression? BindStackAllocInitializer(
            InitializerExpressionSyntax? init,
            TypeSymbol elemType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (init is null)
                return null;

            var exprs = init.Expressions;
            var b = ImmutableArray.CreateBuilder<BoundExpression>(exprs.Count);
            for (int i = 0; i < exprs.Count; i++)
            {
                var e = BindExpression(exprs[i], context, diagnostics);
                e = ApplyConversion(exprs[i], e, elemType, init, context, diagnostics, requireImplicit: true);
                b.Add(e);
            }

            var boundInit = new BoundArrayInitializerExpression(init, elemType, b.ToImmutable());
            context.Recorder.RecordBound(init, boundInit);
            return boundInit;
        }
        private BoundStatement BindReturn(ReturnStatementSyntax rs, BindingContext context, DiagnosticBag diagnostics)
        {
            _flow.ValidateMethodExitTransfer(rs, context, diagnostics);
            var containingMethod = context.ContainingSymbol as MethodSymbol;
            var returnType = containingMethod?.ReturnType
                ?? context.Compilation.GetSpecialType(SpecialType.System_Void);

            if (rs.Expression is null)
            {
                var statement = new BoundReturnStatement(rs, null);
                if (RequiresValueReturn(containingMethod, returnType))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_RET001",
                        DiagnosticSeverity.Error,
                        $"An object of a type convertible to '{returnType.Name}' is required.",
                        new Location(context.SemanticModel.SyntaxTree, rs.Span)));
                    statement.SetHasErrors();
                }

                return statement;
            }

            var expr = BindExpression(rs.Expression, context, diagnostics);
            if (returnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_RET002",
                    DiagnosticSeverity.Error,
                    "Since this function returns void, a return keyword must not be followed by an object expression.",
                    new Location(context.SemanticModel.SyntaxTree, rs.Expression.Span)));

                var statement = new BoundReturnStatement(rs, expr);
                statement.SetHasErrors();
                return statement;
            }

            if (returnType is ByRefTypeSymbol byRefReturnType)
            {
                expr = BindRefReturningExpression(
                    rs.Expression,
                    expr,
                    byRefReturnType,
                    rs,
                    context,
                    diagnostics);

                return new BoundReturnStatement(rs, expr);
            }
            expr = ApplyConversion(
                exprSyntax: rs.Expression,
                expr: expr,
                targetType: returnType,
                diagnosticNode: rs,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);
            return new BoundReturnStatement(rs, expr);
        }

        private static bool RequiresValueReturn(MethodSymbol? method, TypeSymbol returnType)
            => method is not null && !method.IsConstructor &&
               returnType.SpecialType != SpecialType.System_Void &&
               returnType.Kind != SymbolKind.Error;
        private BoundStatement BindYield(YieldStatementSyntax ys, BindingContext context, DiagnosticBag diagnostics)
        {
            bool isYieldReturn = ys.Kind == SyntaxKind.YieldReturnStatement;
            var yieldKind = isYieldReturn ? BoundYieldStatementKind.Return : BoundYieldStatementKind.Break;

            _flow.ValidateMethodExitTransfer(ys, context, diagnostics);
            ValidateYieldContext(ys, isYieldReturn, context, diagnostics);

            TypeSymbol? elementTypeOpt = null;
            bool validIteratorReturn = TryGetIteratorElementType(
                context.Compilation,
                context.ContainingSymbol as MethodSymbol,
                out elementTypeOpt);

            if (!validIteratorReturn)
            {
                var method = context.ContainingSymbol as MethodSymbol;
                var returnType = method?.ReturnType;
                string target = method is not null && method.IsSpecialName ? "accessor" : "method";
                string returnTypeName = returnType?.Name ?? "<unknown>";

                diagnostics.Add(new Diagnostic(
                    "CN_YIELD_RET001",
                    DiagnosticSeverity.Error,
                    $"The body of this {target} cannot be an iterator block because '{returnTypeName}' is not an iterator interface type.",
                    new Location(context.SemanticModel.SyntaxTree, ys.Span)));
            }

            BoundExpression? expr = null;

            if (isYieldReturn)
            {
                if (ys.Expression is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_YIELD_EXPR001",
                        DiagnosticSeverity.Error,
                        "Expression expected after 'yield return'.",
                        new Location(context.SemanticModel.SyntaxTree, ys.Span)));
                }
                else
                {
                    expr = BindExpression(ys.Expression, context, diagnostics);

                    if (expr.Type is ByRefTypeSymbol)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_YIELD_REF001",
                            DiagnosticSeverity.Error,
                            "An iterator cannot yield a value by reference.",
                            new Location(context.SemanticModel.SyntaxTree, ys.Expression.Span)));
                    }
                    else if (validIteratorReturn && elementTypeOpt is not null)
                    {
                        if (ContainsUnsafeType(elementTypeOpt))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_YIELD_UNSAFE001",
                                DiagnosticSeverity.Error,
                                $"Iterator element type '{elementTypeOpt.Name}' cannot be an unsafe type.",
                                new Location(context.SemanticModel.SyntaxTree, ys.Expression.Span)));
                        }
                        else
                        {
                            expr = ApplyConversion(
                                exprSyntax: ys.Expression,
                                expr: expr,
                                targetType: elementTypeOpt,
                                diagnosticNode: ys,
                                context: context,
                                diagnostics: diagnostics,
                                requireImplicit: true);
                        }
                    }
                }
            }

            return new BoundYieldStatement(ys, yieldKind, expr, elementTypeOpt);
        }
        private void ValidateYieldContext(
            YieldStatementSyntax ys,
            bool isYieldReturn,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if ((Flags & BinderFlags.InLambda) != 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_YIELD_LAMBDA001",
                    DiagnosticSeverity.Error,
                    "The yield statement cannot be used inside a lambda expression.",
                    new Location(context.SemanticModel.SyntaxTree, ys.Span)));
            }

            if ((Flags & BinderFlags.UnsafeRegion) != 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_YIELD_UNSAFE002",
                    DiagnosticSeverity.Error,
                    "The yield statement cannot be used inside an unsafe block.",
                    new Location(context.SemanticModel.SyntaxTree, ys.Span)));
            }

            if (_flow.IsInsideFinallyRegion)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_YIELD_FINALLY001",
                    DiagnosticSeverity.Error,
                    "The yield statement cannot be used in the body of a finally clause.",
                    new Location(context.SemanticModel.SyntaxTree, ys.Span)));
            }

            if (isYieldReturn && _flow.IsInsideCatchRegion)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_YIELD_CATCH001",
                    DiagnosticSeverity.Error,
                    "Cannot yield a value in the body of a catch clause.",
                    new Location(context.SemanticModel.SyntaxTree, ys.Span)));
            }

            if (isYieldReturn && _flow.IsInsideTryWithCatchRegion)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_YIELD_TRY001",
                    DiagnosticSeverity.Error,
                    "Cannot yield a value in the body of a try block with a catch clause.",
                    new Location(context.SemanticModel.SyntaxTree, ys.Span)));
            }

            if (context.ContainingSymbol is MethodSymbol method)
            {
                if (method.IsAsync)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_YIELD_ASYNC001",
                        DiagnosticSeverity.Error,
                        "The yield statement is not supported in async methods by this compiler.",
                        new Location(context.SemanticModel.SyntaxTree, ys.Span)));
                }

                var parameters = method.Parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (p.RefKind != ParameterRefKind.None || p.Type is ByRefTypeSymbol || ContainsUnsafeType(p.Type))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_YIELD_PARAM001",
                            DiagnosticSeverity.Error,
                            "Iterators cannot have in, ref, out, or unsafe parameters.",
                            new Location(
                                context.SemanticModel.SyntaxTree,
                                p.Locations.IsDefaultOrEmpty ? ys.Span : p.Locations[0].Span)));
                    }
                }
            }
        }
        private static bool TryGetIteratorElementType(
            Compilation compilation,
            MethodSymbol? method,
            out TypeSymbol elementType)
        {
            elementType = null!;

            if (method is null)
                return false;

            var returnType = method.ReturnType;

            if (returnType is ByRefTypeSymbol || returnType.Kind == SymbolKind.Error)
                return false;

            if (returnType is not NamedTypeSymbol namedReturn)
                return false;

            var enumerable = GetWellKnownType(compilation, "System", "Collections", "IEnumerable", 0);
            var enumerator = GetWellKnownType(compilation, "System", "Collections", "IEnumerator", 0);
            var genericEnumerable = GetWellKnownType(compilation, "System", "Collections", "Generic", "IEnumerable", 1);
            var genericEnumerator = GetWellKnownType(compilation, "System", "Collections", "Generic", "IEnumerator", 1);

            if ((enumerable is not null && AreSameType(namedReturn, enumerable)) ||
                (enumerator is not null && AreSameType(namedReturn, enumerator)))
            {
                elementType = compilation.GetSpecialType(SpecialType.System_Object);
                return true;
            }

            if (genericEnumerable is not null &&
                ReferenceEquals(namedReturn.OriginalDefinition, genericEnumerable))
            {
                var args = namedReturn.TypeArguments;
                if (args.Length == 1)
                {
                    elementType = args[0];
                    return true;
                }
            }

            if (genericEnumerator is not null &&
                ReferenceEquals(namedReturn.OriginalDefinition, genericEnumerator))
            {
                var args = namedReturn.TypeArguments;
                if (args.Length == 1)
                {
                    elementType = args[0];
                    return true;
                }
            }

            return false;
        }
        private static bool ContainsUnsafeType(TypeSymbol type)
        {
            switch (type)
            {
                case PointerTypeSymbol:
                case ByRefTypeSymbol:
                    return true;

                case ArrayTypeSymbol array:
                    return ContainsUnsafeType(array.ElementType);

                case TupleTypeSymbol tuple:
                    for (int i = 0; i < tuple.ElementTypes.Length; i++)
                        if (ContainsUnsafeType(tuple.ElementTypes[i]))
                            return true;

                    return false;

                case NamedTypeSymbol named:
                    var args = named.TypeArguments;
                    for (int i = 0; i < args.Length; i++)
                        if (ContainsUnsafeType(args[i]))
                            return true;

                    return false;

                default:
                    return false;
            }
        }
        private BoundExpression BindRefReturningExpression(
            ExpressionSyntax syntax,
            BoundExpression expr,
            ByRefTypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (expr is not BoundRefExpression br || br.Type is not ByRefTypeSymbol sourceByRef)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFRET001",
                    DiagnosticSeverity.Error,
                    "A ref return expression must be a ref expression.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));

                return new BoundBadExpression((ExpressionSyntax)diagnosticNode);
            }

            if (!ReferenceEquals(sourceByRef.ElementType, targetType.ElementType))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFRET001",
                    DiagnosticSeverity.Error,
                    $"Ref type mismatch: expected ref '{targetType.ElementType.Name}', got ref '{sourceByRef.ElementType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));

                return new BoundBadExpression((ExpressionSyntax)diagnosticNode);
            }

            return br;
        }
        private BoundStatement BindThrow(ThrowStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            _flow.ValidateMethodExitTransfer(node, context, diagnostics);

            if (node.Expression is null)
            {
                if (!_flow.IsInsideCatchRegion)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_THROW000",
                        DiagnosticSeverity.Error,
                        "A rethrow statement can only be used inside a catch block.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadStatement(node);
                }

                return new BoundThrowStatement(node, expressionOpt: null);
            }

            var exType = GetSystemExceptionTypeOrReport(node, context, diagnostics);
            if (exType.Kind == SymbolKind.Error)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_THROW001",
                    DiagnosticSeverity.Error,
                    "Core library type 'System.Exception' is required for throw.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            var expr = BindExpression(node.Expression, context, diagnostics);
            expr = ApplyConversion(
                exprSyntax: node.Expression,
                expr: expr,
                targetType: exType,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            return new BoundThrowStatement(node, expr);
        }
        private BoundStatement BindBreak(BreakStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!_flow.TryGetCurrentBreak(out var breakLabel))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW001",
                    DiagnosticSeverity.Error,
                    "The break statement is not within a loop or switch.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                return new BoundBadStatement(node);
            }
            _flow.ValidateBranchTransfer(node, breakLabel, context, diagnostics);
            return new BoundBreakStatement(node, breakLabel);
        }

        private BoundStatement BindContinue(ContinueStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!_flow.TryGetCurrentContinue(out var continueLabel))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW002",
                    DiagnosticSeverity.Error,
                    "The continue statement is not within a loop.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                return new BoundBadStatement(node);
            }
            _flow.ValidateBranchTransfer(node, continueLabel, context, diagnostics);
            return new BoundContinueStatement(node, continueLabel);
        }
    }
}
