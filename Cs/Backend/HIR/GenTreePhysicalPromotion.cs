using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal sealed class PhysicalPromotionOptions
    {
        public static PhysicalPromotionOptions Default { get; } = new PhysicalPromotionOptions();

        public int MaxPromotedFieldsPerStruct { get; set; } = 8;
        public int MaxPromotedStructSize { get; set; } = 64;
        public bool PromoteArguments { get; set; } = true;
        public bool PromoteLocals { get; set; } = true;
        public bool PromoteTemps { get; set; } = true;
        public bool RewriteFullDefaultStores { get; set; } = true;
        public bool RewriteFullStructCopies { get; set; } = true;
    }

    internal readonly struct PhysicalPromotionResult
    {
        public GenTreeMethod Method { get; }
        public bool Changed { get; }

        public PhysicalPromotionResult(GenTreeMethod method, bool changed)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Changed = changed;
        }
    }

    internal static class GenTreePhysicalPromoter
    {
        public static PhysicalPromotionResult PromoteMethod(GenTreeMethod method, PhysicalPromotionOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= PhysicalPromotionOptions.Default;

            var pass = new Pass(method, options);
            return pass.Run();
        }

        private sealed class Pass
        {
            private readonly GenTreeMethod _method;
            private readonly PhysicalPromotionOptions _options;
            private readonly Dictionary<int, PromotionCandidate> _candidatesByParentLclNum;
            private readonly Dictionary<int, PromotionCandidate> _selectedByParentLclNum;
            private int _nextTreeId;

            public Pass(GenTreeMethod method, PhysicalPromotionOptions options)
            {
                _method = method;
                _options = options;
                _candidatesByParentLclNum = new Dictionary<int, PromotionCandidate>();
                _selectedByParentLclNum = new Dictionary<int, PromotionCandidate>();
                _nextTreeId = ComputeNextTreeId(method);
            }

            public PhysicalPromotionResult Run()
            {
                CollectCandidates(_method.ArgDescriptors);
                CollectCandidates(_method.LocalDescriptors);
                CollectCandidates(_method.TempDescriptors);

                if (_candidatesByParentLclNum.Count == 0)
                {
                    _method.SetPhase(GenTreeMethodPhase.PhysicalPromotedHir);
                    return new PhysicalPromotionResult(_method, changed: false);
                }

                AnalyzeAccesses();
                RejectCandidatesWithUnsatisfiedDependencies();
                SelectCandidates();

                if (_selectedByParentLclNum.Count == 0)
                {
                    RejectUnselectedCandidates();
                    _method.SetPhase(GenTreeMethodPhase.PhysicalPromotedHir);
                    return new PhysicalPromotionResult(_method, changed: false);
                }

                RejectUnselectedCandidates();
                _method.EnsurePromotedStructFieldLocals();
                BindSelectedFieldDescriptors();

                var rewrittenBlocks = ImmutableArray.CreateBuilder<GenTreeBlock>(_method.Blocks.Length);
                bool changed = false;

                for (int i = 0; i < _method.Blocks.Length; i++)
                {
                    var block = _method.Blocks[i];
                    var statements = ImmutableArray.CreateBuilder<GenTree>();
                    for (int s = 0; s < block.Statements.Length; s++)
                    {
                        var statement = block.Statements[s];
                        int countBefore = statements.Count;
                        RewriteStatement(statement, statements);
                        changed |= statements.Count != countBefore + 1 || !ReferenceEquals(statements[countBefore], statement);
                    }

                    rewrittenBlocks.Add(new GenTreeBlock(
                        block.Id,
                        block.StartPc,
                        block.EndPcExclusive,
                        block.EntryStackDepth,
                        block.ExitStackDepth,
                        block.JumpKind,
                        block.Flags,
                        statements.ToImmutable(),
                        block.SuccessorBlockIds,
                        block.SuccessorPcs));
                }

                if (!changed)
                {
                    _method.SetPhase(GenTreeMethodPhase.PhysicalPromotedHir);
                    return new PhysicalPromotionResult(_method, changed: false);
                }

                var rewritten = _method.CloneWithBlocks(rewrittenBlocks.ToImmutable());
                rewritten.SetPhase(GenTreeMethodPhase.PhysicalPromotedHir);
                return new PhysicalPromotionResult(rewritten, changed: true);
            }

            private void CollectCandidates(ImmutableArray<GenLocalDescriptor> descriptors)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    if (descriptor.Category != GenLocalCategory.PromotedStruct)
                        continue;

                    if (!IsLocalKindEnabled(descriptor.Kind))
                        continue;

                    if (!CanPromoteParentDescriptor(descriptor, _options, out var fields))
                        continue;

                    _candidatesByParentLclNum[descriptor.LclNum] = new PromotionCandidate(descriptor, fields);
                }
            }

            private bool IsLocalKindEnabled(GenLocalKind kind)
            {
                return kind switch
                {
                    GenLocalKind.Argument => _options.PromoteArguments,
                    GenLocalKind.Local => _options.PromoteLocals,
                    GenLocalKind.Temporary => _options.PromoteTemps,
                    _ => false,
                };
            }

            private static bool CanPromoteParentDescriptor(
                GenLocalDescriptor descriptor,
                PhysicalPromotionOptions options,
                out ImmutableArray<RuntimeField> fields)
            {
                fields = ImmutableArray<RuntimeField>.Empty;

                if (descriptor.AddressExposed || descriptor.MemoryAliased || descriptor.IsImplicitByRef || descriptor.Pinned || descriptor.IsRefLike)
                    return false;

                var type = descriptor.Type;
                if (type is null || !type.IsValueType || type.Kind != RuntimeTypeKind.Struct)
                    return false;

                if (LclVarDsc.IsRefLikeStorageType(type))
                    return false;

                if (type.SizeOf <= 0 || type.SizeOf > options.MaxPromotedStructSize)
                    return false;

                if (type.InstanceFields.Length == 0 || type.InstanceFields.Length > options.MaxPromotedFieldsPerStruct)
                    return false;

                if (!HasNonOverlappingInstanceFields(type.InstanceFields))
                    return false;

                var builder = ImmutableArray.CreateBuilder<RuntimeField>(type.InstanceFields.Length);
                for (int i = 0; i < type.InstanceFields.Length; i++)
                {
                    var field = type.InstanceFields[i];
                    if (!CanPromoteField(field))
                        return false;
                    builder.Add(field);
                }

                fields = builder.ToImmutable();
                return true;
            }

            private static bool CanPromoteField(RuntimeField field)
            {
                if (field is null)
                    return false;

                if (field.IsStatic)
                    return false;

                var fieldType = field.FieldType;
                var stackKind = StackKindForStorage(fieldType);

                if (fieldType.Kind == RuntimeTypeKind.ByRef || stackKind is GenStackKind.ByRef)
                    return false;

                if (stackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                    return false;

                if (fieldType.IsValueType && fieldType.ContainsGcPointers)
                    return false;

                return MachineAbi.IsPhysicallyPromotableStorage(fieldType, stackKind) ||
                       stackKind is GenStackKind.Ref or GenStackKind.Ptr or GenStackKind.NativeInt or GenStackKind.NativeUInt;
            }

            private static bool HasNonOverlappingInstanceFields(RuntimeField[] fields)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    if (fields[i].IsStatic)
                        continue;

                    int iStart = fields[i].Offset;
                    int iEnd = checked(iStart + Math.Max(1, fields[i].FieldType.SizeOf));
                    for (int j = i + 1; j < fields.Length; j++)
                    {
                        if (fields[j].IsStatic)
                            continue;

                        int jStart = fields[j].Offset;
                        int jEnd = checked(jStart + Math.Max(1, fields[j].FieldType.SizeOf));
                        if (iStart < jEnd && jStart < iEnd)
                            return false;
                    }
                }

                return true;
            }

            private void AnalyzeAccesses()
            {
                for (int b = 0; b < _method.Blocks.Length; b++)
                {
                    var statements = _method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        AnalyzeNode(statements[s], null, -1, isStatementRoot: true);
                }
            }

            private void AnalyzeNode(GenTree node, GenTree? parent, int operandIndex, bool isStatementRoot)
            {
                if (TryGetCandidateFieldAccess(node, out var fieldAccess))
                {
                    if (!fieldAccess.Candidate.HasField(fieldAccess.Field))
                        fieldAccess.Candidate.Reject();
                    else if (node.Kind != GenTreeKind.StoreField || isStatementRoot)
                        fieldAccess.Candidate.NotePromotableAccess();
                    if (node.Kind == GenTreeKind.StoreField && !isStatementRoot)
                        fieldAccess.Candidate.Reject();

                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        AnalyzeNode(node.Operands[i], node, i, isStatementRoot: false);
                    }
                    return;
                }

                if (TryGetCandidateFieldAddress(node, out var fieldAddress))
                {
                    fieldAddress.Candidate.Reject();
                    for (int i = 0; i < node.Operands.Length; i++)
                        AnalyzeNode(node.Operands[i], node, i, isStatementRoot: false);
                    return;
                }

                if (TryGetCandidateAddress(node, out var addressCandidate))
                {
                    if (parent is null || !SsaSlotHelpers.IsContainedLocalFieldAddressUse(parent, operandIndex))
                        addressCandidate.Reject();
                }

                if (TryGetCandidateDirectLoad(node, out var loadCandidate))
                {
                    if (IsSupportedFullStructLoad(node, parent, operandIndex, loadCandidate, out var destinationCandidate))
                    {
                        loadCandidate.NotePromotableAccess();
                        loadCandidate.NoteCopyDependency(destinationCandidate);
                    }
                    else
                    {
                        loadCandidate.Reject();
                    }
                }

                if (TryGetCandidateDirectStore(node, out var storeCandidate))
                {
                    if (isStatementRoot && IsSupportedFullStructStore(node, storeCandidate, out var sourceCandidate))
                    {
                        storeCandidate.NotePromotableAccess();
                        if (sourceCandidate is not null)
                            storeCandidate.NoteCopyDependency(sourceCandidate);
                    }
                    else
                    {
                        storeCandidate.Reject();
                    }
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    AnalyzeNode(node.Operands[i], node, i, isStatementRoot: false);
            }

            private bool IsSupportedFullStructLoad(
                GenTree node,
                GenTree? parent,
                int operandIndex,
                PromotionCandidate candidate,
                out PromotionCandidate destination)
            {
                destination = null!;

                if (parent is null)
                    return false;

                if (!_options.RewriteFullStructCopies)
                    return false;

                if (operandIndex != 0)
                    return false;

                if (!IsStoreToSelectedOrCandidateParent(parent, out destination))
                    return false;

                return ReferenceEquals(candidate.Parent.Type, destination.Parent.Type) && FieldsMatch(candidate, destination);
            }

            private bool IsSupportedFullStructStore(GenTree node, PromotionCandidate candidate, out PromotionCandidate? source)
            {
                source = null;

                if (node.Operands.Length != 1)
                    return false;

                var value = node.Operands[0];
                if (_options.RewriteFullDefaultStores && IsDefaultValueForParent(value, candidate.Parent))
                    return true;

                if (!_options.RewriteFullStructCopies)
                    return false;

                if (!TryGetCandidateDirectLoad(value, out source))
                    return false;

                return ReferenceEquals(source.Parent.Type, candidate.Parent.Type) && FieldsMatch(source, candidate);
            }

            private bool IsStoreToSelectedOrCandidateParent(GenTree node, out PromotionCandidate candidate)
            {
                if (!TryGetCandidateDirectStore(node, out candidate))
                    return false;

                return true;
            }

            private static bool FieldsMatch(PromotionCandidate left, PromotionCandidate right)
            {
                if (left.Fields.Length != right.Fields.Length)
                    return false;

                for (int i = 0; i < left.Fields.Length; i++)
                {
                    if (!ReferenceEquals(left.Fields[i], right.Fields[i]) &&
                        (left.Fields[i].Offset != right.Fields[i].Offset || !ReferenceEquals(left.Fields[i].FieldType, right.Fields[i].FieldType)))
                        return false;
                }

                return true;
            }

            private void RejectCandidatesWithUnsatisfiedDependencies()
            {
                bool changed;
                do
                {
                    changed = false;
                    foreach (var kv in _candidatesByParentLclNum)
                    {
                        var candidate = kv.Value;
                        if (candidate.Rejected)
                            continue;

                        foreach (int dependency in candidate.CopyDependencyParentLclNums)
                        {
                            if (!_candidatesByParentLclNum.TryGetValue(dependency, out var dependencyCandidate) ||
                                dependencyCandidate.Rejected ||
                                !dependencyCandidate.HasPromotableAccess)
                            {
                                candidate.Reject();
                                changed = true;
                                break;
                            }
                        }
                    }
                }
                while (changed);
            }

            private void SelectCandidates()
            {
                foreach (var kv in _candidatesByParentLclNum)
                {
                    var candidate = kv.Value;
                    if (!candidate.Rejected && candidate.HasPromotableAccess)
                        _selectedByParentLclNum.Add(kv.Key, candidate);
                }
            }

            private void RejectUnselectedCandidates()
            {
                foreach (var kv in _candidatesByParentLclNum)
                {
                    if (_selectedByParentLclNum.ContainsKey(kv.Key))
                        continue;

                    if (kv.Value.Parent.IsStructMaterializationTemp)
                        kv.Value.Parent.IsStructMaterializationTemp = false;
                    kv.Value.Parent.MarkMemoryAliased();
                }
            }

            private void BindSelectedFieldDescriptors()
            {
                foreach (var kv in _selectedByParentLclNum)
                {
                    var candidate = kv.Value;
                    for (int i = 0; i < candidate.Fields.Length; i++)
                    {
                        var field = candidate.Fields[i];
                        if (!candidate.Parent.TryGetPromotedField(field, out var fieldDescriptor))
                            throw new InvalidOperationException("Physical promotion did not create a descriptor for promoted field " + field.Name + ".");

                        candidate.SetFieldDescriptor(field, fieldDescriptor);
                    }
                }
            }

            private void RewriteStatement(GenTree statement, ImmutableArray<GenTree>.Builder statements)
            {
                if (TryGetSelectedDirectStore(statement, out var destination) &&
                    statement.Operands.Length == 1 &&
                    IsDefaultValueForParent(statement.Operands[0], destination.Parent))
                {
                    AppendDefaultFieldStores(statement, destination, statements);
                    return;
                }

                if (TryGetSelectedDirectStore(statement, out destination) &&
                    statement.Operands.Length == 1 &&
                    TryGetSelectedDirectLoad(statement.Operands[0], out var source) &&
                    ReferenceEquals(source.Parent.Type, destination.Parent.Type) &&
                    FieldsMatch(source, destination))
                {
                    AppendFieldCopyStores(statement, source, destination, statements);
                    return;
                }

                statements.Add(RewriteNode(statement));
            }

            private GenTree RewriteNode(GenTree node)
            {
                if (TryGetSelectedFieldAccess(node, out var fieldAccess))
                {
                    var fieldDescriptor = fieldAccess.Candidate.GetFieldDescriptor(fieldAccess.Field);
                    if (node.Kind == GenTreeKind.Field)
                    {
                        return CreateLocalLikeLoad(node, fieldDescriptor, node.SourceOp);
                    }

                    var value = RewriteNode(node.Operands[1]);
                    return CreateLocalLikeStore(node, fieldDescriptor, value, node.SourceOp);
                }

                if (node.Operands.Length == 0)
                    return node;

                var operands = ImmutableArray.CreateBuilder<GenTree>(node.Operands.Length);
                bool changed = false;
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    var original = node.Operands[i];
                    var rewritten = RewriteNode(original);
                    changed |= !ReferenceEquals(original, rewritten);
                    operands.Add(rewritten);
                }

                if (!changed)
                    return node;

                return CloneNode(node, operands.ToImmutable());
            }

            private void AppendDefaultFieldStores(GenTree template, PromotionCandidate destination, ImmutableArray<GenTree>.Builder statements)
            {
                for (int i = 0; i < destination.Fields.Length; i++)
                {
                    var field = destination.Fields[i];
                    var fieldDescriptor = destination.GetFieldDescriptor(field);
                    var value = CreateDefaultValue(template, field.FieldType);
                    statements.Add(CreateLocalLikeStore(template, fieldDescriptor, value, template.SourceOp));
                }
            }

            private void AppendFieldCopyStores(
                GenTree template,
                PromotionCandidate source,
                PromotionCandidate destination,
                ImmutableArray<GenTree>.Builder statements)
            {
                for (int i = 0; i < destination.Fields.Length; i++)
                {
                    var destinationField = destination.Fields[i];
                    var sourceField = source.Fields[i];
                    var sourceDescriptor = source.GetFieldDescriptor(sourceField);
                    var destinationDescriptor = destination.GetFieldDescriptor(destinationField);
                    var value = CreateLocalLikeLoad(template, sourceDescriptor, template.SourceOp);
                    statements.Add(CreateLocalLikeStore(template, destinationDescriptor, value, template.SourceOp));
                }
            }

            private GenTree CreateLocalLikeLoad(GenTree template, GenLocalDescriptor descriptor, BytecodeOp sourceOp)
            {
                var kind = descriptor.Kind switch
                {
                    GenLocalKind.Argument => GenTreeKind.Arg,
                    GenLocalKind.Local => GenTreeKind.Local,
                    GenLocalKind.Temporary => GenTreeKind.Temp,
                    _ => throw new InvalidOperationException("Unsupported promoted field descriptor kind."),
                };

                var node = new GenTree(
                    _nextTreeId++,
                    kind,
                    template.Pc,
                    sourceOp,
                    descriptor.Type,
                    descriptor.StackKind,
                    GenTreeFlags.LocalUse | GenTreeFlags.Ordered,
                    ImmutableArray<GenTree>.Empty,
                    int32: descriptor.Index,
                    runtimeType: descriptor.Type);
                AttachDescriptor(node, descriptor);
                return node;
            }

            private GenTree CreateLocalLikeStore(GenTree template, GenLocalDescriptor descriptor, GenTree value, BytecodeOp sourceOp)
            {
                var kind = descriptor.Kind switch
                {
                    GenLocalKind.Argument => GenTreeKind.StoreArg,
                    GenLocalKind.Local => GenTreeKind.StoreLocal,
                    GenLocalKind.Temporary => GenTreeKind.StoreTemp,
                    _ => throw new InvalidOperationException("Unsupported promoted field descriptor kind."),
                };

                var node = new GenTree(
                    _nextTreeId++,
                    kind,
                    template.Pc,
                    sourceOp,
                    descriptor.Type,
                    descriptor.StackKind,
                    GenTreeFlags.LocalDef | GenTreeFlags.VarDef | GenTreeFlags.SideEffect | GenTreeFlags.Ordered,
                    ImmutableArray.Create(value),
                    int32: descriptor.Index,
                    runtimeType: descriptor.Type);
                AttachDescriptor(node, descriptor);
                return node;
            }

            private GenTree CreateDefaultValue(GenTree template, RuntimeType type)
            {
                return new GenTree(
                    _nextTreeId++,
                    GenTreeKind.DefaultValue,
                    template.Pc,
                    template.SourceOp,
                    type,
                    StackKindForStorage(type),
                    GenTreeFlags.None,
                    ImmutableArray<GenTree>.Empty,
                    runtimeType: type);
            }

            private GenTree CloneNode(GenTree node, ImmutableArray<GenTree> operands)
            {
                var clone = new GenTree(
                    _nextTreeId++,
                    node.Kind,
                    node.Pc,
                    node.SourceOp,
                    node.Type,
                    node.StackKind,
                    node.Flags,
                    operands,
                    int32: node.Int32,
                    int64: node.Int64,
                    text: node.Text,
                    runtimeType: node.RuntimeType,
                    field: node.Field,
                    method: node.Method,
                    convKind: node.ConvKind,
                    convFlags: node.ConvFlags,
                    targetPc: node.TargetPc,
                    targetBlockId: node.TargetBlockId);

                if (node.LocalDescriptor is not null)
                    AttachDescriptor(clone, node.LocalDescriptor);

                return clone;
            }

            private static void AttachDescriptor(GenTree node, GenLocalDescriptor descriptor)
            {
                node.LocalDescriptor = descriptor;
                node.ValueKey = GenTreeValueKey.ForTree(node);
            }

            private bool TryGetCandidateFieldAccess(GenTree node, out CandidateFieldAccess access)
            {
                if (node.Field is null)
                {
                    access = default;
                    return false;
                }

                if (node.Kind == GenTreeKind.Field)
                {
                    if (node.Operands.Length < 1)
                    {
                        access = default;
                        return false;
                    }
                }
                else if (node.Kind == GenTreeKind.StoreField)
                {
                    if (node.Operands.Length < 2)
                    {
                        access = default;
                        return false;
                    }
                }
                else
                {
                    access = default;
                    return false;
                }

                var receiver = node.Operands[0];
                if (!TryGetCandidateAddress(receiver, out var candidate))
                {
                    access = default;
                    return false;
                }

                access = new CandidateFieldAccess(candidate, node.Field, 0);
                return true;
            }

            private bool TryGetSelectedFieldAccess(GenTree node, out CandidateFieldAccess access)
            {
                if (!TryGetCandidateFieldAccess(node, out access))
                    return false;

                return _selectedByParentLclNum.ContainsKey(access.Candidate.Parent.LclNum);
            }

            private bool TryGetCandidateFieldAddress(GenTree node, out CandidateFieldAccess access)
            {
                if (node.Kind != GenTreeKind.FieldAddr || node.Operands.Length == 0 || node.Field is null)
                {
                    access = default;
                    return false;
                }

                var receiver = node.Operands[0];
                if (!TryGetCandidateAddress(receiver, out var candidate))
                {
                    access = default;
                    return false;
                }

                access = new CandidateFieldAccess(candidate, node.Field, 0);
                return true;
            }

            private bool TryGetCandidateAddress(GenTree node, out PromotionCandidate candidate)
            {
                if (node.Kind is not (GenTreeKind.ArgAddr or GenTreeKind.LocalAddr or GenTreeKind.TempAddr))
                {
                    candidate = null!;
                    return false;
                }

                if (node.LocalDescriptor is null)
                {
                    candidate = null!;
                    return false;
                }

                return _candidatesByParentLclNum.TryGetValue(node.LocalDescriptor.LclNum, out candidate!);
            }

            private bool TryGetCandidateDirectLoad(GenTree node, out PromotionCandidate candidate)
            {
                if (node.Kind is not (GenTreeKind.Arg or GenTreeKind.Local or GenTreeKind.Temp))
                {
                    candidate = null!;
                    return false;
                }

                if (node.LocalDescriptor is null)
                {
                    candidate = null!;
                    return false;
                }

                return _candidatesByParentLclNum.TryGetValue(node.LocalDescriptor.LclNum, out candidate!);
            }

            private bool TryGetSelectedDirectLoad(GenTree node, out PromotionCandidate candidate)
            {
                if (!TryGetCandidateDirectLoad(node, out candidate))
                    return false;

                return _selectedByParentLclNum.ContainsKey(candidate.Parent.LclNum);
            }

            private bool TryGetCandidateDirectStore(GenTree node, out PromotionCandidate candidate)
            {
                if (node.Kind is not (GenTreeKind.StoreArg or GenTreeKind.StoreLocal or GenTreeKind.StoreTemp))
                {
                    candidate = null!;
                    return false;
                }

                if (node.LocalDescriptor is null)
                {
                    candidate = null!;
                    return false;
                }

                return _candidatesByParentLclNum.TryGetValue(node.LocalDescriptor.LclNum, out candidate!);
            }

            private bool TryGetSelectedDirectStore(GenTree node, out PromotionCandidate candidate)
            {
                if (!TryGetCandidateDirectStore(node, out candidate))
                    return false;

                return _selectedByParentLclNum.ContainsKey(candidate.Parent.LclNum);
            }

            private static bool IsDefaultValueForParent(GenTree node, GenLocalDescriptor parent)
            {
                return node.Kind == GenTreeKind.DefaultValue &&
                       (ReferenceEquals(node.RuntimeType, parent.Type) || ReferenceEquals(node.Type, parent.Type));
            }

            private static GenStackKind StackKindForStorage(RuntimeType type)
            {
                if (type.IsReferenceType)
                    return GenStackKind.Ref;
                if (type.Kind == RuntimeTypeKind.ByRef)
                    return GenStackKind.ByRef;
                if (type.Kind == RuntimeTypeKind.Pointer)
                    return GenStackKind.Ptr;
                if (type.Name == "Single")
                    return GenStackKind.R4;
                if (type.Name == "Double")
                    return GenStackKind.R8;
                if (type.SizeOf <= 4)
                    return GenStackKind.I4;
                if (type.SizeOf <= 8)
                    return GenStackKind.I8;
                return GenStackKind.Value;
            }

            private static int ComputeNextTreeId(GenTreeMethod method)
            {
                int max = -1;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        if (nodes[n].Id > max)
                            max = nodes[n].Id;
                    }
                }

                return max + 1;
            }
        }

        private sealed class PromotionCandidate
        {
            private readonly Dictionary<int, GenLocalDescriptor> _fieldDescriptorsByFieldId;

            public GenLocalDescriptor Parent { get; }
            public ImmutableArray<RuntimeField> Fields { get; }
            public bool Rejected { get; private set; }
            public bool HasPromotableAccess { get; private set; }
            public IEnumerable<int> CopyDependencyParentLclNums => _copyDependencyParentLclNums;

            private readonly HashSet<int> _copyDependencyParentLclNums = new HashSet<int>();

            public PromotionCandidate(GenLocalDescriptor parent, ImmutableArray<RuntimeField> fields)
            {
                Parent = parent ?? throw new ArgumentNullException(nameof(parent));
                Fields = fields.IsDefault ? ImmutableArray<RuntimeField>.Empty : fields;
                _fieldDescriptorsByFieldId = new Dictionary<int, GenLocalDescriptor>();
            }

            public bool HasField(RuntimeField field)
            {
                for (int i = 0; i < Fields.Length; i++)
                {
                    if (ReferenceEquals(Fields[i], field) || Fields[i].FieldId == field.FieldId)
                        return true;
                }

                return false;
            }

            public void Reject()
            {
                Rejected = true;
            }

            public void NotePromotableAccess()
            {
                HasPromotableAccess = true;
            }

            public void NoteCopyDependency(PromotionCandidate candidate)
            {
                if (!ReferenceEquals(candidate, this))
                    _copyDependencyParentLclNums.Add(candidate.Parent.LclNum);
            }

            public void SetFieldDescriptor(RuntimeField field, GenLocalDescriptor descriptor)
            {
                _fieldDescriptorsByFieldId[field.FieldId] = descriptor;
            }

            public GenLocalDescriptor GetFieldDescriptor(RuntimeField field)
            {
                if (_fieldDescriptorsByFieldId.TryGetValue(field.FieldId, out var descriptor))
                    return descriptor;

                throw new InvalidOperationException("No physical promoted field descriptor for " + field.Name + ".");
            }
        }

        private readonly struct CandidateFieldAccess
        {
            public PromotionCandidate Candidate { get; }
            public RuntimeField Field { get; }
            public int ReceiverOperandIndex { get; }

            public CandidateFieldAccess(PromotionCandidate candidate, RuntimeField field, int receiverOperandIndex)
            {
                Candidate = candidate;
                Field = field;
                ReceiverOperandIndex = receiverOperandIndex;
            }
        }
    }
}
