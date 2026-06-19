using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal static class RegisterAllocationVerifier
    {
        public static void Verify(RegisterAllocatedMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            VerifyAllValuesAllocated(method);
            VerifyNoOverlappingRegisterIntervals(method);
            VerifyNoCallerSavedLiveAcrossCalls(method);
            VerifyStackFrameLayout(method);
            VerifyNodeOperands(method);
            VerifyCfgEdgeLocations(method);
            VerifyPrologEpilog(method);
            VerifyUnwindCodes(method);
            VerifyGcInfo(method);
            VerifyFunclets(method);
            VerifyFrameRegions(method);
        }


        private static void VerifyCfgEdgeLocations(RegisterAllocatedMethod method)
        {
            var layout = AllocatorVerifierPositionLayout.Build(method);

            for (int fromId = 0; fromId < method.GenTreeMethod.Cfg.Blocks.Length; fromId++)
            {
                var cfgBlock = method.GenTreeMethod.Cfg.Blocks[fromId];
                for (int s = 0; s < cfgBlock.Successors.Length; s++)
                {
                    var edge = cfgBlock.Successors[s];
                    if ((uint)edge.FromBlockId >= (uint)method.Blocks.Length || (uint)edge.ToBlockId >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException($"post-LSRA CFG location invariant failed: invalid edge {edge}.");

                    var state = BuildExpectedLocationState(method, layout.BlockEndPositions[edge.FromBlockId]);
                    ApplyTrailingEdgeMoves(method, edge, method.Blocks[edge.FromBlockId], state);
                    ApplyLeadingSyntheticMoves(method, edge, method.Blocks[edge.ToBlockId], state);
                    VerifyEdgeLiveInLocations(method, layout, edge, state);
                }
            }
        }

        private static Dictionary<GenTreeValueKey, RegisterValueLocation> BuildExpectedLocationState(
            RegisterAllocatedMethod method,
            int position)
        {
            var state = new Dictionary<GenTreeValueKey, RegisterValueLocation>();
            for (int i = 0; i < method.Allocations.Length; i++)
            {
                var allocation = method.Allocations[i];
                if (!IsAllocationLiveAt(allocation, position))
                    continue;

                var info = method.GenTreeMethod.GetValueInfo(allocation.ValueKey);
                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                var location = allocation.ValueLocationAt(position, abi);
                if (!location.IsEmpty)
                    state[allocation.ValueKey] = location;
            }
            return state;
        }

        private static void VerifyEdgeLiveInLocations(
            RegisterAllocatedMethod method,
            AllocatorVerifierPositionLayout layout,
            CfgEdge edge,
            IReadOnlyDictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            int toStart = layout.BlockStartPositions[edge.ToBlockId];
            for (int i = 0; i < method.Allocations.Length; i++)
            {
                var allocation = method.Allocations[i];
                if (!IsAllocationLiveAt(allocation, toStart))
                    continue;

                var info = method.GenTreeMethod.GetValueInfo(allocation.ValueKey);
                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                var expected = allocation.ValueLocationAt(toStart, abi);
                if (expected.IsEmpty)
                    continue;

                if (!state.TryGetValue(allocation.ValueKey, out var actual))
                {
                    throw new InvalidOperationException(
                        $"post-LSRA CFG location invariant failed on {edge}: live-in value {allocation.ValueKey} has no physical home; expected {expected}.");
                }

                if (!LocationsEqual(actual, expected))
                {
                    throw new InvalidOperationException(
                        $"post-LSRA CFG location invariant failed on {edge}: value {allocation.ValueKey} is {actual}, expected {expected} after synthetic moves.");
                }
            }
        }

        private static void ApplyTrailingEdgeMoves(
            RegisterAllocatedMethod method,
            CfgEdge edge,
            GenTreeBlock block,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            var nodes = block.LinearNodes;
            if (nodes.Length == 0)
                return;

            int end = nodes.Length - 1;
            while (end >= 0 && IsVerifierBlockTerminatorNode(nodes[end]))
                end--;

            int start = end;
            while (start >= 0)
            {
                var node = nodes[start];
                if (IsBlockEntrySplitMove(node) || node.IsPhiCopy)
                {
                    start--;
                    continue;
                }

                break;
            }

            for (int i = start + 1; i <= end; i++)
            {
                var node = nodes[i];
                if (IsMatchingEntryPhiCopy(edge, node) || IsBlockEntrySplitMove(node))
                    ApplyNodeEffectsToLocationState(method, node, state);
            }
        }

        private static void ApplyLeadingSyntheticMoves(
            RegisterAllocatedMethod method,
            CfgEdge edge,
            GenTreeBlock block,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            for (int i = 0; i < block.LinearNodes.Length; i++)
            {
                var node = block.LinearNodes[i];
                if (IsMatchingEntryPhiCopy(edge, node))
                {
                    ApplyNodeEffectsToLocationState(method, node, state);
                    continue;
                }

                if (node.IsPhiCopy)
                    continue;

                break;
            }
        }

        private static void ApplyMissingSemanticPhiCopiesFromBlock(
            RegisterAllocatedMethod method,
            AllocatorVerifierPositionLayout layout,
            CfgEdge edge,
            GenTreeBlock block,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            for (int i = 0; i < block.LinearNodes.Length; i++)
            {
                var node = block.LinearNodes[i];
                if (!IsMatchingEntryPhiCopy(edge, node))
                    continue;

                if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                    continue;

                var destination = node.RegisterResult;
                var destinationKey = destination.LinearValueKey;
                var source = node.RegisterUses[0];
                if (state.ContainsKey(destinationKey))
                    continue;

                if (!TryGetSourceLocationForSemanticPhi(method, layout, edge, source, state, out var sourceLocation))
                    continue;

                var destinationInfo = method.GenTreeMethod.GetValueInfo(destinationKey);
                var destinationAbi = MachineAbi.ClassifyStorageValue(destinationInfo.Type, destinationInfo.StackKind);
                state[destinationKey] = RetargetLocation(destination, destinationAbi, sourceLocation);
            }
        }

        private static bool TryGetSourceLocationForSemanticPhi(
            RegisterAllocatedMethod method,
            AllocatorVerifierPositionLayout layout,
            CfgEdge edge,
            GenTree source,
            IReadOnlyDictionary<GenTreeValueKey, RegisterValueLocation> state,
            out RegisterValueLocation location)
        {
            var sourceKey = source.LinearValueKey;
            if (state.TryGetValue(sourceKey, out location) && !location.IsEmpty)
                return true;

            if (TryGetAllocation(method, sourceKey, out var allocation))
            {
                var info = method.GenTreeMethod.GetValueInfo(sourceKey);
                var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
                int position = layout.BlockEndPositions[edge.FromBlockId];
                if (IsAllocationLiveAt(allocation, position))
                {
                    location = allocation.ValueLocationAt(position, abi);
                    return !location.IsEmpty;
                }
            }

            location = default;
            return false;
        }

        private static RegisterValueLocation RetargetLocation(
            GenTree destination,
            AbiValueInfo destinationAbi,
            RegisterValueLocation sourceLocation)
        {
            if (sourceLocation.IsEmpty || destinationAbi.PassingKind == AbiValuePassingKind.Void)
                return new RegisterValueLocation(destination, destinationAbi.PassingKind, RegisterOperand.None);

            if (destinationAbi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                if (sourceLocation.IsFragmented)
                    return new RegisterValueLocation(destination, destinationAbi.PassingKind, RegisterOperand.None, sourceLocation.Fragments);

                return new RegisterValueLocation(destination, destinationAbi.PassingKind, sourceLocation.Scalar);
            }

            return new RegisterValueLocation(destination, destinationAbi.PassingKind, sourceLocation[0]);
        }

        private static bool IsMatchingEntryPhiCopy(CfgEdge edge, GenTree node)
            => node.IsPhiCopy &&
               node.LinearPhiCopyFromBlockId == edge.FromBlockId &&
               node.LinearPhiCopyToBlockId == edge.ToBlockId;

        private static bool IsVerifierBlockTerminatorNode(GenTree node)
            => node.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse or
               GenTreeKind.Return or GenTreeKind.Throw or GenTreeKind.Rethrow or GenTreeKind.EndFinally;

        private static bool IsBlockEntrySplitMove(GenTree node)
            => GenTreeLirKinds.IsCopyKind(node.Kind) &&
               node.LinearKind == GenTreeLinearKind.Copy &&
               (node.MoveFlags & MoveFlags.Split) != 0;

        private static void ApplyNodeEffectsToLocationState(
            RegisterAllocatedMethod method,
            GenTree node,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            var results = node.Results;
            if (results.Length == 0)
                return;

            var logicalResults = ResolveLogicalResultValues(method, node, results.Length);
            if (logicalResults.Length == 0)
                return;

            if (logicalResults.Length == 1)
            {
                ApplySingleLogicalResult(method, logicalResults[0], results, state);
                return;
            }

            var grouped = new Dictionary<GenTreeValueKey, List<RegisterOperand>>();
            var values = new Dictionary<GenTreeValueKey, GenTree>();
            int count = Math.Min(logicalResults.Length, results.Length);
            for (int i = 0; i < count; i++)
            {
                var value = logicalResults[i];
                var key = value.LinearValueKey;
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<RegisterOperand>();
                    grouped.Add(key, list);
                    values.Add(key, value);
                }
                list.Add(results[i]);
            }

            foreach (var kv in grouped)
                ApplySingleLogicalResult(method, values[kv.Key], kv.Value.ToImmutableArray(), state);
        }

        private static ImmutableArray<GenTree> ResolveLogicalResultValues(
            RegisterAllocatedMethod method,
            GenTree node,
            int resultOperandCount)
        {
            if (!node.RegisterResults.IsDefaultOrEmpty)
            {
                if (node.RegisterResults.Length == 1 && resultOperandCount > 1)
                    return node.RegisterResults;
                return node.RegisterResults;
            }

            var keys = node.LsraInfo.CodegenResultValues;
            if (keys.IsDefaultOrEmpty)
                return ImmutableArray<GenTree>.Empty;

            var builder = ImmutableArray.CreateBuilder<GenTree>(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                if (method.GenTreeMethod.ValueInfoByNode.TryGetValue(keys[i], out var info))
                    builder.Add(info.RepresentativeNode);
            }
            return builder.ToImmutable();
        }

        private static void ApplySingleLogicalResult(
            RegisterAllocatedMethod method,
            GenTree value,
            ImmutableArray<RegisterOperand> operands,
            Dictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            if (operands.Length == 0)
                return;

            var info = method.GenTreeMethod.GetValueInfo(value.LinearValueKey);
            var abi = MachineAbi.ClassifyStorageValue(info.Type, info.StackKind);
            if (abi.PassingKind != AbiValuePassingKind.MultiRegister)
            {
                state[value.LinearValueKey] = new RegisterValueLocation(value, abi.PassingKind, operands[0]);
                return;
            }

            var segments = MachineAbi.GetRegisterSegments(abi);
            if (operands.Length >= segments.Length)
            {
                state[value.LinearValueKey] = new RegisterValueLocation(
                    value,
                    abi.PassingKind,
                    RegisterOperand.None,
                    FirstOperands(operands, segments.Length));
                return;
            }

            var fragments = CreateMutableFragmentHome(method, value, abi, state);
            for (int i = 0; i < operands.Length; i++)
            {
                int index = FindFragmentIndex(fragments, operands[i]);
                if (index < 0 && operands.Length == 1)
                    index = FindFragmentIndexInExpectedHome(method, value, abi, operands[i]);
                if ((uint)index < (uint)fragments.Count)
                    fragments[index] = operands[i];
            }

            state[value.LinearValueKey] = new RegisterValueLocation(value, abi.PassingKind, RegisterOperand.None, fragments.ToImmutableArray());
        }


        private static ImmutableArray<RegisterOperand> FirstOperands(ImmutableArray<RegisterOperand> operands, int count)
        {
            if (operands.Length == count)
                return operands;

            var builder = ImmutableArray.CreateBuilder<RegisterOperand>(count);
            for (int i = 0; i < count; i++)
                builder.Add(operands[i]);
            return builder.ToImmutable();
        }

        private static List<RegisterOperand> CreateMutableFragmentHome(
            RegisterAllocatedMethod method,
            GenTree value,
            AbiValueInfo abi,
            IReadOnlyDictionary<GenTreeValueKey, RegisterValueLocation> state)
        {
            var key = value.LinearValueKey;
            var segments = MachineAbi.GetRegisterSegments(abi);
            if (state.TryGetValue(key, out var current) && current.Count == segments.Length)
            {
                var result = new List<RegisterOperand>(segments.Length);
                for (int i = 0; i < current.Count; i++)
                    result.Add(current[i]);
                return result;
            }

            if (TryGetAllocation(method, key, out var allocation))
            {
                var expected = allocation.ValueLocationAt(allocation.DefinitionPosition, abi);
                if (expected.Count == segments.Length)
                {
                    var result = new List<RegisterOperand>(segments.Length);
                    for (int i = 0; i < expected.Count; i++)
                        result.Add(expected[i]);
                    return result;
                }
            }

            var empty = new List<RegisterOperand>(segments.Length);
            for (int i = 0; i < segments.Length; i++)
                empty.Add(RegisterOperand.None);
            return empty;
        }

        private static int FindFragmentIndex(IReadOnlyList<RegisterOperand> fragments, RegisterOperand operand)
        {
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Equals(operand))
                    return i;
            }
            return -1;
        }

        private static int FindFragmentIndexInExpectedHome(
            RegisterAllocatedMethod method,
            GenTree value,
            AbiValueInfo abi,
            RegisterOperand operand)
        {
            if (!TryGetAllocation(method, value.LinearValueKey, out var allocation))
                return -1;

            var expected = allocation.ValueLocationAt(allocation.DefinitionPosition, abi);
            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i].Equals(operand))
                    return i;
            }
            return -1;
        }

        private static bool TryGetAllocation(RegisterAllocatedMethod method, GenTreeValueKey key, out RegisterAllocationInfo allocation)
        {
            for (int i = 0; i < method.Allocations.Length; i++)
            {
                if (method.Allocations[i].ValueKey.Equals(key))
                {
                    allocation = method.Allocations[i];
                    return true;
                }
            }

            allocation = null!;
            return false;
        }

        private static bool LocationsEqual(RegisterValueLocation actual, RegisterValueLocation expected)
        {
            if (actual.PassingKind != expected.PassingKind || actual.Count != expected.Count)
                return false;

            for (int i = 0; i < actual.Count; i++)
            {
                if (!actual[i].Equals(expected[i]))
                    return false;
            }

            return true;
        }

        private static bool IsAllocationLiveAt(RegisterAllocationInfo allocation, int position)
        {
            for (int i = 0; i < allocation.Ranges.Length; i++)
            {
                var range = allocation.Ranges[i];
                if (range.Start <= position && position < range.End)
                    return true;
            }
            return false;
        }

        private sealed class AllocatorVerifierPositionLayout
        {
            public readonly Dictionary<int, int> NodePositions;
            public readonly int[] BlockStartPositions;
            public readonly int[] BlockEndPositions;

            private AllocatorVerifierPositionLayout(Dictionary<int, int> nodePositions, int[] blockStartPositions, int[] blockEndPositions)
            {
                NodePositions = nodePositions;
                BlockStartPositions = blockStartPositions;
                BlockEndPositions = blockEndPositions;
            }

            public static AllocatorVerifierPositionLayout Build(RegisterAllocatedMethod method)
            {
                if (method.LsraBlockStartPositions.Length == method.Blocks.Length &&
                    method.LsraBlockEndPositions.Length == method.Blocks.Length &&
                    method.LsraNodePositions.Count != 0)
                {
                    var starts = new int[method.LsraBlockStartPositions.Length];
                    var ends = new int[method.LsraBlockEndPositions.Length];
                    for (int i = 0; i < starts.Length; i++)
                        starts[i] = method.LsraBlockStartPositions[i];
                    for (int i = 0; i < ends.Length; i++)
                        ends[i] = method.LsraBlockEndPositions[i];

                    return new AllocatorVerifierPositionLayout(new Dictionary<int, int>(method.LsraNodePositions), starts, ends);
                }

                var genMethod = method.GenTreeMethod;
                var nodePositions = new Dictionary<int, int>();
                var fallbackStarts = new int[genMethod.Blocks.Length];
                var fallbackEnds = new int[genMethod.Blocks.Length];
                int position = 0;

                var order = genMethod.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(genMethod.Cfg)
                    : LinearBlockOrder.Normalize(genMethod.Cfg, genMethod.LinearBlockOrder);

                for (int o = 0; o < order.Length; o++)
                {
                    int b = order[o];
                    fallbackStarts[b] = position;
                    var nodes = genMethod.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        if (!nodePositions.TryAdd(node.LinearId, position))
                            throw new InvalidOperationException($"post-LSRA CFG location invariant failed: duplicate input LIR node id {node.LinearId}.");

                        if (node.IsPhiCopy)
                        {
                            while (n + 1 < nodes.Length && SamePhiCopyGroup(node, nodes[n + 1]))
                            {
                                n++;
                                if (!nodePositions.TryAdd(nodes[n].LinearId, position))
                                    throw new InvalidOperationException($"post-LSRA CFG location invariant failed: duplicate input LIR node id {nodes[n].LinearId}.");
                            }
                        }

                        position += 2;
                    }

                    fallbackEnds[b] = position;
                    position += 2;
                }

                return new AllocatorVerifierPositionLayout(nodePositions, fallbackStarts, fallbackEnds);
            }

            private static bool SamePhiCopyGroup(GenTree left, GenTree right)
                => left.IsPhiCopy &&
                   right.IsPhiCopy &&
                   left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                   left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;
        }

        private static void VerifyFunclets(RegisterAllocatedMethod method)
        {
            if (method.Funclets.Length == 0)
                throw new InvalidOperationException("Register method has no root funclet metadata.");

            if (!method.Funclets[0].IsRoot || method.Funclets[0].EntryBlockId != 0)
                throw new InvalidOperationException("Register method funclet 0 must be the root funclet at B0.");

            var seenBlocks = new HashSet<int>();
            for (int i = 0; i < method.Funclets.Length; i++)
            {
                var funclet = method.Funclets[i];
                if (funclet.Index != i)
                    throw new InvalidOperationException($"Funclet index mismatch: expected {i}, found {funclet.Index}.");
                if ((uint)funclet.EntryBlockId >= (uint)method.Blocks.Length)
                    throw new InvalidOperationException($"Funclet {i} has invalid entry block B{funclet.EntryBlockId}.");
                if (funclet.ParentFuncletIndex >= method.Funclets.Length)
                    throw new InvalidOperationException($"Funclet {i} has invalid parent funclet {funclet.ParentFuncletIndex}.");
                if (i == 0 && funclet.ParentFuncletIndex != -1)
                    throw new InvalidOperationException("Root funclet must not have a parent funclet.");
                if (i != 0 && funclet.ParentFuncletIndex < 0)
                    throw new InvalidOperationException($"Non-root funclet {i} must have a parent funclet.");
                if (i != 0 && funclet.ParentFuncletIndex >= i)
                    throw new InvalidOperationException($"Funclet {i} parent must be emitted before the child funclet.");
                if (!ContainsBlock(funclet.BlockIds, funclet.EntryBlockId))
                    throw new InvalidOperationException($"Funclet {i} does not contain its entry block B{funclet.EntryBlockId}.");

                for (int b = 0; b < funclet.BlockIds.Length; b++)
                {
                    int blockId = funclet.BlockIds[b];
                    if ((uint)blockId >= (uint)method.Blocks.Length)
                        throw new InvalidOperationException($"Funclet {i} contains invalid block B{blockId}.");
                    if (!seenBlocks.Add(blockId))
                        throw new InvalidOperationException($"Block B{blockId} belongs to more than one funclet.");
                }
            }

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                if (!seenBlocks.Contains(method.Blocks[b].Id))
                    throw new InvalidOperationException($"Block B{method.Blocks[b].Id} does not belong to any funclet.");
            }
        }

        private static bool ContainsBlock(ImmutableArray<int> blocks, int blockId)
        {
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] == blockId)
                    return true;
            }
            return false;
        }

        private static void VerifyFrameRegions(RegisterAllocatedMethod method)
        {
            var nodesById = new Dictionary<int, GenTree>();
            foreach (var node in method.LinearNodes)
                nodesById[node.Id] = node;

            for (int i = 0; i < method.FrameRegions.Length; i++)
            {
                var region = method.FrameRegions[i];
                if (region.FuncletIndex >= method.Funclets.Length)
                    throw new InvalidOperationException($"Frame region has invalid funclet index {region.FuncletIndex}.");
                if (!nodesById.TryGetValue(region.FirstNodeId, out var first))
                    throw new InvalidOperationException($"Frame region starts at missing node {region.FirstNodeId}.");
                if (!nodesById.TryGetValue(region.LastNodeId, out var last))
                    throw new InvalidOperationException($"Frame region ends at missing node {region.LastNodeId}.");
                if (first.BlockId != region.BlockId || last.BlockId != region.BlockId)
                    throw new InvalidOperationException("Frame region endpoints are not in the recorded block.");

                if (first.Kind != GenTreeKind.StackFrameOp || last.Kind != GenTreeKind.StackFrameOp)
                    throw new InvalidOperationException("Frame region endpoints do not match region kind.");
            }
        }

        private static void VerifyUnwindCodes(RegisterAllocatedMethod method)
        {
            var nodesById = new Dictionary<int, GenTree>();
            foreach (var node in method.LinearNodes)
                nodesById[node.Id] = node;

            var seen = new HashSet<int>();
            for (int i = 0; i < method.UnwindCodes.Length; i++)
            {
                var code = method.UnwindCodes[i];
                if (!seen.Add(code.NodeId))
                    throw new InvalidOperationException($"Duplicate unwind code for node {code.NodeId}.");

                if (!nodesById.TryGetValue(code.NodeId, out var node))
                    throw new InvalidOperationException($"Unwind code references missing node {code.NodeId}.");

                if (node.Kind != GenTreeKind.StackFrameOp)
                    throw new InvalidOperationException($"Unwind code references non-prolog node {code.NodeId}.");

                if (node.BlockId != code.BlockId || node.Ordinal != code.Ordinal)
                    throw new InvalidOperationException($"Unwind code placement does not match node {code.NodeId}.");

                if (code.Size < 0 || code.StackOffset < 0)
                    throw new InvalidOperationException($"Unwind code contains invalid stack range: {code}.");
            }

            if (method.HasPrologEpilog && !method.StackFrame.IsEmpty && method.UnwindCodes.Length == 0)
                throw new InvalidOperationException("Non-empty framed method has no unwind codes.");
        }

        private static bool IsFuncletEntryBlock(RegisterAllocatedMethod method, int blockId)
        {
            for (int i = 0; i < method.Funclets.Length; i++)
            {
                if (method.Funclets[i].EntryBlockId == blockId)
                    return true;
            }
            return false;
        }

        private static void VerifyGcInfo(RegisterAllocatedMethod method)
        {
            VerifyGcLiveRanges(method);
            VerifyGcTransitions(method);
            VerifyGcInterruptibleRanges(method);
        }

        private static void VerifyGcLiveRanges(RegisterAllocatedMethod method)
        {
            for (int i = 0; i < method.GcLiveRanges.Length; i++)
            {
                var range = method.GcLiveRanges[i];
                if (range.StartPosition >= range.EndPosition)
                    throw new InvalidOperationException($"Empty GC live range: {range}.");
                if ((uint)range.FuncletIndex >= (uint)method.Funclets.Length)
                    throw new InvalidOperationException($"GC live range has invalid funclet index: {range}.");
                VerifyGcRootIdentity(method, range.Root, "GC live range");
                if (range.Root.Location.IsNone)
                    throw new InvalidOperationException($"GC live range has no storage location: {range}.");
                if (range.Root.Offset < 0)
                    throw new InvalidOperationException($"GC live range has negative field offset: {range}.");
                if (range.Root.Offset != 0 && range.Root.Location.IsRegister)
                    throw new InvalidOperationException($"Field GC live range cannot be represented by a bare register: {range}.");
                if (range.Root.Location.IsFrameSlot && range.Root.Offset >= range.Root.Location.FrameSlotSize)
                    throw new InvalidOperationException($"GC live range offset escapes its frame slot: {range}.");
                if ((range.Flags & RegisterGcLiveRangeFlags.Pinned) != 0 && range.FuncletIndex == 0)
                    throw new InvalidOperationException($"Only filter funclet stack roots may be pinned by GC info: {range}.");
                VerifyOperandStorage(method, range.Root.Location, isUse: true);
            }

            if (method.Funclets.Length > 1 && !method.GcReportOnlyLeafFunclet)
                throw new InvalidOperationException("EH methods must use leaf-funclet-only GC reporting.");
        }

        private static void VerifyGcRootIdentity(RegisterAllocatedMethod method, RegisterGcLiveRoot root, string context)
        {
            if (root.RequiresValueInfo)
            {
                if (!method.GenTreeMethod.ValueInfoByNode.ContainsKey(root.Value.LinearValueKey))
                    throw new InvalidOperationException($"{context} references unknown GenTree value {root.Value}.");
                return;
            }

            if (!root.Location.IsFrameSlot)
                throw new InvalidOperationException($"{context} has descriptor-home identity but is not a frame slot root: {root}.");

            if (root.Value.LocalDescriptor is null)
                throw new InvalidOperationException($"{context} has descriptor-home identity but no local descriptor owner: {root}.");

            var descriptor = root.Value.LocalDescriptor;
            if (root.Type is not null && !ReferenceEquals(root.Type, descriptor.Type))
                throw new InvalidOperationException($"{context} descriptor-home type does not match the owner descriptor: {root}.");
        }

        private static void VerifyGcTransitions(RegisterAllocatedMethod method)
        {
            int previousPosition = -1;
            for (int i = 0; i < method.GcTransitions.Length; i++)
            {
                var transition = method.GcTransitions[i];
                if (transition.Position < previousPosition)
                    throw new InvalidOperationException("GC transitions are not sorted by GenTree LIR position.");
                previousPosition = transition.Position;

                switch (transition.Kind)
                {
                    case RegisterGcTransitionKind.Enter:
                        if (transition.After is null || transition.Before is not null)
                            throw new InvalidOperationException("GC enter transition must contain only the after root.");
                        VerifyTransitionRoot(method, transition.After.Value);
                        break;
                    case RegisterGcTransitionKind.Exit:
                        if (transition.Before is null || transition.After is not null)
                            throw new InvalidOperationException("GC exit transition must contain only the before root.");
                        VerifyTransitionRoot(method, transition.Before.Value);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown GC transition kind {transition.Kind}.");
                }
            }
        }

        private static void VerifyTransitionRoot(RegisterAllocatedMethod method, RegisterGcLiveRoot root)
        {
            VerifyGcRootIdentity(method, root, "GC transition");
            if (root.Location.IsNone)
                throw new InvalidOperationException($"GC transition root has no storage location: {root.Value}.");
            if (root.Offset != 0 && root.Location.IsRegister)
                throw new InvalidOperationException($"GC transition field root cannot be represented by a bare register: {root}.");
            VerifyOperandStorage(method, root.Location, isUse: true);
        }

        private static void VerifyGcInterruptibleRanges(RegisterAllocatedMethod method)
        {
            var nodesById = new Dictionary<int, GenTree>();
            foreach (var node in method.LinearNodes)
                nodesById[node.Id] = node;

            int previousStart = -1;
            for (int i = 0; i < method.GcInterruptibleRanges.Length; i++)
            {
                var range = method.GcInterruptibleRanges[i];
                if (range.StartPosition >= range.EndPosition)
                    throw new InvalidOperationException($"Empty GC interruptible range: {range}.");
                if (range.StartPosition < previousStart)
                    throw new InvalidOperationException("GC interruptible ranges are not sorted by GenTree LIR position.");
                previousStart = range.StartPosition;
                if ((uint)range.FuncletIndex >= (uint)method.Funclets.Length)
                    throw new InvalidOperationException($"GC interruptible range has invalid funclet index: {range}.");
                if (!nodesById.ContainsKey(range.FirstNodeId))
                    throw new InvalidOperationException($"GC interruptible range starts at missing node {range.FirstNodeId}.");
                if (!nodesById.ContainsKey(range.LastNodeId))
                    throw new InvalidOperationException($"GC interruptible range ends at missing node {range.LastNodeId}.");
            }

            if (method.Funclets.Length > 1)
            {
                var hasRangeByFunclet = new bool[method.Funclets.Length];
                for (int i = 0; i < method.GcInterruptibleRanges.Length; i++)
                    hasRangeByFunclet[method.GcInterruptibleRanges[i].FuncletIndex] = true;

                for (int i = 0; i < method.Funclets.Length; i++)
                {
                    if (method.Funclets[i].BlockIds.Length != 0 && !hasRangeByFunclet[i])
                        throw new InvalidOperationException($"Funclet {i} has no GC interruptible range.");
                }
            }
        }

        private static void VerifyPrologEpilog(RegisterAllocatedMethod method)
        {
            bool hasFrameLinearNodes = false;
            foreach (var node in method.LinearNodes)
            {
                if (node.Kind == GenTreeKind.StackFrameOp)
                {
                    hasFrameLinearNodes = true;
                    VerifyFrameNode(method, node);
                }
                else if (node.FrameOperation != FrameOperation.None || node.Immediate != 0)
                {
                    throw new InvalidOperationException("Non-frame node carries frame operation metadata.");
                }
            }

            if (!method.HasPrologEpilog)
            {
                if (hasFrameLinearNodes)
                    throw new InvalidOperationException("Method has prolog/epilog nodes but is not marked as frame-code generated.");
                return;
            }

            if (method.StackFrame.IsEmpty)
            {
                if (hasFrameLinearNodes)
                    throw new InvalidOperationException("Empty-frame method must not contain prolog/epilog nodes.");
                return;
            }

            if (method.Blocks.Length == 0)
                throw new InvalidOperationException("Frame-code generated method has no blocks.");

            var entry = method.Blocks[0].LinearNodes;
            int firstNonProlog = 0;
            while (firstNonProlog < entry.Length && IsPrologFrameNode(entry[firstNonProlog]))
                firstNonProlog++;

            if (firstNonProlog == 0)
                throw new InvalidOperationException("Non-empty-frame method is missing an entry prolog.");

            for (int i = firstNonProlog; i < entry.Length; i++)
            {
                if (IsPrologFrameNode(entry[i]))
                    throw new InvalidOperationException("Prolog nodes must be a contiguous prefix of the entry block.");
            }

            bool sawReturn = false;
            foreach (var block in method.Blocks)
            {
                for (int i = 0; i < block.LinearNodes.Length; i++)
                {
                    var node = block.LinearNodes[i];
                    if (node.Kind == GenTreeKind.StackFrameOp)
                    {
                        if (IsPrologFrameOperation(node.FrameOperation))
                        {
                            if (!IsFuncletEntryBlock(method, block.Id) || !IsContiguousPrologPrefix(block.LinearNodes, i))
                                throw new InvalidOperationException("Prolog node found outside a contiguous funclet-entry prolog prefix.");
                        }
                        else if (IsEpilogFrameOperation(node.FrameOperation))
                        {
                            if (!IsInContiguousEpilogBeforeExit(block.LinearNodes, i))
                                throw new InvalidOperationException("Epilog node is not part of a contiguous sequence immediately preceding a return or funclet exit.");
                        }
                    }

                    if (node.Kind == GenTreeKind.EndFinally ||
                        (node.Kind == GenTreeKind.Branch && node.SourceOp == BytecodeOp.Leave && IsFuncletBlock(method, block.Id)))
                    {
                        if (!HasContiguousEpilogBefore(block.LinearNodes, i))
                            throw new InvalidOperationException("Funclet exit node is missing an immediately preceding epilog sequence.");
                    }

                    if (node.Kind == GenTreeKind.Return)
                    {
                        sawReturn = true;
                        if (!IsHiddenReturnBufferCopyReturn(method, node) &&
                            !HasContiguousEpilogBefore(block.LinearNodes, i) &&
                            (IsFuncletBlock(method, block.Id) || !ReturnMustRunFinallyBeforeMethodExit(method, block.Id)))
                            throw new InvalidOperationException("Return node is missing an immediately preceding epilog sequence.");
                    }
                }
            }

            if (!sawReturn)
                return;
        }

        private static bool IsHiddenReturnBufferCopyReturn(RegisterAllocatedMethod method, GenTree node)
            => node.Kind == GenTreeKind.Return &&
               node.Uses.Length != 0 &&
               MachineAbi.RequiresHiddenReturnBuffer(method.GenTreeMethod.RuntimeMethod);

        private static bool HasContiguousEpilogBefore(ImmutableArray<GenTree> nodes, int returnIndex)
        {
            int i = returnIndex - 1;
            if (i < 0 || !IsEpilogFrameNode(nodes[i]))
                return false;

            while (i >= 0 && IsEpilogFrameNode(nodes[i]))
                i--;

            return true;
        }
        private static bool ReturnMustRunFinallyBeforeMethodExit(RegisterAllocatedMethod method, int blockId)
        {
            var regions = method.GenTreeMethod.Cfg.ExceptionRegions;
            for (int i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                if (region.Kind == CfgExceptionRegionKind.Finally &&
                    blockId >= region.TryStartBlockId &&
                    blockId < region.TryEndBlockIdExclusive)
                {
                    return true;
                }
            }
            return false;
        }
        private static bool IsPrologFrameNode(GenTree node)
            => node.Kind == GenTreeKind.StackFrameOp && IsPrologFrameOperation(node.FrameOperation);
        private static bool IsEpilogFrameNode(GenTree node)
            => node.Kind == GenTreeKind.StackFrameOp && IsEpilogFrameOperation(node.FrameOperation);
        private static bool IsPrologFrameOperation(FrameOperation operation)
            => operation is
                FrameOperation.AllocateFrame or
                FrameOperation.SaveReturnAddress or
                FrameOperation.SaveCalleeSavedRegister or
                FrameOperation.EstablishFramePointer or
                FrameOperation.EnterFuncletFrame;
        private static bool IsEpilogFrameOperation(FrameOperation operation)
            => operation is
                FrameOperation.LeaveFuncletFrame or
                FrameOperation.RestoreStackPointerFromFramePointer or
                FrameOperation.RestoreCalleeSavedRegister or
                FrameOperation.RestoreReturnAddress or
                FrameOperation.FreeFrame;
        private static bool IsContiguousPrologPrefix(ImmutableArray<GenTree> nodes, int index)
        {
            if ((uint)index >= (uint)nodes.Length || !IsPrologFrameNode(nodes[index]))
                return false;
            for (int i = 0; i <= index; i++)
            {
                if (!IsPrologFrameNode(nodes[i]))
                    return false;
            }
            return true;
        }
        private static bool IsInContiguousEpilogBeforeExit(ImmutableArray<GenTree> nodes, int index)
        {
            if ((uint)index >= (uint)nodes.Length || !IsEpilogFrameNode(nodes[index]))
                return false;
            int i = index;
            while (i < nodes.Length && IsEpilogFrameNode(nodes[i]))
                i++;
            return i < nodes.Length &&
                (nodes[i].Kind is GenTreeKind.Return or GenTreeKind.EndFinally ||
                    (nodes[i].Kind == GenTreeKind.Branch && nodes[i].SourceOp == BytecodeOp.Leave));
        }
        private static bool IsFuncletBlock(RegisterAllocatedMethod method, int blockId)
        {
            var funclets = method.Funclets;
            for (int i = 0; i < funclets.Length; i++)
            {
                if (funclets[i].IsRoot)
                    continue;

                var blocks = funclets[i].BlockIds;
                for (int b = 0; b < blocks.Length; b++)
                    if (blocks[b] == blockId)
                        return true;
            }
            return false;
        }
        private static void VerifyFrameNode(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.FrameOperation == FrameOperation.None)
                throw new InvalidOperationException("Frame node has no frame operation.");

            if (node.RegisterResults.Length != 0 ||
                node.RegisterUses.Length != 0 ||
                node.LsraInfo.CodegenResultValues.Length != 0 ||
                node.LsraInfo.CodegenUseValues.Length != 0 ||
                method.GenTreeMethod.ValueInfoByNode.ContainsKey(node.ValueKey))
            {
                throw new InvalidOperationException("Frame node must not carry GenTree value metadata.");
            }

            if (node.Operands.Length != 0)
                throw new InvalidOperationException("Frame node must not have GenTree operands.");

            if (node.Results.Length != 1)
                throw new InvalidOperationException("Frame node must have exactly one result operand.");

            var result = node.Results[0];
            switch (node.FrameOperation)
            {
                case FrameOperation.AllocateFrame:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.StackPointer, "frame allocation result");
                    RequireSingleRegisterUse(node, MachineRegisters.StackPointer, "frame allocation use");
                    RequireImmediate(node, method.StackFrame.FrameSize, "frame allocation size");
                    return;

                case FrameOperation.SaveReturnAddress:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    if (!result.IsFrameSlot || result.FrameSlotKind != StackFrameSlotKind.ReturnAddress)
                        throw new InvalidOperationException("Return-address prolog node must write a return-address frame slot.");
                    if (result.FrameBase != RegisterFrameBase.StackPointer)
                        throw new InvalidOperationException("Return-address prolog node must write a stack-pointer-relative frame slot.");
                    RequireSingleRegisterUse(node, MachineRegisters.ReturnAddress, "return-address save use");
                    RequireImmediate(node, 0, "return-address save immediate");
                    return;

                case FrameOperation.SaveCalleeSavedRegister:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    if (!result.IsFrameSlot || result.FrameSlotKind != StackFrameSlotKind.CalleeSavedRegister)
                        throw new InvalidOperationException("Callee-save prolog node must write a callee-saved frame slot.");
                    if (result.FrameBase != RegisterFrameBase.StackPointer)
                        throw new InvalidOperationException("Callee-save prolog node must write a stack-pointer-relative frame slot.");
                    if (node.Uses.Length != 1 || !node.Uses[0].IsRegister || !MachineRegisters.IsCalleeSaved(node.Uses[0].Register))
                        throw new InvalidOperationException("Callee-save prolog node must read one callee-saved register.");
                    RequireImmediate(node, 0, "callee-save prolog immediate");
                    return;

                case FrameOperation.EstablishFramePointer:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.FramePointer, "frame pointer establishment result");
                    RequireSingleRegisterUse(node, MachineRegisters.StackPointer, "frame pointer establishment use");
                    RequireImmediate(node, 0, "frame pointer establishment immediate");
                    return;

                case FrameOperation.EnterFuncletFrame:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    if (!result.IsRegister || result.Register is not (MachineRegister.X2 or MachineRegister.X8))
                        throw new InvalidOperationException("Funclet prolog must establish either SP or FP as the funclet frame anchor.");
                    RequireSingleRegisterUse(node, MachineRegisters.StackPointer, "funclet frame establishment use");
                    RequireImmediate(node, 0, "funclet frame establishment immediate");
                    return;

                case FrameOperation.LeaveFuncletFrame:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.StackPointer, "funclet frame detach result");
                    if (node.Uses.Length != 1 || !node.Uses[0].IsRegister || node.Uses[0].Register is not (MachineRegister.X2 or MachineRegister.X8))
                        throw new InvalidOperationException("Funclet epilog must read exactly one SP or FP frame anchor.");
                    RequireImmediate(node, 0, "funclet frame detach immediate");
                    return;

                case FrameOperation.RestoreStackPointerFromFramePointer:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.StackPointer, "stack pointer restore result");
                    RequireSingleRegisterUse(node, MachineRegisters.FramePointer, "stack pointer restore use");
                    RequireImmediate(node, 0, "stack pointer restore immediate");
                    return;

                case FrameOperation.RestoreCalleeSavedRegister:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    if (!result.IsRegister || !MachineRegisters.IsCalleeSaved(result.Register))
                        throw new InvalidOperationException("Callee-save epilog node must write one callee-saved register.");
                    if (node.Uses.Length != 1
                        || !node.Uses[0].IsFrameSlot
                        || node.Uses[0].FrameSlotKind != StackFrameSlotKind.CalleeSavedRegister)
                        throw new InvalidOperationException("Callee-save epilog node must read one callee-saved frame slot.");
                    if (node.Uses[0].FrameBase != RegisterFrameBase.StackPointer)
                        throw new InvalidOperationException("Callee-save epilog node must read a stack-pointer-relative frame slot.");
                    RequireImmediate(node, 0, "callee-save epilog immediate");
                    return;

                case FrameOperation.RestoreReturnAddress:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.ReturnAddress, "return-address restore result");
                    if (node.Uses.Length != 1
                        || !node.Uses[0].IsFrameSlot
                        || node.Uses[0].FrameSlotKind != StackFrameSlotKind.ReturnAddress)
                        throw new InvalidOperationException("Return-address epilog node must read one return-address frame slot.");
                    if (node.Uses[0].FrameBase != RegisterFrameBase.StackPointer)
                        throw new InvalidOperationException("Return-address epilog node must read a stack-pointer-relative frame slot.");
                    RequireImmediate(node, 0, "return-address restore immediate");
                    return;

                case FrameOperation.FreeFrame:
                    RequireFrameKind(node, GenTreeKind.StackFrameOp);
                    RequireRegister(result, MachineRegisters.StackPointer, "frame free result");
                    RequireSingleRegisterUse(node, MachineRegisters.StackPointer, "frame free use");
                    RequireImmediate(node, method.StackFrame.FrameSize, "frame free size");
                    return;

                default:
                    throw new InvalidOperationException("Unknown frame operation " + node.FrameOperation + ".");
            }
        }

        private static void RequireFrameKind(GenTree node, GenTreeKind expected)
        {
            if (node.Kind != GenTreeKind.StackFrameOp)
                throw new InvalidOperationException(
                    $"Frame operation {node.FrameOperation} is stored as {node.Kind}, expected {expected}.");
        }

        private static void RequireImmediate(GenTree node, int expected, string what)
        {
            if (node.Immediate != expected)
                throw new InvalidOperationException($"{what} must be {expected}, found {node.Immediate}.");
        }

        private static void RequireRegister(RegisterOperand operand, MachineRegister expected, string what)
        {
            if (!operand.IsRegister || operand.Register != expected)
                throw new InvalidOperationException($"{what} must be {MachineRegisters.Format(expected)}.");
        }

        private static void RequireSingleRegisterUse(GenTree node, MachineRegister expected, string what)
        {
            if (node.Uses.Length != 1)
                throw new InvalidOperationException(what + " must contain exactly one use.");
            RequireRegister(node.Uses[0], expected, what);
        }

        private static void VerifyAllValuesAllocated(RegisterAllocatedMethod method)
        {
            for (int i = 0; i < method.GenTreeMethod.Values.Length; i++)
            {
                var value = method.GenTreeMethod.Values[i].RepresentativeNode;
                if (!method.AllocationByNode.ContainsKey(value))
                    throw new InvalidOperationException("Missing register allocation for " + FormatAllocationValue(value) + ".");
            }
        }

        private static void VerifyNoOverlappingRegisterIntervals(RegisterAllocatedMethod method)
        {
            var segments = CollectLocatedRegisterSegments(method);
            for (int i = 0; i < segments.Count; i++)
            {
                var left = segments[i];
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var right = segments[j];
                    if (left.Segment.Location.Register != right.Segment.Location.Register)
                        continue;
                    if (left.Segment.Start >= right.Segment.End || right.Segment.Start >= left.Segment.End)
                        continue;

                    int start = Math.Max(left.Segment.Start, right.Segment.Start);
                    int end = Math.Min(left.Segment.End, right.Segment.End);
                    if (!RangesIntersect(left.Allocation.Ranges, right.Allocation.Ranges, start, end))
                        continue;

                    throw new InvalidOperationException(
                        "Register " + MachineRegisters.Format(left.Segment.Location.Register) +
                        $" assigned to overlapping allocation segments {left.DisplayName} and {right.DisplayName}.");
                }
            }
        }

        private readonly struct LocatedRegisterSegment
        {
            public readonly RegisterAllocationInfo Allocation;
            public readonly RegisterAllocationSegment Segment;
            public readonly string DisplayName;

            public LocatedRegisterSegment(RegisterAllocationInfo allocation, RegisterAllocationSegment segment, string displayName)
            {
                Allocation = allocation;
                Segment = segment;
                DisplayName = displayName;
            }
        }

        private static List<LocatedRegisterSegment> CollectLocatedRegisterSegments(RegisterAllocatedMethod method)
        {
            var result = new List<LocatedRegisterSegment>();
            for (int i = 0; i < method.Allocations.Length; i++)
            {
                var allocation = method.Allocations[i];
                for (int s = 0; s < allocation.Segments.Length; s++)
                {
                    var segment = allocation.Segments[s];
                    if (segment.Location.IsRegister)
                        result.Add(new LocatedRegisterSegment(allocation, segment, FormatAllocationValue(allocation.Value)));
                }

                for (int f = 0; f < allocation.Fragments.Length; f++)
                {
                    var fragment = allocation.Fragments[f];
                    for (int s = 0; s < fragment.Segments.Length; s++)
                    {
                        var segment = fragment.Segments[s];
                        if (segment.Location.IsRegister)
                            result.Add(new LocatedRegisterSegment(
                                allocation,
                                segment,
                                FormatAllocationValue(allocation.Value) + "#" + fragment.SegmentIndex.ToString()));
                    }
                }
            }

            return result;
        }

        private static string FormatAllocationValue(GenTree value)
        {
            if (value is null)
                return "<null>";

            if (value.HasSsaUse || value.HasSsaDefinition)
                return value.LinearValueKey.ToString();

            return value.Kind.ToString() + "#" + value.Id.ToString();
        }

        private static bool RangesIntersect(ImmutableArray<LinearLiveRange> left, ImmutableArray<LinearLiveRange> right, int minPosition, int maxPosition)
        {
            int i = 0;
            int j = 0;
            while (i < left.Length && j < right.Length)
            {
                var a = left[i];
                var b = right[j];
                int start = Math.Max(Math.Max(a.Start, b.Start), minPosition);
                int end = Math.Min(Math.Min(a.End, b.End), maxPosition);
                if (start < end)
                    return true;
                if (a.End <= b.Start)
                    i++;
                else
                    j++;
            }
            return false;
        }

        private static void VerifyNoCallerSavedLiveAcrossCalls(RegisterAllocatedMethod method)
        {
            var callPositions = BuildCallPositions(method.GenTreeMethod);
            if (callPositions.Length == 0)
                return;

            var segments = CollectLocatedRegisterSegments(method);
            for (int i = 0; i < segments.Count; i++)
            {
                var located = segments[i];
                var segment = located.Segment;
                var allocation = located.Allocation;
                if (!MachineRegisters.IsCallerSaved(segment.Location.Register))
                    continue;

                for (int c = 0; c < callPositions.Length; c++)
                {
                    int callPos = callPositions[c];

                    if (!segment.Contains(callPos + 1))
                        continue;

                    if (RangesCrossCall(allocation.Ranges, callPos))
                    {
                        throw new InvalidOperationException(
                            "Caller-saved register " + MachineRegisters.Format(segment.Location.Register) +
                            $" assigned to allocation segment of {located.DisplayName} live across call at GenTree LIR position {callPos}.");
                    }
                }
            }
        }

        private static ImmutableArray<int> BuildCallPositions(GenTreeMethod method)
        {
            var positions = new SortedSet<int>();
            for (int i = 0; i < method.RefPositions.Length; i++)
            {
                var rp = method.RefPositions[i];
                if (rp.Kind == LinearRefPositionKind.Kill && rp.RegisterMask != 0)
                    positions.Add(rp.Position);
            }


            return positions.ToImmutableArray();
        }

        private static bool RangesCrossCall(ImmutableArray<LinearLiveRange> ranges, int callPosition)
        {
            for (int i = 0; i < ranges.Length; i++)
            {
                var range = ranges[i];
                if (range.Start <= callPosition && callPosition + 1 < range.End)
                    return true;
            }
            return false;
        }

        private static void VerifyStackFrameLayout(RegisterAllocatedMethod method)
        {
            var layout = method.StackFrame;
            if (layout.IsEmpty)
                return;

            if (layout.FrameAlignment <= 0)
                throw new InvalidOperationException("Invalid stack frame alignment.");
            if (layout.FrameSize < 0 || layout.FrameSize % layout.FrameAlignment != 0)
                throw new InvalidOperationException("Invalid finalized stack frame size " + layout.FrameSize + ".");

            if (method.Funclets.Length > 1)
            {
                if (layout.FrameModel != RegisterStackFrameModel.SharedRootFrameWithFunclets)
                    throw new InvalidOperationException("Funclet method must use a shared root stack frame model.");
                if (!layout.UsesFramePointer)
                    throw new InvalidOperationException("Funclet method must preserve a stable frame pointer.");
            }
            else if (layout.FrameModel == RegisterStackFrameModel.SharedRootFrameWithFunclets)
            {
                throw new InvalidOperationException("Shared funclet stack frame model used by a method without funclets.");
            }
            else if (layout.FrameModel == RegisterStackFrameModel.Leaf && layout.FrameSize != 0)
            {
                throw new InvalidOperationException("Leaf stack frame model cannot have a non-empty frame.");
            }

            var slots = new List<StackFrameSlot>();
            slots.AddRange(layout.CalleeSavedSlots);
            slots.AddRange(layout.ArgumentSlots);
            slots.AddRange(layout.LocalSlots);
            slots.AddRange(layout.TempSlots);
            slots.AddRange(layout.SpillSlots);
            slots.AddRange(layout.OutgoingArgumentSlots);

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.Offset < 0 || slot.Size < 0 || slot.Alignment <= 0)
                    throw new InvalidOperationException($"Invalid stack frame slot {slot}.");
                if (slot.Offset % slot.Alignment != 0)
                    throw new InvalidOperationException($"Misaligned stack frame slot {slot}.");
                if (slot.EndOffset > layout.FrameSize)
                    throw new InvalidOperationException($"Stack frame slot escapes frame: " + slot + ".");

                for (int j = i + 1; j < slots.Count; j++)
                {
                    var other = slots[j];
                    if (slot.Offset < other.EndOffset && other.Offset < slot.EndOffset)
                        throw new InvalidOperationException($"Overlapping stack frame slots: {slot} and {other}.");
                }
            }
        }

        private static void VerifyNodeOperands(RegisterAllocatedMethod method)
        {
            foreach (var block in method.Blocks)
            {
                foreach (var node in block.LinearNodes)
                    VerifyNode(method, node);
            }
        }

        private static void VerifyNode(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.Kind == GenTreeKind.GcPoll)
            {
                if (node.Results.Length != 0 || node.Uses.Length != 0 ||
                    node.RegisterResults.Length != 0 || node.RegisterUses.Length != 0 ||
                    node.UseRoles.Length != 0 || node.GenTreeLinearId < 0)
                {
                    throw new InvalidOperationException("GC poll node must be standalone and linked to a linear IR poll node.");
                }

                if (node.Source is not null && node.Source.LinearId != node.GenTreeLinearId)
                {
                    throw new InvalidOperationException(
                        $"GC poll node {node.Id} is linked to GenTree GenTree LIR node {node.GenTreeLinearId}, " +
                        $"but its GenTree source has linear IR id {node.Source.LinearId}.");
                }
                return;
            }

            if (node.UseRoles.Length != node.Uses.Length)
                throw new InvalidOperationException("Node use role count does not match use operand count.");

            if (node.Source is not null && node.GenTreeLinearId >= 0 && node.Source.LinearId != node.GenTreeLinearId)
            {
                throw new InvalidOperationException(
                    $"Register node {node.Id} is linked to GenTree GenTree LIR node {node.GenTreeLinearId}, " +
                    $"but its GenTree source has linear IR id {node.Source.LinearId}.");
            }

            for (int i = 0; i < node.Results.Length; i++)
            {
                var operand = node.Results[i];
                if (operand.IsNone)
                    throw new InvalidOperationException("Node results contain empty register operand.");

                VerifyOperandStorage(method, operand, isUse: false);

                if (i < node.RegisterResults.Length)
                    VerifyOperandClass(method, operand, node.RegisterResults[i], "result");
            }

            for (int i = 0; i < node.Uses.Length; i++)
            {
                var operand = node.Uses[i];
                if (operand.IsNone)
                    throw new InvalidOperationException("Node uses empty register operand.");

                VerifyOperandStorage(method, operand, isUse: true);

                if (i < node.RegisterUses.Length && node.UseRoles[i] != OperandRole.HiddenReturnBuffer)
                    VerifyOperandClass(method, operand, node.RegisterUses[i], "use");
            }


            if (GenTreeLirKinds.IsCopyKind(node.Kind) &&
                node.Results.Length == 1 && node.Uses.Length == 1 &&
                node.Results[0].RegisterClass != node.Uses[0].RegisterClass &&
                node.MoveKind != MoveKind.Register)
            {
                throw new InvalidOperationException($"Move crosses register classes: {node.Uses[0]} -> {node.Results[0]}.");
            }

            if (node.MoveKind == MoveKind.MemoryToMemory &&
                !IsBlockCopyMove(method, node))
            {
                throw new InvalidOperationException(
                    $"Move must not be memory-to-memory after copy resolution: {node.Uses[0]} -> {node.Results[0]}.");
            }

            if (GenTreeLirKinds.IsRealTree(node))
            {
                if (IsCallLike(node.TreeKind))
                    VerifyCallLikeAbiShape(method, node);
                else if (node.TreeKind == GenTreeKind.Return)
                    VerifyReturnAbiShape(method, node);

                if (RequiresRegisterOnlyTreeShape(node))
                    VerifyRegisterOnlyTreeShape(node);
            }
        }


        private static bool IsBlockCopyMove(RegisterAllocatedMethod method, GenTree node)
        {
            for (int i = 0; i < node.RegisterResults.Length; i++)
            {
                if (IsBlockCopyValue(method, node.RegisterResults[i]))
                    return true;
            }
            if (node.RegisterUses.Length != 0 && IsBlockCopyValue(method, node.RegisterUses[0]))
                return true;
            return false;
        }

        private static bool IsBlockCopyValue(RegisterAllocatedMethod method, GenTree value)
        {
            var valueInfo = method.GenTreeMethod.GetValueInfo(value);
            return MachineAbi.IsBlockCopyValue(valueInfo.Type, valueInfo.StackKind);
        }

        private static bool IsCallLike(GenTreeKind kind)
            => kind is
                GenTreeKind.Call or
                GenTreeKind.VirtualCall or
                GenTreeKind.DelegateInvoke or
                GenTreeKind.NewObject;

        private static bool RequiresRegisterOnlyTreeShape(GenTree node)
        {
            if (!GenTreeLirKinds.IsRealTree(node))
                return false;

            if (node.TreeKind == GenTreeKind.Return)
                return false;

            var lowering = GenTreeLinearLoweringClassifier.Classify(
                node.Source,
                node.RegisterResults.Length == 1 ? node.RegisterResults[0] : null,
                node.RegisterUses);

            return lowering.HasFlag(GenTreeLinearFlags.RequiresRegisterOperands) &&
                   !lowering.HasFlag(GenTreeLinearFlags.AbiCall);
        }

        private static void VerifyRegisterOnlyTreeShape(GenTree node)
        {
            for (int i = 0; i < node.Results.Length; i++)
            {
                if (!node.Results[i].IsRegister)
                {
                    throw new InvalidOperationException(
                        $"Lowered tree result {i} must be a register, actual: {node.Results[i]}.");
                }
            }

            for (int i = 0; i < node.Uses.Length; i++)
            {
                if (!node.Uses[i].IsRegister)
                {
                    throw new InvalidOperationException(
                        $"Lowered tree use {i} must be a register, actual: {node.Uses[i]}.");
                }
            }
        }

        private static void VerifyCallLikeAbiShape(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.Uses.Length != node.RegisterUses.Length)
                throw new InvalidOperationException("Call-like node must preserve one GenTree value per ABI argument operand or fragment.");
            if (node.UseRoles.Length != node.Uses.Length)
                throw new InvalidOperationException("Call-like node must preserve one operand role per ABI argument operand or fragment.");

            var descriptor = BuildExpectedCallDescriptorFromAllocatedShape(method, node);
            if (descriptor.ArgumentSegments.Length != node.Uses.Length)
            {
                throw new InvalidOperationException(
                    $"Call-like node ABI operand count mismatch. Actual: {node.Uses.Length}" +
                    $", expected: {descriptor.ArgumentSegments.Length}.");
            }

            for (int i = 0; i < descriptor.ArgumentSegments.Length; i++)
            {
                var segment = descriptor.ArgumentSegments[i];
                var expectedRole = segment.IsHiddenReturnBuffer
                    ? OperandRole.HiddenReturnBuffer
                    : OperandRole.Normal;
                if (node.UseRoles[i] != expectedRole)
                {
                    throw new InvalidOperationException(
                        $"Call argument {i} has wrong ABI role. Actual: {node.UseRoles[i]}, expected: {expectedRole}.");
                }

                if (!node.RegisterUses[i].Equals(segment.Value))
                {
                    throw new InvalidOperationException(
                        $"Call argument {i} has wrong GenTree value metadata. Actual: {node.RegisterUses[i]}, expected: {segment.Value}.");
                }

                var expected = ExpectedAbiArgumentOperand(segment.Location);
                if (!MatchesAbiOperand(method, node.Uses[i], expected))
                {
                    throw new InvalidOperationException(
                        $"Call argument {i} is not in the expected ABI location. Actual: {node.Uses[i]}, expected: {expected}.");
                }
            }

            if (node.RegisterResult is not null)
            {
                var abi = descriptor.ReturnAbi;
                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    if (node.RegisterResults.Length != 0 || node.Results.Length != 0)
                    {
                        if (node.RegisterResults.Length != 1 || !node.RegisterResults[0].Equals(node.RegisterResult))
                            throw new InvalidOperationException("Scalar call-like result must preserve its linear IR result metadata.");
                        if (node.Results.Length != 1)
                            throw new InvalidOperationException("Scalar call-like result must have exactly one result operand.");

                        if (node.Kind == GenTreeKind.NewObject)
                        {
                            if (!node.Results[0].IsFrameSlot)
                                throw new InvalidOperationException("Reference newobj result must be a frame home that preserves the object across the constructor call.");
                        }
                        else
                        {
                            var expectedReturn = RegisterOperand.ForRegister(
                                abi.RegisterClass == RegisterClass.Float
                                    ? MachineRegisters.FloatReturnValue0
                                    : MachineRegisters.ReturnValue0);

                            if (!node.Results[0].Equals(expectedReturn))
                            {
                                throw new InvalidOperationException(
                                    "Call-like node result is not in the ABI return register. Actual: " +
                                    node.Results[0] + ", expected: " + expectedReturn + ".");
                            }
                        }
                    }
                }
                else if (abi.PassingKind == AbiValuePassingKind.Indirect)
                {
                    if (node.RegisterResults.Length != 0 || node.Results.Length != 0)
                        throw new InvalidOperationException(
                            "Indirect call-like results must be represented by a hidden return-buffer argument, not a result operand.");
                    if (!descriptor.HasHiddenReturnBuffer)
                        throw new InvalidOperationException("Indirect call-like result has no hidden return-buffer descriptor segment.");
                }
                else if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    if (node.RegisterResults.Length != 0 || node.Results.Length != 0)
                        throw new InvalidOperationException("Multi-register call-like result must not be represented on the call node itself.");
                }
                else if (node.Results.Length != 0 || node.RegisterResults.Length != 0)
                {
                    throw new InvalidOperationException("Void call-like result must not have result operands.");
                }
            }
            else if (node.Results.Length != 0 || node.RegisterResults.Length != 0)
            {
                throw new InvalidOperationException("Void call-like node must not have result operands.");
            }
        }

        private static AbiCallDescriptor BuildExpectedCallDescriptorFromAllocatedShape(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.RegisterUses.Length != node.Uses.Length)
                throw new InvalidOperationException("Call-like node must preserve one GenTree value per ABI argument operand or fragment.");
            if (node.UseRoles.Length != node.Uses.Length)
                throw new InvalidOperationException("Call-like node must preserve one operand role per ABI argument operand or fragment.");

            var explicitArguments = ImmutableArray.CreateBuilder<GenTree>();
            GenTree? hiddenReturnBufferValue = null;

            int index = 0;
            while (index < node.RegisterUses.Length)
            {
                var role = node.UseRoles[index];
                if (role == OperandRole.HiddenReturnBuffer)
                {
                    if (hiddenReturnBufferValue is not null)
                        throw new InvalidOperationException("Call-like node has more than one hidden return-buffer operand.");
                    if (node.RegisterResult is not null)
                        throw new InvalidOperationException("Hidden return-buffer call-like node must not also expose a result value on the call node.");

                    hiddenReturnBufferValue = node.RegisterUses[index];
                    index++;
                    continue;
                }

                if (role != OperandRole.Normal)
                    throw new InvalidOperationException("Call-like node has an unknown ABI operand role: " + role + ".");

                AddCompressedExpandedCallArgument(method, node, explicitArguments, ref index);
            }

            return MachineAbi.BuildCallDescriptor(
                explicitArguments.ToImmutable(),
                method.GenTreeMethod.GetValueInfo,
                hiddenReturnBufferValue ?? node.RegisterResult,
                node.Method,
                node.Kind == GenTreeKind.NewObject);
        }

        private static void AddCompressedExpandedCallArgument(
            RegisterAllocatedMethod method,
            GenTree node,
            ImmutableArray<GenTree>.Builder explicitArguments,
            ref int index)
        {
            if ((uint)index >= (uint)node.RegisterUses.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            var value = node.RegisterUses[index];
            var info = method.GenTreeMethod.GetValueInfo(value);
            var abi = MachineAbi.ClassifyValue(info.Type, info.StackKind, isReturn: false);
            int operandCount = abi.PassingKind == AbiValuePassingKind.MultiRegister
                ? MachineAbi.GetRegisterSegments(abi).Length
                : 1;

            if (operandCount <= 0)
                operandCount = 1;
            if (index + operandCount > node.RegisterUses.Length)
            {
                throw new InvalidOperationException(
                    $"Call-like node has an incomplete expanded ABI argument for {value}. " +
                    $"Actual remaining operands: {node.RegisterUses.Length - index}, expected: {operandCount}.");
            }

            for (int i = 0; i < operandCount; i++)
            {
                int operandIndex = index + i;
                if (node.UseRoles[operandIndex] != OperandRole.Normal)
                {
                    throw new InvalidOperationException(
                        $"Expanded ABI argument fragment {i} for {value} has wrong role. " +
                        $"Actual: {node.UseRoles[operandIndex]}, expected: {OperandRole.Normal}.");
                }

                if (!node.RegisterUses[operandIndex].Equals(value))
                {
                    throw new InvalidOperationException(
                        $"Expanded ABI argument fragment {i} has wrong GenTree value metadata. " +
                        $"Actual: {node.RegisterUses[operandIndex]}, expected: {value}.");
                }
            }

            explicitArguments.Add(value);
            index += operandCount;
        }

        private static RegisterOperand ExpectedAbiArgumentOperand(AbiArgumentLocation location)
        {
            if (location.IsRegister)
                return RegisterOperand.ForRegister(location.Register);

            return RegisterOperand.ForOutgoingArgumentSlot(
                location.RegisterClass,
                location.StackSlotIndex,
                location.StackOffset,
                location.Size);
        }

        private static void VerifyReturnAbiShape(RegisterAllocatedMethod method, GenTree node)
        {
            if (node.Uses.Length == 0)
                return;

            if (node.RegisterUses.Length == 0 || node.Uses.Length != node.RegisterUses.Length)
                throw new InvalidOperationException("Return node must preserve one GenTree value per ABI return operand or fragment.");

            var value = node.RegisterUses[0];
            for (int i = 1; i < node.RegisterUses.Length; i++)
            {
                if (!node.RegisterUses[i].Equals(value))
                    throw new InvalidOperationException("Return node fragments must all refer to the same GenTree value.");
            }

            var valueInfo = method.GenTreeMethod.GetValueInfo(value);
            var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);

            if (abi.PassingKind is not (AbiValuePassingKind.ScalarRegister or AbiValuePassingKind.MultiRegister))
            {
                if (node.Uses.Length != 1)
                    throw new InvalidOperationException("Composite buffer return must carry exactly one return-buffer operand.");
                if (node.Uses[0].IsRegister)
                    throw new InvalidOperationException($"Composite buffer return must not be rewritten to a scalar return register: {node.Uses[0]}.");
                return;
            }

            var expected = ExpectedReturnOperands(abi);
            if (node.Uses.Length != expected.Length)
                throw new InvalidOperationException($"Return node ABI operand count mismatch. Actual: {node.Uses.Length}, expected: {expected.Length}.");

            for (int i = 0; i < expected.Length; i++)
            {
                if (!node.Uses[i].Equals(expected[i]))
                {
                    throw new InvalidOperationException(
                        $"Return value fragment {i} is not in the ABI return register. Actual: " +
                        $"{node.Uses[i]}, expected: {expected[i]}.");
                }
            }
        }

        private static ImmutableArray<RegisterOperand> ExpectedReturnOperands(AbiValueInfo abi)
        {
            if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                return ImmutableArray.Create(RegisterOperand.ForRegister(
                    abi.RegisterClass == RegisterClass.Float ? MachineRegisters.FloatReturnValue0 : MachineRegisters.ReturnValue0));

            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(abi);
                var result = ImmutableArray.CreateBuilder<RegisterOperand>(segments.Length);
                int general = 0;
                int floating = 0;
                for (int i = 0; i < segments.Length; i++)
                    result.Add(RegisterOperand.ForRegister(GetReturnRegisterForVerifier(segments[i].RegisterClass, ref general, ref floating)));
                return result.ToImmutable();
            }

            return ImmutableArray<RegisterOperand>.Empty;
        }

        private static MachineRegister GetReturnRegisterForVerifier(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
        {
            if (registerClass == RegisterClass.Float)
            {
                int index = floatIndex++;
                return index switch
                {
                    0 => MachineRegisters.FloatReturnValue0,
                    1 => MachineRegisters.FloatReturnValue1,
                    _ => MachineRegister.Invalid,
                };
            }
            if (registerClass == RegisterClass.General)
            {
                int index = generalIndex++;
                return index switch
                {
                    0 => MachineRegisters.ReturnValue0,
                    1 => MachineRegisters.ReturnValue1,
                    _ => MachineRegister.Invalid,
                };
            }
            return MachineRegister.Invalid;
        }

        private static bool MatchesAbiOperand(RegisterAllocatedMethod method, RegisterOperand actual, RegisterOperand expected)
        {
            if (expected.IsRegister)
                return actual.Equals(expected);

            if (!expected.IsOutgoingArgumentSlot)
                return actual.Equals(expected);

            if (actual.RegisterClass != expected.RegisterClass)
                return false;

            if (actual.IsOutgoingArgumentSlot)
            {
                return actual.FrameSlotIndex == expected.FrameSlotIndex &&
                       actual.FrameOffset == expected.FrameOffset &&
                       actual.FrameSlotSize == expected.FrameSlotSize;
            }

            if (!actual.IsFrameSlot || actual.FrameSlotKind != StackFrameSlotKind.OutgoingArgument || actual.FrameSlotIndex != expected.FrameSlotIndex)
                return false;

            if (!method.StackFrame.TryGetOutgoingArgumentSlot(expected.FrameSlotIndex, out var slot))
                return false;

            int expectedOffset = checked(slot.Offset + expected.FrameOffset);
            int expectedSize = expected.FrameSlotSize > 0 ? expected.FrameSlotSize : slot.Size;
            return actual.FrameOffset == expectedOffset && actual.FrameSlotSize == expectedSize;
        }


        private static void VerifyOperandStorage(RegisterAllocatedMethod method, RegisterOperand operand, bool isUse)
        {
            if (operand.IsNone)
                return;

            if (operand.RegisterClass == RegisterClass.Invalid)
                throw new InvalidOperationException("Operand has invalid register class.");

            if (operand.IsRegister)
            {
                if (operand.Register == MachineRegister.Invalid)
                    throw new InvalidOperationException(isUse ? "Node uses invalid register." : "Node has invalid result register.");
                if (!MachineRegisters.IsRegisterInClass(operand.Register, operand.RegisterClass))
                    throw new InvalidOperationException($"Register {MachineRegisters.Format(operand.Register)} does not match operand class {operand.RegisterClass}.");
            }

            if (operand.IsSpillSlot && (uint)operand.SpillSlot >= (uint)method.SpillSlotCount)
                throw new InvalidOperationException((isUse ? "Node uses invalid spill slot " : "Node writes invalid spill slot ") + operand.SpillSlot + ".");

            if (operand.IsUnresolvedFrameSlot)
                throw new InvalidOperationException($"Node contains an unfinalized frame operand: {operand}.");

            if (operand.IsFrameSlot)
            {
                if (operand.FrameSlotKind == StackFrameSlotKind.Invalid)
                    throw new InvalidOperationException("Frame operand has invalid slot kind.");
                if (operand.FrameBase is not (RegisterFrameBase.StackPointer or RegisterFrameBase.FramePointer or RegisterFrameBase.IncomingArgumentBase))
                    throw new InvalidOperationException("Frame operand has invalid base register.");
                if (operand.FrameSlotIndex < 0 || operand.FrameOffset < 0 || operand.FrameSlotSize <= 0)
                    throw new InvalidOperationException("Frame operand has invalid slot coordinates.");
                if (operand.FrameBase != RegisterFrameBase.IncomingArgumentBase)
                {
                    if (method.StackFrame.IsEmpty)
                        throw new InvalidOperationException("Node uses finalized frame operand but method has no finalized stack frame layout.");
                    if (operand.FrameOffset + operand.FrameSlotSize > method.StackFrame.FrameSize)
                        throw new InvalidOperationException($"Frame operand escapes finalized frame: {operand}.");
                }
            }
        }

        private static void VerifyOperandClass(RegisterAllocatedMethod method, RegisterOperand operand, GenTree value, string role)
        {
            var valueInfo = method.GenTreeMethod.GetValueInfo(value);
            var expected = valueInfo.RegisterClass;
            if (operand.RegisterClass == expected)
                return;

            var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: false);
            if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(abi);
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].RegisterClass == operand.RegisterClass)
                        return;
                }
            }

            var returnAbi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);
            if (returnAbi.PassingKind == AbiValuePassingKind.MultiRegister)
            {
                var segments = MachineAbi.GetRegisterSegments(returnAbi);
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].RegisterClass == operand.RegisterClass)
                        return;
                }
            }

            throw new InvalidOperationException($"Node {role} operand {operand} has class {operand.RegisterClass} but GenTree value {value} requires {expected}.");
        }
    }
}
