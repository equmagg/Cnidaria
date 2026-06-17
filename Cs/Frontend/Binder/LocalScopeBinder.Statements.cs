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
        private BoundStatement BindUsingStatement(UsingStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.AwaitKeyword.Span.Length != 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING_AWAIT000",
                    DiagnosticSeverity.Error,
                    "await using is not supported.",
                    new Location(context.SemanticModel.SyntaxTree, node.AwaitKeyword.Span)));
            }

            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);

            if (node.Declaration is not null)
            {
                var declarations = scope.BindUsingResourceDeclaration(node, node.Declaration, context, diagnostics);
                var body = scope.BindStatement(node.Statement, context, diagnostics);
                return new BoundUsingStatement(node, declarations, expressionOpt: null, body);
            }

            if (node.Expression is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING000",
                    DiagnosticSeverity.Error,
                    "A using statement requires a resource expression or resource declaration.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                var body = scope.BindStatement(node.Statement, context, diagnostics);
                return new BoundUsingStatement(
                    node,
                    ImmutableArray<BoundLocalDeclarationStatement>.Empty,
                    new BoundBadExpression(node),
                    body);
            }

            var expression = BindExpression(node.Expression, context, diagnostics);
            if (expression.Type is NullTypeSymbol or DefaultLiteralTypeSymbol &&
                DeclarationBuilder.TryFindSystemType(context.Compilation.GlobalNamespace, "IDisposable", 0, out var disposableType))
            {
                expression = ApplyConversion(
                    exprSyntax: node.Expression,
                    expr: expression,
                    targetType: disposableType,
                    diagnosticNode: node.Expression,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }
            ValidateUsingResource(expression.Type, node.Expression, node.Expression, context, diagnostics);

            var expressionBody = scope.BindStatement(node.Statement, context, diagnostics);
            return new BoundUsingStatement(node, ImmutableArray<BoundLocalDeclarationStatement>.Empty, expression, expressionBody);
        }
        private ImmutableArray<BoundLocalDeclarationStatement> BindUsingResourceDeclaration(
            UsingStatementSyntax ownerSyntax,
            VariableDeclarationSyntax declaration,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var isVar = IsVar(declaration.Type);
            TypeSymbol? explicitType = null;
            if (!isVar)
                explicitType = BindType(declaration.Type, context, diagnostics);

            var builder = ImmutableArray.CreateBuilder<BoundLocalDeclarationStatement>(declaration.Variables.Count);
            for (int i = 0; i < declaration.Variables.Count; i++)
            {
                var v = declaration.Variables[i];
                if (v.Initializer is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_USING_INIT000",
                        DiagnosticSeverity.Error,
                        "A using resource declaration must be initialized.",
                        new Location(context.SemanticModel.SyntaxTree, v.Span)));
                }

                var bound = BindSingleDeclarator(
                    ownerSyntax,
                    v,
                    isVar,
                    explicitType,
                    isRefLocal: false,
                    isConst: false,
                    isUsing: true,
                    context,
                    diagnostics);

                if (bound is BoundLocalDeclarationStatement localDeclaration)
                    builder.Add(localDeclaration);
            }

            return builder.ToImmutable();
        }
        private void ValidateUsingResource(
            TypeSymbol resourceType,
            ExpressionSyntax? resourceExpressionSyntax,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (resourceType.Kind == SymbolKind.Error)
                return;

            if (resourceType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING_DISPOSE000",
                    DiagnosticSeverity.Error,
                    "A using resource cannot be of type 'void'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return;
            }

            if (resourceType is NamedTypeSymbol nt && nt.IsRefLikeType && FindAccessibleDisposeMethod(resourceType) is not null)
                return;

            if (!DeclarationBuilder.TryFindSystemType(context.Compilation.GlobalNamespace, "IDisposable", 0, out var disposableType))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING_DISPOSE001",
                    DiagnosticSeverity.Error,
                    "Type 'System.IDisposable' is required for using statements.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return;
            }

            var conversionSyntax = resourceExpressionSyntax
                ?? MakeIdentifierName(resourceType.Name, diagnosticNode.Span);
            var dummy = new BoundTypeOnlyExpression(conversionSyntax, resourceType);
            var conversion = ClassifyConversion(dummy, disposableType, context);
            if (conversion.Exists && conversion.IsImplicit)
                return;
            diagnostics.Add(new Diagnostic(
                "CN_USING_DISPOSE002",
                DiagnosticSeverity.Error,
                $"Type '{resourceType.Name}' used in a using statement must be implicitly convertible to 'System.IDisposable' " +
                $"or be a ref-like type with an accessible parameterless instance Dispose method.",
                new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
        }
        private static MethodSymbol? FindAccessibleDisposeMethod(TypeSymbol type)
        {
            var method = FindParameterlessInstanceMethod(type, "Dispose", null);
            return method is not null && method.ReturnType.SpecialType == SpecialType.System_Void
                ? method
                : null;
        }
        private static MethodSymbol? FindParameterlessInstanceMethod(TypeSymbol type, string name, TypeSymbol? returnType)
        {
            var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance);

            MethodSymbol? Visit(TypeSymbol current)
            {
                if (!seen.Add(current))
                    return null;
                if (current is not NamedTypeSymbol nt)
                    return null;

                var members = nt.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is not MethodSymbol method)
                        continue;
                    if (method.IsStatic)
                        continue;
                    if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                        continue;
                    if (method.TypeParameters.Length != 0)
                        continue;
                    if (method.Parameters.Length != 0)
                        continue;
                    if (returnType is not null && !ReferenceEquals(method.ReturnType, returnType))
                        continue;
                    return method;
                }

                var interfaces = nt.Interfaces;
                for (int i = 0; i < interfaces.Length; i++)
                {
                    var m = Visit(interfaces[i]);
                    if (m is not null)
                        return m;
                }

                if (nt.BaseType is TypeSymbol baseType)
                    return Visit(baseType);

                return null;
            }

            return Visit(type);
        }
        private BoundStatement BindTry(TryStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Catches.Count == 0 && node.Finally == null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRY000",
                    DiagnosticSeverity.Error,
                    "A try statement must have at least one catch clause or a finally clause.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
            }
            _flow.PushTryRegion(node.Catches.Count != 0);
            BoundStatement tryStmt;
            try
            {
                tryStmt = BindStatement(node.Block, context, diagnostics);
            }
            finally
            {
                _flow.PopExceptionRegion();
            }
            var tryBlock = tryStmt as BoundBlockStatement
                ?? new BoundBlockStatement(node.Block, ImmutableArray<BoundStatement>.Empty);
            if (tryStmt is not BoundBlockStatement)
                tryBlock.SetHasErrors();

            var catchBlocks = ImmutableArray.CreateBuilder<BoundCatchBlock>(node.Catches.Count);
            var prevUnconditionalCatchTypes = new List<TypeSymbol>(Math.Max(0, node.Catches.Count));

            for (int i = 0; i < node.Catches.Count; i++)
            {
                var c = node.Catches[i];
                if (c.Declaration == null && c.Filter == null && i != node.Catches.Count - 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_TRY001",
                        DiagnosticSeverity.Error,
                        "A catch clause without an exception type must be the last catch clause.",
                        new Location(context.SemanticModel.SyntaxTree, c.Span)));
                }

                var boundCatch = BindCatchClause(c, context, diagnostics);
                var currentType = boundCatch.ExceptionType;
                if (currentType.Kind != SymbolKind.Error)
                {
                    for (int j = 0; j < prevUnconditionalCatchTypes.Count; j++)
                    {
                        var prev = prevUnconditionalCatchTypes[j];
                        if (prev.Kind != SymbolKind.Error)
                            continue;

                        if (IsSameOrDerivedFrom(currentType, prev))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_TRY002",
                                DiagnosticSeverity.Error,
                                "A previous catch clause already catches all exceptions of this or of a super type.",
                                new Location(context.SemanticModel.SyntaxTree, c.Span)));
                            break;
                        }
                    }
                }

                if (c.Filter == null)
                    prevUnconditionalCatchTypes.Add(currentType);

                catchBlocks.Add(boundCatch);
            }
            BoundBlockStatement? finallyBlockOpt = null;
            if (node.Finally != null)
            {
                _flow.PushFinallyRegion();
                BoundStatement finallyStmt;
                try
                {
                    finallyStmt = BindStatement(node.Finally.Block, context, diagnostics);
                }
                finally
                {
                    _flow.PopExceptionRegion();
                }
                finallyBlockOpt = finallyStmt as BoundBlockStatement
                    ?? new BoundBlockStatement(node.Finally.Block, ImmutableArray<BoundStatement>.Empty);
                if (finallyStmt is not BoundBlockStatement)
                    finallyBlockOpt.SetHasErrors();
            }

            return new BoundTryStatement(node, tryBlock, catchBlocks.ToImmutable(), finallyBlockOpt);
        }
        private BoundCatchBlock BindCatchClause(CatchClauseSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);

            TypeSymbol exceptionType;
            LocalSymbol? exceptionLocalOpt = null;

            if (node.Declaration != null)
            {
                exceptionType = scope.BindType(node.Declaration.Type, context, diagnostics);
                exceptionType = scope.ValidateCatchExceptionType(exceptionType, node.Declaration.Type, context, diagnostics);
                exceptionLocalOpt = scope.DeclareCatchExceptionLocal(node.Declaration, exceptionType, context, diagnostics);
            }
            else
            {
                exceptionType = context.Compilation.GetSpecialType(SpecialType.System_Object);
            }

            BoundExpression? filterOpt = null;
            if (node.Filter != null)
            {
                var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
                var raw = scope.BindExpression(node.Filter.FilterExpression, context, diagnostics);
                filterOpt = scope.ApplyConversion(
                    exprSyntax: node.Filter.FilterExpression,
                    expr: raw,
                    targetType: boolType,
                    diagnosticNode: node.Filter.FilterExpression,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            _flow.PushCatchRegion();
            BoundStatement bodyStmt;
            try
            {
                bodyStmt = scope.BindStatement(node.Block, context, diagnostics);
            }
            finally
            {
                _flow.PopExceptionRegion();
            }
            var body = bodyStmt as BoundBlockStatement
                ?? new BoundBlockStatement(node.Block, ImmutableArray<BoundStatement>.Empty);
            if (bodyStmt is not BoundBlockStatement)
                body.SetHasErrors();

            var bound = new BoundCatchBlock(node, exceptionType, exceptionLocalOpt, filterOpt, body);

            context.Recorder.RecordBound(node, bound);

            return bound;
        }
        private LocalSymbol? DeclareCatchExceptionLocal(
            CatchDeclarationSyntax decl,
            TypeSymbol exceptionType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (decl.Identifier.Span.Length == 0)
                return null;

            var name = decl.Identifier.ValueText ?? string.Empty;
            if (name.Length == 0)
                return null;

            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CATCHVAR000",
                    DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, decl.Identifier.Span)));
            }

            var local = new LocalSymbol(
                name: name,
                containing: _containing,
                type: exceptionType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, decl.Identifier.Span)));

            _locals[name] = local;
            context.Recorder.RecordDeclared(decl, local);

            return local;
        }
        private TypeSymbol ValidateCatchExceptionType(
            TypeSymbol exceptionType,
            TypeSyntax typeSyntax,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (exceptionType.Kind == SymbolKind.Error)
                return exceptionType;
            if (exceptionType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE000",
                    DiagnosticSeverity.Error,
                    "Cannot catch 'void'.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new ErrorTypeSymbol("void", containing: null, ImmutableArray<Location>.Empty);
            }

            if (exceptionType is PointerTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE001",
                    DiagnosticSeverity.Error,
                    "Cannot catch a pointer type.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new ErrorTypeSymbol("ptr", containing: null, ImmutableArray<Location>.Empty);
            }

            if (exceptionType is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE002",
                    DiagnosticSeverity.Error,
                    "Cannot catch an array type.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new ErrorTypeSymbol("array", containing: null, ImmutableArray<Location>.Empty);
            }

            if (exceptionType is TypeParameterSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE003",
                    DiagnosticSeverity.Error,
                    "Cannot catch a type parameter.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                return new ErrorTypeSymbol("typeparam", containing: null, ImmutableArray<Location>.Empty);
            }

            if (exceptionType is NamedTypeSymbol nt)
            {
                if (nt.TypeKind != TypeKind.Class)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_TRYTYPE004",
                        DiagnosticSeverity.Error,
                        "The type caught must be a class type.",
                        new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
                }
            }
            else
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE004",
                    DiagnosticSeverity.Error,
                    "The type caught must be a class type.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
            }

            var sysException = GetSystemExceptionTypeOrReport(typeSyntax, context, diagnostics);
            if (sysException.Kind != SymbolKind.Error &&
                !IsSameOrDerivedFrom(exceptionType, sysException))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TRYTYPE005",
                    DiagnosticSeverity.Error,
                    "The type caught must be derived from 'System.Exception'.",
                    new Location(context.SemanticModel.SyntaxTree, typeSyntax.Span)));
            }

            return exceptionType;
        }
        private static bool IsSameOrDerivedFrom(TypeSymbol type, TypeSymbol baseType)
        {
            for (TypeSymbol? cur = type; cur != null; cur = cur.BaseType)
                if (ReferenceEquals(cur, baseType))
                    return true;

            return false;
        }

        private static NamedTypeSymbol GetSystemExceptionTypeOrReport(
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var exceptionType = context.Compilation.GetSpecialType(SpecialType.System_Exception);
            if (exceptionType.Kind == SymbolKind.Error)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_EXC000",
                    DiagnosticSeverity.Error,
                    "Core library type 'System.Exception' is required for try/catch/throw.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
            }
            return exceptionType;
        }
        private BoundStatement BindIf(IfStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);

            var condition = BindExpression(node.Condition, context, diagnostics);
            condition = ApplyConversion(
                exprSyntax: node.Condition,
                expr: condition,
                targetType: boolType,
                diagnosticNode: node.Condition,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            var thenBinder = CreateFlowScopeBinderForTrue(condition);
            var thenStmt = thenBinder.BindStatement(node.Statement, context, diagnostics);

            BoundStatement? elseStmt = null;
            if (node.Else != null)
                elseStmt = BindStatement(node.Else.Statement, context, diagnostics);

            return new BoundIfStatement(node, condition, thenStmt, elseStmt);
        }
        private BoundExpression BindSwitchLabelCondition(
            SyntaxNode diagnosticNode,
            ExpressionSyntax tmpExprSyntax,
            BoundExpression tmpExpr,
            TypeSymbol governingType,
            SwitchLabelSyntax label,
            BindingContext ctx,
            DiagnosticBag diagnostics,
            out bool isFallbackLabel,
            out bool isDefaultLabel,
            out SwitchCaseKey? gotoCaseKey)
        {
            isFallbackLabel = false;
            isDefaultLabel = false;
            gotoCaseKey = null;
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);

            if (label is DefaultSwitchLabelSyntax)
            {
                isFallbackLabel = true;
                isDefaultLabel = true;
                return MakeBoolLiteral(label, ctx, value: true);
            }

            if (label is CaseSwitchLabelSyntax cs)
            {
                var value = BindExpression(cs.Value, ctx, diagnostics);
                value = ApplyConversion(cs.Value, value, governingType, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                if (value.HasErrors) return new BoundBadExpression(label);
                if (!value.ConstantValueOpt.HasValue)
                {
                    diagnostics.Add(new Diagnostic("CN_SWITCH000", DiagnosticSeverity.Error,
                        "A switch case label must be a constant expression.",
                        new Location(ctx.SemanticModel.SyntaxTree, cs.Value.Span)));
                    return MakeBoolLiteral(label, ctx, value: false);
                }
                gotoCaseKey = new SwitchCaseKey(value.ConstantValueOpt.Value);
                var eqTok = MakeToken(SyntaxKind.EqualsEqualsToken, cs.CaseKeyword.Span);
                var eqSyntax = new BinaryExpressionSyntax(SyntaxKind.EqualsExpression, tmpExprSyntax, eqTok, cs.Value);
                return BindEqualityBinary(eqSyntax, tmpExpr, value, ctx, diagnostics);
            }

            if (label is CasePatternSwitchLabelSyntax cps)
            {
                BoundExpression match;
                switch (cps.Pattern)
                {
                    case ConstantPatternSyntax cp:
                        {
                            var value = BindExpression(cp.Expression, ctx, diagnostics);
                            value = ApplyConversion(cp.Expression, value, governingType, diagnosticNode, ctx, diagnostics, requireImplicit: true);
                            if (value.HasErrors) return new BoundBadExpression(label);
                            if (!value.ConstantValueOpt.HasValue)
                            {
                                diagnostics.Add(new Diagnostic("CN_SWITCH000", DiagnosticSeverity.Error,
                                    "A switch case label must be a constant expression.",
                                    new Location(ctx.SemanticModel.SyntaxTree, cp.Expression.Span)));
                                match = MakeBoolLiteral(label, ctx, value: false);
                                break;
                            }
                            if (cps.WhenClause is null)
                                gotoCaseKey = new SwitchCaseKey(value.ConstantValueOpt.Value);
                            var eqTok = MakeToken(SyntaxKind.EqualsEqualsToken, cps.CaseKeyword.Span);
                            var eqSyntax = new BinaryExpressionSyntax(SyntaxKind.EqualsExpression, tmpExprSyntax, eqTok, cp.Expression);
                            match = BindEqualityBinary(eqSyntax, tmpExpr, value, ctx, diagnostics);
                            break;
                        }
                    case DiscardPatternSyntax:
                        match = MakeBoolLiteral(label, ctx, value: true);
                        isFallbackLabel = cps.WhenClause is null;
                        break;
                    default:
                        diagnostics.Add(new Diagnostic("CN_SWITCH001", DiagnosticSeverity.Error,
                            $"Switch pattern '{cps.Pattern.Kind}' is not supported.",
                            new Location(ctx.SemanticModel.SyntaxTree, cps.Pattern.Span)));
                        match = new BoundBadExpression(label);
                        break;
                }

                if (cps.WhenClause is null) return match;

                var whenCond = BindExpression(cps.WhenClause.Condition, ctx, diagnostics);
                whenCond = ApplyConversion(cps.WhenClause.Condition, whenCond, boolType, cps.WhenClause, ctx, diagnostics, requireImplicit: true);
                if (match.HasErrors || whenCond.HasErrors) return new BoundBadExpression(label);
                return MakeLogicalAnd(label, match, whenCond, ctx, diagnostics);
            }

            diagnostics.Add(new Diagnostic("CN_SWITCH002", DiagnosticSeverity.Error,
                $"Switch label '{label.Kind}' is not supported.",
                new Location(ctx.SemanticModel.SyntaxTree, label.Span)));
            return new BoundBadExpression(label);
        }

        private BoundExpression BindSwitchExpressionArmCondition(
            SwitchExpressionArmSyntax arm,
            ExpressionSyntax tmpExprSyntax,
            BoundExpression tmpExpr,
            TypeSymbol governingType,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);
            BoundExpression match;
            switch (arm.Pattern)
            {
                case ConstantPatternSyntax cp:
                    {
                        var value = BindExpression(cp.Expression, ctx, diagnostics);
                        value = ApplyConversion(cp.Expression, value, governingType, arm, ctx, diagnostics, requireImplicit: true);
                        if (value.HasErrors) return new BoundBadExpression(arm);
                        if (!value.ConstantValueOpt.HasValue)
                        {
                            diagnostics.Add(new Diagnostic("CN_SWITCHEXPR000", DiagnosticSeverity.Error,
                                "A switch expression arm pattern must be a constant expression.",
                                new Location(ctx.SemanticModel.SyntaxTree, cp.Expression.Span)));
                            match = MakeBoolLiteral(arm, ctx, value: false);
                            break;
                        }

                        var eqTok = MakeToken(SyntaxKind.EqualsEqualsToken, arm.EqualsGreaterThanToken.Span);
                        var eqSyntax = new BinaryExpressionSyntax(SyntaxKind.EqualsExpression, tmpExprSyntax, eqTok, cp.Expression);
                        match = BindEqualityBinary(eqSyntax, tmpExpr, value, ctx, diagnostics);
                        break;
                    }
                case DiscardPatternSyntax:
                    match = MakeBoolLiteral(arm, ctx, value: true);
                    break;
                default:
                    diagnostics.Add(new Diagnostic("CN_SWITCHEXPR001", DiagnosticSeverity.Error,
                        $"Switch expression pattern '{arm.Pattern.Kind}' is not supported.",
                        new Location(ctx.SemanticModel.SyntaxTree, arm.Pattern.Span)));
                    match = new BoundBadExpression(arm);
                    break;
            }

            if (arm.WhenClause is null) return match;
            var whenCond = BindExpression(arm.WhenClause.Condition, ctx, diagnostics);
            whenCond = ApplyConversion(arm.WhenClause.Condition, whenCond, boolType, arm.WhenClause, ctx, diagnostics, requireImplicit: true);
            if (match.HasErrors || whenCond.HasErrors) return new BoundBadExpression(arm);
            return MakeLogicalAnd(arm, match, whenCond, ctx, diagnostics);
        }

        private BoundStatement BindSwitchStatement(SwitchStatementSyntax node, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var governing = BindExpression(node.Expression, ctx, diagnostics);
            if (governing.HasErrors) return new BoundBadStatement(node);
            if (governing.Type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic("CN_SWITCH003", DiagnosticSeverity.Error,
                    "The switch expression must not be of type 'void'.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Expression.Span)));
                return new BoundBadStatement(node);
            }

            var tmp = NewTemp("$switch_tmp", governing.Type);
            var tmpSyntax = MakeIdentifierName(tmp.Name, node.Expression.Span);
            var tmpExpr = new BoundLocalExpression(tmpSyntax, tmp);
            var tmpDecl = new BoundLocalDeclarationStatement(node, tmp, governing);

            var breakLabel = _flow.NewGeneratedLabel("switch_break");
            int sectionCount = node.Sections.Count;
            var sectionLabels = new LabelSymbol[sectionCount];
            for (int i = 0; i < sectionCount; i++)
                sectionLabels[i] = _flow.NewGeneratedLabel("switch_section");

            var sectionConds = new BoundExpression[sectionCount];
            var gotoCaseLabels = new Dictionary<SwitchCaseKey, LabelSymbol>();
            LabelSymbol? gotoDefaultLabel = null;
            int fallbackSection = -1;

            for (int i = 0; i < sectionCount; i++)
            {
                var sec = node.Sections[i];
                BoundExpression? cond = null;
                bool secHasFallback = false;

                for (int l = 0; l < sec.Labels.Count; l++)
                {
                    bool isFallback;
                    bool isDefaultLabel;
                    SwitchCaseKey? gotoCaseKey;
                    var labelCond = BindSwitchLabelCondition(
                        node,
                        tmpSyntax,
                        tmpExpr,
                        governing.Type,
                        sec.Labels[l],
                        ctx,
                        diagnostics,
                        out isFallback,
                        out isDefaultLabel,
                        out gotoCaseKey);
                    if (isFallback)
                        secHasFallback = true;
                    else
                        cond = cond is null ? labelCond : MakeLogicalOr(sec, cond, labelCond, ctx, diagnostics);

                    if (isDefaultLabel)
                        gotoDefaultLabel ??= sectionLabels[i];

                    if (gotoCaseKey.HasValue && !gotoCaseLabels.ContainsKey(gotoCaseKey.Value))
                        gotoCaseLabels.Add(gotoCaseKey.Value, sectionLabels[i]);
                }

                sectionConds[i] = cond ?? MakeBoolLiteral(sec, ctx, value: false);
                if (secHasFallback)
                {
                    if (fallbackSection < 0) fallbackSection = i;
                    else
                        diagnostics.Add(new Diagnostic("CN_SWITCH004", DiagnosticSeverity.Error,
                            "Multiple default labels in switch statement.",
                            new Location(ctx.SemanticModel.SyntaxTree, sec.Span)));
                }
            }

            var stmts = ImmutableArray.CreateBuilder<BoundStatement>();
            stmts.Add(tmpDecl);

            for (int i = 0; i < sectionCount; i++)
            {
                var cond = sectionConds[i];
                if (TryGetBoolConstant(cond, out var bc) && bc == false) continue;
                if (TryGetBoolConstant(cond, out bc) && bc == true)
                {
                    stmts.Add(new BoundGotoStatement(node, sectionLabels[i]));
                    continue;
                }
                stmts.Add(new BoundConditionalGotoStatement(node, cond, sectionLabels[i], jumpIfTrue: true));
            }

            if (fallbackSection >= 0) stmts.Add(new BoundGotoStatement(node, sectionLabels[fallbackSection]));
            else stmts.Add(new BoundGotoStatement(node, breakLabel));
            var switchGotoScope = new SwitchGotoScope(governing.Type, gotoCaseLabels, gotoDefaultLabel);
            _flow.PushBreak(breakLabel);
            _flow.PushSwitchGotoScope(switchGotoScope);
            try
            {
                for (int i = 0; i < sectionCount; i++)
                {
                    var sec = node.Sections[i];

                    _flow.RegisterLabelDefinition(sectionLabels[i]);
                    stmts.Add(new BoundLabelStatement(sec, sectionLabels[i]));

                    var sectionBinder = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
                    var secStatements = sec.Statements.ToArray();
                    sectionBinder.PredeclareLocalFunctionsInStatementList(ImmutableArray.CreateRange(secStatements), ctx, diagnostics);

                    var boundSecStmts = ImmutableArray.CreateBuilder<BoundStatement>(secStatements.Length);
                    for (int s = 0; s < secStatements.Length; s++)
                        boundSecStmts.Add(sectionBinder.BindStatement(secStatements[s], ctx, diagnostics));

                    stmts.Add(new BoundBlockStatement(sec, boundSecStmts.ToImmutable()));
                    stmts.Add(new BoundGotoStatement(sec, breakLabel)); // implicit break
                }
            }
            finally
            {
                _flow.PopSwitchGotoScope();
                _flow.PopBreak();
            }

            stmts.Add(new BoundLabelStatement(node, breakLabel));
            return new BoundBlockStatement(node, stmts.ToImmutable());
        }

        private BoundExpression BindSwitchExpression(SwitchExpressionSyntax node, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var governing = BindExpression(node.GoverningExpression, ctx, diagnostics);
            if (governing.HasErrors) return new BoundBadExpression(node);
            if (governing.Type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic("CN_SWITCHEXPR002", DiagnosticSeverity.Error,
                    "The switch governing expression must not be of type 'void'.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.GoverningExpression.Span)));
                return new BoundBadExpression(node);
            }

            var tmp = NewTemp("$switch_expr_tmp", governing.Type);
            var tmpSyntax = MakeIdentifierName(tmp.Name, node.GoverningExpression.Span);
            var tmpExpr = new BoundLocalExpression(tmpSyntax, tmp);
            var tmpDecl = new BoundLocalDeclarationStatement(node, tmp, governing);

            var arms = node.Arms;
            int armCount = arms.Count;
            if (armCount == 0)
            {
                diagnostics.Add(new Diagnostic("CN_SWITCHEXPR003", DiagnosticSeverity.Error,
                    "A switch expression must have at least one arm.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var boundArmExprs = new BoundExpression[armCount];
            var armIsThrow = new bool[armCount];
            for (int i = 0; i < armCount; i++)
            {
                armIsThrow[i] = arms[i].Expression is ThrowExpressionSyntax;
                boundArmExprs[i] = BindExpression(arms[i].Expression, ctx, diagnostics);
            }

            TypeSymbol? resultType = null;
            for (int i = 0; i < armCount; i++)
            {
                if (armIsThrow[i])
                    continue;
                var t = boundArmExprs[i].Type;
                if (t is ErrorTypeSymbol)
                    continue;
                if (resultType is null)
                {
                    resultType = t;
                    continue;
                }
                var probe = new DiagnosticBag();
                var merged = ClassifyConditionalResultType(
                    ctx.Compilation,
                    ctx.SemanticModel.SyntaxTree,
                    resultType,
                    t,
                    node,
                    probe);
                if (merged is null || merged is ErrorTypeSymbol)
                {
                    resultType = null;
                    break;
                }
                resultType = merged;
            }
            if (resultType is null || resultType is ErrorTypeSymbol)
            {
                var objType = ctx.Compilation.GetSpecialType(SpecialType.System_Object);
                bool ok = true;
                for (int i = 0; i < armCount; i++)
                {
                    if (armIsThrow[i])
                        continue;
                    var e = boundArmExprs[i];
                    if (e.HasErrors)
                        continue;
                    var conv = ClassifyConversion(e, objType, ctx);
                    if (!conv.Exists || !conv.IsImplicit)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    resultType = objType;
            }
            if (resultType is null || resultType is ErrorTypeSymbol || resultType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic("CN_SWITCHEXPR004", DiagnosticSeverity.Error,
                    "Cannot determine a common type for switch expression arms.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var resultLocal = NewTemp("$switch_expr_result", resultType);
            var resultSyntax = MakeIdentifierName(resultLocal.Name, node.SwitchKeyword.Span);
            var resultLocalExpr = new BoundLocalExpression(resultSyntax, resultLocal);
            var resultDecl = new BoundLocalDeclarationStatement(node, resultLocal, initializer: null);

            var endLabel = _flow.NewGeneratedLabel("switch_expr_end");
            var armLabels = new LabelSymbol[armCount];
            for (int i = 0; i < armCount; i++)
                armLabels[i] = _flow.NewGeneratedLabel("switch_expr_arm");

            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();
            sideEffects.Add(tmpDecl);
            sideEffects.Add(resultDecl);

            for (int i = 0; i < armCount; i++)
            {
                var arm = arms[i];
                var cond = BindSwitchExpressionArmCondition(arm, tmpSyntax, tmpExpr, governing.Type, ctx, diagnostics);
                if (TryGetBoolConstant(cond, out var bc) && bc == false) continue;
                if (TryGetBoolConstant(cond, out bc) && bc == true)
                {
                    sideEffects.Add(new BoundGotoStatement(arm, armLabels[i]));
                    continue;
                }
                sideEffects.Add(new BoundConditionalGotoStatement(arm, cond, armLabels[i], jumpIfTrue: true));
            }

            sideEffects.Add(new BoundGotoStatement(node, endLabel)); // no match => default(resultLocal)

            for (int i = 0; i < armCount; i++)
            {
                var arm = arms[i];
                sideEffects.Add(new BoundLabelStatement(arm, armLabels[i]));
                var armValue = boundArmExprs[i];
                if (!armIsThrow[i] && !armValue.HasErrors)
                {
                    armValue = ApplyConversion(arm.Expression, armValue, resultType, arm, ctx, diagnostics, requireImplicit: true);
                }
                else if (armValue is BoundBadExpression bad)
                {
                    bad.SetType(resultType);
                }
                var assign = new BoundAssignmentExpression(arm, resultLocalExpr, armValue);
                sideEffects.Add(new BoundExpressionStatement(arm, assign));
                sideEffects.Add(new BoundGotoStatement(arm, endLabel));
            }

            sideEffects.Add(new BoundLabelStatement(node, endLabel));

            return new BoundSequenceExpression(
                node,
                locals: ImmutableArray.Create(tmp, resultLocal),
                sideEffects: sideEffects.ToImmutable(),
                value: resultLocalExpr);
        }
        private BoundStatement BindWhile(WhileStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);

            var breakLabel = _flow.NewGeneratedLabel("break");
            var continueLabel = _flow.NewGeneratedLabel("continue");

            var condition = BindExpression(node.Condition, context, diagnostics);
            condition = ApplyConversion(
                exprSyntax: node.Condition,
                expr: condition,
                targetType: boolType,
                diagnosticNode: node.Condition,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            var bodyBinder = CreateFlowScopeBinderForTrue(condition);
            _flow.PushLoop(breakLabel, continueLabel);
            BoundStatement body;
            try
            {
                body = bodyBinder.BindStatement(node.Statement, context, diagnostics);
            }
            finally
            {
                _flow.PopLoop();
            }
            return new BoundWhileStatement(node, condition, body, breakLabel, continueLabel);
        }
        private BoundStatement BindDoWhile(DoStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var breakLabel = _flow.NewGeneratedLabel("break");
            var continueLabel = _flow.NewGeneratedLabel("continue");

            _flow.PushLoop(breakLabel, continueLabel);
            BoundStatement body;
            try
            {
                body = BindStatement(node.Statement, context, diagnostics);
            }
            finally
            {
                _flow.PopLoop();
            }

            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
            var condition = BindExpression(node.Condition, context, diagnostics);
            condition = ApplyConversion(
                exprSyntax: node.Condition,
                expr: condition,
                targetType: boolType,
                diagnosticNode: node.Condition,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            return new BoundDoWhileStatement(node, body, condition, breakLabel, continueLabel);
        }
        private BoundStatement BindFor(ForStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            var initializers = ImmutableArray.CreateBuilder<BoundStatement>();

            var breakLabel = _flow.NewGeneratedLabel("break");
            var continueLabel = _flow.NewGeneratedLabel("continue");

            if (node.Declaration != null)
                initializers.AddRange(scope.BindForVariableDeclaration(node.Declaration, node.Declaration, context, diagnostics));

            for (int i = 0; i < node.Initializers.Count; i++)
            {
                var initSyntax = node.Initializers[i];
                var initExpr = scope.BindDiscardedExpression(initSyntax, context, diagnostics);
                initializers.Add(new BoundExpressionStatement(initSyntax, initExpr));
            }

            BoundExpression? conditionOpt = null;
            if (node.Condition != null)
            {
                var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
                var cond = scope.BindExpression(node.Condition, context, diagnostics);
                conditionOpt = scope.ApplyConversion(
                    exprSyntax: node.Condition,
                    expr: cond,
                    targetType: boolType,
                    diagnosticNode: node.Condition,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            var incrementors = ImmutableArray.CreateBuilder<BoundStatement>();
            for (int i = 0; i < node.Incrementors.Count; i++)
            {
                var incSyntax = node.Incrementors[i];
                var incExpr = scope.BindDiscardedExpression(incSyntax, context, diagnostics);
                incrementors.Add(new BoundExpressionStatement(incSyntax, incExpr));
            }
            _flow.PushLoop(breakLabel, continueLabel);
            BoundStatement body;
            try
            {
                body = scope.BindStatement(node.Statement, context, diagnostics);
            }
            finally
            {
                _flow.PopLoop();
            }

            return new BoundForStatement(node, initializers.ToImmutable(), conditionOpt, incrementors.ToImmutable(), body, breakLabel, continueLabel);
        }
    }
}
