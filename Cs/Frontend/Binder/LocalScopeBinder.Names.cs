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
        private BoundExpression BindTupleExpression(TupleExpressionSyntax te, BindingContext context, DiagnosticBag diagnostics)
        {
            var args = te.Arguments;
            if (args.Count < 2)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_TUPEXPR000",
                    DiagnosticSeverity.Error,
                    "A tuple expression must contain at least two elements.",
                    new Location(context.SemanticModel.SyntaxTree, te.Span)));
                return new BoundBadExpression(te);
            }

            var elements = ImmutableArray.CreateBuilder<BoundExpression>(args.Count);
            var names = new string?[args.Count];
            var nameIsExplicit = new bool[args.Count];
            var types = ImmutableArray.CreateBuilder<TypeSymbol>(args.Count);
            var explicitNames = new HashSet<string>(StringComparer.Ordinal);
            bool hasErrors = false;

            for (int i = 0; i < args.Count; i++)
            {
                var a = args[i];
                var e = BindExpression(a.Expression, context, diagnostics);

                elements.Add(e);
                types.Add(e.Type);

                if (e.Type.SpecialType == SpecialType.System_Void)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_TUPEXPR001",
                        DiagnosticSeverity.Error,
                        "Tuple elements may not be of type 'void'.",
                        new Location(context.SemanticModel.SyntaxTree, a.Expression.Span)));
                    hasErrors = true;
                }

                bool isExplicitName = a.NameColon != null;
                string? name = isExplicitName
                    ? a.NameColon!.Name.Identifier.ValueText
                    : TryInferTupleElementName(a.Expression);

                if (!string.IsNullOrEmpty(name) && isExplicitName)
                {
                    if (!explicitNames.Add(name!))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_TUPNAME000",
                            DiagnosticSeverity.Error,
                            $"Tuple element name '{name}' is a duplicate.",
                            new Location(context.SemanticModel.SyntaxTree, a.NameColon!.Span)));
                        name = null;
                        isExplicitName = false;
                        hasErrors = true;
                    }
                }

                names[i] = name;
                nameIsExplicit[i] = isExplicitName && !string.IsNullOrEmpty(name);
            }
            var inferredNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i];
                if (nameIsExplicit[i] || string.IsNullOrEmpty(name) || explicitNames.Contains(name!))
                    continue;

                inferredNameCounts.TryGetValue(name!, out int count);
                inferredNameCounts[name!] = count + 1;
            }
            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i];
                if (nameIsExplicit[i] || string.IsNullOrEmpty(name))
                    continue;

                if (explicitNames.Contains(name!) || inferredNameCounts[name!] != 1)
                    names[i] = null;
            }
            var elementNames = ImmutableArray.CreateRange(names);
            var tupleType = context.Compilation.CreateTupleType(types.ToImmutable(), elementNames);
            return new BoundTupleExpression(te, tupleType, elements.ToImmutable(), elementNames, hasErrors);
            static string? TryInferTupleElementName(ExpressionSyntax expr) => expr switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText,
                MemberAccessExpressionSyntax ma => GetSimpleName(ma.Name),
                _ => null
            };

        }
        private static bool TupleHasUninferableElements(TupleTypeSymbol tupleType)
        {
            var elems = tupleType.ElementTypes;
            for (int i = 0; i < elems.Length; i++)
            {
                var t = elems[i];
                if (t is NullTypeSymbol)
                    return true;
                if (t.SpecialType == SpecialType.System_Void)
                    return true;
                if (t is ErrorTypeSymbol)
                    return true;
            }
            return false;
        }
        private LValue BindLValue(
            ExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool requireReadable = false)
        {
            var target = BindAssignableValue(node, context, diagnostics);

            if (!requireReadable || target.HasErrors)
                return new LValue(target, target);

            if (node is MemberAccessExpressionSyntax ma &&
                target is BoundMemberAccessExpression { Member: PropertySymbol })
            {
                var read = BindMemberAccess(ma, BindValueKind.RValue, context, diagnostics);
                return new LValue(target, read);
            }

            if (node is ElementAccessExpressionSyntax ea &&
                target is BoundIndexerAccessExpression)
            {
                var read = BindElementAccess(ea, BindValueKind.RValue, context, diagnostics);
                return new LValue(target, read);
            }

            return new LValue(target, target);
        }
        private bool TryBindSimpleValue(IdentifierNameSyntax id, out BoundExpression value)
        {
            var name = id.Identifier.ValueText ?? "";

            if (TryGetLocalFromEnclosingScopes(name, out var local))
            {
                value = new BoundLocalExpression(id, local!);
                return true;
            }

            if (TryGetParameterFromEnclosingScopes(name, out var param))
            {
                value = new BoundParameterExpression(id, param!);
                return true;
            }

            value = null!;
            return false;
        }

        private static string GetSimpleName(SimpleNameSyntax name)
        {
            return name switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText ?? "",
                GenericNameSyntax g => g.Identifier.ValueText ?? "",
                _ => ""
            };
        }
        private BoundExpression BindIdentifier(IdentifierNameSyntax id, BindingContext context, DiagnosticBag diagnostics)
        {
            var name = id.Identifier.ValueText ?? "";

            if (TryGetLocalFromEnclosingScopes(name, out var local))
                return new BoundLocalExpression(id, local!);

            if (TryGetParameterFromEnclosingScopes(name, out var param))
                return new BoundParameterExpression(id, param!);

            if (TryBindUnqualifiedMember(id, name, BindValueKind.RValue, context, diagnostics, out var memberExpr))
                return memberExpr;

            if (TryBindImportedStaticMember(id, name, BindValueKind.RValue, context, diagnostics, out var staticMemberExpr))
                return staticMemberExpr;

            if (TryBindUnqualifiedMethodGroup(id, name, context, diagnostics, out var methodGroup))
                return methodGroup;

            diagnostics.Add(new Diagnostic("CN_BIND003", DiagnosticSeverity.Error,
                $"Use of undeclared identifier '{name}'.",
                new Location(context.SemanticModel.SyntaxTree, id.Span)));

            return new BoundBadExpression(id);
        }
        private BoundExpression BindGenericMethodGroup(
            GenericNameSyntax nameSyntax,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = nameSyntax.Identifier.ValueText ?? "";
            if (TryBindUnqualifiedMethodGroup(nameSyntax, name, context, diagnostics, out var group))
                return group;

            diagnostics.Add(new Diagnostic(
                "CN_BIND_METHODGROUP001",
                DiagnosticSeverity.Error,
                $"No method group '{name}' found.",
                new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));

            return new BoundBadExpression(nameSyntax);
        }

        private bool TryBindUnqualifiedMethodGroup(
            SimpleNameSyntax nameSyntax,
            string name,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = null!;

            if (string.IsNullOrEmpty(name))
                return false;

            if (TryGetLocalFunctionFromEnclosingScopes(name, out var localFunc) && localFunc is not null)
            {
                var localMethods = ApplyExplicitTypeArgumentsToMethodGroup(
                    nameSyntax,
                    ImmutableArray.Create<MethodSymbol>(localFunc),
                    context,
                    diagnostics,
                    out bool localHadArityMatch);

                if (localMethods.IsDefaultOrEmpty)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MG_ARITY001",
                        DiagnosticSeverity.Error,
                        localHadArityMatch
                            ? $"No overload of '{name}' satisfies the supplied type arguments."
                            : $"No overload of '{name}' has the supplied number of type arguments.",
                        new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));

                    result = new BoundBadExpression(nameSyntax);
                    return true;
                }

                result = new BoundMethodGroupExpression(nameSyntax, name, receiverOpt: null, localMethods);
                return true;
            }

            NamedTypeSymbol? containingType = null;
            for (Symbol? s = context.ContainingSymbol; s != null; s = s.ContainingSymbol)
            {
                if (s is NamedTypeSymbol nt)
                {
                    containingType = nt;
                    break;
                }
            }

            bool inStaticContext = context.ContainingSymbol switch
            {
                MethodSymbol m => m.IsStatic,
                FieldSymbol f => f.IsStatic,
                PropertySymbol p => p.IsStatic,
                _ => false
            };

            ImmutableArray<MethodSymbol> candidates = ImmutableArray<MethodSymbol>.Empty;
            BoundExpression? receiver = null;
            bool foundInContainingType = false;

            if (containingType is not null)
            {
                var typeCandidates = LookupMethods(containingType, name);
                if (!typeCandidates.IsDefaultOrEmpty)
                {
                    foundInContainingType = true;
                    typeCandidates = typeCandidates
                        .Where(m => AccessibilityHelper.IsAccessible(m, context))
                        .ToImmutableArray();

                    if (typeCandidates.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CALL_ACC_MG001",
                            DiagnosticSeverity.Error,
                            $"No accessible overload of '{name}' found in type '{containingType.Name}'.",
                            new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));

                        result = new BoundBadExpression(nameSyntax);
                        return true;
                    }

                    if (inStaticContext)
                        typeCandidates = typeCandidates.Where(m => m.IsStatic).ToImmutableArray();

                    if (typeCandidates.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MG_STATIC001",
                            DiagnosticSeverity.Error,
                            "An object reference is required for the non-static method group.",
                            new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));

                        result = new BoundBadExpression(nameSyntax);
                        return true;
                    }

                    candidates = ApplyExplicitTypeArgumentsToMethodGroup(
                        nameSyntax,
                        typeCandidates,
                        context,
                        diagnostics,
                        out bool typeHadArityMatch);

                    if (candidates.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MG_ARITY002",
                            DiagnosticSeverity.Error,
                            typeHadArityMatch
                                ? $"No overload of '{name}' satisfies the supplied type arguments."
                                : $"No overload of '{name}' has the supplied number of type arguments.",
                            new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));

                        result = new BoundBadExpression(nameSyntax);
                        return true;
                    }

                    if (candidates.Any(m => !m.IsStatic))
                        receiver = new BoundThisExpression(nameSyntax, containingType);
                }
            }

            if (candidates.IsDefaultOrEmpty && !foundInContainingType)
            {
                var importedStatic = LookupImportedStaticMethods(name, context);
                if (!importedStatic.IsDefaultOrEmpty)
                {
                    candidates = ApplyExplicitTypeArgumentsToMethodGroup(
                        nameSyntax,
                        importedStatic,
                        context,
                        diagnostics,
                        out bool importedHadArityMatch);

                    if (candidates.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MG_ARITY003",
                            DiagnosticSeverity.Error,
                            importedHadArityMatch
                                ? $"No imported overload of '{name}' satisfies the supplied type arguments."
                                : $"No imported overload of '{name}' has the supplied number of type arguments.",
                            new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));

                        result = new BoundBadExpression(nameSyntax);
                        return true;
                    }
                }
            }

            if (candidates.IsDefaultOrEmpty)
                return false;

            result = new BoundMethodGroupExpression(nameSyntax, name, receiver, candidates);
            return true;
        }

        private ImmutableArray<MethodSymbol> ApplyExplicitTypeArgumentsToMethodGroup(
            SimpleNameSyntax nameSyntax,
            ImmutableArray<MethodSymbol> candidates,
            BindingContext context,
            DiagnosticBag diagnostics,
            out bool hadArityMatch)
        {
            hadArityMatch = true;
            if (nameSyntax is not GenericNameSyntax genericName)
                return candidates;

            var explicitTypeArgs = BindTypeArguments(genericName.TypeArgumentList.Arguments, context, diagnostics);
            int arity = explicitTypeArgs.Length;
            var arityMatches = candidates.Where(m => m.TypeParameters.Length == arity).ToImmutableArray();
            hadArityMatch = !arityMatches.IsDefaultOrEmpty;
            if (arityMatches.IsDefaultOrEmpty)
                return ImmutableArray<MethodSymbol>.Empty;

            var constructed = ImmutableArray.CreateBuilder<MethodSymbol>(arityMatches.Length);
            for (int i = 0; i < arityMatches.Length; i++)
            {
                var def = arityMatches[i];
                if (!GenericConstraintChecker.CheckMethodInstantiation(
                    methodDefinition: def,
                    typeArguments: explicitTypeArgs,
                    getArgSpan: a => genericName.TypeArgumentList.Arguments[a].Span,
                    context: context,
                    diagnostics: diagnostics))
                {
                    continue;
                }

                constructed.Add(new ConstructedMethodSymbol(def, explicitTypeArgs, context.Compilation.TypeManager));
            }

            return constructed.Count == 0 ? ImmutableArray<MethodSymbol>.Empty : constructed.ToImmutable();
        }

        private static bool TryBindUnqualifiedMember(
            IdentifierNameSyntax id,
            string name,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = null!;

            NamedTypeSymbol? containingType = null;
            for (Symbol? s = context.ContainingSymbol; s != null; s = s.ContainingSymbol)
            {
                if (s is NamedTypeSymbol nt)
                {
                    containingType = nt;
                    break;
                }
            }
            if (containingType is null)
                return false;

            bool inStaticContext = context.ContainingSymbol switch
            {
                MethodSymbol m => m.IsStatic,
                FieldSymbol f => f.IsStatic,
                PropertySymbol p => p.IsStatic,
                _ => false
            };

            var members = LookupMembers(containingType, name);
            if (members.IsDefaultOrEmpty)
                return false;

            var accessibleMembers = FilterAccessibleMembers(members, context);
            if (accessibleMembers.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ACC001",
                    DiagnosticSeverity.Error,
                    $"Member '{name}' is inaccessible due to its protection level.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));

                result = new BoundBadExpression(id);
                return true;
            }

            members = accessibleMembers;

            FieldSymbol? field = null;
            PropertySymbol? prop = null;

            for (int i = 0; i < members.Length; i++)
            {
                switch (members[i])
                {
                    case FieldSymbol fs:
                        if (field is not null || prop is not null)
                        {
                            diagnostics.Add(new Diagnostic("CN_BIND_MEM001", DiagnosticSeverity.Error,
                                $"Member name '{name}' is ambiguous.",
                                new Location(context.SemanticModel.SyntaxTree, id.Span)));
                            result = new BoundBadExpression(id);
                            return true;
                        }
                        field = fs;
                        break;

                    case PropertySymbol ps:
                        if (prop is not null || field is not null)
                        {
                            diagnostics.Add(new Diagnostic("CN_BIND_MEM001", DiagnosticSeverity.Error,
                                $"Member name '{name}' is ambiguous.",
                                new Location(context.SemanticModel.SyntaxTree, id.Span)));
                            result = new BoundBadExpression(id);
                            return true;
                        }
                        prop = ps;
                        break;
                }
            }
            if (field is null && prop is null)
                return false;

            // field
            if (field is not null)
            {

                if (!field.IsStatic && inStaticContext)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC011", DiagnosticSeverity.Error,
                        $"An object reference is required for the non-static field '{field.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    result = new BoundBadExpression(id);
                    return true;
                }

                BoundExpression? receiver = field.IsStatic ? null : new BoundThisExpression(id, containingType);

                bool isRefField = field.Type is ByRefTypeSymbol;
                TypeSymbol fieldValueType = isRefField ? ((ByRefTypeSymbol)field.Type).ElementType : field.Type;
                if (valueKind == BindValueKind.LValue &&
                    !field.IsStatic &&
                    IsReadOnlyStructThisReceiver(receiver, context) &&
                    !isRefField)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_READONLY_THIS001",
                        DiagnosticSeverity.Error,
                        "Cannot assign to instance members of 'this' in a readonly struct instance member.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));

                    result = new BoundBadExpression(id);
                    return true;
                }
                bool allowCtorReadonlyWrite =
                    valueKind == BindValueKind.LValue &&
                    field.IsReadOnly &&
                    CanAssignReadOnlyFieldInConstructor(field, receiver, context);

                bool canWriteField = !field.IsConst && (isRefField || !field.IsReadOnly || allowCtorReadonlyWrite);

                var cv = field.IsConst ? field.ConstantValueOpt : Optional<object>.None;
                result = new BoundMemberAccessExpression(id, receiver, field, fieldValueType, isLValue: canWriteField, constantValueOpt: cv);
                return true;
            }
            // property
            if (!prop!.IsStatic && inStaticContext)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC021", DiagnosticSeverity.Error,
                    $"An object reference is required for the non-static property '{prop.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }
            BoundExpression? propReceiver = prop.IsStatic ? null : new BoundThisExpression(id, containingType);
            bool canReadProperty =
                prop.GetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.GetMethod, context);

            bool canWriteProperty =
                prop.SetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.SetMethod, context);
            if (valueKind == BindValueKind.LValue &&
                !prop.IsStatic &&
                IsReadOnlyStructThisReceiver(propReceiver, context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_READONLY_THIS002",
                    DiagnosticSeverity.Error,
                    "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));

                result = new BoundBadExpression(id);
                return true;
            }
            if (valueKind == BindValueKind.RValue && !canReadProperty)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC020", DiagnosticSeverity.Error,
                    $"Property '{prop.Name}' has no accessible getter.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }

            bool allowCtorAutoPropWrite =
                valueKind == BindValueKind.LValue &&
                !canWriteProperty &&
                CanAssignReadOnlyAutoPropertyInConstructor(prop, propReceiver, context);

            if (valueKind == BindValueKind.LValue && !canWriteProperty && !allowCtorAutoPropWrite)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC021", DiagnosticSeverity.Error,
                    $"Property '{prop.Name}' has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                result = new BoundBadExpression(id);
                return true;
            }

            result = new BoundMemberAccessExpression(
                id,
                propReceiver,
                prop,
                prop.Type,
                isLValue: canWriteProperty || allowCtorAutoPropWrite);

            return true;
        }
        private BoundExpression BindRefExpression(
            RefExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var operand = BindAssignableValue(node.Expression, context, diagnostics);
            if (operand.HasErrors)
                return operand;

            if (!operand.IsLValue)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF000",
                    DiagnosticSeverity.Error,
                    "The operand of 'ref' must be an assignable variable.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return new BoundBadExpression(node);
            }

            if (operand is BoundLocalExpression bl && bl.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF001",
                    DiagnosticSeverity.Error,
                    "Cannot create a ref to a const local.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return new BoundBadExpression(node);
            }

            if (operand is BoundMemberAccessExpression { Member: PropertySymbol } ||
                operand is BoundIndexerAccessExpression)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF002",
                    DiagnosticSeverity.Error,
                    "A property cannot be used as a ref value here.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return new BoundBadExpression(node);
            }

            if (operand is BoundParameterExpression pe &&
                pe.Parameter.IsReadOnlyRef &&
                !IsInsideIntrinsicMethod(context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF003",
                    DiagnosticSeverity.Error,
                    "Cannot use a readonly by-ref parameter as a writable ref.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return new BoundBadExpression(node);
            }

            var refElementType = operand.Type is ByRefTypeSymbol br ? br.ElementType : operand.Type;
            var byRefType = context.Compilation.CreateByRefType(refElementType);
            return new BoundRefExpression(node, byRefType, operand);
        }

        private BoundExpression BindAddressOf(
            PrefixUnaryExpressionSyntax node, BoundExpression operand, BindingContext context, DiagnosticBag diagnostics)
        {
            EnsureUnsafe(node, context, diagnostics);

            if (!operand.IsLValue)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ADDR000",
                    DiagnosticSeverity.Error,
                    "The operand of '&' must be an assignable variable.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                return new BoundBadExpression(node);
            }

            if (operand is BoundLocalExpression bl)
            {
                if (bl.Local.IsConst)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ADDR001",
                        DiagnosticSeverity.Error,
                        "Cannot take the address of a const local.",
                        new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                    var bad = new BoundBadExpression(node);
                    bad.SetType(context.Compilation.CreatePointerType(operand.Type));
                    return bad;
                }
                else if (bl.Local.IsReadOnly)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ADDR_READONLYLOCAL",
                        DiagnosticSeverity.Error,
                        "Cannot take the address of a readonly local.",
                        new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                    var bad = new BoundBadExpression(node);
                    bad.SetType(context.Compilation.CreatePointerType(operand.Type));
                    return bad;
                }
            }



            if (operand.Type.IsReferenceType || operand.Type is ArrayTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ADDR002",
                    DiagnosticSeverity.Error,
                    $"Cannot take the address of managed type '{operand.Type.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(context.Compilation.CreatePointerType(operand.Type));
                return bad;
            }

            var ptrType = context.Compilation.CreatePointerType(operand.Type);
            return new BoundAddressOfExpression(node, ptrType, operand);
        }

        private BoundExpression BindPointerIndirection(
            PrefixUnaryExpressionSyntax node, BoundExpression operand, BindingContext context, DiagnosticBag diagnostics)
        {
            EnsureUnsafe(node, context, diagnostics);

            if (operand.Type is not PointerTypeSymbol pt)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DEREF000",
                    DiagnosticSeverity.Error,
                    "The operand of '*' must be a pointer.",
                    new Location(context.SemanticModel.SyntaxTree, node.Operand.Span)));

                return new BoundBadExpression(node);
            }

            return new BoundPointerIndirectionExpression(node, pt.PointedAtType, operand);
        }

        private bool TryBindConditionalAccessExpressionStatement(
            ExpressionSyntax expression,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundStatement statement)
        {
            if (expression is ParenthesizedExpressionSyntax paren)
                return TryBindConditionalAccessExpressionStatement(paren.Expression, context, diagnostics, out statement);

            if (expression is AssignmentExpressionSyntax assignment &&
                assignment.Left is ConditionalAccessExpressionSyntax conditionalLeft)
            {
                statement = BindConditionalAssignmentStatement(assignment, conditionalLeft, context, diagnostics);
                return true;
            }

            if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                statement = BindConditionalAccessStatement(conditionalAccess, context, diagnostics);
                return true;
            }

            statement = null!;
            return false;
        }

        private BoundStatement BindConditionalAccessStatement(
            ConditionalAccessExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiver = BindExpression(node.Expression, context, diagnostics);
            if (receiver.HasErrors)
                return new BoundExpressionStatement(node, new BoundBadExpression(node));

            if (!TryPrepareConditionalReceiver(node, receiver, allowNullableValueTypeReceiver: true, context, diagnostics,
                    out var receiverTemp, out var receiverDecl, out var accessReceiver, out var condition))
            {
                return new BoundExpressionStatement(node, new BoundBadExpression(node));
            }

            var whenNotNull = BindConditionalWhenNotNull(node.WhenNotNull, accessReceiver, BindValueKind.RValue, context, diagnostics);
            if (whenNotNull is BoundMethodGroupExpression)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CONDACCESS_MG001",
                    DiagnosticSeverity.Error,
                    "Conditional access to a method group must be invoked or converted to a delegate.",
                    new Location(context.SemanticModel.SyntaxTree, node.WhenNotNull.Span)));
                whenNotNull = new BoundBadExpression(node.WhenNotNull);
            }

            var thenStatement = new BoundExpressionStatement(node.WhenNotNull, whenNotNull);
            var ifStatement = new BoundIfStatement(node, condition, thenStatement, elseOpt: null);

            return new BoundBlockStatement(
                node,
                ImmutableArray.Create<BoundStatement>(receiverDecl, ifStatement));
        }

        private BoundStatement BindConditionalAssignmentStatement(
            AssignmentExpressionSyntax assignment,
            ConditionalAccessExpressionSyntax conditionalLeft,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiver = BindExpression(conditionalLeft.Expression, context, diagnostics);
            if (receiver.HasErrors)
                return new BoundExpressionStatement(assignment, new BoundBadExpression(assignment));

            return BindConditionalAssignmentStatementCore(assignment, conditionalLeft, receiver, context, diagnostics);
        }

        private BoundStatement BindConditionalAssignmentStatementCore(
            AssignmentExpressionSyntax assignment,
            ConditionalAccessExpressionSyntax conditionalLeft,
            BoundExpression receiver,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (!TryPrepareConditionalReceiver(conditionalLeft, receiver, allowNullableValueTypeReceiver: false, context, diagnostics,
                    out var receiverTemp, out var receiverDecl, out var accessReceiver, out var condition))
            {
                return new BoundExpressionStatement(assignment, new BoundBadExpression(assignment));
            }

            BoundStatement thenStatement;
            if (conditionalLeft.WhenNotNull is ConditionalAccessExpressionSyntax nested)
            {
                var nestedReceiver = BindConditionalWhenNotNull(nested.Expression, accessReceiver, BindValueKind.RValue, context, diagnostics);
                thenStatement = BindConditionalAssignmentStatementCore(assignment, nested, nestedReceiver, context, diagnostics);
            }
            else
            {
                var lv = BindConditionalLValue(
                    conditionalLeft.WhenNotNull,
                    accessReceiver,
                    requireReadable: assignment.Kind != SyntaxKind.SimpleAssignmentExpression,
                    context,
                    diagnostics);

                var right = BindExpression(assignment.Right, context, diagnostics);
                var boundAssignment = BindAssignmentToLValue(assignment, lv, right, context, diagnostics);
                thenStatement = new BoundExpressionStatement(assignment, boundAssignment);
            }

            var ifStatement = new BoundIfStatement(conditionalLeft, condition, thenStatement, elseOpt: null);
            return new BoundBlockStatement(
                conditionalLeft,
                ImmutableArray.Create<BoundStatement>(receiverDecl, ifStatement));
        }

        private BoundExpression BindConditionalAccess(
            ConditionalAccessExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiver = BindExpression(node.Expression, context, diagnostics);
            if (receiver.HasErrors)
                return new BoundBadExpression(node);

            return BindConditionalAccessCore(
                node,
                receiver,
                accessReceiver => BindConditionalWhenNotNull(node.WhenNotNull, accessReceiver, BindValueKind.RValue, context, diagnostics),
                context,
                diagnostics);
        }

        private BoundExpression BindConditionalAssignment(
            AssignmentExpressionSyntax assignment,
            ConditionalAccessExpressionSyntax conditionalLeft,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiver = BindExpression(conditionalLeft.Expression, context, diagnostics);
            if (receiver.HasErrors)
                return new BoundBadExpression(assignment);

            return BindConditionalAssignmentCore(assignment, conditionalLeft, receiver, context, diagnostics);
        }

        private BoundExpression BindConditionalAssignmentCore(
            AssignmentExpressionSyntax assignment,
            ConditionalAccessExpressionSyntax conditionalLeft,
            BoundExpression receiver,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            return BindConditionalAccessCore(
                conditionalLeft,
                receiver,
                accessReceiver =>
                {
                    if (conditionalLeft.WhenNotNull is ConditionalAccessExpressionSyntax nested)
                    {
                        var nestedReceiver = BindConditionalWhenNotNull(nested.Expression, accessReceiver, BindValueKind.RValue, context, diagnostics);
                        return BindConditionalAssignmentCore(assignment, nested, nestedReceiver, context, diagnostics);
                    }

                    var lv = BindConditionalLValue(
                        conditionalLeft.WhenNotNull,
                        accessReceiver,
                        requireReadable: assignment.Kind != SyntaxKind.SimpleAssignmentExpression,
                        context,
                        diagnostics);

                    var right = BindExpression(assignment.Right, context, diagnostics);
                    return BindAssignmentToLValue(assignment, lv, right, context, diagnostics);
                },
                context,
                diagnostics,
                allowNullableValueTypeReceiver: false,
                diagnosticNode: assignment);
        }

        private BoundExpression BindConditionalAccessCore(
            SyntaxNode syntax,
            BoundExpression receiver,
            Func<BoundExpression, BoundExpression> bindWhenNotNull,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool allowNullableValueTypeReceiver = true,
            SyntaxNode? diagnosticNode = null)
        {
            diagnosticNode ??= syntax;

            if (!TryPrepareConditionalReceiver(diagnosticNode, receiver, allowNullableValueTypeReceiver, context, diagnostics,
                    out var receiverTemp, out var receiverDecl, out var accessReceiver, out var condition))
            {
                return new BoundBadExpression(syntax);
            }

            var whenNotNull = bindWhenNotNull(accessReceiver);
            if (whenNotNull.HasErrors)
                return new BoundBadExpression(syntax);

            if (whenNotNull is BoundMethodGroupExpression)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CONDACCESS_MG001",
                    DiagnosticSeverity.Error,
                    "Conditional access to a method group must be invoked or converted to a delegate.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            var resultType = GetConditionalAccessResultType(syntax, whenNotNull.Type, context, diagnostics);
            if (resultType is null || resultType is ErrorTypeSymbol)
                return new BoundBadExpression(syntax);

            var whenTrue = ApplyConversion(
                exprSyntax: (ExpressionSyntax)whenNotNull.Syntax,
                expr: whenNotNull,
                targetType: resultType,
                diagnosticNode: syntax,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            var whenFalse = MakeNullConditionalDefaultValue(syntax, resultType, context, diagnostics);
            if (whenTrue.HasErrors || whenFalse.HasErrors)
                return new BoundBadExpression(syntax);

            var conditional = new BoundConditionalExpression(
                syntax,
                resultType,
                condition,
                whenTrue,
                whenFalse,
                Optional<object>.None);

            return new BoundSequenceExpression(
                syntax,
                ImmutableArray.Create(receiverTemp),
                ImmutableArray.Create<BoundStatement>(receiverDecl),
                conditional);
        }

        private bool TryPrepareConditionalReceiver(
            SyntaxNode syntax,
            BoundExpression receiver,
            bool allowNullableValueTypeReceiver,
            BindingContext context,
            DiagnosticBag diagnostics,
            out LocalSymbol receiverTemp,
            out BoundLocalDeclarationStatement receiverDecl,
            out BoundExpression accessReceiver,
            out BoundExpression condition)
        {
            receiverTemp = null!;
            receiverDecl = null!;
            accessReceiver = null!;
            condition = null!;

            if (receiver.Type.SpecialType == SpecialType.System_Void || receiver.Type is PointerTypeSymbol or ByRefTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CONDACCESS001",
                    DiagnosticSeverity.Error,
                    $"Operator '?' cannot be applied to operand of type '{receiver.Type.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return false;
            }

            bool isNullableReceiver = TryGetSystemNullableInfo(receiver.Type, out var nullableReceiverType, out var nullableUnderlyingType);
            if (receiver.Type.IsValueType && !isNullableReceiver)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CONDACCESS001",
                    DiagnosticSeverity.Error,
                    $"Operator '?' cannot be applied to operand of type '{receiver.Type.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return false;
            }

            if (isNullableReceiver && !allowNullableValueTypeReceiver)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CONDASG_NULLABLE001",
                    DiagnosticSeverity.Error,
                    "The receiver of a null-conditional assignment cannot be a nullable value type because the unwrapped value is not a variable.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return false;
            }

            receiverTemp = NewTemp("$cond_recv", receiver.Type);
            receiverDecl = new BoundLocalDeclarationStatement(syntax, receiverTemp, receiver);
            var receiverTempExpr = new BoundLocalExpression(syntax, receiverTemp);
            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);

            if (isNullableReceiver)
            {
                var hasValue = FindNullableHasValueGetter(nullableReceiverType);
                var getValueOrDefault = FindNullableGetValueOrDefault(nullableReceiverType, nullableUnderlyingType);
                if (hasValue is null || getValueOrDefault is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CONDACCESS_NULLABLE000",
                        DiagnosticSeverity.Error,
                        "Missing Nullable<T> members (HasValue / GetValueOrDefault).",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    return false;
                }

                condition = new BoundCallExpression(syntax, receiverTempExpr, hasValue, ImmutableArray<BoundExpression>.Empty);
                accessReceiver = new BoundCallExpression(syntax, receiverTempExpr, getValueOrDefault, ImmutableArray<BoundExpression>.Empty);
                return true;
            }
            BoundExpression conditionReceiver = receiverTempExpr;
            if (receiver.Type is TypeParameterSymbol tp &&
                (tp.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) == 0)
            {
                var objectType = context.Compilation.GetSpecialType(SpecialType.System_Object);
                conditionReceiver = new BoundConversionExpression(
                    syntax: syntax,
                    type: objectType,
                    operand: receiverTempExpr,
                    conversion: new Conversion(ConversionKind.Boxing),
                    isChecked: false);
            }
            condition = new BoundBinaryExpression(
                syntax,
                BoundBinaryOperatorKind.NotEquals,
                boolType,
                conditionReceiver,
                new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null),
                Optional<object>.None);
            accessReceiver = receiverTempExpr;
            return true;
        }

        private TypeSymbol? GetConditionalAccessResultType(
            SyntaxNode syntax,
            TypeSymbol whenNotNullType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (whenNotNullType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CONDACCESS_VOID001",
                    DiagnosticSeverity.Error,
                    "A null-conditional access to a void-returning member can only be used as an expression statement.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return null;
            }

            if (whenNotNullType.IsReferenceType || TryGetSystemNullableInfo(whenNotNullType, out _, out _))
                return whenNotNullType;

            if (whenNotNullType.IsValueType)
                return MakeNullableType(syntax, whenNotNullType, context, diagnostics);

            diagnostics.Add(new Diagnostic(
                "CN_CONDACCESS_TYPE001",
                DiagnosticSeverity.Error,
                $"The result type '{whenNotNullType.Name}' of a conditional access cannot be made nullable.",
                new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
            return null;
        }

        private BoundExpression MakeNullConditionalDefaultValue(
            SyntaxNode syntax,
            TypeSymbol resultType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (TryGetSystemNullableInfo(resultType, out _, out _))
                return MakeDefaultValue(syntax, resultType);

            var nullLiteral = new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null);
            return ApplyConversion(
                exprSyntax: (ExpressionSyntax)syntax,
                expr: nullLiteral,
                targetType: resultType,
                diagnosticNode: syntax,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);
        }

        private NamedTypeSymbol MakeNullableType(
            SyntaxNode syntax,
            TypeSymbol underlyingType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var nullableDefinition = TypeBinder.GetSystemNullableDefinitionOrReport(context, diagnostics, syntax);
            if (nullableDefinition.Kind == SymbolKind.Error)
                return (NamedTypeSymbol)nullableDefinition;

            var args = ImmutableArray.Create(underlyingType);
            var nullableType = context.Compilation.ConstructNamedType(nullableDefinition, args);
            GenericConstraintChecker.CheckNamedTypeInstantiation(
                nullableType,
                args,
                _ => syntax.Span,
                context,
                diagnostics);
            return nullableType;
        }

        private LValue BindConditionalLValue(
            ExpressionSyntax syntax,
            BoundExpression receiver,
            bool requireReadable,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var target = BindConditionalWhenNotNull(syntax, receiver, BindValueKind.LValue, context, diagnostics);
            if (!requireReadable || target.HasErrors)
                return new LValue(target, target);

            if (target is BoundMemberAccessExpression { Member: PropertySymbol })
            {
                var read = BindConditionalWhenNotNull(syntax, receiver, BindValueKind.RValue, context, diagnostics);
                return new LValue(target, read);
            }

            if (target is BoundIndexerAccessExpression)
            {
                var read = BindConditionalWhenNotNull(syntax, receiver, BindValueKind.RValue, context, diagnostics);
                return new LValue(target, read);
            }

            return new LValue(target, target);
        }

        private BoundExpression BindConditionalWhenNotNull(
            ExpressionSyntax syntax,
            BoundExpression receiver,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            switch (syntax)
            {
                case MemberBindingExpressionSyntax memberBinding:
                    return BindMemberOnBoundReceiver(memberBinding, receiver, memberBinding.Name, valueKind, context, diagnostics);

                case ElementBindingExpressionSyntax elementBinding:
                    return BindElementOnBoundReceiver(elementBinding, receiver, elementBinding.ArgumentList, valueKind, context, diagnostics);

                case MemberAccessExpressionSyntax memberAccess:
                    {
                        var boundReceiver = BindConditionalWhenNotNull(memberAccess.Expression, receiver, BindValueKind.RValue, context, diagnostics);
                        if (boundReceiver.HasErrors)
                            return new BoundBadExpression(memberAccess);
                        return BindMemberOnBoundReceiver(memberAccess, boundReceiver, memberAccess.Name, valueKind, context, diagnostics);
                    }

                case ElementAccessExpressionSyntax elementAccess:
                    {
                        var boundReceiver = BindConditionalWhenNotNull(elementAccess.Expression, receiver, BindValueKind.RValue, context, diagnostics);
                        if (boundReceiver.HasErrors)
                            return new BoundBadExpression(elementAccess);
                        return BindElementOnBoundReceiver(elementAccess, boundReceiver, elementAccess.ArgumentList, valueKind, context, diagnostics);
                    }

                case InvocationExpressionSyntax invocation:
                    return BindConditionalInvocation(invocation, receiver, context, diagnostics);

                case ConditionalAccessExpressionSyntax nestedConditional:
                    {
                        var nestedReceiver = BindConditionalWhenNotNull(nestedConditional.Expression, receiver, BindValueKind.RValue, context, diagnostics);
                        if (nestedReceiver.HasErrors)
                            return new BoundBadExpression(nestedConditional);
                        return BindConditionalAccessCore(
                            nestedConditional,
                            nestedReceiver,
                            accessReceiver => BindConditionalWhenNotNull(nestedConditional.WhenNotNull, accessReceiver, BindValueKind.RValue, context, diagnostics),
                            context,
                            diagnostics);
                    }

                default:
                    diagnostics.Add(new Diagnostic(
                        "CN_CONDACCESS002",
                        DiagnosticSeverity.Error,
                        $"Unsupported expression in conditional access: {syntax.Kind}.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    return new BoundBadExpression(syntax);
            }
        }

        private BoundExpression BindConditionalInvocation(
            InvocationExpressionSyntax invocation,
            BoundExpression receiver,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var argSyntaxes = invocation.ArgumentList.Arguments;
            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));
            var boundArgs = args.ToImmutable();

            switch (invocation.Expression)
            {
                case MemberBindingExpressionSyntax memberBinding:
                    return BindMemberInvocationOnBoundReceiver(invocation, receiver, memberBinding.Name, argSyntaxes, boundArgs, context, diagnostics);

                case MemberAccessExpressionSyntax memberAccess:
                    {
                        var boundReceiver = BindConditionalWhenNotNull(memberAccess.Expression, receiver, BindValueKind.RValue, context, diagnostics);
                        if (boundReceiver.HasErrors)
                            return new BoundBadExpression(invocation);
                        return BindMemberInvocationOnBoundReceiver(invocation, boundReceiver, memberAccess.Name, argSyntaxes, boundArgs, context, diagnostics);
                    }

                case ElementBindingExpressionSyntax elementBinding:
                    {
                        var delegateValue = BindElementOnBoundReceiver(elementBinding, receiver, elementBinding.ArgumentList, BindValueKind.RValue, context, diagnostics);
                        return BindDelegateInvocation(invocation, delegateValue, argSyntaxes, boundArgs, context, diagnostics);
                    }

                case ElementAccessExpressionSyntax elementAccess:
                    {
                        var boundReceiver = BindConditionalWhenNotNull(elementAccess.Expression, receiver, BindValueKind.RValue, context, diagnostics);
                        if (boundReceiver.HasErrors)
                            return new BoundBadExpression(invocation);
                        var delegateValue = BindElementOnBoundReceiver(elementAccess, boundReceiver, elementAccess.ArgumentList, BindValueKind.RValue, context, diagnostics);
                        return BindDelegateInvocation(invocation, delegateValue, argSyntaxes, boundArgs, context, diagnostics);
                    }

                default:
                    {
                        var delegateValue = BindConditionalWhenNotNull(invocation.Expression, receiver, BindValueKind.RValue, context, diagnostics);
                        return BindDelegateInvocation(invocation, delegateValue, argSyntaxes, boundArgs, context, diagnostics);
                    }
            }
        }

        private BoundExpression BindMemberInvocationOnBoundReceiver(
            InvocationExpressionSyntax invocation,
            BoundExpression receiver,
            SimpleNameSyntax nameSyntax,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = GetSimpleName(nameSyntax);
            if (string.IsNullOrEmpty(name))
            {
                diagnostics.Add(new Diagnostic("CN_CALL020", DiagnosticSeverity.Error,
                    "Invalid member name in invocation.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(invocation);
            }

            var receiverType = GetReceiverTypeForMemberLookup(receiver.Type, context);
            if (receiverType is null)
            {
                diagnostics.Add(new Diagnostic("CN_CALL021", DiagnosticSeverity.Error,
                    "Receiver is not a type or a value with members.",
                    new Location(context.SemanticModel.SyntaxTree, invocation.Expression.Span)));
                return new BoundBadExpression(invocation);
            }

            var candidates = LookupMethods(receiverType, name)
                .Where(m => !m.IsStatic && AccessibilityHelper.IsAccessible(m, context))
                .ToImmutableArray();

            if (!candidates.IsDefaultOrEmpty)
            {
                if (nameSyntax is GenericNameSyntax genericName)
                {
                    var explicitTypeArgs = BindTypeArguments(genericName.TypeArgumentList.Arguments, context, diagnostics);
                    var arity = explicitTypeArgs.Length;
                    var arityMatches = candidates.Where(m => m.TypeParameters.Length == arity).ToImmutableArray();
                    if (arityMatches.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CALLG010",
                            DiagnosticSeverity.Error,
                            $"No overload of '{name}' has {arity} type parameter(s).",
                            new Location(context.SemanticModel.SyntaxTree, genericName.Span)));
                        return new BoundBadExpression(invocation);
                    }

                    var constructed = ImmutableArray.CreateBuilder<MethodSymbol>(arityMatches.Length);
                    for (int i = 0; i < arityMatches.Length; i++)
                    {
                        var def = arityMatches[i];
                        if (!GenericConstraintChecker.CheckMethodInstantiation(
                            def,
                            explicitTypeArgs,
                            a => genericName.TypeArgumentList.Arguments[a].Span,
                            context,
                            diagnostics))
                        {
                            continue;
                        }

                        constructed.Add(new ConstructedMethodSymbol(def, explicitTypeArgs, context.Compilation.TypeManager));
                    }

                    candidates = constructed.ToImmutable();
                }

                if (TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: invocation))
                {
                    var callReceiver = PrepareReceiverForResolvedMemberCall(
                        invocation.Expression,
                        receiver,
                        chosen!,
                        context);
                    return new BoundCallExpression(invocation, callReceiver, chosen!, convertedArgs);
                }

                return new BoundBadExpression(invocation);
            }

            var extensionCandidates = LookupExtensionMethods(name, receiver, context);
            if (!extensionCandidates.IsDefaultOrEmpty)
            {
                var extArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(args.Length + 1);
                extArgsBuilder.Add(receiver);
                extArgsBuilder.AddRange(args);
                var extArgs = extArgsBuilder.ToImmutable();

                if (TryResolveOverload(
                    candidates: extensionCandidates,
                    args: extArgs,
                    getArgExprSyntax: i => i == 0 ? invocation.Expression : argSyntaxes[i - 1].Expression,
                    getArgRefKindKeyword: i => i == 0 ? null : argSyntaxes[i - 1].RefKindKeyword,
                    getArgName: i => i == 0 ? null : argSyntaxes[i - 1].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: invocation))
                {
                    return new BoundCallExpression(invocation, receiverOpt: null, method: chosen!, arguments: convertedArgs);
                }

                return new BoundBadExpression(invocation);
            }

            var memberValue = BindMemberOnBoundReceiver(invocation.Expression, receiver, nameSyntax, BindValueKind.RValue, context, diagnostics);
            if (!memberValue.HasErrors && TryGetDelegateInvokeMethod(memberValue.Type, out _, out _))
                return BindDelegateInvocation(invocation, memberValue, argSyntaxes, args, context, diagnostics);

            diagnostics.Add(new Diagnostic("CN_CALL022", DiagnosticSeverity.Error,
                $"No method '{name}' found on type '{receiverType.Name}'.",
                new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
            return new BoundBadExpression(invocation);
        }

        private BoundExpression BindMemberOnBoundReceiver(
            ExpressionSyntax syntax,
            BoundExpression receiver,
            SimpleNameSyntax nameSyntax,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = GetSimpleName(nameSyntax);
            if (string.IsNullOrEmpty(name))
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC000", DiagnosticSeverity.Error,
                    "Invalid member name in member access.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(syntax);
            }

            var receiverType = GetReceiverTypeForMemberLookup(receiver.Type, context);
            if (receiverType is null)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC001", DiagnosticSeverity.Error,
                    "Receiver is not a type or a value with members.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            var members = FilterAccessibleMembers(LookupMembers(receiverType, name), context);
            if (members.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC002", DiagnosticSeverity.Error,
                    $"No member '{name}' found on type '{receiverType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(syntax);
            }

            FieldSymbol? field = null;
            PropertySymbol? prop = null;
            bool hasMethod = false;
            bool hasType = false;
            for (int i = 0; i < members.Length; i++)
            {
                switch (members[i])
                {
                    case FieldSymbol f: field ??= f; break;
                    case PropertySymbol p: prop ??= p; break;
                    case MethodSymbol: hasMethod = true; break;
                    case NamedTypeSymbol: hasType = true; break;
                }
            }

            if (field is null && prop is null)
            {
                if (hasMethod)
                {
                    var methodBuilder = ImmutableArray.CreateBuilder<MethodSymbol>();
                    for (int i = 0; i < members.Length; i++)
                        if (members[i] is MethodSymbol method && !method.IsStatic)
                            methodBuilder.Add(method);

                    var methods = ApplyExplicitTypeArgumentsToMethodGroup(
                        nameSyntax,
                        methodBuilder.ToImmutable(),
                        context,
                        diagnostics,
                        out bool hadArityMatch);

                    if (methods.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC_MG001",
                            DiagnosticSeverity.Error,
                            hadArityMatch
                                ? $"No overload of '{name}' satisfies the supplied type arguments or receiver kind."
                                : $"No overload of '{name}' has the supplied number of type arguments.",
                            new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                        return new BoundBadExpression(syntax);
                    }

                    return new BoundMethodGroupExpression(syntax, name, receiver, methods);
                }

                diagnostics.Add(new Diagnostic(
                    hasType ? "CN_MEMACC004" : "CN_MEMACC005",
                    DiagnosticSeverity.Error,
                    hasType ? "A type name is not a value." : "Member access does not resolve to a value member.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            if (field is not null && prop is not null)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC006", DiagnosticSeverity.Error,
                    $"Member name '{name}' is ambiguous between a field and a property.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(syntax);
            }

            if (field is not null)
            {
                if (valueKind == BindValueKind.LValue && field.IsConst)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC010", DiagnosticSeverity.Error,
                        "Cannot assign to a const field.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    return new BoundBadExpression(syntax);
                }

                if (field.IsStatic)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC012", DiagnosticSeverity.Error,
                        "A static field cannot be accessed with an instance reference.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    return new BoundBadExpression(syntax);
                }

                bool isRefField = field.Type is ByRefTypeSymbol;
                TypeSymbol fieldValueType = isRefField ? ((ByRefTypeSymbol)field.Type).ElementType : field.Type;

                if (valueKind == BindValueKind.LValue && IsReadOnlyValueReceiver(receiver, context) && !isRefField)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_READONLY_THIS001",
                        DiagnosticSeverity.Error,
                        "Cannot assign to instance members of 'this' in a readonly struct instance member.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    return new BoundBadExpression(syntax);
                }

                bool allowCtorReadonlyWrite =
                    valueKind == BindValueKind.LValue &&
                    field.IsReadOnly &&
                    CanAssignReadOnlyFieldInConstructor(field, receiver, context);

                if (valueKind == BindValueKind.LValue && field.IsReadOnly && !allowCtorReadonlyWrite && !isRefField)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC013", DiagnosticSeverity.Error,
                        "Cannot assign to a readonly field except in a constructor of the same type.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    return new BoundBadExpression(syntax);
                }

                bool canWriteField = !field.IsConst && (isRefField || !field.IsReadOnly || allowCtorReadonlyWrite);
                return new BoundMemberAccessExpression(
                    syntax,
                    receiver,
                    field,
                    fieldValueType,
                    isLValue: canWriteField,
                    constantValueOpt: field.IsConst ? field.ConstantValueOpt : Optional<object>.None);
            }

            bool canReadProperty = prop!.GetMethod is not null && AccessibilityHelper.IsAccessible(prop.GetMethod, context);
            bool canWriteProperty = prop.SetMethod is not null && AccessibilityHelper.IsAccessible(prop.SetMethod, context);

            if (valueKind == BindValueKind.RValue && !canReadProperty)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC020", DiagnosticSeverity.Error,
                    "Property has no accessible getter.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            bool allowCtorAutoPropWrite =
                valueKind == BindValueKind.LValue &&
                !canWriteProperty &&
                CanAssignReadOnlyAutoPropertyInConstructor(prop, receiver, context);

            if (valueKind == BindValueKind.LValue && !canWriteProperty && !allowCtorAutoPropWrite)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC021", DiagnosticSeverity.Error,
                    "Property has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            if (prop.IsStatic)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC023", DiagnosticSeverity.Error,
                    "A static property cannot be accessed with an instance reference.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            if (valueKind == BindValueKind.LValue && IsReadOnlyValueReceiver(receiver, context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_READONLY_THIS002",
                    DiagnosticSeverity.Error,
                    "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            return new BoundMemberAccessExpression(
                syntax,
                receiver,
                prop,
                prop.Type,
                isLValue: canWriteProperty || allowCtorAutoPropWrite);
        }

        private BoundExpression BindInlineArrayElementAccess(
            ExpressionSyntax syntax,
            BoundExpression receiver,
            BracketedArgumentListSyntax argumentList,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics,
            InlineArrayFacts.InlineArrayInfo info)
        {
            if (argumentList.Arguments.Count != 1)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_INLINEARRAY010",
                    DiagnosticSeverity.Error,
                    "Inline array element access expects exactly one index argument.",
                    new Location(context.SemanticModel.SyntaxTree, argumentList.Span)));
                return new BoundBadExpression(syntax);
            }

            var argSyntax = argumentList.Arguments[0].Expression;
            if (argSyntax is RangeExpressionSyntax)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_INLINEARRAY011",
                    DiagnosticSeverity.Error,
                    valueKind == BindValueKind.LValue
                        ? "Cannot assign to an inline array slice."
                        : "Inline array slicing is not implemented.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Span)));
                return new BoundBadExpression(syntax);
            }

            var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            BoundExpression index;
            if (argSyntax is PrefixUnaryExpressionSyntax pre && pre.Kind == SyntaxKind.IndexExpression)
            {
                var value = BindExpression(pre.Operand, context, diagnostics);
                value = ApplyConversion(
                    exprSyntax: pre.Operand,
                    expr: value,
                    targetType: intType,
                    diagnosticNode: syntax,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                Optional<object> constantIndexOpt = Optional<object>.None;
                if (!value.HasErrors && TryGetInt32Constant(value, out int fromEndOffset))
                {
                    long actualIndex = (long)info.Length - fromEndOffset;
                    if (!ValidateInlineArrayConstantIndex(
                        argSyntax,
                        actualIndex,
                        info.Length,
                        context,
                        diagnostics))
                    {
                        return new BoundBadExpression(syntax);
                    }

                    constantIndexOpt = new Optional<object>((int)actualIndex);
                }

                index = new BoundBinaryExpression(
                    syntax,
                    BoundBinaryOperatorKind.Subtract,
                    intType,
                    new BoundLiteralExpression(syntax, intType, info.Length),
                    value,
                    constantIndexOpt,
                    isChecked: false);
            }
            else
            {
                index = BindExpression(argSyntax, context, diagnostics);
                index = ApplyConversion(
                    exprSyntax: argSyntax,
                    expr: index,
                    targetType: intType,
                    diagnosticNode: syntax,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (!index.HasErrors && TryGetInt32Constant(index, out int constantIndex) &&
                    !ValidateInlineArrayConstantIndex(
                        argSyntax,
                        constantIndex,
                        info.Length,
                        context,
                        diagnostics))
                {
                    return new BoundBadExpression(syntax);
                }
            }

            if (index.HasErrors)
                return new BoundBadExpression(syntax);

            if (valueKind == BindValueKind.LValue)
            {
                if (!receiver.IsLValue)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_INLINEARRAY012",
                        DiagnosticSeverity.Error,
                        "Cannot assign to an inline array element because the receiver is not a variable.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    return new BoundBadExpression(syntax);
                }

                if (IsReadOnlyValueReceiver(receiver, context))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_INLINEARRAY013",
                        DiagnosticSeverity.Error,
                        "Cannot assign to an inline array element through a readonly receiver.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    return new BoundBadExpression(syntax);
                }
            }

            return new BoundInlineArrayElementAccessExpression(
                syntax,
                receiver,
                info.ElementField,
                index,
                info.Length,
                isLValue: receiver.IsLValue);
        }

        private static bool ValidateInlineArrayConstantIndex(
            SyntaxNode diagnosticNode,
            long index,
            int length,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (length > 0 && (ulong)index < (uint)length)
                return true;

            diagnostics.Add(new Diagnostic(
                "CN_INLINEARRAY014",
                DiagnosticSeverity.Error,
                "Index is outside the bounds of the inline array.",
                new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
            return false;
        }

        private static bool TryGetInt32Constant(BoundExpression expression, out int value)
        {
            value = 0;
            if (!expression.ConstantValueOpt.HasValue || expression.ConstantValueOpt.Value is null)
                return false;

            switch (expression.ConstantValueOpt.Value)
            {
                case int i:
                    value = i;
                    return true;
                case sbyte i:
                    value = i;
                    return true;
                case byte i:
                    value = i;
                    return true;
                case short i:
                    value = i;
                    return true;
                case ushort i:
                    value = i;
                    return true;
            }

            return false;
        }

        private BoundExpression BindElementOnBoundReceiver(
            ExpressionSyntax syntax,
            BoundExpression receiver,
            BracketedArgumentListSyntax argumentList,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (argumentList.Arguments.Count == 1 && argumentList.Arguments[0].Expression is RangeExpressionSyntax)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SLICE000",
                    DiagnosticSeverity.Error,
                    valueKind == BindValueKind.LValue ? "Cannot assign to a slice." : "Conditional slicing is not implemented.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            if (receiver.Type is ArrayTypeSymbol arrayType)
            {
                if (argumentList.Arguments.Count != arrayType.Rank)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ELEM002",
                        DiagnosticSeverity.Error,
                        arrayType.Rank == 1
                            ? "Array element access expects exactly one index argument."
                            : $"Array element access expects exactly {arrayType.Rank} index arguments.",
                        new Location(context.SemanticModel.SyntaxTree, argumentList.Span)));
                    return new BoundBadExpression(syntax);
                }

                var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                var indices = ImmutableArray.CreateBuilder<BoundExpression>(argumentList.Arguments.Count);
                for (int i = 0; i < argumentList.Arguments.Count; i++)
                {
                    var arg = argumentList.Arguments[i];
                    var index = BindExpression(arg.Expression, context, diagnostics);
                    index = ApplyConversion(arg.Expression, index, intType, syntax, context, diagnostics, requireImplicit: true);
                    indices.Add(index);
                }

                return new BoundArrayElementAccessExpression(syntax, arrayType.ElementType, receiver, indices.ToImmutable());
            }

            if (InlineArrayFacts.TryGetInfo(receiver.Type, out var inlineArray))
                return BindInlineArrayElementAccess(syntax, receiver, argumentList, valueKind, context, diagnostics, inlineArray);

            var receiverType = GetReceiverTypeForMemberLookup(receiver.Type);
            if (receiverType is not null)
            {
                var indexers = FilterAccessibleIndexers(LookupIndexers(receiverType), context)
                    .Where(p => !p.IsStatic)
                    .ToImmutableArray();

                if (!indexers.IsDefaultOrEmpty)
                {
                    var rawArgs = ImmutableArray.CreateBuilder<BoundExpression>(argumentList.Arguments.Count);
                    bool hasArgErrors = false;
                    for (int i = 0; i < argumentList.Arguments.Count; i++)
                    {
                        var arg = BindExpression(argumentList.Arguments[i].Expression, context, diagnostics);
                        rawArgs.Add(arg);
                        if (arg.HasErrors)
                            hasArgErrors = true;
                    }

                    if (hasArgErrors)
                        return new BoundBadExpression(syntax);

                    var fakeSyntax = new ElementAccessExpressionSyntax((ExpressionSyntax)receiver.Syntax, argumentList);
                    if (!TryResolveIndexerOverload(indexers, fakeSyntax, rawArgs.ToImmutable(), context, diagnostics,
                            out var chosenIndexer, out var convertedArgs))
                    {
                        return new BoundBadExpression(syntax);
                    }

                    bool canReadIndexer = chosenIndexer!.GetMethod is not null && AccessibilityHelper.IsAccessible(chosenIndexer.GetMethod, context);
                    bool canWriteIndexer = chosenIndexer.SetMethod is not null && AccessibilityHelper.IsAccessible(chosenIndexer.SetMethod, context);
                    bool isRefReturnIndexer = chosenIndexer.GetMethod is MethodSymbol getMethod && getMethod.ReturnType is ByRefTypeSymbol;

                    if (valueKind == BindValueKind.RValue && !canReadIndexer)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC024",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible getter.",
                            new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                        return new BoundBadExpression(syntax);
                    }

                    if (valueKind == BindValueKind.LValue && isRefReturnIndexer && !canReadIndexer)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC024",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible getter.",
                            new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                        return new BoundBadExpression(syntax);
                    }

                    if (valueKind == BindValueKind.LValue && IsReadOnlyValueReceiver(receiver, context))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_READONLY_THIS002",
                            DiagnosticSeverity.Error,
                            "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                            new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                        return new BoundBadExpression(syntax);
                    }

                    bool allowCtorAutoPropWrite =
                        valueKind == BindValueKind.LValue &&
                        !canWriteIndexer &&
                        CanAssignReadOnlyAutoPropertyInConstructor(chosenIndexer, receiver, context);

                    if (valueKind == BindValueKind.LValue &&
                        !canWriteIndexer &&
                        !allowCtorAutoPropWrite &&
                        !(isRefReturnIndexer && canReadIndexer))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC025",
                            DiagnosticSeverity.Error,
                            "Indexer has no accessible setter.",
                            new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                        return new BoundBadExpression(syntax);
                    }

                    return new BoundIndexerAccessExpression(
                        syntax,
                        receiver,
                        chosenIndexer,
                        convertedArgs,
                        isLValue: canWriteIndexer || allowCtorAutoPropWrite || (isRefReturnIndexer && canReadIndexer));
                }
            }

            diagnostics.Add(new Diagnostic(
                "CN_ELEM000",
                DiagnosticSeverity.Error,
                "Element access is not implemented for this type.",
                new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
            return new BoundBadExpression(syntax);
        }

    }
}
