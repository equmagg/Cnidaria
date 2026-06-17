using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class LinearVerifier
    {
        public static void Verify(GenTreeProgram program)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            for (int i = 0; i < program.Methods.Length; i++)
                Verify(program.Methods[i]);
        }

        public static void Verify(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            VerifyCore(method);
        }

        public static void VerifyBeforeLsra(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            VerifyCore(method);
            VerifyLoweringInvariants(method);
        }

        private static void VerifyCore(GenTreeMethod method)
        {
            VerifyBlocks(method);
            VerifyLinearBlockOrder(method);
            VerifyOperands(method);
            VerifyValues(method);
            VerifySsaBinding(method);
            VerifyRefPositions(method);
        }


        private static void VerifyLoweringInvariants(GenTreeMethod method)
        {
            var useRefsByNodeAndUse = new Dictionary<(int NodeId, int UseIndex), List<LinearRefPosition>>();
            var defRefsByNode = new Dictionary<int, List<LinearRefPosition>>();
            var internalRefsByNode = new Dictionary<int, List<LinearRefPosition>>();

            for (int i = 0; i < method.RefPositions.Length; i++)
            {
                var rp = method.RefPositions[i];
                switch (rp.Kind)
                {
                    case LinearRefPositionKind.Use:
                        if (!useRefsByNodeAndUse.TryGetValue((rp.NodeId, rp.OperandIndex), out var uses))
                        {
                            uses = new List<LinearRefPosition>();
                            useRefsByNodeAndUse.Add((rp.NodeId, rp.OperandIndex), uses);
                        }
                        uses.Add(rp);
                        break;

                    case LinearRefPositionKind.Def:
                        if (!defRefsByNode.TryGetValue(rp.NodeId, out var defs))
                        {
                            defs = new List<LinearRefPosition>();
                            defRefsByNode.Add(rp.NodeId, defs);
                        }
                        defs.Add(rp);
                        break;

                    case LinearRefPositionKind.Internal:
                        if (!internalRefsByNode.TryGetValue(rp.NodeId, out var internals))
                        {
                            internals = new List<LinearRefPosition>();
                            internalRefsByNode.Add(rp.NodeId, internals);
                        }
                        internals.Add(rp);
                        break;
                }
            }

            var lastUsePositions = new Dictionary<GenTreeValueKey, int>();
            foreach (var interval in method.LiveIntervals)
            {
                var key = interval.Value.LinearValueKey;
                lastUsePositions[key] = interval.UsePositions.Length == 0 ? -1 : interval.UsePositions[interval.UsePositions.Length - 1];
            }

            var seenLoweringNodeIds = new HashSet<int>();
            for (int n = 0; n < method.LinearNodes.Length; n++)
            {
                var node = method.LinearNodes[n];
                if ((uint)node.LinearId >= (uint)method.LinearNodes.Length)
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node list index {n} contains out-of-range linear node id {node.LinearId}.");

                if (!seenLoweringNodeIds.Add(node.LinearId))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: duplicate linear node id {node.LinearId}.");

                if (node.LinearKind == GenTreeLinearKind.Tree && !node.HasLoweringFlag(GenTreeLinearFlags.IsStandaloneLoweredNode))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node {node.LinearId} is not marked as standalone lowered LIR.");

                VerifyContainedAndRegOptionalOperands(method, node, useRefsByNodeAndUse);
                VerifyInternalRegisterInvariants(node, internalRefsByNode.TryGetValue(node.LinearId, out var internals) ? internals : null);
                VerifyUnusedValueInvariant(method, node, defRefsByNode.TryGetValue(node.LinearId, out var defs) ? defs : null);
            }

            VerifyLastUseInvariants(method, lastUsePositions);
        }

        private static void VerifyContainedAndRegOptionalOperands(
            GenTreeMethod method,
            GenTree node,
            IReadOnlyDictionary<(int NodeId, int UseIndex), List<LinearRefPosition>> useRefsByNodeAndUse)
        {
            if (node.OperandFlags.IsDefaultOrEmpty || node.Operands.IsDefaultOrEmpty)
                return;

            bool registerOnly = node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands);
            int flagCount = Math.Min(node.OperandFlags.Length, node.Operands.Length);
            int useIndex = 0;

            for (int operandIndex = 0; operandIndex < node.Operands.Length; operandIndex++)
            {
                var operandFlags = operandIndex < flagCount ? node.OperandFlags[operandIndex] : LirOperandFlags.None;
                bool contained = (operandFlags & LirOperandFlags.Contained) != 0;
                bool regOptional = (operandFlags & LirOperandFlags.RegOptional) != 0;

                if (contained && regOptional)
                {
                    throw new InvalidOperationException(
                        $"pre-LSRA lowering invariant failed: node {node.LinearId} operand {operandIndex} is both contained and reg-optional.");
                }

                var operand = node.Operands[operandIndex];
                var value = operand.RegisterResult;

                if (contained)
                {
                    if (!operand.IsContainedInLinear)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: node {node.LinearId} operand {operandIndex} is marked contained, " +
                            $"but operand node {operand.LinearId} is not marked contained-in-linear.");
                    }

                    if (value is not null)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: contained operand {operandIndex} of node {node.LinearId} still exposes register value {value.LinearValueKey}.");
                    }

                    continue;
                }

                if (value is null)
                    continue;

                if (useIndex >= node.RegisterUses.Length || !node.RegisterUses[useIndex].Equals(value))
                {
                    throw new InvalidOperationException(
                        $"pre-LSRA lowering invariant failed: node {node.LinearId} operand {operandIndex} does not map to register-use index {useIndex}.");
                }

                if (regOptional)
                {
                    if (registerOnly)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: node {node.LinearId} operand {operandIndex} is reg-optional on a register-only node.");
                    }

                    if (!useRefsByNodeAndUse.TryGetValue((node.LinearId, useIndex), out var refs) || refs.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: reg-optional operand {operandIndex} of node {node.LinearId} has no use ref-position.");
                    }

                    for (int r = 0; r < refs.Count; r++)
                    {
                        var rp = refs[r];
                        if (!rp.HasFlag(LinearRefPositionFlags.RegOptional) || rp.HasFlag(LinearRefPositionFlags.RequiresRegister))
                        {
                            throw new InvalidOperationException(
                                $"pre-LSRA lowering invariant failed: reg-optional operand {operandIndex} of node {node.LinearId} produced incompatible ref-position {rp}.");
                        }
                    }
                }

                useIndex++;
            }
        }

        private static void VerifyInternalRegisterInvariants(GenTree node, List<LinearRefPosition>? internalRefs)
        {
            byte expectedGeneral = node.LinearLowering.InternalGeneralRegisters;
            byte expectedFloat = node.LinearLowering.InternalFloatRegisters;
            byte actualGeneral = 0;
            byte actualFloat = 0;

            if (internalRefs is not null)
            {
                for (int i = 0; i < internalRefs.Count; i++)
                {
                    var rp = internalRefs[i];
                    if (!rp.HasFlag(LinearRefPositionFlags.Internal) || !rp.HasFlag(LinearRefPositionFlags.RequiresRegister) || rp.Value is not null)
                    {
                        throw new InvalidOperationException(
                            $"pre-LSRA lowering invariant failed: node {node.LinearId} has malformed internal ref-position {rp}.");
                    }

                    if (rp.RegisterClass == RegisterClass.General)
                        actualGeneral += rp.MinimumRegisterCount;
                    else if (rp.RegisterClass == RegisterClass.Float)
                        actualFloat += rp.MinimumRegisterCount;
                    else
                        throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node {node.LinearId} has invalid internal register class {rp.RegisterClass}.");
                }
            }

            if (actualGeneral != expectedGeneral || actualFloat != expectedFloat)
            {
                throw new InvalidOperationException(
                    $"pre-LSRA lowering invariant failed: node {node.LinearId} internal register refs are gen={actualGeneral}, float={actualFloat}; " +
                    $"lowering requested gen={expectedGeneral}, float={expectedFloat}.");
            }
        }

        private static void VerifyUnusedValueInvariant(GenTreeMethod method, GenTree node, List<LinearRefPosition>? defRefs)
        {
            if (node.RegisterResults.Length == 0)
            {
                if (node.HasLoweringFlag(GenTreeLinearFlags.UnusedValue))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node {node.LinearId} is marked unused but defines no value.");
                return;
            }

            bool allResultsUnused = true;
            for (int r = 0; r < node.RegisterResults.Length; r++)
            {
                var result = node.RegisterResults[r];
                if (!method.LiveIntervalByNode.TryGetValue(result, out var interval))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: node {node.LinearId} defines {result.LinearValueKey} without a live interval.");

                if (interval.UsePositions.Length != 0)
                    allResultsUnused = false;
            }

            bool markedUnused = node.HasLoweringFlag(GenTreeLinearFlags.UnusedValue);
            if (allResultsUnused != markedUnused)
            {
                throw new InvalidOperationException(
                    $"pre-LSRA lowering invariant failed: node {node.LinearId} unused flag is {markedUnused}, expected {allResultsUnused}.");
            }

            if (!allResultsUnused && (defRefs is null || defRefs.Count == 0))
                throw new InvalidOperationException($"pre-LSRA lowering invariant failed: used result of node {node.LinearId} has no def ref-position.");
        }

        private static void VerifyLastUseInvariants(GenTreeMethod method, IReadOnlyDictionary<GenTreeValueKey, int> lastUsePositions)
        {
            foreach (var rp in method.RefPositions)
            {
                if (rp.Kind != LinearRefPositionKind.Use || rp.Value is null)
                    continue;

                if (!lastUsePositions.TryGetValue(rp.Value.LinearValueKey, out int lastUsePosition))
                    throw new InvalidOperationException($"pre-LSRA lowering invariant failed: use ref-position {rp} references a value without interval.");

                bool expected = rp.Position == lastUsePosition;
                bool actual = rp.HasFlag(LinearRefPositionFlags.LastUse);
                if (expected != actual)
                {
                    throw new InvalidOperationException(
                        $"pre-LSRA lowering invariant failed: use ref-position {rp} last-use flag is {actual}, expected {expected}.");
                }
            }
        }

        private static void VerifyBlocks(GenTreeMethod method)
        {
            if (method.Blocks.Length != method.Cfg.Blocks.Length)
                throw new InvalidOperationException("linear IR block count does not match CFG block count.");

            var seenNodes = new HashSet<int>();

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                if (block.Id != b)
                    throw new InvalidOperationException($"linear IR requires dense block ids. Expected B{b}, found B{block.Id}.");

                for (int i = 0; i < block.LinearNodes.Length; i++)
                {
                    var node = block.LinearNodes[i];
                    if ((uint)node.LinearId >= (uint)method.LinearNodes.Length)
                        throw new InvalidOperationException($"GenTree GenTree LIR node id {node.LinearId} is outside method node table length {method.LinearNodes.Length}.");

                    if (!seenNodes.Add(node.LinearId))
                        throw new InvalidOperationException("Duplicate GenTree GenTree LIR node id " + node.LinearId + ".");

                    if (node.LinearBlockId != block.Id)
                        throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} is stored in B{block.Id} but says B{node.LinearBlockId}.");

                    if (node.IsPhiCopy)
                    {
                        if ((uint)node.LinearPhiCopyFromBlockId >= (uint)method.Blocks.Length ||
                            (uint)node.LinearPhiCopyToBlockId >= (uint)method.Blocks.Length)
                        {
                            throw new InvalidOperationException(
                                $"linear IR phi copy node {node.LinearId} has invalid edge B{node.LinearPhiCopyFromBlockId}->B{node.LinearPhiCopyToBlockId}.");
                        }

                        if (node.LinearBlockId != node.LinearPhiCopyFromBlockId && node.LinearBlockId != node.LinearPhiCopyToBlockId)
                        {
                            throw new InvalidOperationException(
                                $"linear IR phi copy node {node.LinearId} for edge B{node.LinearPhiCopyFromBlockId}->B{node.LinearPhiCopyToBlockId} " +
                                $"is placed in unrelated B{node.LinearBlockId}.");
                        }
                    }

                    if (node.LinearOrdinal != i)
                        throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} in B{block.Id} has ordinal {node.LinearOrdinal}, expected {i}.");

                    if (node.Previous != (i == 0 ? null : block.LinearNodes[i - 1]))
                        throw new InvalidOperationException($"Broken previous link at GenTree GenTree LIR node {node.LinearId}.");

                    if (node.Next != (i + 1 == block.LinearNodes.Length ? null : block.LinearNodes[i + 1]))
                        throw new InvalidOperationException($"Broken next link at GenTree GenTree LIR node {node.LinearId}.");
                }
            }

            if (seenNodes.Count != method.LinearNodes.Length)
                throw new InvalidOperationException("linear IR method node list does not match block node lists.");

            VerifyGenTreeLinks(method);
        }

        private static void VerifyGenTreeLinks(GenTreeMethod method)
        {
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                for (int i = 0; i < block.LinearNodes.Length; i++)
                {
                    var tree = block.LinearNodes[i];
                    if (tree.LinearId < 0)
                        throw new InvalidOperationException($"GenTree node {tree.Id} in B{block.Id} is missing linear IR identity.");
                    if (tree.LinearBlockId != block.Id)
                        throw new InvalidOperationException($"GenTree node {tree.Id} is stored in B{block.Id} but says B{tree.LinearBlockId}.");
                    if (tree.LinearOrdinal != i)
                        throw new InvalidOperationException($"GenTree node {tree.Id} in B{block.Id} has linear IR ordinal {tree.LinearOrdinal}, expected {i}.");
                    if (tree.Previous != (i == 0 ? null : block.LinearNodes[i - 1]))
                        throw new InvalidOperationException($"Broken GenTree previous link at node {tree.Id}.");
                    if (tree.Next != (i + 1 == block.LinearNodes.Length ? null : block.LinearNodes[i + 1]))
                        throw new InvalidOperationException($"Broken GenTree next link at node {tree.Id}.");
                }
            }
        }

        private static void VerifyLinearBlockOrder(GenTreeMethod method)
        {
            if (method.LinearBlockOrder.Length != method.Blocks.Length)
                throw new InvalidOperationException("linear IR linear block order does not cover every block.");

            var seen = new bool[method.Blocks.Length];
            for (int i = 0; i < method.LinearBlockOrder.Length; i++)
            {
                int blockId = method.LinearBlockOrder[i];
                if ((uint)blockId >= (uint)method.Blocks.Length)
                    throw new InvalidOperationException($"linear IR linear block order contains invalid block id B{blockId}.");
                if (seen[blockId])
                    throw new InvalidOperationException($"linear IR linear block order contains duplicate block id B{blockId}.");
                seen[blockId] = true;
            }
        }

        private static void VerifyOperands(GenTreeMethod method)
        {
            foreach (var node in method.LinearNodes)
            {
                if (node.LinearKind != GenTreeLinearKind.Tree)
                    continue;

                if (!node.OperandFlags.IsDefault && node.OperandFlags.Length != node.Operands.Length)
                    throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} has operand flag count {node.OperandFlags.Length} but operand count {node.Operands.Length}.");

                int useIndex = 0;
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    var flags = node.OperandFlags.IsDefaultOrEmpty ? LirOperandFlags.None : node.OperandFlags[i];
                    if ((flags & LirOperandFlags.Contained) != 0)
                        continue;

                    var operandTree = node.Operands[i];
                    if (operandTree.RegisterResult is null)
                        continue;

                    if (useIndex >= node.RegisterUses.Length || !node.RegisterUses[useIndex].Equals(operandTree.RegisterResult))
                        throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} operand/use mapping is inconsistent at operand {i}.");

                    useIndex++;
                }

                if (useIndex != node.RegisterUses.Length)
                    throw new InvalidOperationException($"GenTree GenTree LIR node {node.LinearId} has {node.RegisterUses.Length} uses but only {useIndex} non-contained operands.");
            }
        }

        private static void VerifyValues(GenTreeMethod method)
        {
            var declared = new HashSet<GenTreeValueKey>();
            var tempDefinitions = new Dictionary<GenTree, GenTree>();
            var tempUseCounts = new Dictionary<GenTree, int>();
            var tempUseBlocks = new Dictionary<GenTree, int>();

            for (int i = 0; i < method.Values.Length; i++)
            {
                var info = method.Values[i];
                if (!declared.Add(info.Value))
                    throw new InvalidOperationException("Duplicate GenTree value " + info.Value + ".");

                if (!info.RepresentativeNode.LinearValueKey.Equals(info.Value))
                {
                    throw new InvalidOperationException(
                        "GenTree value " + info.Value + " is represented by node " + info.RepresentativeNode +
                        " whose linear key is " + info.RepresentativeNode.LinearValueKey + ".");
                }
            }

            foreach (var node in method.LinearNodes)
            {
                if (node.RegisterResult is not null)
                {
                    var result = node.RegisterResult;
                    var resultKey = result.LinearValueKey;
                    if (!declared.Contains(resultKey))
                        throw new InvalidOperationException($"Node {node.LinearId} defines undeclared value {resultKey}.");

                    if (method.GetValueInfo(resultKey).Origin == GenTreeValueOrigin.TreeNode)
                    {
                        if (tempDefinitions.ContainsKey(result))
                            throw new InvalidOperationException("GenTree LIR tree node " + result + " has multiple definitions.");
                        tempDefinitions.Add(result, node);
                    }
                }

                for (int i = 0; i < node.RegisterUses.Length; i++)
                {
                    var use = node.RegisterUses[i];
                    var useKey = use.LinearValueKey;
                    if (!declared.Contains(useKey))
                        throw new InvalidOperationException($"Node {node.LinearId} uses undeclared value {useKey}.");

                    if (method.GetValueInfo(useKey).Origin == GenTreeValueOrigin.TreeNode)
                    {
                        tempUseCounts.TryGetValue(use, out int count);
                        count++;
                        tempUseCounts[use] = count;
                        tempUseBlocks[use] = node.LinearBlockId;
                        if (count > 1)
                        {
                            throw new InvalidOperationException($"GenTree LIR tree node {use} has more than one use.");
                        }
                    }
                }
            }

            foreach (var kv in tempUseCounts)
            {
                if (!tempDefinitions.TryGetValue(kv.Key, out var defNode))
                    throw new InvalidOperationException($"GenTree LIR tree node {kv.Key} is used without a definition.");

                if (tempUseBlocks[kv.Key] != defNode.LinearBlockId)
                {
                    throw new InvalidOperationException($"GenTree LIR tree node {kv.Key} crosses a basic-block boundary.");
                }
            }
        }


        private static void VerifySsaBinding(GenTreeMethod method)
        {
            var ssa = method.Ssa;
            if (ssa is null)
                return;

            var declaredSsaValues = new HashSet<SsaValueName>();
            for (int i = 0; i < ssa.InitialValues.Length; i++)
                declaredSsaValues.Add(ssa.InitialValues[i]);
            for (int i = 0; i < ssa.ValueDefinitions.Length; i++)
                declaredSsaValues.Add(ssa.ValueDefinitions[i].Name);

            for (int i = 0; i < method.Values.Length; i++)
            {
                var key = method.Values[i].Value;
                if (!key.IsSsaValue)
                    continue;

                var name = new SsaValueName(key.SsaSlot, key.SsaVersion);
                if (!declaredSsaValues.Contains(name))
                    throw new InvalidOperationException("linear IR contains SSA value " + name + " that is not declared by the SSA side table.");
            }

            var expectedPhiCopies = new Dictionary<(int from, int to, GenTreeValueKey source, GenTreeValueKey destination), int>();
            for (int b = 0; b < ssa.Blocks.Length; b++)
            {
                var block = ssa.Blocks[b];
                for (int p = 0; p < block.Phis.Length; p++)
                {
                    var phi = block.Phis[p];
                    var destination = GenTreeValueKey.ForSsaValue(phi.Target);
                    for (int i = 0; i < phi.Inputs.Length; i++)
                    {
                        var input = phi.Inputs[i];
                        if (input.Value.Equals(phi.Target))
                            continue;

                        var key = (input.PredecessorBlockId, phi.BlockId, GenTreeValueKey.ForSsaValue(input.Value), destination);
                        expectedPhiCopies.TryGetValue(key, out int count);
                        expectedPhiCopies[key] = count + 1;
                    }
                }
            }

            var actualPhiCopies = new Dictionary<(int from, int to, GenTreeValueKey source, GenTreeValueKey destination), int>();
            foreach (var node in method.LinearNodes)
            {
                VerifyPromotedValueIdentity(method, node, node.RegisterResult, isDefinition: true);
                for (int u = 0; u < node.RegisterUses.Length; u++)
                    VerifyPromotedValueIdentity(method, node, node.RegisterUses[u], isDefinition: false);

                if (node.SsaStoreTargetName.HasValue)
                {
                    if (node.RegisterResult is null)
                        throw new InvalidOperationException("SSA store node " + node.LinearId + " has no register result value.");

                    var targetKey = GenTreeValueKey.ForSsaValue(node.SsaStoreTargetName.Value);
                    if (!node.RegisterResult.LinearValueKey.Equals(targetKey))
                        throw new InvalidOperationException("SSA store node " + node.LinearId + " defines " + node.RegisterResult.LinearValueKey + " but is annotated as " + targetKey + ".");
                }

                if (!node.IsPhiCopy)
                    continue;

                if (node.RegisterUses.Length != 1 || node.RegisterResult is null)
                    throw new InvalidOperationException("SSA phi copy node " + node.LinearId + " must have one source and one destination.");

                var source = node.RegisterUses[0].LinearValueKey;
                var destination = node.RegisterResult.LinearValueKey;
                if (!source.IsSsaValue || !destination.IsSsaValue)
                    throw new InvalidOperationException("SSA phi copy node " + node.LinearId + " must copy SSA values, got " + source + " -> " + destination + ".");

                var key = (node.LinearPhiCopyFromBlockId, node.LinearPhiCopyToBlockId, source, destination);
                actualPhiCopies.TryGetValue(key, out int count);
                actualPhiCopies[key] = count + 1;
            }

            foreach (var kv in expectedPhiCopies)
            {
                actualPhiCopies.TryGetValue(kv.Key, out int actual);
                if (actual != kv.Value)
                    throw new InvalidOperationException("Missing or duplicated SSA phi edge copy for B" + kv.Key.from + "->B" + kv.Key.to + " " + kv.Key.source + " -> " + kv.Key.destination + ".");
            }

            foreach (var kv in actualPhiCopies)
            {
                expectedPhiCopies.TryGetValue(kv.Key, out int expected);
                if (expected != kv.Value)
                    throw new InvalidOperationException("Unexpected SSA phi edge copy for B" + kv.Key.from + "->B" + kv.Key.to + " " + kv.Key.source + " -> " + kv.Key.destination + ".");
            }
        }

        private static void VerifyPromotedValueIdentity(GenTreeMethod method, GenTree owner, GenTree? value, bool isDefinition)
        {
            if (value is null)
                return;

            var descriptor = value.LocalDescriptor;
            if (descriptor is null || !descriptor.SsaPromoted)
                return;

            if (!value.LinearValueKey.IsSsaValue)
            {
                string direction = isDefinition ? "defines" : "uses";
                throw new InvalidOperationException("linear IR node " + owner.LinearId + " " + direction + " promoted local " + descriptor + " through a raw local value key " + value.LinearValueKey + ".");
            }

            var key = value.LinearValueKey;
            bool sameSlot = key.SsaSlot.HasLclNum
                ? key.SsaSlot.LclNum == descriptor.LclNum
                : descriptor.Kind switch
                {
                    GenLocalKind.Argument => key.SsaSlot.Kind == SsaSlotKind.Arg && key.SsaSlot.Index == descriptor.Index,
                    GenLocalKind.Local => key.SsaSlot.Kind == SsaSlotKind.Local && key.SsaSlot.Index == descriptor.Index,
                    GenLocalKind.Temporary => key.SsaSlot.Kind == SsaSlotKind.Temp && key.SsaSlot.Index == descriptor.Index,
                    _ => false,
                };

            if (!sameSlot)
                throw new InvalidOperationException("SSA value " + key + " is attached to mismatched local descriptor " + descriptor + ".");
        }


        private static void VerifyRefPositions(GenTreeMethod method)
        {
            int previousPosition = -1;
            for (int i = 0; i < method.RefPositions.Length; i++)
            {
                var rp = method.RefPositions[i];
                if (rp.Position < 0)
                    throw new InvalidOperationException($"linear IR ref-position has negative position: {rp}.");
                if (rp.Position < previousPosition)
                    throw new InvalidOperationException("linear IR ref-positions are not sorted by position.");
                previousPosition = rp.Position;

                if (rp.Kind is LinearRefPositionKind.Use or LinearRefPositionKind.Def)
                {
                    var valueKey = rp.Value is null ? default : rp.Value.LinearValueKey;
                    if (rp.Value is null || !method.ValueInfoByNode.ContainsKey(valueKey))
                        throw new InvalidOperationException($"linear IR ref-position references an unknown value: {rp}.");
                    if (rp.RegisterClass == RegisterClass.Invalid)
                        throw new InvalidOperationException($"GenTree value ref-position has invalid register class: {rp}.");
                    if (rp.FixedRegister != MachineRegister.Invalid && !MachineRegisters.IsRegisterInClass(rp.FixedRegister, rp.RegisterClass))
                        throw new InvalidOperationException($"linear IR fixed ref-position register does not match its class: {rp}.");
                    if (rp.IsAbiSegment && rp.AbiSegmentSize <= 0)
                        throw new InvalidOperationException($"linear IR ABI segment ref-position has invalid segment size: {rp}.");
                }
                else if (rp.Kind == LinearRefPositionKind.Kill)
                {
                    if (rp.Value is not null)
                        throw new InvalidOperationException($"linear IR kill ref-position must not carry a value: {rp}.");
                    if (rp.RegisterMask == 0)
                        throw new InvalidOperationException($"linear IR kill ref-position must carry a non-empty kill register mask: {rp}.");
                    if (rp.FixedRegister != MachineRegister.Invalid && (rp.RegisterMask & MachineRegisters.MaskOf(rp.FixedRegister)) == 0)
                        throw new InvalidOperationException($"linear IR kill ref-position fixed register is not present in its kill mask: {rp}.");
                    if (rp.FixedRegister != MachineRegister.Invalid &&
                        rp.RegisterClass != RegisterClass.Invalid &&
                        !MachineRegisters.IsRegisterInClass(rp.FixedRegister, rp.RegisterClass))
                    {
                        throw new InvalidOperationException($"linear IR kill ref-position register does not match its class: {rp}.");
                    }
                }
                else if (rp.Kind == LinearRefPositionKind.Internal)
                {
                    if (rp.Value is not null)
                        throw new InvalidOperationException($"linear IR internal ref-position must not carry a value: {rp}.");
                    if (rp.FixedRegister != MachineRegister.Invalid)
                        throw new InvalidOperationException($"linear IR internal ref-position must be allocated from its mask, not pre-fixed: {rp}.");
                    if (rp.RegisterClass == RegisterClass.Invalid)
                        throw new InvalidOperationException($"linear IR internal ref-position has invalid register class: {rp}.");
                    if (rp.RegisterMask == 0)
                        throw new InvalidOperationException($"linear IR internal ref-position has empty register mask: {rp}.");
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported linear IR ref-position kind: {rp.Kind}.");
                }
            }
        }
    }
}
