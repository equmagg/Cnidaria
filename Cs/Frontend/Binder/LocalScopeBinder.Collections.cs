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
        private bool CanBindTargetTypedCollectionExpression(
            BoundUnboundCollectionExpression unbound,
            TypeSymbol targetType,
            BindingContext context)
        {
            ArrayTypeSymbol? spreadTargetArray = null;
            TypeSymbol? spreadElementType = null;

            if (TryGetArrayLikeCollectionTarget(targetType, context.Compilation, out var concreteArrayType, out var arrayElementType))
            {
                spreadTargetArray = concreteArrayType;
                spreadElementType = arrayElementType;
            }
            else if (TryGetSpanLikeElementType(targetType, out _, out var spanElementType))
            {
                spreadElementType = spanElementType;
                spreadTargetArray = context.Compilation.CreateArrayType(spanElementType, rank: 1);
            }
            else if (TryGetExactListElementType(targetType, out _, out var listElementType))
            {
                spreadElementType = listElementType;
                spreadTargetArray = context.Compilation.CreateArrayType(listElementType, rank: 1);
            }

            if (spreadTargetArray is not null && spreadElementType is not null)
            {
                for (int i = 0; i < unbound.Elements.Length; i++)
                {
                    var element = unbound.Elements[i];
                    if (element.Kind == BoundCollectionElementKind.Expression)
                    {
                        var conv = ClassifyConversion(element.Expression, spreadElementType, context);
                        if (!conv.Exists || !conv.IsImplicit)
                            return false;
                        continue;
                    }

                    var spreadType = element.Expression.Type;
                    if (element.Expression is BoundUnboundCollectionExpression nestedCollection)
                    {
                        if (!CanBindTargetTypedCollectionExpression(nestedCollection, spreadTargetArray, context))
                            return false;
                        continue;
                    }

                    if (spreadType is not ArrayTypeSymbol spreadArray || spreadArray.Rank != 1)
                        return false;
                    if (!ReferenceEquals(spreadArray.ElementType, spreadElementType))
                        return false;
                }
                return true;
            }

            if (targetType is not NamedTypeSymbol nt)
                return false;
            if (nt.IsRefLikeType)
                return false;

            bool hasSpread = false;
            for (int i = 0; i < unbound.Elements.Length; i++)
                hasSpread |= unbound.Elements[i].Kind == BoundCollectionElementKind.Spread;

            if (hasSpread)
                return false;

            var allCtorCandidates = LookupConstructors(nt);
            var ctorCandidates = allCtorCandidates
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();

            bool hasCtor = false;
            if (nt.TypeKind == TypeKind.Struct)
            {
                hasCtor = true;
            }
            else
            {
                for (int i = 0; i < ctorCandidates.Length; i++)
                {
                    if (ctorCandidates[i].Parameters.Length == 0)
                    {
                        hasCtor = true;
                        break;
                    }
                }
            }

            if (!hasCtor)
                return false;

            for (int i = 0; i < unbound.Elements.Length; i++)
            {
                if (unbound.Elements[i].Kind != BoundCollectionElementKind.Expression)
                    return false;

                if (TryResolveCollectionInitializerAddCall(
                    elementSyntax: ((ExpressionElementSyntax)unbound.Elements[i].Syntax).Expression,
                    receiverType: nt,
                    receiverSyntax: (ExpressionSyntax)unbound.Syntax,
                    receiverOpt: null,
                    argumentExpressions: ImmutableArray.Create(unbound.Elements[i].Expression),
                    context: context,
                    diagnostics: null,
                    addCall: out _,
                    reportDiagnostics: false))
                {
                    continue;
                }

                return false;
            }

            return true;
        }
        private BoundExpression BindCollectionExpression(
            CollectionExpressionSyntax node,
            TypeSymbol targetType,
            BoundUnboundCollectionExpression unbound,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (TryGetSpanLikeElementType(targetType, out var spanLikeType, out var spanElementType))
            {
                bool hasSpread = false;
                for (int i = 0; i < unbound.Elements.Length; i++)
                    hasSpread |= unbound.Elements[i].Kind == BoundCollectionElementKind.Spread;
                if (!hasSpread)
                    return BindCollectionExpressionAsSpanCollection(node, spanLikeType, spanElementType, unbound, context, diagnostics);
                var spanBackingArray = context.Compilation.CreateArrayType(spanElementType, rank: 1);

                var arrayExpr = BindCollectionExpressionAsArray(node, spanBackingArray, spanElementType, unbound, context, diagnostics);
                if (arrayExpr.HasErrors)
                    return arrayExpr;

                var conv = ClassifyConversion(arrayExpr, targetType, context);
                if (!conv.Exists || !conv.IsImplicit)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COLL002",
                        DiagnosticSeverity.Error,
                        $"No implicit conversion from '{arrayExpr.Type.Name}' to '{targetType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    var bad = new BoundBadExpression(node);
                    bad.SetType(targetType);
                    return bad;
                }

                return ApplyConversion(
                    exprSyntax: node,
                    expr: arrayExpr,
                    targetType: targetType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            if (TryGetArrayLikeCollectionTarget(targetType, context.Compilation, out var concreteArrayType, out var arrayElementType))
            {
                var arrayExpr = BindCollectionExpressionAsArray(node, concreteArrayType, arrayElementType, unbound, context, diagnostics);
                if (arrayExpr.HasErrors)
                    return arrayExpr;

                if (ReferenceEquals(arrayExpr.Type, targetType))
                    return arrayExpr;

                var conv = ClassifyConversion(arrayExpr, targetType, context);
                if (!conv.Exists || !conv.IsImplicit)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COLL003",
                        DiagnosticSeverity.Error,
                        $"No implicit conversion from '{arrayExpr.Type.Name}' to '{targetType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    var bad = new BoundBadExpression(node);
                    bad.SetType(targetType);
                    return bad;
                }

                return ApplyConversion(
                    exprSyntax: node,
                    expr: arrayExpr,
                    targetType: targetType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
            }

            if (TryGetExactListElementType(targetType, out var listType, out var listElementType))
                return BindCollectionExpressionAsListSpecialCase(node, listType, listElementType, unbound, context, diagnostics);

            if (targetType is NamedTypeSymbol namedTarget)
                return BindCollectionExpressionAsConstructibleCollection(node, namedTarget, unbound, context, diagnostics);

            diagnostics.Add(new Diagnostic(
                "CN_COLL004",
                DiagnosticSeverity.Error,
                $"No collection expression conversion exists from '[...]' to '{targetType.Name}'.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));
            var badExpr = new BoundBadExpression(node);
            badExpr.SetType(targetType);
            return badExpr;

        }

        private bool TryGetArrayLikeCollectionTarget(
            TypeSymbol targetType,
            Compilation compilation,
            out ArrayTypeSymbol concreteArrayType,
            out TypeSymbol elementType)
        {
            if (targetType is ArrayTypeSymbol at && at.Rank == 1)
            {
                concreteArrayType = at;
                elementType = at.ElementType;
                return true;
            }

            if (targetType is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface && nt.TypeArguments.Length == 1 &&
                IsSupportedArrayLikeInterface(nt.OriginalDefinition))
            {
                elementType = nt.TypeArguments[0];
                concreteArrayType = compilation.CreateArrayType(elementType, rank: 1);
                return true;
            }

            concreteArrayType = null!;
            elementType = null!;
            return false;
        }
        private static string GetNamespaceFullName(NamespaceSymbol ns)
        {
            if (ns.IsGlobalNamespace)
                return string.Empty;

            var parts = new Stack<string>();
            Symbol? cur = ns;
            while (cur is NamespaceSymbol curNs && !curNs.IsGlobalNamespace)
            {
                parts.Push(curNs.Name);
                cur = curNs.ContainingSymbol;
            }

            return string.Join(".", parts);
        }
        private static bool IsSupportedArrayLikeInterface(NamedTypeSymbol type)
        {
            if (type.ContainingSymbol is not NamespaceSymbol ns)
                return false;


            var fullNs = GetNamespaceFullName(ns);
            if (!string.Equals(fullNs, "System.Collections.Generic", StringComparison.Ordinal))
                return false;

            return type.Name switch
            {
                "IEnumerable" => true,
                "ICollection" => true,
                "IReadOnlyCollection" => true,
                "IReadOnlyList" => true,
                "IList" => true,
                _ => false,
            };
        }
        private static bool TryGetExactListElementType(TypeSymbol type, out NamedTypeSymbol listType, out TypeSymbol elementType)
        {
            listType = null!;
            elementType = null!;

            if (type is not NamedTypeSymbol nt)
                return false;

            var def = nt.OriginalDefinition;
            if (def.Arity != 1 || !string.Equals(def.Name, "List", StringComparison.Ordinal))
                return false;

            if (def.ContainingSymbol is not NamespaceSymbol ns ||
                !string.Equals(GetNamespaceFullName(ns), "System.Collections.Generic", StringComparison.Ordinal))
            {
                return false;
            }

            listType = nt;
            var args = nt.TypeArguments;
            elementType = args.Length == 1 ? args[0] : def.TypeParameters[0];
            return true;
        }
        private static NamedTypeSymbol? LookupTypeByMetadataName(
            Compilation compilation,
            string namespaceName,
            string typeName,
            int arity)
        {
            NamespaceSymbol current = compilation.GlobalNamespace;
            if (!string.IsNullOrEmpty(namespaceName))
            {
                var parts = namespaceName.Split('.');
                for (int i = 0; i < parts.Length; i++)
                {
                    NamespaceSymbol? next = null;
                    var children = current.GetNamespaceMembers();
                    for (int j = 0; j < children.Length; j++)
                    {
                        if (string.Equals(children[j].Name, parts[i], StringComparison.Ordinal))
                        {
                            next = children[j];
                            break;
                        }
                    }

                    if (next is null)
                        return null;

                    current = next;
                }
            }

            var types = current.GetTypeMembers(typeName, arity);
            return types.IsDefaultOrEmpty ? null : types[0];
        }
        private bool TryFindCollectionsMarshalSetCountMethod(
            Compilation compilation,
            NamedTypeSymbol listType,
            TypeSymbol elementType,
            out MethodSymbol method)
        {
            method = null!;
            var marshalType = LookupTypeByMetadataName(compilation, "System.Runtime.InteropServices", "CollectionsMarshal", 0);
            if (marshalType is null)
                return false;

            var int32 = compilation.GetSpecialType(SpecialType.System_Int32);
            foreach (var member in marshalType.GetMembers())
            {
                if (member is not MethodSymbol ms || !ms.IsStatic || ms.IsConstructor)
                    continue;
                if (!string.Equals(ms.Name, "SetCount", StringComparison.Ordinal))
                    continue;
                if (ms.TypeParameters.Length != 1)
                    continue;

                var constructed = new ConstructedMethodSymbol(ms, ImmutableArray.Create(elementType), compilation.TypeManager);
                if (constructed.Parameters.Length != 2)
                    continue;
                if (!ReferenceEquals(constructed.Parameters[0].Type, listType))
                    continue;
                if (!ReferenceEquals(constructed.Parameters[1].Type, int32))
                    continue;

                method = constructed;
                return true;
            }

            return false;
        }
        private bool TryFindCollectionsMarshalAsSpanMethod(
            Compilation compilation,
            NamedTypeSymbol listType,
            TypeSymbol elementType,
            out MethodSymbol method,
            out NamedTypeSymbol spanType)
        {
            method = null!;
            spanType = null!;

            var marshalType = LookupTypeByMetadataName(compilation, "System.Runtime.InteropServices", "CollectionsMarshal", 0);
            if (marshalType is null)
                return false;

            foreach (var member in marshalType.GetMembers())
            {
                if (member is not MethodSymbol ms || !ms.IsStatic || ms.IsConstructor)
                    continue;
                if (!string.Equals(ms.Name, "AsSpan", StringComparison.Ordinal))
                    continue;
                if (ms.TypeParameters.Length != 1)
                    continue;

                var constructed = new ConstructedMethodSymbol(ms, ImmutableArray.Create(elementType), compilation.TypeManager);
                if (constructed.Parameters.Length != 1)
                    continue;
                if (!ReferenceEquals(constructed.Parameters[0].Type, listType))
                    continue;
                if (!TryGetSpanLikeElementType(constructed.ReturnType, out var retSpanType, out var retElementType))
                    continue;
                if (!ReferenceEquals(retElementType, elementType))
                    continue;

                method = constructed;
                spanType = retSpanType;
                return true;
            }

            return false;
        }
        private static bool TryFindIntIndexer(
            NamedTypeSymbol receiverType,
            TypeSymbol intType,
            TypeSymbol elementType,
            bool requireWritable,
            out PropertySymbol indexer)
        {
            indexer = null!;
            var members = LookupMembers(receiverType, "Item");
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is not PropertySymbol prop || prop.IsStatic)
                    continue;
                if (prop.Parameters.Length != 1)
                    continue;
                if (!ReferenceEquals(prop.Parameters[0].Type, intType))
                    continue;

                var propElementType = prop.Type is ByRefTypeSymbol br ? br.ElementType : prop.Type;
                if (!ReferenceEquals(propElementType, elementType))
                    continue;
                if (requireWritable && !prop.HasSet && prop.Type is not ByRefTypeSymbol)
                    continue;

                indexer = prop;
                return true;
            }

            return false;
        }
        private bool TryFindIntConstructor(
            NamedTypeSymbol type,
            TypeSymbol intType,
            BindingContext context,
            out MethodSymbol ctor)
        {
            ctor = null!;
            var ctors = LookupConstructors(type)
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();

            for (int i = 0; i < ctors.Length; i++)
            {
                var candidate = ctors[i];
                if (candidate.Parameters.Length != 1)
                    continue;
                if (!ReferenceEquals(candidate.Parameters[0].Type, intType))
                    continue;

                ctor = candidate;
                return true;
            }

            return false;
        }
        private BoundExpression BindCollectionExpressionAsSpanCollection(
            CollectionExpressionSyntax node,
            NamedTypeSymbol spanLikeType,
            TypeSymbol elementType,
            BoundUnboundCollectionExpression unbound,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var converted = ImmutableArray.CreateBuilder<BoundExpression>(unbound.Elements.Length);

            for (int i = 0; i < unbound.Elements.Length; i++)
            {
                var element = unbound.Elements[i];
                if (element.Kind != BoundCollectionElementKind.Expression)
                    throw new InvalidOperationException("Spread element reached span collection fast path.");

                var exprSyntax = ((ExpressionElementSyntax)element.Syntax).Expression;
                converted.Add(ApplyConversion(
                    exprSyntax: exprSyntax,
                    expr: element.Expression,
                    targetType: elementType,
                    diagnosticNode: element.Syntax,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true));
            }

            return new BoundSpanCollectionExpression(
                node,
                spanLikeType,
                elementType,
                converted.ToImmutable());
        }
        private BoundExpression BindCollectionExpressionAsListSpecialCase(
            CollectionExpressionSyntax node,
            NamedTypeSymbol targetType,
            TypeSymbol elementType,
            BoundUnboundCollectionExpression unbound,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);

            if (!TryFindCollectionsMarshalSetCountMethod(context.Compilation, targetType, elementType, out var setCountMethod) ||
                !TryFindCollectionsMarshalAsSpanMethod(context.Compilation, targetType, elementType, out var asSpanMethod, out var spanType) ||
                !TryFindIntIndexer(spanType, int32, elementType, requireWritable: true, out var spanIndexer))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COLL011",
                    DiagnosticSeverity.Error,
                    "Missing required CollectionsMarshal helpers for List<T> collection expression lowering.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                var bad = new BoundBadExpression(node);
                bad.SetType(targetType);
                return bad;
            }

            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();
            var captured = new List<(BoundCollectionElementKind Kind, SyntaxNode Syntax, LocalSymbol Local)>();

            for (int i = 0; i < unbound.Elements.Length; i++)
            {
                var element = unbound.Elements[i];
                if (element.Kind == BoundCollectionElementKind.Expression)
                {
                    var exprSyntax = ((ExpressionElementSyntax)element.Syntax).Expression;
                    var converted = ApplyConversion(
                        exprSyntax: exprSyntax,
                        expr: element.Expression,
                        targetType: elementType,
                        diagnosticNode: element.Syntax,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    var temp = NewTemp($"$coll_elem{i}", elementType);
                    locals.Add(temp);
                    sideEffects.Add(new BoundLocalDeclarationStatement(element.Syntax, temp, converted));
                    captured.Add((element.Kind, element.Syntax, temp));
                    continue;
                }

                var spreadSyntax = (SpreadElementSyntax)element.Syntax;
                BoundExpression spreadExpr = element.Expression;
                var spreadArrayType = context.Compilation.CreateArrayType(elementType, rank: 1);
                if (spreadExpr is BoundUnboundCollectionExpression nestedCollection)
                    spreadExpr = BindCollectionExpression(spreadSyntax.Expression as CollectionExpressionSyntax ?? node, spreadArrayType, nestedCollection, context, diagnostics);

                if (spreadExpr.Type is not ArrayTypeSymbol spreadArray || spreadArray.Rank != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COLL012",
                        DiagnosticSeverity.Error,
                        "Spread element must be a single-dimensional array in the current List<T>-targeted implementation.",
                        new Location(context.SemanticModel.SyntaxTree, spreadSyntax.Expression.Span)));
                    var bad = new BoundBadExpression(node);
                    bad.SetType(targetType);
                    return bad;
                }
                if (!ReferenceEquals(spreadArray.ElementType, elementType))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COLL013",
                        DiagnosticSeverity.Error,
                        $"Spread element array has element type '{spreadArray.ElementType.Name}', expected '{elementType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, spreadSyntax.Expression.Span)));
                    var bad = new BoundBadExpression(node);
                    bad.SetType(targetType);
                    return bad;
                }

                var tempSpread = NewTemp($"$coll_spread{i}", spreadArrayType);
                locals.Add(tempSpread);
                sideEffects.Add(new BoundLocalDeclarationStatement(spreadSyntax, tempSpread, spreadExpr));
                captured.Add((element.Kind, spreadSyntax, tempSpread));
            }

            BoundExpression totalCount = new BoundLiteralExpression(node, int32, 0);
            for (int i = 0; i < captured.Count; i++)
            {
                BoundExpression addend;
                if (captured[i].Kind == BoundCollectionElementKind.Expression)
                {
                    addend = new BoundLiteralExpression(captured[i].Syntax, int32, 1);
                }
                else
                {
                    addend = CreateArrayLengthAccess(captured[i].Syntax, new BoundLocalExpression(captured[i].Syntax, captured[i].Local), int32, context, diagnostics);
                    if (addend.HasErrors)
                    {
                        var bad = new BoundBadExpression(node);
                        bad.SetType(targetType);
                        return bad;
                    }
                }

                totalCount = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.Add,
                    int32,
                    totalCount,
                    addend,
                    Optional<object>.None,
                    isChecked: false);
            }

            BoundExpression created;
            if (TryFindIntConstructor(targetType, int32, context, out var capacityCtor))
            {
                created = new BoundObjectCreationExpression(node, targetType, capacityCtor, ImmutableArray.Create(totalCount));
            }
            else
            {
                var emptyArgs = SeparatedSyntaxList<ArgumentSyntax>.Empty;
                created = BindObjectCreationCoreFromBoundArgs(
                    syntax: node,
                    type: targetType,
                    argSyntaxes: emptyArgs,
                    boundArgs: ImmutableArray<BoundExpression>.Empty,
                    diagnosticSpan: node.Span,
                    context: context,
                    diagnostics: diagnostics);
            }

            if (created.HasErrors)
                return created;

            var listTemp = NewTemp("$coll_list", targetType);
            locals.Add(listTemp);
            var listExpr = new BoundLocalExpression(node, listTemp);
            sideEffects.Add(new BoundLocalDeclarationStatement(node, listTemp, created));

            sideEffects.Add(new BoundExpressionStatement(
                node,
                new BoundCallExpression(
                    node,
                    receiverOpt: null,
                    method: setCountMethod,
                    arguments: ImmutableArray.Create<BoundExpression>(listExpr, totalCount))));

            var spanTemp = NewTemp("$coll_span", spanType);
            locals.Add(spanTemp);
            var spanExpr = new BoundLocalExpression(node, spanTemp);
            sideEffects.Add(new BoundLocalDeclarationStatement(
                node,
                spanTemp,
                new BoundCallExpression(
                    node,
                    receiverOpt: null,
                    method: asSpanMethod,
                    arguments: ImmutableArray.Create<BoundExpression>(listExpr))));

            var zero = new BoundLiteralExpression(node, int32, 0);
            var one = new BoundLiteralExpression(node, int32, 1);
            var indexTemp = NewTemp("$coll_idx", int32);
            locals.Add(indexTemp);
            sideEffects.Add(new BoundLocalDeclarationStatement(node, indexTemp, zero));

            for (int i = 0; i < captured.Count; i++)
            {
                var capturedItem = captured[i];
                if (capturedItem.Kind == BoundCollectionElementKind.Expression)
                {
                    var valueRead = new BoundLocalExpression(capturedItem.Syntax, capturedItem.Local);
                    var indexRead = new BoundLocalExpression(capturedItem.Syntax, indexTemp);
                    var indexerSyntax = capturedItem.Syntax as ExpressionSyntax ?? node;
                    var left1 = new BoundIndexerAccessExpression(
                        indexerSyntax,
                        spanExpr,
                        spanIndexer,
                        ImmutableArray.Create<BoundExpression>(indexRead),
                        isLValue: true);
                    sideEffects.Add(new BoundExpressionStatement(
                        capturedItem.Syntax,
                        new BoundAssignmentExpression(capturedItem.Syntax, left1, valueRead)));

                    sideEffects.Add(new BoundExpressionStatement(
                        capturedItem.Syntax,
                        new BoundAssignmentExpression(
                            capturedItem.Syntax,
                            new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                            new BoundBinaryExpression(
                                capturedItem.Syntax,
                                BoundBinaryOperatorKind.Add,
                                int32,
                                new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                                one,
                                Optional<object>.None,
                                isChecked: false))));
                    continue;
                }

                var spreadArrayExpr = new BoundLocalExpression(capturedItem.Syntax, capturedItem.Local);
                var spreadLen = CreateArrayLengthAccess(capturedItem.Syntax, spreadArrayExpr, int32, context, diagnostics);
                if (spreadLen.HasErrors)
                {
                    var bad = new BoundBadExpression(node);
                    bad.SetType(targetType);
                    return bad;
                }

                var srcIndexTemp = NewTemp($"$coll_srcidx{i}", int32);
                locals.Add(srcIndexTemp);
                sideEffects.Add(new BoundLocalDeclarationStatement(capturedItem.Syntax, srcIndexTemp, zero));

                var checkLabel = _flow.NewGeneratedLabel("coll_copy_check");
                var doneLabel = _flow.NewGeneratedLabel("coll_copy_done");

                sideEffects.Add(new BoundLabelStatement(capturedItem.Syntax, checkLabel));
                sideEffects.Add(new BoundConditionalGotoStatement(
                    capturedItem.Syntax,
                    new BoundBinaryExpression(
                        capturedItem.Syntax,
                        BoundBinaryOperatorKind.LessThan,
                        boolType,
                        new BoundLocalExpression(capturedItem.Syntax, srcIndexTemp),
                        spreadLen,
                        Optional<object>.None),
                    doneLabel,
                    jumpIfTrue: false));

                var right = new BoundArrayElementAccessExpression(
                    capturedItem.Syntax,
                    elementType,
                    spreadArrayExpr,
                    new BoundLocalExpression(capturedItem.Syntax, srcIndexTemp));
                var left = new BoundIndexerAccessExpression(
                    node,
                    spanExpr,
                    spanIndexer,
                    ImmutableArray.Create<BoundExpression>(new BoundLocalExpression(capturedItem.Syntax, indexTemp)),
                    isLValue: true);

                sideEffects.Add(new BoundExpressionStatement(
                    capturedItem.Syntax,
                    new BoundAssignmentExpression(capturedItem.Syntax, left, right)));

                sideEffects.Add(new BoundExpressionStatement(
                    capturedItem.Syntax,
                    new BoundAssignmentExpression(
                        capturedItem.Syntax,
                        new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                        new BoundBinaryExpression(
                            capturedItem.Syntax,
                            BoundBinaryOperatorKind.Add,
                            int32,
                            new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                            one,
                            Optional<object>.None,
                            isChecked: false))));

                sideEffects.Add(new BoundExpressionStatement(
                    capturedItem.Syntax,
                    new BoundAssignmentExpression(
                        capturedItem.Syntax,
                        new BoundLocalExpression(capturedItem.Syntax, srcIndexTemp),
                        new BoundBinaryExpression(
                            capturedItem.Syntax,
                            BoundBinaryOperatorKind.Add,
                            int32,
                            new BoundLocalExpression(capturedItem.Syntax, srcIndexTemp),
                            one,
                            Optional<object>.None,
                            isChecked: false))));

                sideEffects.Add(new BoundGotoStatement(capturedItem.Syntax, checkLabel));
                sideEffects.Add(new BoundLabelStatement(capturedItem.Syntax, doneLabel));
            }
            return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), listExpr);
        }
        private BoundExpression BindCollectionExpressionAsConstructibleCollection(
            CollectionExpressionSyntax node,
            NamedTypeSymbol targetType,
            BoundUnboundCollectionExpression unbound,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            bool hasSpread = false;
            for (int i = 0; i < unbound.Elements.Length; i++)
                hasSpread |= unbound.Elements[i].Kind == BoundCollectionElementKind.Spread;

            if (hasSpread)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COLL005",
                    DiagnosticSeverity.Error,
                    "Spread elements are currently only supported when the collection expression is bound to an array target.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                var bad = new BoundBadExpression(node);
                bad.SetType(targetType);
                return bad;
            }

            var emptyArgs = SeparatedSyntaxList<ArgumentSyntax>.Empty;
            BoundExpression created = BindObjectCreationCoreFromBoundArgs(
                syntax: node,
                type: targetType,
                argSyntaxes: emptyArgs,
                boundArgs: ImmutableArray<BoundExpression>.Empty,
                diagnosticSpan: node.Span,
                context: context,
                diagnostics: diagnostics);

            if (created.HasErrors)
                return created;

            var temp = NewTemp("$coll", targetType);
            var tempExpr = new BoundLocalExpression(node, temp);
            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();
            locals.Add(temp);
            sideEffects.Add(new BoundLocalDeclarationStatement(node, temp, created));

            for (int i = 0; i < unbound.Elements.Length; i++)
            {
                var element = unbound.Elements[i];
                if (element.Kind != BoundCollectionElementKind.Expression)
                    continue;

                var exprElement = (ExpressionElementSyntax)element.Syntax;
                if (!TryResolveCollectionInitializerAddCall(
                    elementSyntax: exprElement.Expression,
                    receiverType: targetType,
                    receiverSyntax: node,
                    receiverOpt: tempExpr,
                    argumentExpressions: ImmutableArray.Create(element.Expression),
                    context: context,
                    diagnostics: diagnostics,
                    addCall: out var addCall,
                    reportDiagnostics: true))
                {
                    var bad = new BoundBadExpression(node);
                    bad.SetType(targetType);
                    return bad;
                }

                sideEffects.Add(new BoundExpressionStatement(exprElement, addCall!));
            }

            return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), tempExpr);
        }
        private BoundExpression BindCollectionExpressionAsArray(
            CollectionExpressionSyntax node,
            ArrayTypeSymbol arrayType,
            TypeSymbol elementType,
            BoundUnboundCollectionExpression unbound,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            bool hasSpread = false;
            for (int i = 0; i < unbound.Elements.Length; i++)
                hasSpread |= unbound.Elements[i].Kind == BoundCollectionElementKind.Spread;

            if (!hasSpread)
            {
                var converted = ImmutableArray.CreateBuilder<BoundExpression>(unbound.Elements.Length);
                for (int i = 0; i < unbound.Elements.Length; i++)
                {
                    var exprElement = unbound.Elements[i];
                    if (exprElement.Kind != BoundCollectionElementKind.Expression)
                        continue;

                    var exprSyntax = ((ExpressionElementSyntax)exprElement.Syntax).Expression;
                    converted.Add(ApplyConversion(
                        exprSyntax: exprSyntax,
                        expr: exprElement.Expression,
                        targetType: elementType,
                        diagnosticNode: exprElement.Syntax,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true));
                }

                var init = new BoundArrayInitializerExpression(node, elementType, converted.ToImmutable());
                var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                var count = new BoundLiteralExpression(node, intType, converted.Count);
                return new BoundArrayCreationExpression(node, arrayType, elementType, count, init);
            }

            var int32 = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();
            var captured = new List<(BoundCollectionElementKind Kind, SyntaxNode Syntax, LocalSymbol Local)>();

            for (int i = 0; i < unbound.Elements.Length; i++)
            {
                var element = unbound.Elements[i];
                if (element.Kind == BoundCollectionElementKind.Expression)
                {
                    var exprSyntax = ((ExpressionElementSyntax)element.Syntax).Expression;
                    var converted = ApplyConversion(
                        exprSyntax: exprSyntax,
                        expr: element.Expression,
                        targetType: elementType,
                        diagnosticNode: element.Syntax,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    var temp = NewTemp($"$coll_elem{i}", elementType);
                    locals.Add(temp);
                    sideEffects.Add(new BoundLocalDeclarationStatement(element.Syntax, temp, converted));
                    captured.Add((element.Kind, element.Syntax, temp));
                    continue;
                }

                var spreadSyntax = (SpreadElementSyntax)element.Syntax;
                BoundExpression spreadExpr = element.Expression;
                if (spreadExpr is BoundUnboundCollectionExpression nestedCollection)
                    spreadExpr = BindCollectionExpression(spreadSyntax.Expression as CollectionExpressionSyntax ?? node, arrayType, nestedCollection, context, diagnostics);

                if (spreadExpr.Type is not ArrayTypeSymbol spreadArray || spreadArray.Rank != 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COLL006",
                        DiagnosticSeverity.Error,
                        "Spread element must be a single-dimensional array in the current array-targeted implementation.",
                        new Location(context.SemanticModel.SyntaxTree, spreadSyntax.Expression.Span)));
                    var bad = new BoundBadExpression(node);
                    bad.SetType(arrayType);
                    return bad;
                }
                if (!ReferenceEquals(spreadArray.ElementType, elementType))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COLL007",
                        DiagnosticSeverity.Error,
                        $"Spread element array has element type '{spreadArray.ElementType.Name}', expected '{elementType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, spreadSyntax.Expression.Span)));
                    var bad = new BoundBadExpression(node);
                    bad.SetType(arrayType);
                    return bad;
                }
                {
                    var temp = NewTemp($"$coll_spread{i}", spreadArray);
                    locals.Add(temp);
                    sideEffects.Add(new BoundLocalDeclarationStatement(spreadSyntax, temp, spreadExpr));
                    captured.Add((element.Kind, spreadSyntax, temp));
                }
            }

            BoundExpression totalCount = new BoundLiteralExpression(node, int32, 0);
            for (int i = 0; i < captured.Count; i++)
            {
                BoundExpression addend;
                if (captured[i].Kind == BoundCollectionElementKind.Expression)
                {
                    addend = new BoundLiteralExpression(captured[i].Syntax, int32, 1);
                }
                else
                {
                    addend = CreateArrayLengthAccess(captured[i].Syntax, new BoundLocalExpression(captured[i].Syntax, captured[i].Local), int32, context, diagnostics);
                    if (addend.HasErrors)
                    {
                        var bad = new BoundBadExpression(node);
                        bad.SetType(arrayType);
                        return bad;
                    }
                }

                totalCount = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.Add,
                    int32,
                    totalCount,
                    addend,
                    Optional<object>.None,
                    isChecked: false);
            }

            var arrayTemp = NewTemp("$coll_arr", arrayType);
            locals.Add(arrayTemp);
            var arrayExpr = new BoundLocalExpression(node, arrayTemp);
            sideEffects.Add(new BoundLocalDeclarationStatement(
                node,
                arrayTemp,
                new BoundArrayCreationExpression(node, arrayType, elementType, totalCount, initializerOpt: null)));

            var indexTemp = NewTemp("$coll_idx", int32);
            locals.Add(indexTemp);
            var indexRead = new BoundLocalExpression(node, indexTemp);
            sideEffects.Add(new BoundLocalDeclarationStatement(node, indexTemp, new BoundLiteralExpression(node, int32, 0)));

            if (!TryFindArrayCopyMethod(context, int32, out var arrayBaseType, out var copyMethod))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COLL008",
                    DiagnosticSeverity.Error,
                    "Missing required 'Array.Copy(Array, int, Array, int, int)' overload for collection expression spreading.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                var bad = new BoundBadExpression(node);
                bad.SetType(arrayType);
                return bad;
            }

            var zero = new BoundLiteralExpression(node, int32, 0);

            for (int i = 0; i < captured.Count; i++)
            {
                var capturedItem = captured[i];
                if (capturedItem.Kind == BoundCollectionElementKind.Expression)
                {
                    var valueRead = new BoundLocalExpression(capturedItem.Syntax, capturedItem.Local);
                    var indexForWrite = new BoundLocalExpression(capturedItem.Syntax, indexTemp);
                    var left = new BoundArrayElementAccessExpression(capturedItem.Syntax, elementType, arrayExpr, indexForWrite);
                    var assign = new BoundAssignmentExpression(capturedItem.Syntax, left, valueRead);
                    sideEffects.Add(new BoundExpressionStatement(capturedItem.Syntax, assign));

                    var increment = new BoundAssignmentExpression(
                        capturedItem.Syntax,
                        new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                        new BoundBinaryExpression(
                            capturedItem.Syntax,
                            BoundBinaryOperatorKind.Add,
                            int32,
                            new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                            new BoundLiteralExpression(capturedItem.Syntax, int32, 1),
                            Optional<object>.None,
                            isChecked: false));
                    sideEffects.Add(new BoundExpressionStatement(capturedItem.Syntax, increment));
                    continue;
                }

                var spreadLocalExpr = new BoundLocalExpression(capturedItem.Syntax, capturedItem.Local);
                var spreadLen = CreateArrayLengthAccess(capturedItem.Syntax, spreadLocalExpr, int32, context, diagnostics);
                if (spreadLen.HasErrors)
                {
                    var bad = new BoundBadExpression(node);
                    bad.SetType(arrayType);
                    return bad;
                }

                var srcAsArray = ApplyConversion(
                    exprSyntax: ((SpreadElementSyntax)capturedItem.Syntax).Expression,
                    expr: spreadLocalExpr,
                    targetType: arrayBaseType,
                    diagnosticNode: capturedItem.Syntax,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
                var dstAsArray = ApplyConversion(
                    exprSyntax: node,
                    expr: arrayExpr,
                    targetType: arrayBaseType,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
                if (srcAsArray.HasErrors || dstAsArray.HasErrors)
                {
                    var bad = new BoundBadExpression(node);
                    bad.SetType(arrayType);
                    return bad;
                }
                {
                    var copyCall = new BoundCallExpression(
                        capturedItem.Syntax,
                        receiverOpt: null,
                        copyMethod,
                        ImmutableArray.Create<BoundExpression>(
                            srcAsArray,
                            zero,
                            dstAsArray,
                            new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                            spreadLen));
                    sideEffects.Add(new BoundExpressionStatement(capturedItem.Syntax, copyCall));

                    var increment = new BoundAssignmentExpression(
                        capturedItem.Syntax,
                        new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                        new BoundBinaryExpression(
                            capturedItem.Syntax,
                            BoundBinaryOperatorKind.Add,
                            int32,
                            new BoundLocalExpression(capturedItem.Syntax, indexTemp),
                            spreadLen,
                            Optional<object>.None,
                            isChecked: false));
                    sideEffects.Add(new BoundExpressionStatement(capturedItem.Syntax, increment));
                }
            }

            return new BoundSequenceExpression(node, locals.ToImmutable(), sideEffects.ToImmutable(), arrayExpr);
        }
        private BoundExpression CreateArrayLengthAccess(
            SyntaxNode syntax,
            BoundExpression arrayExpr,
            TypeSymbol intType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiverTypeForLookup = GetReceiverTypeForMemberLookup(arrayExpr.Type);
            if (receiverTypeForLookup is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COLL009",
                    DiagnosticSeverity.Error,
                    "Array spread receiver has no accessible members.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            Symbol? lengthMember = null;
            var members = LookupMembers(receiverTypeForLookup, "Length");
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is PropertySymbol p && !p.IsStatic && ReferenceEquals(p.Type, intType))
                {
                    lengthMember = p;
                    break;
                }
                if (members[i] is FieldSymbol f && !f.IsStatic && ReferenceEquals(f.Type, intType))
                {
                    lengthMember = f;
                    break;
                }
            }

            if (lengthMember is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COLL010",
                    DiagnosticSeverity.Error,
                    "Array spread receiver does not have an accessible 'Length' member.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            return new BoundMemberAccessExpression(
                syntax: syntax as ExpressionSyntax ?? new IdentifierNameSyntax(default),
                receiverOpt: arrayExpr,
                member: lengthMember,
                type: intType,
                isLValue: false);
        }
        private bool TryFindArrayCopyMethod(
            BindingContext context,
            TypeSymbol intType,
            out NamedTypeSymbol arrayBaseType,
            out MethodSymbol copyMethod)
        {
            var arrayBase = context.Compilation.GetSpecialType(SpecialType.System_Array);
            if (arrayBase is not NamedTypeSymbol arrayType)
            {
                arrayBaseType = null!;
                copyMethod = null!;
                return false;
            }

            var members = arrayType.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is not MethodSymbol m) continue;
                if (!m.IsStatic || m.IsConstructor) continue;
                if (!string.Equals(m.Name, "Copy", StringComparison.Ordinal)) continue;
                if (m.Parameters.Length != 5) continue;
                if (!ReferenceEquals(m.Parameters[0].Type, arrayType)) continue;
                if (!ReferenceEquals(m.Parameters[1].Type, intType)) continue;
                if (!ReferenceEquals(m.Parameters[2].Type, arrayType)) continue;
                if (!ReferenceEquals(m.Parameters[3].Type, intType)) continue;
                if (!ReferenceEquals(m.Parameters[4].Type, intType)) continue;

                arrayBaseType = arrayType;
                copyMethod = m;
                return true;
            }

            arrayBaseType = arrayType;
            copyMethod = null!;
            return false;
        }
        private bool TryResolveCollectionInitializerAddCall(
            ExpressionSyntax elementSyntax,
            NamedTypeSymbol receiverType,
            ExpressionSyntax receiverSyntax,
            BoundExpression? receiverOpt,
            ImmutableArray<BoundExpression> argumentExpressions,
            BindingContext context,
            DiagnosticBag? diagnostics,
            out BoundExpression? addCall,
            bool reportDiagnostics)
        {
            addCall = null;

            var instanceCandidates = LookupMethods(receiverType, "Add")
                .OfType<MethodSymbol>()
                .Where(m => !m.IsStatic)
                .Where(m => AccessibilityHelper.IsAccessible(m, context))
                .ToImmutableArray();

            if (!instanceCandidates.IsDefaultOrEmpty)
            {
                var sink = diagnostics ?? new DiagnosticBag();
                if (TryResolveOverload(
                    candidates: instanceCandidates,
                    args: argumentExpressions,
                    getArgExprSyntax: _ => elementSyntax,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: sink,
                    diagnosticNode: elementSyntax))
                {
                    var callReceiver = receiverOpt ?? new BoundThisExpression(receiverSyntax, receiverType);
                    addCall = new BoundCallExpression(elementSyntax, callReceiver, chosen!, convertedArgs);
                    return true;
                }
            }

            var receiverForExtensions = receiverOpt;
            if (receiverForExtensions is null)
                receiverForExtensions = new BoundThisExpression(receiverSyntax, receiverType);

            var extensionCandidates = LookupExtensionMethods("Add", receiverForExtensions, context);
            if (!extensionCandidates.IsDefaultOrEmpty)
            {
                var extArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(argumentExpressions.Length + 1);
                extArgsBuilder.Add(receiverForExtensions);
                extArgsBuilder.AddRange(argumentExpressions);
                var extArgs = extArgsBuilder.ToImmutable();
                var sink = diagnostics ?? new DiagnosticBag();

                if (TryResolveOverload(
                    candidates: extensionCandidates,
                    args: extArgs,
                    getArgExprSyntax: i => i == 0 ? receiverSyntax : elementSyntax,
                    getArgRefKindKeyword: i => null,
                    getArgName: i => null,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: sink,
                    diagnosticNode: elementSyntax))
                {
                    addCall = new BoundCallExpression(
                        elementSyntax,
                        receiverOpt: null,
                        method: chosen!,
                        arguments: convertedArgs);
                    return true;
                }
            }

            if (reportDiagnostics && diagnostics is not null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COLINIT002",
                    DiagnosticSeverity.Error,
                    $"No accessible 'Add' method found on type '{receiverType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, elementSyntax.Span)));
            }

            return false;
        }
        private bool CanBindTargetTypedObjectCreation(
            BoundUnboundImplicitObjectCreationExpression unbound,
            TypeSymbol targetType,
            BindingContext context)
        {
            if (targetType is not NamedTypeSymbol nt)
                return false;
            var ioc = (ImplicitObjectCreationExpressionSyntax)unbound.Syntax;
            var argSyntaxes = ioc.ArgumentList.Arguments;
            var boundArgs = unbound.Arguments;
            if (argSyntaxes.Count != boundArgs.Length)
                return false;
            var allCtorCandidates = LookupConstructors(nt);
            var ctorCandidates = allCtorCandidates
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();

            if (nt.TypeKind == TypeKind.Struct && boundArgs.Length == 0)
            {
                bool hasAccessibleParameterlessCtor = false;
                for (int i = 0; i < ctorCandidates.Length; i++)
                    if (ctorCandidates[i].Parameters.Length == 0)
                    {
                        hasAccessibleParameterlessCtor = true;
                        break;
                    }
                if (!hasAccessibleParameterlessCtor)
                    return true;
            }
            if (ctorCandidates.IsDefaultOrEmpty)
                return false;

            for (int i = 0; i < ctorCandidates.Length; i++)
            {
                var m = ctorCandidates[i];
                if (m.Parameters.Length != boundArgs.Length)
                    continue;
                bool ok = true;
                for (int a = 0; a < boundArgs.Length; a++)
                {
                    if (!ArgumentRefKindMatchesParameter(argSyntaxes[a].RefKindKeyword, m.Parameters[a]))
                    {
                        ok = false;
                        break;
                    }
                    var conv = ClassifyConversion(boundArgs[a], m.Parameters[a].Type, context);
                    if (!conv.Exists || !conv.IsImplicit)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return true;
            }
            return false;
        }
    }
}
