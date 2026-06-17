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
        private static bool TryGetBoolConstant(BoundExpression expr, out bool value)
        {
            if (expr.ConstantValueOpt.HasValue && expr.ConstantValueOpt.Value is bool b)
            {
                value = b;
                return true;
            }
            value = false;
            return false;
        }
        private static bool IsTrueErrorType(TypeSymbol type)
        {
            if (type is not NamedTypeSymbol nt)
                return false;

            if (nt.TypeKind != TypeKind.Error)
                return false;

            return !string.Equals(nt.Name, "<unbound>", StringComparison.Ordinal);
        }

        private static bool ShouldSuppressCascade(BoundExpression expr)
            => expr.HasErrors || IsTrueErrorType(expr.Type);


        private static BoundExpression CreateErrorConversion(ExpressionSyntax exprSyntax, BoundExpression expr, TypeSymbol targetType)
        {
            if (ReferenceEquals(expr.Type, targetType))
                return expr;

            if (expr is BoundThrowExpression te)
            {
                te.SetType(targetType);
                return te;
            }

            var converted = new BoundConversionExpression(
                exprSyntax,
                targetType,
                expr,
                new Conversion(ConversionKind.None),
                isChecked: false);

            converted.SetHasErrors();
            return converted;
        }

        private BoundExpression MakeLogicalNot(
            SyntaxNode syntax,
            BoundExpression operand,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);

            if (operand.HasErrors)
                return new BoundBadExpression(syntax);

            if (operand.Type.SpecialType != SpecialType.System_Boolean)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SWITCH_BOOL000",
                    DiagnosticSeverity.Error,
                    "Internal error: synthesized pattern condition is not boolean.",
                    new Location(ctx.SemanticModel.SyntaxTree, syntax.Span)));

                return new BoundBadExpression(syntax);
            }

            if (operand is BoundUnaryExpression u &&
                u.OperatorKind == BoundUnaryOperatorKind.LogicalNot)
            {
                return u.Operand;
            }

            Optional<object> constantValue = Optional<object>.None;
            if (operand.ConstantValueOpt.HasValue && operand.ConstantValueOpt.Value is bool b)
                constantValue = new Optional<object>(!b);

            return new BoundUnaryExpression(
                syntax,
                BoundUnaryOperatorKind.LogicalNot,
                boolType,
                operand,
                constantValue,
                isChecked: false);
        }

        private BoundExpression MakeLogicalAnd(SyntaxNode syntax, BoundExpression left, BoundExpression right, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);
            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(syntax);
            if (left.Type.SpecialType != SpecialType.System_Boolean || right.Type.SpecialType != SpecialType.System_Boolean)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SWITCH_BOOL000",
                    DiagnosticSeverity.Error,
                    "Internal error: synthesized switch condition is not boolean.",
                    new Location(ctx.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }
            return new BoundBinaryExpression(syntax, BoundBinaryOperatorKind.LogicalAnd, boolType, left, right, Optional<object>.None);
        }

        private BoundExpression MakeLogicalOr(SyntaxNode syntax, BoundExpression left, BoundExpression right, BindingContext ctx, DiagnosticBag diagnostics)
        {
            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);
            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(syntax);
            if (left.Type.SpecialType != SpecialType.System_Boolean || right.Type.SpecialType != SpecialType.System_Boolean)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SWITCH_BOOL000",
                    DiagnosticSeverity.Error,
                    "Internal error: synthesized switch condition is not boolean.",
                    new Location(ctx.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }
            return new BoundBinaryExpression(syntax, BoundBinaryOperatorKind.LogicalOr, boolType, left, right, Optional<object>.None);
        }
        private bool TryGetLocalFromEnclosingScopes(string name, out LocalSymbol? local)
        {
            for (Binder? b = this; b is LocalScopeBinder ls; b = ls.Parent)
            {
                if (ls._locals.TryGetValue(name, out local))
                    return true;
            }
            local = null!;
            return false;
        }
        private bool TryGetParameterFromEnclosingScopes(string name, out ParameterSymbol? param)
        {
            for (Binder? b = this; b is LocalScopeBinder ls; b = ls.Parent)
            {
                if (ls._parameters.TryGetValue(name, out param))
                    return true;
            }
            param = null!;
            return false;
        }
        private bool IsNameDeclaredInEnclosingScopes(string name)
        {
            for (Binder? b = this; b is LocalScopeBinder ls && !ReferenceEquals(ls, _nameConflictStop); b = ls.Parent)
            {
                if (ls._locals.ContainsKey(name) || ls._parameters.ContainsKey(name) || ls._localFunctions.ContainsKey(name))
                    return true;
            }
            return false;
        }
        private bool TryGetLocalFunctionFromEnclosingScopes(string name, out LocalFunctionSymbol? localFunc)
        {
            for (Binder? b = this; b is LocalScopeBinder ls; b = ls.Parent)
            {
                if (ls._localFunctions.TryGetValue(name, out var f))
                {
                    localFunc = f;
                    return true;
                }
            }
            localFunc = null;
            return false;
        }
        private bool EnsureUnsafe(SyntaxNode diagnosticNode, BindingContext context, DiagnosticBag diagnostics)
        {
            if (IsUnsafeContext(context.ContainingSymbol, Flags))
                return true;

            diagnostics.Add(new Diagnostic(
                "CN_UNSAFE000",
                DiagnosticSeverity.Warning,
                "Unsafe code may only appear in an unsafe context.",
                new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

            return false;
        }
        private static bool IsVar(TypeSyntax typeSyntax)
        {
            return typeSyntax is IdentifierNameSyntax id &&
                   StringComparer.Ordinal.Equals(id.Identifier.ValueText, "var");
        }
        private BoundExpression Record(ExpressionSyntax s, BoundExpression b, BindingContext ctx)
        {
            ctx.Recorder.RecordBound(s, b);
            return b;
        }
        private BoundStatement Record(StatementSyntax s, BoundStatement b, BindingContext ctx)
        {
            ctx.Recorder.RecordBound(s, b);
            return b;
        }
        private bool TryBindImportedStaticMember(
            IdentifierNameSyntax id,
            string name,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = null!;

            var imports = GetImports(context);
            if (imports.StaticTypes.IsDefaultOrEmpty)
                return false;

            FieldSymbol? field = null;
            PropertySymbol? prop = null;
            bool ambiguous = false;

            for (int i = 0; i < imports.StaticTypes.Length; i++)
            {
                var t = imports.StaticTypes[i];
                var members = LookupMembers(t, name);
                if (members.IsDefaultOrEmpty)
                    continue;

                members = FilterAccessibleMembers(members, context);
                if (members.IsDefaultOrEmpty)
                    continue;

                for (int m = 0; m < members.Length; m++)
                {
                    switch (members[m])
                    {
                        case FieldSymbol fs when fs.IsStatic:
                            if (field is not null || prop is not null)
                                ambiguous = true;
                            else
                                field = fs;
                            break;

                        case PropertySymbol ps when ps.IsStatic:
                            if (prop is not null || field is not null)
                                ambiguous = true;
                            else
                                prop = ps;
                            break;
                    }
                }

                if (ambiguous)
                    break;
            }
            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BIND_USINGSTATIC001",
                    DiagnosticSeverity.Error,
                    $"Member name '{name}' is ambiguous due to multiple 'using static' imports.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }
            if (field is null && prop is null)
                return false;

            if (field is not null)
            {
                bool isRefField = field.Type is ByRefTypeSymbol;
                TypeSymbol fieldValueType = isRefField ? ((ByRefTypeSymbol)field.Type).ElementType : field.Type;

                bool canWriteField = !field.IsConst && (isRefField || !field.IsReadOnly);
                var cv = field.IsConst ? field.ConstantValueOpt : Optional<object>.None;

                result = new BoundMemberAccessExpression(
                    id,
                    receiverOpt: null,
                    member: field,
                    type: fieldValueType,
                    isLValue: canWriteField,
                    constantValueOpt: cv);
                return true;
            }

            bool canReadProperty =
                prop!.GetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.GetMethod, context);

            bool canWriteProperty =
                prop.SetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.SetMethod, context);

            if (valueKind == BindValueKind.RValue && !canReadProperty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MEMACC_USINGSTATIC001",
                    DiagnosticSeverity.Error,
                    $"Property '{prop.Name}' has no accessible getter.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }

            if (valueKind == BindValueKind.LValue && !canWriteProperty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MEMACC_USINGSTATIC002",
                    DiagnosticSeverity.Error,
                    $"Property '{prop.Name}' has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }

            result = new BoundMemberAccessExpression(
                id,
                receiverOpt: null,
                member: prop,
                type: prop.Type,
                isLValue: canWriteProperty);
            return true;
        }
        private ImmutableArray<MethodSymbol> LookupImportedStaticMethods(string name, BindingContext context)
        {
            var imports = GetImports(context);
            if (imports.StaticTypes.IsDefaultOrEmpty)
                return ImmutableArray<MethodSymbol>.Empty;

            var b = ImmutableArray.CreateBuilder<MethodSymbol>();

            for (int i = 0; i < imports.StaticTypes.Length; i++)
            {
                var t = imports.StaticTypes[i];
                var methods = LookupMethods(t, name);
                if (methods.IsDefaultOrEmpty)
                    continue;

                for (int m = 0; m < methods.Length; m++)
                {
                    var ms = methods[m];
                    if (!ms.IsStatic)
                        continue;
                    if (!AccessibilityHelper.IsAccessible(ms, context))
                        continue;
                    b.Add(ms);
                }
            }

            return b.Count == 0 ? ImmutableArray<MethodSymbol>.Empty : b.ToImmutable();
        }
        public override ImmutableArray<Symbol> LookupSymbols(int position, string? name = null)
        {
            var builder = ImmutableArray.CreateBuilder<Symbol>();

            if (name == null)
            {
                foreach (var p in _parameters.Values) builder.Add(p);
                foreach (var l in _locals.Values) builder.Add(l);
                foreach (var f in _localFunctions.Values) builder.Add(f);
            }
            else
            {
                if (_parameters.TryGetValue(name, out var p)) builder.Add(p);
                if (_locals.TryGetValue(name, out var l)) builder.Add(l);
                if (_localFunctions.TryGetValue(name, out var f)) builder.Add(f);
            }

            if (Parent != null)
                builder.AddRange(Parent.LookupSymbols(position, name));

            return builder.ToImmutable();
        }
        internal void PredeclareLocalFunctionsInStatementList(
            ImmutableArray<StatementSyntax> statements,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            for (int i = 0; i < statements.Length; i++)
            {
                if (statements[i] is LocalFunctionStatementSyntax lf)
                    DeclareLocalFunction(lf, context, diagnostics);
            }
        }

        private void PredeclareLocalFunctionsInBlock(BlockSyntax block, BindingContext context, DiagnosticBag diagnostics)
        {
            var stmts = block.Statements;
            for (int i = 0; i < stmts.Count; i++)
            {
                if (stmts[i] is LocalFunctionStatementSyntax lf)
                    DeclareLocalFunction(lf, context, diagnostics);
            }
        }
        private BoundExpression LowerStackAllocToSpanCreation(
            ExpressionSyntax exprSyntax,
            BoundStackAllocArrayCreationExpression sa,
            NamedTypeSymbol spanLikeType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (!TryFindSpanPointerCtor(spanLikeType, context.Compilation, out var ctor))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STACKALLOC_SPAN001",
                    DiagnosticSeverity.Error,
                    $"Missing '{spanLikeType.Name}' constructor (void*, int).",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(spanLikeType);
                return bad;
            }

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);

            var countTmp = NewTemp("$stackalloc_len", int32);
            var ptrTmp = NewTemp("$stackalloc_ptr", sa.Type);

            var countDecl = new BoundLocalDeclarationStatement(diagnosticNode, countTmp, sa.Count);
            var countExpr = new BoundLocalExpression(diagnosticNode, countTmp);

            var sa2 = new BoundStackAllocArrayCreationExpression(
                sa.Syntax,
                (PointerTypeSymbol)sa.Type,
                sa.ElementType,
                countExpr,
                sa.InitializerOpt);

            var ptrDecl = new BoundLocalDeclarationStatement(diagnosticNode, ptrTmp, sa2);
            var ptrExpr = new BoundLocalExpression(diagnosticNode, ptrTmp);

            var arg0 = ApplyConversion(exprSyntax, ptrExpr, ctor.Parameters[0].Type, diagnosticNode, context, diagnostics, requireImplicit: true);
            var arg1 = ApplyConversion(exprSyntax, countExpr, ctor.Parameters[1].Type, diagnosticNode, context, diagnostics, requireImplicit: true);

            var created = new BoundObjectCreationExpression(exprSyntax, spanLikeType, ctor, ImmutableArray.Create(arg0, arg1));

            return new BoundSequenceExpression(
                diagnosticNode,
                locals: ImmutableArray.Create(countTmp, ptrTmp),
                sideEffects: ImmutableArray.Create<BoundStatement>(countDecl, ptrDecl),
                value: created);
        }
        private static bool TryGetSystemSpanDefinition(Compilation compilation, out NamedTypeSymbol spanDef)
        {
            spanDef = null!;

            NamespaceSymbol? system = null;
            var nss = compilation.GlobalNamespace.GetNamespaceMembers();
            for (int i = 0; i < nss.Length; i++)
            {
                if (string.Equals(nss[i].Name, "System", StringComparison.Ordinal))
                {
                    system = nss[i];
                    break;
                }
            }
            if (system is null)
                return false;

            var spans = system.GetTypeMembers("Span", arity: 1);
            if (spans.IsDefaultOrEmpty)
                return false;

            spanDef = spans[0].OriginalDefinition;
            return true;
        }
        private static bool TryGetSpanLikeElementType(TypeSymbol type, out NamedTypeSymbol spanLikeType, out TypeSymbol elementType)
        {
            spanLikeType = null!;
            elementType = null!;

            if (type is not NamedTypeSymbol nt) return false;

            var def = nt.OriginalDefinition;
            if (def.Arity != 1) return false;

            bool isSpan = string.Equals(def.Name, "Span", StringComparison.Ordinal);
            bool isReadOnlySpan = string.Equals(def.Name, "ReadOnlySpan", StringComparison.Ordinal);
            if (!isSpan && !isReadOnlySpan) return false;

            if (def.ContainingSymbol is not NamespaceSymbol ns || !string.Equals(ns.Name, "System", StringComparison.Ordinal))
                return false;

            spanLikeType = nt;
            var args = nt.TypeArguments;
            elementType = args.Length == 1 ? args[0] : def.TypeParameters[0];
            return true;
        }
        private static bool TryFindSpanPointerCtor(NamedTypeSymbol spanLikeType, Compilation compilation, out MethodSymbol ctor)
        {
            ctor = null!;
            var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
            var voidType = compilation.GetSpecialType(SpecialType.System_Void);
            var voidPtr = compilation.CreatePointerType(voidType);

            foreach (var m in spanLikeType.GetMembers())
            {
                if (m is not MethodSymbol ms || !ms.IsConstructor || ms.IsStatic) continue;
                if (ms.Parameters.Length != 2) continue;
                if (ReferenceEquals(ms.Parameters[0].Type, voidPtr) && ReferenceEquals(ms.Parameters[1].Type, int32))
                {
                    ctor = ms;
                    return true;
                }
            }
            return false;
        }
        private LocalFunctionSymbol DeclareLocalFunction(
            LocalFunctionStatementSyntax lf,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var tree = context.SemanticModel.SyntaxTree;
            var name = lf.Identifier.ValueText ?? "";

            if (name.Length == 0)
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC000", DiagnosticSeverity.Error,
                    "Local function name is missing.",
                    new Location(tree, lf.Span)));
                name = "error";
            }
            if (_localFunctions.ContainsKey(name))
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC001", DiagnosticSeverity.Error,
                    $"A local function named '{name}' is already defined in this scope.",
                    new Location(tree, lf.Identifier.Span)));
                return _localFunctions[name];
            }
            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic("CN_LFUNC002", DiagnosticSeverity.Error,
                    $"Cannot declare local function '{name}' because that name is used in an enclosing local scope.",
                    new Location(tree, lf.Identifier.Span)));
            }
            for (int i = 0; i < lf.Modifiers.Count; i++)
            {
                var k = lf.Modifiers[i].Kind;
                if (k != SyntaxKind.StaticKeyword && k != SyntaxKind.AsyncKeyword && k != SyntaxKind.UnsafeKeyword)
                {
                    diagnostics.Add(new Diagnostic("CN_LFUNC003", DiagnosticSeverity.Error,
                        $"Modifier '{lf.Modifiers[i].ValueText}' is not valid on a local function.",
                        new Location(tree, lf.Modifiers[i].Span)));
                }
            }


            var isStatic =
                HasModifier(lf.Modifiers, SyntaxKind.StaticKeyword) ||
                (_containing is MethodSymbol containingMethod && containingMethod.IsStatic);
            var isAsync = HasModifier(lf.Modifiers, SyntaxKind.AsyncKeyword);
            var isUnsafe = HasModifier(lf.Modifiers, SyntaxKind.UnsafeKeyword);

            var locations = ImmutableArray.Create(new Location(tree, lf.Identifier.Span));
            var sym = new LocalFunctionSymbol(name, _containing, lf, tree, locations, isStatic, isAsync);
            var typeParameters = DeclareLocalFunctionTypeParameters(lf, sym, tree, context, diagnostics);
            sym.SetTypeParameters(typeParameters);
            GenericConstraintBinder.BindOwnerConstraintClauses(
                tree, lf.ConstraintClauses, sym.TypeParameters, sym, diagnostics);

            _localFunctions.Add(name, sym);
            context.Recorder.RecordDeclared(lf, sym);
            var sigBinder =
                isUnsafe && (Flags & BinderFlags.UnsafeRegion) == 0
                ? new LocalScopeBinder(parent: this, flags: Flags | BinderFlags.UnsafeRegion, containing: _containing)
                : this;
            var sigContext = new BindingContext(context.Compilation, context.SemanticModel, sym, context.Recorder);
            var returnType = sigBinder.BindType(lf.ReturnType, sigContext, diagnostics);

            var pars = lf.ParameterList.Parameters;
            var pb = ImmutableArray.CreateBuilder<ParameterSymbol>(pars.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < pars.Count; i++)
            {
                var p = pars[i];
                var pn = p.Identifier.ValueText ?? "";
                if (!seen.Add(pn))
                {
                    diagnostics.Add(new Diagnostic("CN_LFUNC005", DiagnosticSeverity.Error,
                        $"Duplicate parameter name '{pn}'.",
                        new Location(tree, p.Identifier.Span)));
                }

                TypeSymbol pt;
                if (p.Type != null)
                    pt = sigBinder.BindType(p.Type, sigContext, diagnostics);
                else
                {
                    diagnostics.Add(new Diagnostic("CN_LFUNC006", DiagnosticSeverity.Error,
                        "Parameter type is required for local functions.",
                        new Location(tree, p.Span)));
                    pt = new ErrorTypeSymbol("error", containing: null, locations: ImmutableArray<Location>.Empty);
                }
                var pRefKind = DeclarationBuilder.GetParameterRefKind(p);
                if (pRefKind != ParameterRefKind.None && pt is not ByRefTypeSymbol)
                    pt = sigContext.Compilation.CreateByRefType(pt);
                var parameter = new ParameterSymbol(
                    pn,
                    sym,
                    pt,
                    ImmutableArray.Create(new Location(tree, p.Identifier.Span)),
                    isReadOnlyRef: DeclarationBuilder.IsReadOnlyByRefParameter(p),
                    refKind: pRefKind,
                    isScoped: DeclarationBuilder.IsScopedParameter(p),
                    isParams: DeclarationBuilder.IsParamsParameter(p));

                if (p.Default is not null)
                    BindLocalFunctionParameterDefault(p, parameter, sigBinder, sigContext, diagnostics);

                pb.Add(parameter);
            }

            sym.SetSignature(returnType, pb.ToImmutable());
            return sym;
        }
        private void BindLocalFunctionParameterDefault(
            ParameterSyntax parameterSyntax,
            ParameterSymbol parameter,
            LocalScopeBinder sigBinder,
            BindingContext sigContext,
            DiagnosticBag diagnostics)
        {
            var def = parameterSyntax.Default;
            if (def is null)
                return;

            if (parameter.RefKind != ParameterRefKind.None)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PARAMDEFAULT002",
                    DiagnosticSeverity.Error,
                    "Optional parameters cannot be ref/out/in.",
                    new Location(sigContext.SemanticModel.SyntaxTree, def.Span)));
                return;
            }

            if (parameter.IsParams)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PARAMDEFAULT003",
                    DiagnosticSeverity.Error,
                    "'params' parameters cannot have a default value.",
                    new Location(sigContext.SemanticModel.SyntaxTree, def.Span)));
                return;
            }

            var init = sigBinder.BindExpression(def.Value, sigContext, diagnostics);
            init = sigBinder.ApplyConversion(
                exprSyntax: def.Value,
                expr: init,
                targetType: parameter.Type,
                diagnosticNode: parameterSyntax,
                context: sigContext,
                diagnostics: diagnostics,
                requireImplicit: true);

            if (!init.ConstantValueOpt.HasValue)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_PARAMDEFAULT001",
                    DiagnosticSeverity.Error,
                    "Optional parameter default value must be a compile-time constant.",
                    new Location(sigContext.SemanticModel.SyntaxTree, def.Span)));
                return;
            }

            parameter.SetDefaultValue(init.ConstantValueOpt);
        }
        private ImmutableArray<TypeParameterSymbol> DeclareLocalFunctionTypeParameters(
        LocalFunctionStatementSyntax lf,
        LocalFunctionSymbol owner,
        SyntaxTree tree,
        BindingContext context,
        DiagnosticBag diagnostics)
        {
            var list = lf.TypeParameterList;
            if (list == null || list.Parameters.Count == 0)
                return ImmutableArray<TypeParameterSymbol>.Empty;

            var builder = ImmutableArray.CreateBuilder<TypeParameterSymbol>(list.Parameters.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < list.Parameters.Count; i++)
            {
                var p = list.Parameters[i];
                var name = p.Identifier.ValueText ?? "";

                if (name.Length == 0)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_LFUNC_TP000",
                        DiagnosticSeverity.Error,
                        "Local function type parameter name is missing.",
                        new Location(tree, p.Span)));

                    name = "error";
                }

                if (!seen.Add(name))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_LFUNC_TP001",
                        DiagnosticSeverity.Error,
                        $"Duplicate type parameter name '{name}'.",
                        new Location(tree, p.Identifier.Span)));
                }

                var tp = new TypeParameterSymbol(
                    name,
                    owner,
                    ordinal: i,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)));

                builder.Add(tp);
                context.Recorder.RecordDeclared(p, tp);
            }

            return builder.ToImmutable();
        }
    }
}
