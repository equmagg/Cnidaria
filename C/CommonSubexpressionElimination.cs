using System.Collections.Generic;

namespace Cnidaria.C;

internal static partial class SsaOptimizer
{
    private sealed partial class Pass
    {
        private readonly Dictionary<GimpleStatementAnnotations, GimpleOperandInfo> _commonAssignments = new();
        private readonly Dictionary<GimpleOperandInfo, GimpleOperandInfo> _commonOperands = new();

        private void FindCommonSubexpressions()
        {
            var available = new Dictionary<int, GimpleName>();
            var undo = new List<(int Number, GimpleName? Previous)>();
            var work = new Stack<(ControlFlowBlock Block, int Restore)>();
            work.Push((_function.ControlFlowFunction.Entry, -1));
            foreach (var definition in _function.Definitions)
            {
                if (definition.Kind == GimpleDefinitionKind.Entry)
                    MakeAvailable(definition.Name);
            }

            while (work.Count != 0)
            {
                var (flow, restore) = work.Pop();
                if (restore >= 0)
                {
                    for (var i = undo.Count - 1; i >= restore; i--)
                    {
                        var (number, previous) = undo[i];
                        if (previous is null)
                            available.Remove(number);
                        else
                            available[number] = previous;
                    }
                    undo.RemoveRange(restore, undo.Count - restore);
                    continue;
                }

                if (!flow.IsReachable || flow.IsExit || !_function.TryGetBlock(flow, out var block) || block is null)
                    continue;

                work.Push((flow, undo.Count));
                foreach (var phi in block.Phis)
                    MakeAvailable(phi.Result);
                foreach (var instruction in block.Statements)
                {
                    foreach (var operand in instruction.Operands)
                        FindOperand(operand);

                    var definition = GetPrimaryDefinition(instruction);
                    if (definition is not null && instruction.Statement is GimpleAssignStatement assign &&
                        (assign.RhsClass != GimpleRhsClass.Single || assign.Op1 is not (GimpleName or GimpleConstantValue)) &&
                        (instruction.Flags & (GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall)) == 0 &&
                        CanReplaceNameWithConstant(definition.Name) && AllOperandsReusable(instruction) &&
                        _function.ValueNumbering.TryGetValueNumber(definition.Name, out var number) && number is not null &&
                        TryReplacement(number, definition.Name.Type, out var replacement))
                    {
                        _commonAssignments[instruction] = replacement;
                    }

                    foreach (var result in instruction.Definitions)
                        MakeAvailable(result.Name);
                }

                for (var i = flow.DominatorChildren.Length - 1; i >= 0; i--)
                    work.Push((flow.DominatorChildren[i], -1));
            }

            void MakeAvailable(GimpleName name)
            {
                if (name.IsUndefined || name.Variable.Kind == GimpleVariableKind.Memory ||
                    !IsCseScalar(name.Type) || !CanReplaceNameWithConstant(name) ||
                    !_function.ValueNumbering.TryGetValueNumber(name, out var number) || number is null)
                    return;

                available.TryGetValue(number.Id, out var previous);
                if (previous is not null && SameType(previous.Type, name.Type))
                    return;
                undo.Add((number.Id, previous));
                available[number.Id] = name;
            }

            bool TryReplacement(ValueNumber number, QualifiedType type, out GimpleOperandInfo replacement)
            {
                replacement = null!;
                if (!IsCseScalar(type) || IsVolatileOrAtomic(type))
                    return false;
                if (number.HasConstantValue && SameType(number.Type, type))
                {
                    replacement = CreateConstantExpression(new GimpleConstantValue(number.ConstantValue, type));
                    return true;
                }
                if (!available.TryGetValue(number.Id, out var name) || !SameType(name.Type, type))
                    return false;
                replacement = CreateNameExpression(name, name, GimpleOperandRole.Value);
                return true;
            }

            void FindOperand(GimpleOperandInfo operand)
            {
                if (!operand.IsAddress && operand.Name is null && operand.Original is not GimpleConstantValue &&
                    IsReusableOperand(operand) &&
                    _function.ValueNumbering.TryGetValueNumber(operand, out var number) && number is not null &&
                    TryReplacement(number, operand.Original.Type, out var replacement))
                {
                    _commonOperands[operand] = replacement;
                    return;
                }
                foreach (var child in operand.Children)
                    FindOperand(child);
            }
        }

        private static bool IsCseScalar(QualifiedType type)
            => type.Type.Kind is TypeKind.Builtin or TypeKind.Enum or TypeKind.Pointer;

        private static bool AllOperandsReusable(GimpleStatementAnnotations instruction)
        {
            foreach (var operand in instruction.Operands)
            {
                if (!IsReusableOperand(operand))
                    return false;
            }
            return true;
        }

        private static bool IsReusableOperand(GimpleOperandInfo operand)
        {
            if (operand.WritesMemory || operand.ContainsCall ||
                (!operand.IsAddress && IsVolatileOrAtomic(operand.Original.Type)) ||
                operand.Name is not null && HasExplicitRegister(operand.Name))
                return false;
            foreach (var child in operand.Children)
            {
                if (!IsReusableOperand(child))
                    return false;
            }
            return true;
        }
    }
}
