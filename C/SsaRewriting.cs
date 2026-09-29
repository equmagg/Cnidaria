using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.C;

/// <summary>Rebuilds statement annotations after operands inside them are replaced</summary>
internal static class SsaRewriting
{
    public static GimpleStatementAnnotations Rewrite(GimpleStatementAnnotations instruction, Func<GimpleOperandInfo, GimpleOperandInfo?> replace,
        Dictionary<GimpleDefinition, GimpleDefinition> redefinitions)
    {
        var expressions = instruction.Operands.Select(operand => Replace(operand, replace)).ToImmutableArray();
        var statement = GimpleNameMaterializer.MaterializeStatement(instruction.Statement, expressions, instruction.Definitions);
        var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(instruction.Definitions.Length);
        foreach (var definition in instruction.Definitions)
        {
            var target = definition.Target is GimpleName ? definition.Target : null;
            var redefined = new GimpleDefinition(definition.Name, definition.Kind, definition.Block, statement, target, definition.Parameter);
            redefinitions[definition] = redefined;
            definitions.Add(redefined);
        }
        var uses = CollectUses(expressions, instruction.Block, statement);
        if (instruction.MemoryInput is not null)
            uses = uses.Add(new GimpleUse(instruction.MemoryInput, GimpleUseKind.Memory, instruction.Block, statement, value: null));
        return new GimpleStatementAnnotations(instruction.Ordinal, instruction.Block, statement, instruction.InputStatement, expressions,
            uses, definitions.MoveToImmutable(), instruction.MemoryInput, instruction.MemoryOutput, instruction.Flags);
    }

    private static GimpleOperandInfo Replace(GimpleOperandInfo node, Func<GimpleOperandInfo, GimpleOperandInfo?> replace)
    {
        if (replace(node) is { } replacement)
            return replacement;
        if (node.Name is not null || node.Children.Length == 0)
            return node;
        var children = node.Children.Select(child => Replace(child, replace)).ToImmutableArray();
        if (children.SequenceEqual(node.Children))
            return node;
        var original = Recompose(node.Original, children.Select(static child => child.Original).ToArray());
        return new GimpleOperandInfo(original, null, children, node.ReadsMemory, node.WritesMemory, node.ContainsCall, node.Role);
    }

    private static GimpleValue Recompose(GimpleValue original, GimpleValue[] children)
        => original switch
        {
            GimpleUnaryExpression unary => new GimpleUnaryExpression(unary.Code, children[0], unary.Type, unary.Syntax),
            GimpleBinaryExpression binary => new GimpleBinaryExpression(children[0], binary.Code, children[1], binary.Type, binary.Syntax),
            GimpleConversionExpression conversion => new GimpleConversionExpression(children[0], conversion.Type, conversion.ConversionKind, conversion.Syntax),
            GimpleCastExpression cast => new GimpleCastExpression(children[0], cast.Type, cast.Syntax),
            GimpleAddressOfExpression address => new GimpleAddressOfExpression((GimplePlace)children[0], address.Type, address.Syntax),
            GimpleIndirectExpression indirect => new GimpleIndirectExpression(children[0], indirect.Type, indirect.Syntax),
            GimpleElementAccessExpression element => new GimpleElementAccessExpression(children[0], children.Length > 1 ? children[1] : null, element.Type, element.Syntax),
            GimpleMemberAccessExpression member => new GimpleMemberAccessExpression(children[0], member.ThroughPointer, member.NameToken, member.Field, member.Type, member.Syntax),
            _ => original,
        };

    public static GimpleOperandInfo Build(GimpleValue value, GimpleOperandRole role, bool readsMemory)
    {
        switch (value)
        {
            case GimpleName name:
                return new GimpleOperandInfo(name, name, ImmutableArray<GimpleOperandInfo>.Empty, false, false, false);
            case GimpleAddressOfExpression address:
                return Composite(address, false, role, BuildPlace(address.Target));
            case GimpleIndirectExpression indirect:
                return role == GimpleOperandRole.Address
                    ? Composite(indirect, false, role, Build(indirect.Address, GimpleOperandRole.Value, false))
                    : Composite(indirect, readsMemory, role, Build(indirect.Address, GimpleOperandRole.Value, false));
            case GimpleElementAccessExpression element:
                {
                    var baseNode = role == GimpleOperandRole.Address ? BuildAddressBase(element.Expression) : Build(element.Expression, GimpleOperandRole.Value, readsMemory);
                    return element.Index is null
                        ? Composite(element, readsMemory, role, baseNode)
                        : Composite(element, readsMemory, role, baseNode, Build(element.Index, GimpleOperandRole.Value, false));
                }
            case GimpleMemberAccessExpression member:
                {
                    var baseNode = member.ThroughPointer ? Build(member.Expression, GimpleOperandRole.Value, false)
                        : role == GimpleOperandRole.Address ? BuildAddressBase(member.Expression) : Build(member.Expression, GimpleOperandRole.Value, readsMemory);
                    return Composite(member, readsMemory, role, baseNode);
                }
            case GimpleUnaryExpression unary:
                return Composite(unary, false, role, Build(unary.Operand, GimpleOperandRole.Value, false));
            case GimpleBinaryExpression binary:
                return Composite(binary, false, role, Build(binary.Left, GimpleOperandRole.Value, false), Build(binary.Right, GimpleOperandRole.Value, false));
            case GimpleConversionExpression conversion:
                return Composite(conversion, false, role, Build(conversion.Operand, GimpleOperandRole.Value, false));
            case GimpleCastExpression cast:
                return Composite(cast, false, role, Build(cast.Operand, GimpleOperandRole.Value, false));
            default:
                return new GimpleOperandInfo(value, null, ImmutableArray<GimpleOperandInfo>.Empty, false, false, false, role);
        }
    }

    private static GimpleOperandInfo BuildPlace(GimplePlace place)
        => place is GimpleName ? Build(place, GimpleOperandRole.Value, false) : Build(place, GimpleOperandRole.Address, false);

    private static GimpleOperandInfo BuildAddressBase(GimpleValue value)
        => value.Type.Type is not PointerType && value is GimplePlace place and not GimpleName
            ? Build(place, GimpleOperandRole.Address, false)
            : Build(value, GimpleOperandRole.Value, false);

    private static GimpleOperandInfo Composite(GimpleValue original, bool readsMemory, GimpleOperandRole role, params GimpleOperandInfo[] children)
        => new(original, null, children.ToImmutableArray(), readsMemory || children.Any(static child => child.ReadsMemory), false, false, role);

    public static ImmutableArray<GimpleUse> CollectUses(ImmutableArray<GimpleOperandInfo> expressions, ControlFlowBlock block, GimpleStatement statement)
    {
        var uses = ImmutableArray.CreateBuilder<GimpleUse>();
        foreach (var expression in expressions)
            Collect(expression);
        return uses.ToImmutable();

        void Collect(GimpleOperandInfo node)
        {
            if (node.Name is not null)
            {
                uses.Add(new GimpleUse(node.Name, node.IsAddress ? GimpleUseKind.Address : GimpleUseKind.Value, block, statement, node.Original));
                return;
            }
            foreach (var child in node.Children)
                Collect(child);
        }
    }
}
