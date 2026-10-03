using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal sealed partial class LocalScopeBinder
    {
        // Type arguments an extension member takes from its receiver before inference looks at the call's arguments
        private Dictionary<MethodSymbol, ImmutableArray<TypeSymbol>>? _fixedLeadingTypeArguments;

        private IEnumerable<MethodSymbol> EnumerateExtensionMembers(BindingContext context, string name, ExtensionMemberKind kind, bool isStatic)
        {
            foreach (var container in EnumerateExtensionContainerTypes(context, blocksOnly: true))
            {
                foreach (var member in container.GetMembers())
                {
                    if (member is MethodSymbol { ExtensionMember: { } info } method &&
                        info.Kind == kind && info.IsStatic == isStatic && StringComparer.Ordinal.Equals(info.Name, name))
                    {
                        yield return method;
                    }
                }
            }
        }

        // The block's type arguments under which an extension member applies to the receiver, or default when it does not
        private ImmutableArray<TypeSymbol> InferExtensionBlockTypeArguments(
            MethodSymbol definition, BoundExpression receiver, bool isStaticAccess, BindingContext context)
        {
            var info = definition.ExtensionMember!;
            var target = isStaticAccess ? info.ExtendedType : definition.Parameters[0].Type;
            if (target is null)
                return default;
            if (target is ByRefTypeSymbol byRef)
                target = byRef.ElementType;

            var typeArguments = ImmutableArray<TypeSymbol>.Empty;
            if (info.BlockTypeParameterCount != 0)
            {
                var typeParameters = definition.TypeParameters;
                var inferences = new TypeSymbol?[typeParameters.Length];
                InferMethodTypeArgumentsFromParameter(target, receiver, typeParameters, inferences, new bool[typeParameters.Length]);
                var builder = ImmutableArray.CreateBuilder<TypeSymbol>(info.BlockTypeParameterCount);
                var map = ImmutableDictionary.CreateBuilder<TypeParameterSymbol, TypeSymbol>();
                for (int i = 0; i < info.BlockTypeParameterCount; i++)
                {
                    if (inferences[i] is not TypeSymbol inferred || inferred is DefaultLiteralTypeSymbol)
                        return default;
                    builder.Add(inferred);
                    map[typeParameters[i]] = inferred;
                }
                typeArguments = builder.ToImmutable();
                target = TypeSubstituter.Substitute(target, context.Compilation.TypeManager, map.ToImmutable());
                if (!GenericConstraintChecker.CheckMethodInstantiation(definition, typeArguments, _ => receiver.Syntax.Span, context, new DiagnosticBag()))
                    return default;
            }

            bool applies = isStaticAccess
                ? AreSameType(receiver.Type, target)
                : ClassifyConversion(receiver, target, context).Kind is ConversionKind.Identity or ConversionKind.ImplicitReference or ConversionKind.Boxing;
            return applies ? typeArguments : default;
        }

        private static MethodSymbol ConstructExtensionMember(MethodSymbol definition, ImmutableArray<TypeSymbol> typeArguments, BindingContext context)
            => typeArguments.IsDefaultOrEmpty ? definition : new ConstructedMethodSymbol(definition, typeArguments, context.Compilation.TypeManager);

        // x.M<...>(...) where M's block type parameters come from x and only its own are explicit
        private MethodSymbol? ConstructInstanceExtensionCandidate(
            MethodSymbol definition, ImmutableArray<TypeSymbol> explicitTypeArgs, BoundExpression receiver, BindingContext context)
        {
            int blockCount = definition.ExtensionMember?.BlockTypeParameterCount ?? 0;
            if (definition.TypeParameters.Length - blockCount != explicitTypeArgs.Length)
                return null;
            if (blockCount == 0)
                return new ConstructedMethodSymbol(definition, explicitTypeArgs, context.Compilation.TypeManager);

            var blockArgs = InferExtensionBlockTypeArguments(definition, receiver, isStaticAccess: false, context);
            return blockArgs.IsDefault ? null : new ConstructedMethodSymbol(definition, blockArgs.AddRange(explicitTypeArgs), context.Compilation.TypeManager);
        }

        private BoundExpression? TryBindStaticExtensionInvocation(
            InvocationExpressionSyntax inv,
            MemberAccessExpressionSyntax ma,
            string name,
            TypeSymbol type,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiver = new BoundTypeOnlyExpression(ma.Expression, type);
            var explicitTypeArgs = ma.Name is GenericNameSyntax generic
                ? BindTypeArguments(generic.TypeArgumentList.Arguments, context, diagnostics)
                : default;
            var fixedTypeArguments = new Dictionary<MethodSymbol, ImmutableArray<TypeSymbol>>(ReferenceEqualityComparer<MethodSymbol>.Instance);
            var candidates = ImmutableArray.CreateBuilder<MethodSymbol>();
            foreach (var definition in EnumerateExtensionMembers(context, name, ExtensionMemberKind.Method, isStatic: true))
            {
                if (!AccessibilityHelper.IsAccessible(definition, context))
                    continue;
                var blockArgs = InferExtensionBlockTypeArguments(definition, receiver, isStaticAccess: true, context);
                if (blockArgs.IsDefault)
                    continue;

                int ownCount = definition.TypeParameters.Length - blockArgs.Length;
                if (!explicitTypeArgs.IsDefault)
                {
                    if (ownCount == explicitTypeArgs.Length)
                        candidates.Add(ConstructExtensionMember(definition, blockArgs.AddRange(explicitTypeArgs), context));
                }
                else if (ownCount == 0)
                {
                    candidates.Add(ConstructExtensionMember(definition, blockArgs, context));
                }
                else
                {
                    fixedTypeArguments[definition] = blockArgs;
                    candidates.Add(definition);
                }
            }
            if (candidates.Count == 0)
                return null;

            var outerFixed = _fixedLeadingTypeArguments;
            _fixedLeadingTypeArguments = fixedTypeArguments;
            try
            {
                if (!TryResolveOverload(
                    candidates: candidates.ToImmutable(),
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: inv))
                {
                    return new BoundBadExpression(inv);
                }
                return new BoundCallExpression(inv, receiverOpt: null, chosen!, convertedArgs);
            }
            finally
            {
                _fixedLeadingTypeArguments = outerFixed;
            }
        }

        private BoundExpression? TryBindExtensionProperty(
            MemberAccessExpressionSyntax ma,
            string name,
            BoundExpression? receiverValue,
            TypeSymbol? staticType,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            bool isStatic = receiverValue is null;
            var receiver = receiverValue ?? (staticType is null ? null : new BoundTypeOnlyExpression(ma.Expression, staticType));
            if (receiver is null)
                return null;

            var accessorsByBlock = new Dictionary<(Symbol? Container, string Grouping), (MethodSymbol? Getter, MethodSymbol? Setter)>();
            foreach (var definition in EnumerateExtensionMembers(context, name, ExtensionMemberKind.Property, isStatic))
            {
                var key = (definition.ContainingSymbol, definition.ExtensionMember!.GroupingTypeName);
                accessorsByBlock.TryGetValue(key, out var pair);
                accessorsByBlock[key] = definition.ExtensionMember.IsSetter ? (pair.Getter, definition) : (definition, pair.Setter);
            }

            ExtensionPropertySymbol? found = null;
            bool ambiguous = false;
            foreach (var (getterDefinition, setterDefinition) in accessorsByBlock.Values)
            {
                var anyAccessor = (getterDefinition ?? setterDefinition)!;
                var blockArgs = InferExtensionBlockTypeArguments(anyAccessor, receiver, isStatic, context);
                if (blockArgs.IsDefault)
                    continue;

                var getter = getterDefinition is null ? null : ConstructExtensionMember(getterDefinition, blockArgs, context);
                var setter = setterDefinition is null ? null : ConstructExtensionMember(setterDefinition, blockArgs, context);
                var type = getter?.ReturnType ?? setter!.Parameters[^1].Type;
                if (type is ByRefTypeSymbol byRef)
                    type = byRef.ElementType;
                ambiguous |= found is not null;
                found = new ExtensionPropertySymbol(name, anyAccessor.ContainingSymbol, type, isStatic, getter, setter);
            }
            if (found is null)
                return null;
            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic("CN_EXT010", DiagnosticSeverity.Error,
                    $"The extension property '{name}' is ambiguous.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(ma);
            }

            bool canRead = found.GetMethod is not null && AccessibilityHelper.IsAccessible(found.GetMethod, context);
            bool canWrite = found.SetMethod is not null && AccessibilityHelper.IsAccessible(found.SetMethod, context);
            if (valueKind == BindValueKind.RValue ? !canRead : !canWrite)
            {
                diagnostics.Add(new Diagnostic("CN_EXT011", DiagnosticSeverity.Error,
                    valueKind == BindValueKind.RValue ? "Extension property has no accessible getter." : "Extension property has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }
            return new BoundMemberAccessExpression(ma, receiverValue, found, found.Type, isLValue: canWrite);
        }
    }
}
