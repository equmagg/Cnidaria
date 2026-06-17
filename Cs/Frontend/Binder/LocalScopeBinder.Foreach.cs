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
        private enum ForEachResolutionStatus : byte
        {
            NotApplicable,
            Success,
            Error
        }
        private readonly struct ForEachResolution
        {
            public readonly BoundForEachEnumeratorKind Kind;
            public readonly TypeSymbol CollectionType;
            public readonly TypeSymbol EnumeratorType;
            public readonly TypeSymbol ElementType;
            public readonly Conversion CollectionConversion;
            public readonly MethodSymbol? GetEnumeratorMethodOpt;
            public readonly bool GetEnumeratorIsExtensionMethod;
            public readonly PropertySymbol? CurrentPropertyOpt;
            public readonly MethodSymbol? MoveNextMethodOpt;

            public ForEachResolution(
                BoundForEachEnumeratorKind kind,
                TypeSymbol collectionType,
                TypeSymbol enumeratorType,
                TypeSymbol elementType,
                Conversion collectionConversion,
                MethodSymbol? getEnumeratorMethodOpt,
                bool getEnumeratorIsExtensionMethod,
                PropertySymbol? currentPropertyOpt,
                MethodSymbol? moveNextMethodOpt)
            {
                Kind = kind;
                CollectionType = collectionType;
                EnumeratorType = enumeratorType;
                ElementType = elementType;
                CollectionConversion = collectionConversion;
                GetEnumeratorMethodOpt = getEnumeratorMethodOpt;
                GetEnumeratorIsExtensionMethod = getEnumeratorIsExtensionMethod;
                CurrentPropertyOpt = currentPropertyOpt;
                MoveNextMethodOpt = moveNextMethodOpt;
            }
        }

        private BoundStatement BindForEachVariable(
            ForEachVariableStatementSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic(
                "CN_FOREACH_VAR001",
                DiagnosticSeverity.Error,
                "foreach deconstruction is not supported.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));

            return new BoundBadStatement(node);
        }
        private BoundStatement BindForEach(
            ForEachStatementSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (node.AwaitKeyword.Span.Length != 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH_AWAIT001",
                    DiagnosticSeverity.Error,
                    "await foreach is not supported.",
                    new Location(context.SemanticModel.SyntaxTree, node.AwaitKeyword.Span)));

                return new BoundBadStatement(node);
            }

            var collection = BindExpression(node.Expression, context, diagnostics);
            if (collection.HasErrors)
                return new BoundBadStatement(node);

            if (!TryResolveForEach(node, collection, context, diagnostics, out var foreachInfo))
                return new BoundBadStatement(node);

            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);

            var name = node.Identifier.ValueText ?? string.Empty;
            if (name.Length == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH_LOCAL001",
                    DiagnosticSeverity.Error,
                    "Invalid foreach iteration variable name.",
                    new Location(context.SemanticModel.SyntaxTree, node.Identifier.Span)));

                return new BoundBadStatement(node);
            }

            if (scope.IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_LOCAL001",
                    DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, node.Identifier.Span)));
            }

            var elementType = foreachInfo.ElementType is ByRefTypeSymbol byRefElement
                ? byRefElement.ElementType
                : foreachInfo.ElementType;

            TypeSymbol iterationType;
            Conversion iterationConversion;

            if (IsVar(node.Type))
            {
                if (elementType is NullTypeSymbol || elementType.SpecialType == SpecialType.System_Void)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FOREACH_VAR002",
                        DiagnosticSeverity.Error,
                        "Cannot infer the type of the foreach iteration variable.",
                        new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));

                    iterationType = new ErrorTypeSymbol("foreach", containing: null, ImmutableArray<Location>.Empty);
                    iterationConversion = new Conversion(ConversionKind.None);
                }
                else
                {
                    iterationType = elementType;
                    iterationConversion = new Conversion(ConversionKind.Identity);
                }
            }
            else
            {
                iterationType = scope.BindType(node.Type, context, diagnostics);

                var dummyElement = new BoundTypeOnlyExpression(node.Expression, elementType);
                iterationConversion = scope.ClassifyConversion(dummyElement, iterationType, context);

                if (!iterationConversion.Exists)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FOREACH_CONV001",
                        DiagnosticSeverity.Error,
                        $"Cannot convert foreach element of type '{elementType.Name}' to iteration variable type '{iterationType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                }
            }

            var iterationLocal = new LocalSymbol(
                name: name,
                containing: _containing,
                type: iterationType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, node.Identifier.Span)),
                isReadOnly: true);

            scope.DeclareLocal(iterationLocal, node, context);

            var breakLabel = _flow.NewGeneratedLabel("break");
            var continueLabel = _flow.NewGeneratedLabel("continue");

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

            return new BoundForEachStatement(
                syntax: node,
                enumeratorKind: foreachInfo.Kind,
                iterationVariable: iterationLocal,
                collection: collection,
                collectionType: foreachInfo.CollectionType,
                enumeratorType: foreachInfo.EnumeratorType,
                elementType: elementType,
                collectionConversion: foreachInfo.CollectionConversion,
                getEnumeratorMethodOpt: foreachInfo.GetEnumeratorMethodOpt,
                getEnumeratorIsExtensionMethod: foreachInfo.GetEnumeratorIsExtensionMethod,
                currentPropertyOpt: foreachInfo.CurrentPropertyOpt,
                moveNextMethodOpt: foreachInfo.MoveNextMethodOpt,
                iterationConversion: iterationConversion,
                body: body,
                breakLabel: breakLabel,
                continueLabel: continueLabel);
        }
        private bool TryResolveForEach(
            ForEachStatementSyntax node,
            BoundExpression collection,
            BindingContext context,
            DiagnosticBag diagnostics,
            out ForEachResolution result)
        {
            if (TryResolveArrayForEach(collection, context, out result))
                return true;
            if (TryResolveStringForEach(collection, context, out result))
                return true;
            if (TryResolveSpanForEach(collection, out result))
                return true;

            var patternStatus = TryResolvePatternForEach(node, collection, context, diagnostics, out result);
            if (patternStatus == ForEachResolutionStatus.Success)
                return true;
            if (patternStatus == ForEachResolutionStatus.Error)
                return false;

            if (TryResolveEnumerableInterfaceForEach(node, collection, context, diagnostics, out result))
                return true;

            var extensionStatus = TryResolveExtensionForEach(node, collection, context, diagnostics, out result);
            if (extensionStatus == ForEachResolutionStatus.Success)
                return true;
            if (extensionStatus == ForEachResolutionStatus.Error)
                return false;

            diagnostics.Add(new Diagnostic(
                "CN_FOREACH001",
                DiagnosticSeverity.Error,
                $"foreach statement cannot operate on variables of type '{collection.Type.Name}' because no suitable public 'GetEnumerator' was found.",
                new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

            result = default;
            return false;
        }
        private bool TryResolveArrayForEach(
            BoundExpression collection,
            BindingContext context,
            out ForEachResolution result)
        {
            if (collection.Type is not ArrayTypeSymbol arrayType)
            {
                result = default;
                return false;
            }

            var ienumerable = GetWellKnownType(context.Compilation, "System", "Collections", "IEnumerable", 0) ?? collection.Type;
            var ienumerator = GetWellKnownType(context.Compilation, "System", "Collections", "IEnumerator", 0) ?? collection.Type;
            var conv = ClassifyConversion(collection, ienumerable, context);

            result = new ForEachResolution(
                kind: BoundForEachEnumeratorKind.Array,
                collectionType: ienumerable,
                enumeratorType: ienumerator,
                elementType: arrayType.ElementType,
                collectionConversion: conv.Exists ? conv : new Conversion(ConversionKind.Identity),
                getEnumeratorMethodOpt: null,
                getEnumeratorIsExtensionMethod: false,
                currentPropertyOpt: null,
                moveNextMethodOpt: null);

            return true;
        }
        private bool TryResolveStringForEach(
            BoundExpression collection,
            BindingContext context,
            out ForEachResolution result)
        {
            if (collection.Type.SpecialType != SpecialType.System_String)
            {
                result = default;
                return false;
            }

            var charType = context.Compilation.GetSpecialType(SpecialType.System_Char);

            result = new ForEachResolution(
                kind: BoundForEachEnumeratorKind.String,
                collectionType: collection.Type,
                enumeratorType: collection.Type,
                elementType: charType,
                collectionConversion: new Conversion(ConversionKind.Identity),
                getEnumeratorMethodOpt: null,
                getEnumeratorIsExtensionMethod: false,
                currentPropertyOpt: null,
                moveNextMethodOpt: null);

            return true;
        }
        private bool TryResolveSpanForEach(
            BoundExpression collection,
            out ForEachResolution result)
        {
            if (!TryGetSpanLikeElementType(collection.Type, out var spanLikeType, out var elementType))
            {
                result = default;
                return false;
            }

            result = new ForEachResolution(
                kind: BoundForEachEnumeratorKind.Span,
                collectionType: spanLikeType,
                enumeratorType: spanLikeType,
                elementType: elementType,
                collectionConversion: new Conversion(ConversionKind.Identity),
                getEnumeratorMethodOpt: null,
                getEnumeratorIsExtensionMethod: false,
                currentPropertyOpt: null,
                moveNextMethodOpt: null);

            return true;
        }

        private ForEachResolutionStatus TryResolvePatternForEach(
            ForEachStatementSyntax node,
            BoundExpression collection,
            BindingContext context,
            DiagnosticBag diagnostics,
            out ForEachResolution result)
        {
            result = default;

            var receiverType = GetReceiverTypeForMemberLookup(collection.Type);
            if (receiverType is null)
                return ForEachResolutionStatus.NotApplicable;

            var candidates = GetApplicableGetEnumeratorInstanceCandidates(receiverType, context);
            if (candidates.Length != 1)
                return ForEachResolutionStatus.NotApplicable;

            var getEnumerator = candidates[0];
            if (!TryValidateEnumeratorShape(
                diagnosticNode: node.Expression,
                enumeratorTypeSymbol: getEnumerator.ReturnType,
                context: context,
                diagnostics: diagnostics,
                elementType: out var elementType,
                currentProperty: out var currentProperty,
                moveNextMethod: out var moveNextMethod))
            {
                return ForEachResolutionStatus.Error;
            }

            result = new ForEachResolution(
                kind: BoundForEachEnumeratorKind.Pattern,
                collectionType: collection.Type,
                enumeratorType: getEnumerator.ReturnType,
                elementType: elementType,
                collectionConversion: new Conversion(ConversionKind.Identity),
                getEnumeratorMethodOpt: getEnumerator,
                getEnumeratorIsExtensionMethod: false,
                currentPropertyOpt: currentProperty,
                moveNextMethodOpt: moveNextMethod);

            return ForEachResolutionStatus.Success;
        }

        private bool TryResolveEnumerableInterfaceForEach(
            ForEachStatementSyntax node,
            BoundExpression collection,
            BindingContext context,
            DiagnosticBag diagnostics,
            out ForEachResolution result)
        {
            result = default;

            var genericIEnumerableDef = GetWellKnownType(context.Compilation, "System", "Collections", "Generic", "IEnumerable", 1);
            var genericIEnumeratorDef = GetWellKnownType(context.Compilation, "System", "Collections", "Generic", "IEnumerator", 1);
            var nongenericIEnumerable = GetWellKnownType(context.Compilation, "System", "Collections", "IEnumerable", 0);
            var nongenericIEnumerator = GetWellKnownType(context.Compilation, "System", "Collections", "IEnumerator", 0);
            var objectType = context.Compilation.GetSpecialType(SpecialType.System_Object);

            if (genericIEnumerableDef is not null && genericIEnumeratorDef is not null)
            {
                var genericCandidates = GetImplementedInterfaces(collection.Type, genericIEnumerableDef);

                if (genericCandidates.Length > 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FOREACH_ENUM001",
                        DiagnosticSeverity.Error,
                        $"foreach statement is ambiguous for type '{collection.Type.Name}' because it converts to multiple 'IEnumerable<T>' interfaces.",
                        new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                    return false;
                }

                if (genericCandidates.Length == 1)
                {
                    var enumerableType = genericCandidates[0];
                    var conv = ClassifyConversion(collection, enumerableType, context);
                    if (conv.Exists && conv.IsImplicit)
                    {
                        var elementType = enumerableType.TypeArguments[0];
                        var enumeratorType = context.Compilation.ConstructNamedType(
                            genericIEnumeratorDef,
                            ImmutableArray.Create(elementType));

                        if (!TryValidateEnumeratorShape(
                            diagnosticNode: node.Expression,
                            enumeratorTypeSymbol: enumeratorType,
                            context: context,
                            diagnostics: diagnostics,
                            elementType: out var validatedElementType,
                            currentProperty: out var currentProperty,
                            moveNextMethod: out var moveNextMethod))
                        {
                            return false;
                        }

                        var getEnumerator = GetSinglePublicParameterlessInstanceMethod(enumerableType, "GetEnumerator", context);

                        result = new ForEachResolution(
                            kind: BoundForEachEnumeratorKind.Interface,
                            collectionType: enumerableType,
                            enumeratorType: enumeratorType,
                            elementType: validatedElementType,
                            collectionConversion: conv,
                            getEnumeratorMethodOpt: getEnumerator,
                            getEnumeratorIsExtensionMethod: false,
                            currentPropertyOpt: currentProperty,
                            moveNextMethodOpt: moveNextMethod);

                        return true;
                    }
                }
            }

            if (nongenericIEnumerable is not null && nongenericIEnumerator is not null)
            {
                var conv = ClassifyConversion(collection, nongenericIEnumerable, context);
                if (conv.Exists && conv.IsImplicit)
                {
                    if (!TryValidateEnumeratorShape(
                        diagnosticNode: node.Expression,
                        enumeratorTypeSymbol: nongenericIEnumerator,
                        context: context,
                        diagnostics: diagnostics,
                        elementType: out _,
                        currentProperty: out var currentProperty,
                        moveNextMethod: out var moveNextMethod))
                    {
                        return false;
                    }

                    var getEnumerator = GetSinglePublicParameterlessInstanceMethod(nongenericIEnumerable, "GetEnumerator", context);

                    result = new ForEachResolution(
                        kind: BoundForEachEnumeratorKind.Interface,
                        collectionType: nongenericIEnumerable,
                        enumeratorType: nongenericIEnumerator,
                        elementType: objectType,
                        collectionConversion: conv,
                        getEnumeratorMethodOpt: getEnumerator,
                        getEnumeratorIsExtensionMethod: false,
                        currentPropertyOpt: currentProperty,
                        moveNextMethodOpt: moveNextMethod);

                    return true;
                }
            }

            return false;
        }

        private ForEachResolutionStatus TryResolveExtensionForEach(
            ForEachStatementSyntax node,
            BoundExpression collection,
            BindingContext context,
            DiagnosticBag diagnostics,
            out ForEachResolution result)
        {
            result = default;

            var candidates = GetApplicableGetEnumeratorExtensionCandidates(collection, context);
            if (candidates.Length == 0)
                return ForEachResolutionStatus.NotApplicable;

            if (candidates.Length != 1)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH_EXT001",
                    DiagnosticSeverity.Error,
                    "Ambiguous extension GetEnumerator() for foreach.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));

                return ForEachResolutionStatus.Error;
            }

            var getEnumerator = candidates[0];
            if (!TryValidateEnumeratorShape(
                diagnosticNode: node.Expression,
                enumeratorTypeSymbol: getEnumerator.ReturnType,
                context: context,
                diagnostics: diagnostics,
                elementType: out var elementType,
                currentProperty: out var currentProperty,
                moveNextMethod: out var moveNextMethod))
            {
                return ForEachResolutionStatus.Error;
            }

            var receiverConv = ClassifyConversion(collection, getEnumerator.Parameters[0].Type, context);

            result = new ForEachResolution(
                kind: BoundForEachEnumeratorKind.Pattern,
                collectionType: collection.Type,
                enumeratorType: getEnumerator.ReturnType,
                elementType: elementType,
                collectionConversion: receiverConv,
                getEnumeratorMethodOpt: getEnumerator,
                getEnumeratorIsExtensionMethod: true,
                currentPropertyOpt: currentProperty,
                moveNextMethodOpt: moveNextMethod);

            return ForEachResolutionStatus.Success;
        }
        private bool TryValidateEnumeratorShape(
            SyntaxNode diagnosticNode,
            TypeSymbol enumeratorTypeSymbol,
            BindingContext context,
            DiagnosticBag diagnostics,
            out TypeSymbol elementType,
            out PropertySymbol currentProperty,
            out MethodSymbol moveNextMethod)
        {
            elementType = new ErrorTypeSymbol("foreach", containing: null, ImmutableArray<Location>.Empty);
            currentProperty = null!;
            moveNextMethod = null!;

            if (enumeratorTypeSymbol is not NamedTypeSymbol enumeratorType ||
                (enumeratorType.TypeKind != TypeKind.Class &&
                 enumeratorType.TypeKind != TypeKind.Struct &&
                 enumeratorType.TypeKind != TypeKind.Interface))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH002",
                    DiagnosticSeverity.Error,
                    "GetEnumerator() must return a class, struct, or interface type.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                return false;
            }

            ImmutableArray<PropertySymbol> currentCandidates = GetClosestReadableInstanceCurrentProperties(enumeratorType, context);

            if (currentCandidates.Length < 1)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH003",
                    DiagnosticSeverity.Error,
                    "Enumerator type must contain a readable public instance property named 'Current'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                return false;
            }
            if (currentCandidates.Length > 1)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH003B",
                    DiagnosticSeverity.Error,
                    "Enumerator type property is ambiguous.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                return false;
            }

            currentProperty = currentCandidates[0];
            elementType = currentProperty.Type is ByRefTypeSymbol byRef
                ? byRef.ElementType
                : currentProperty.Type;

            var moveNextCandidates = ImmutableArray.CreateBuilder<MethodSymbol>();
            var moveNextMethods = LookupMethods(enumeratorType, "MoveNext");
            for (int i = 0; i < moveNextMethods.Length; i++)
            {
                var method = moveNextMethods[i];
                if (method.IsStatic)
                    continue;
                if (method.TypeParameters.Length != 0)
                    continue;
                if (method.Parameters.Length != 0)
                    continue;
                if (!AccessibilityHelper.IsAccessible(method, context))
                    continue;
                if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
                    continue;

                moveNextCandidates.Add(method);
            }

            if (moveNextCandidates.Count != 1)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH004",
                    DiagnosticSeverity.Error,
                    "Enumerator type must contain a public instance parameterless method named 'MoveNext' returning 'bool'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                return false;
            }

            moveNextMethod = moveNextCandidates[0];
            return true;
        }
        private static ImmutableArray<PropertySymbol> GetClosestReadableInstanceCurrentProperties(
            NamedTypeSymbol enumeratorType, BindingContext context)
        {
            if (enumeratorType.TypeKind == TypeKind.Interface)
                return GetClosestReadableInstanceCurrentPropertiesOnInterface(enumeratorType, context);

            var builder = ImmutableArray.CreateBuilder<PropertySymbol>();

            var members = LookupMembers(enumeratorType, "Current");
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is PropertySymbol property &&
                    IsReadableInstanceProperty(property, context))
                {
                    builder.Add(property);
                }
            }

            return builder.ToImmutable();
        }
        private static ImmutableArray<PropertySymbol> GetClosestReadableInstanceCurrentPropertiesOnInterface(
            NamedTypeSymbol root, BindingContext context)
        {
            var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            var queue = new Queue<NamedTypeSymbol>();
            queue.Enqueue(root);

            while (queue.Count != 0)
            {
                int levelCount = queue.Count;
                var builder = ImmutableArray.CreateBuilder<PropertySymbol>();

                for (int n = 0; n < levelCount; n++)
                {
                    var current = queue.Dequeue();
                    if (!seen.Add(current))
                        continue;

                    var members = current.GetMembers();
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i] is PropertySymbol property &&
                            StringComparer.Ordinal.Equals(property.Name, "Current") &&
                            property.ExplicitInterfaceImplementation is null &&
                            IsReadableInstanceProperty(property, context))
                        {
                            builder.Add(property);
                        }
                    }

                    var interfaces = current.Interfaces;
                    for (int i = 0; i < interfaces.Length; i++)
                    {
                        if (interfaces[i] is NamedTypeSymbol iface &&
                            iface.TypeKind == TypeKind.Interface)
                        {
                            queue.Enqueue(iface);
                        }
                    }
                }

                if (builder.Count != 0)
                    return builder.ToImmutable();
            }

            return ImmutableArray<PropertySymbol>.Empty;
        }
        private static bool IsReadableInstanceProperty(PropertySymbol property, BindingContext context)
            => !property.IsStatic &&
                   property.GetMethod is not null &&
                   AccessibilityHelper.IsAccessible(property, context) &&
                   AccessibilityHelper.IsAccessible(property.GetMethod, context);
        private ImmutableArray<MethodSymbol> GetApplicableGetEnumeratorInstanceCandidates(
            NamedTypeSymbol receiverType, BindingContext context)
        {
            var methods = LookupMethods(receiverType, "GetEnumerator");
            var builder = ImmutableArray.CreateBuilder<MethodSymbol>();

            for (int i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (method.IsStatic)
                    continue;
                if (method.TypeParameters.Length != 0)
                    continue;
                if (method.Parameters.Length != 0)
                    continue;
                if (!AccessibilityHelper.IsAccessible(method, context))
                    continue;

                builder.Add(method);
            }

            return builder.ToImmutable();
        }

        private ImmutableArray<MethodSymbol> GetApplicableGetEnumeratorExtensionCandidates(
            BoundExpression collection, BindingContext context)
        {
            var methods = LookupExtensionMethods("GetEnumerator", collection, context);
            var builder = ImmutableArray.CreateBuilder<MethodSymbol>();

            for (int i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (!IsExtensionMethod(method))
                    continue;
                if (method.TypeParameters.Length != 0)
                    continue;
                if (method.Parameters.Length != 1)
                    continue;
                if (!AccessibilityHelper.IsAccessible(method, context))
                    continue;

                builder.Add(method);
            }

            return builder.ToImmutable();
        }

        private static MethodSymbol? GetSinglePublicParameterlessInstanceMethod(
            NamedTypeSymbol type, string name, BindingContext context)
        {
            var methods = LookupMethods(type, name);
            MethodSymbol? found = null;

            for (int i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                if (method.IsStatic)
                    continue;
                if (method.TypeParameters.Length != 0)
                    continue;
                if (method.Parameters.Length != 0)
                    continue;
                if (!AccessibilityHelper.IsAccessible(method, context))
                    continue;

                if (found is not null)
                    return null;

                found = method;
            }

            return found;
        }
        private static ImmutableArray<NamedTypeSymbol> GetImplementedInterfaces(
            TypeSymbol type, NamedTypeSymbol interfaceDefinition)
        {
            var builder = ImmutableArray.CreateBuilder<NamedTypeSymbol>();
            var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance);

            void Visit(TypeSymbol current)
            {
                if (!seen.Add(current))
                    return;

                if (current is not NamedTypeSymbol nt)
                    return;

                if (nt.TypeKind == TypeKind.Interface &&
                    ReferenceEquals(nt.OriginalDefinition, interfaceDefinition))
                {
                    bool duplicate = false;
                    for (int i = 0; i < builder.Count; i++)
                    {
                        if (ReferenceEquals(builder[i], nt))
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                        builder.Add(nt);
                }

                var interfaces = nt.Interfaces;
                for (int i = 0; i < interfaces.Length; i++)
                    Visit(interfaces[i]);

                if (nt.BaseType is TypeSymbol baseType)
                    Visit(baseType);
            }

            Visit(type);
            return builder.ToImmutable();
        }
        private static NamedTypeSymbol? GetWellKnownType(
            Compilation compilation,
            string ns0,
            string ns1,
            string typeName,
            int arity)
        {
            return GetWellKnownType(compilation, new[] { ns0, ns1 }, typeName, arity);
        }
        private static NamedTypeSymbol? GetWellKnownType(
            Compilation compilation,
            string ns0,
            string ns1,
            string ns2,
            string typeName,
            int arity)
        {
            return GetWellKnownType(compilation, new[] { ns0, ns1, ns2 }, typeName, arity);
        }

        private static NamedTypeSymbol? GetWellKnownType(
            Compilation compilation,
            string[] namespaceParts,
            string typeName,
            int arity)
        {
            NamespaceSymbol current = compilation.GlobalNamespace;

            for (int p = 0; p < namespaceParts.Length; p++)
            {
                var next = current.GetNamespaceMembers();
                NamespaceSymbol? found = null;

                for (int i = 0; i < next.Length; i++)
                {
                    if (string.Equals(next[i].Name, namespaceParts[p], StringComparison.Ordinal))
                    {
                        found = next[i];
                        break;
                    }
                }

                if (found is null)
                    return null;

                current = found;
            }

            var types = current.GetTypeMembers(typeName, arity);
            if (types.IsDefaultOrEmpty)
                return null;

            return types[0].OriginalDefinition;
        }
        private BoundExpression BindDiscardedExpression(
            ExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (node is ParenthesizedExpressionSyntax paren)
                return BindDiscardedExpression(paren.Expression, context, diagnostics);

            if (node is PostfixUnaryExpressionSyntax post)
            {
                if (post.Kind == SyntaxKind.PostIncrementExpression)
                    return BindPostfixIncrementOrDecrement(post, isIncrement: true, resultUsed: false, context, diagnostics);

                if (post.Kind == SyntaxKind.PostDecrementExpression)
                    return BindPostfixIncrementOrDecrement(post, isIncrement: false, resultUsed: false, context, diagnostics);
            }

            if (node is ImplicitObjectCreationExpressionSyntax ioc)
                return BindImplicitObjectCreation(ioc, context, diagnostics);

            var expr = BindExpression(node, context, diagnostics);
            if (expr is BoundUnboundCollectionExpression)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COLL001",
                    DiagnosticSeverity.Error,
                    "Collection expression has no target type in this context.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(new ErrorTypeSymbol("collection", containing: null, ImmutableArray<Location>.Empty));
                return bad;
            }

            if (expr is BoundMethodGroupExpression)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MG_NOTARGET001",
                    DiagnosticSeverity.Error,
                    "Method group has no target type in this context.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                var bad = new BoundBadExpression(node);
                bad.SetType(new ErrorTypeSymbol("method group", containing: null, ImmutableArray<Location>.Empty));
                return bad;
            }

            return expr;
        }
    }
}
