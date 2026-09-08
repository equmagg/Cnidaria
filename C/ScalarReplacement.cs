using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.C
{
    public sealed class ScalarReplacementOptions
    {
        public static ScalarReplacementOptions Default { get; } = new ScalarReplacementOptions();

        public bool Enabled { get; }
        public int MaxAggregateSize { get; }
        public int MaxFieldsPerAggregate { get; }
        public int MaxPromotedFields { get; }
        public int MaxAnalysisNodes { get; }
        public int MaxStatementGrowth { get; }

        public ScalarReplacementOptions(
            bool enabled = true,
            int maxAggregateSize = 64,
            int maxFieldsPerAggregate = 8,
            int maxPromotedFields = 64,
            int maxAnalysisNodes = 32768,
            int maxStatementGrowth = 256)
        {
            if (maxAggregateSize < 1) throw new ArgumentOutOfRangeException(nameof(maxAggregateSize));
            if (maxFieldsPerAggregate < 1) throw new ArgumentOutOfRangeException(nameof(maxFieldsPerAggregate));
            if (maxPromotedFields < 1) throw new ArgumentOutOfRangeException(nameof(maxPromotedFields));
            if (maxAnalysisNodes < 1) throw new ArgumentOutOfRangeException(nameof(maxAnalysisNodes));
            if (maxStatementGrowth < 0) throw new ArgumentOutOfRangeException(nameof(maxStatementGrowth));
            Enabled = enabled;
            MaxAggregateSize = maxAggregateSize;
            MaxFieldsPerAggregate = maxFieldsPerAggregate;
            MaxPromotedFields = maxPromotedFields;
            MaxAnalysisNodes = maxAnalysisNodes;
            MaxStatementGrowth = maxStatementGrowth;
        }
    }

    public static class ScalarReplacement
    {
        public static GimpleTree Replace(GimpleTree tree, ScalarReplacementOptions? options = null)
        {
            if (tree is null) throw new ArgumentNullException(nameof(tree));
            options ??= ScalarReplacementOptions.Default;
            if (!options.Enabled) return tree;

            ImmutableArray<GimpleNode>.Builder? members = null;
            for (var i = 0; i < tree.Members.Length; i++)
            {
                if (tree.Members[i] is not GimpleFunctionDefinition function || function.HasScalarReplacementApplied) continue;
                var rewritten = new Pass(function, tree.SemanticModel.Compilation.Options.Target, options).Run();
                if (ReferenceEquals(rewritten, function)) continue;
                members ??= tree.Members.ToBuilder();
                members[i] = rewritten;
            }

            return members is null ? tree : new GimpleTree(tree.SemanticModel, members.ToImmutable(), tree.Diagnostics, tree.HasInliningApplied);
        }

        private enum AccessKind : byte { Read, Write, Reject, Zero }

        private sealed class Layout
        {
            public readonly QualifiedType Type;
            public readonly List<(FieldSymbol? Field, Layout Layout)> Children = new();
            public int LeafCount;
            public int Size;
            public int Alignment;

            public Layout(QualifiedType type) => Type = type;
        }

        private sealed class Candidate
        {
            public readonly GimplePlace Root;
            public readonly Layout Layout;
            public readonly List<GimplePlace> Fields = new();
            public readonly List<GimpleTemporaryValue> Replacements = new();
            public long Benefit;
            public long Cost;
            public long Growth;
            public long LoopWriteCost;
            public bool Rejected;
            public bool Selected;
            public bool NeedsMemory;

            public Candidate(GimplePlace root, Layout layout)
            {
                Root = root;
                Layout = layout;
                NeedsMemory = root is GimpleSymbolValue { Symbol: ParameterSymbol };
            }
        }

        private sealed class Access
        {
            public readonly Candidate Candidate;
            public readonly Layout Layout;
            public readonly int FirstField;

            public Access(Candidate candidate, Layout layout, int firstField)
            {
                Candidate = candidate;
                Layout = layout;
                FirstField = firstField;
            }

            public bool IsScalar => Layout.Children.Count == 0;
        }

        private sealed class StatementAccesses
        {
            public readonly List<Access> Reads = new();
            public readonly List<Access> Writes = new();
            public Access? Zero;
        }

        private sealed record ArrayAddress(GimpleAssignStatement Definition, Access Access, int Block, int Statement);

        private sealed class Pass
        {
            private const int MaxDepth = 16;
            private readonly GimpleFunctionDefinition _function;
            private readonly TargetInfo _target;
            private readonly ScalarReplacementOptions _options;
            private readonly Dictionary<GimpleVariableKey, Candidate> _candidates = new();
            private readonly Dictionary<QualifiedType, Layout?> _layouts = new();
            private readonly Dictionary<GimpleValue, Access?> _accesses = new();
            private readonly Dictionary<GimpleStatement, StatementAccesses> _statements = new();
            private readonly Dictionary<GimpleTemporaryValue, ArrayAddress> _arrayAddresses = new();
            private readonly Dictionary<GimpleTemporaryValue, int> _temporaryUses = new();
            private readonly HashSet<GimpleTemporaryValue> _singleDefinitions = new();
            private int _work;
            private bool _exhausted;
            private int _nextTemporary;
            private int _blockIndex;
            private int _statementIndex;
            private int _firstReplacement;

            public Pass(GimpleFunctionDefinition function, TargetInfo target, ScalarReplacementOptions options)
            {
                _function = function;
                _target = target;
                _options = options;
            }

            public GimpleFunctionDefinition Run()
            {
                foreach (var temporary in _function.Temporaries)
                {
                    if (!Spend()) return _function;
                    if (temporary.Ordinal == int.MaxValue) return _function;
                    _nextTemporary = Math.Max(_nextTemporary, temporary.Ordinal + 1);
                    AddCandidate(temporary);
                }
                if (_function.Symbol?.FunctionType is FunctionType signature)
                    foreach (var parameter in signature.Parameters)
                    {
                        if (!Spend()) return _function;
                        AddCandidate(new GimpleSymbolValue(parameter, parameter.Type));
                    }

                foreach (var block in _function.Blocks)
                {
                    if (!Spend()) return _function;
                    foreach (var statement in block.Statements)
                    {
                        if (!Spend()) return _function;
                        if (statement is GimpleDeclarationStatement declaration &&
                            declaration.Symbol is VariableSymbol variable &&
                            declaration.StorageClass is StorageClass.None or StorageClass.Auto or StorageClass.Register &&
                            variable.StorageClass is StorageClass.None or StorageClass.Auto or StorageClass.Register &&
                            variable.ExplicitRegisterName is null && declaration.Declaration.Initializer is null)
                        {
                            AddCandidate(new GimpleSymbolValue(variable, declaration.Type, declaration.Syntax));
                        }
                    }
                }
                if (_exhausted || _candidates.Count == 0) return _function;

                _firstReplacement = _nextTemporary;
                FindArrayAddresses();
                if (_exhausted) return _function;
                var weights = GetBlockWeights();
                if (_exhausted) return _function;
                for (_blockIndex = 0; _blockIndex < _function.Blocks.Length && !_exhausted; _blockIndex++)
                    for (_statementIndex = 0; _statementIndex < _function.Blocks[_blockIndex].Statements.Length && !_exhausted; _statementIndex++)
                        Analyze(_function.Blocks[_blockIndex].Statements[_statementIndex], weights[_blockIndex]);
                if (_exhausted) return _function;

                var selected = new List<Candidate>();
                foreach (var candidate in _candidates.Values)
                    if (!candidate.Rejected && candidate.Benefit > candidate.Cost + candidate.Layout.LeafCount)
                        selected.Add(candidate);
                selected.Sort((a, b) => (b.Benefit - b.Cost).CompareTo(a.Benefit - a.Cost));
                var fieldBudget = _options.MaxPromotedFields;
                long growthBudget = _options.MaxStatementGrowth;
                foreach (var candidate in selected)
                {
                    if (candidate.Layout.LeafCount > fieldBudget || candidate.Growth > growthBudget ||
                        (long)_nextTemporary + (_options.MaxPromotedFields - fieldBudget) + candidate.Layout.LeafCount > int.MaxValue) continue;
                    candidate.Selected = true;
                    fieldBudget -= candidate.Layout.LeafCount;
                    growthBudget -= candidate.Growth;
                }
                if (fieldBudget == _options.MaxPromotedFields) return _function;

                var copyDependencies = new Dictionary<Candidate, List<Candidate>>();
                void Depend(Candidate from, Candidate to)
                {
                    if (ReferenceEquals(from, to)) return;
                    if (!copyDependencies.TryGetValue(from, out var dependents)) copyDependencies.Add(from, dependents = new());
                    dependents.Add(to);
                }
                foreach (var pair in _statements)
                {
                    if (CanDecomposeCopy(pair.Key, pair.Value))
                    {
                        var source = pair.Value.Reads[0].Candidate;
                        var destination = pair.Value.Writes[0].Candidate;
                        Depend(source, destination);
                        Depend(destination, source);
                        continue;
                    }
                    foreach (var access in pair.Value.Reads) access.Candidate.NeedsMemory = true;
                    foreach (var access in pair.Value.Writes) access.Candidate.NeedsMemory = true;
                }

                var rejected = new Queue<Candidate>();
                void CheckMemoryCost(Candidate candidate)
                {
                    if (candidate.Selected && candidate.NeedsMemory &&
                        candidate.Benefit <= candidate.Cost + 2L * candidate.Layout.LeafCount + candidate.LoopWriteCost)
                    {
                        candidate.Selected = false;
                        rejected.Enqueue(candidate);
                    }
                }
                foreach (var candidate in selected) CheckMemoryCost(candidate);
                while (rejected.TryDequeue(out var candidate))
                    if (copyDependencies.TryGetValue(candidate, out var dependents))
                        foreach (var dependent in dependents)
                        {
                            dependent.NeedsMemory = true;
                            CheckMemoryCost(dependent);
                        }

                var changed = false;
                foreach (var candidate in selected)
                    if (candidate.Selected)
                    {
                        changed = true;
                        CreateFields(candidate, candidate.Root, candidate.Layout);
                    }
                if (!changed) return _function;

                var temporaries = ImmutableArray.CreateBuilder<GimpleTemporaryValue>();
                foreach (var temporary in _function.Temporaries)
                    if ((!TryGetCandidate(temporary, out var candidate) || !candidate.Selected || candidate.NeedsMemory) &&
                        (!_arrayAddresses.TryGetValue(temporary, out var address) || !address.Access.Candidate.Selected))
                        temporaries.Add(temporary);
                foreach (var candidate in _candidates.Values)
                    temporaries.AddRange(candidate.Replacements);

                var blocks = ImmutableArray.CreateBuilder<GimpleBasicBlock>(_function.Blocks.Length);
                foreach (var block in _function.Blocks)
                {
                    var statements = ImmutableArray.CreateBuilder<GimpleStatement>();
                    if (ReferenceEquals(block.Label, _function.EntryLabel))
                        foreach (var candidate in _candidates.Values)
                            if (candidate.Selected && candidate.Root is GimpleSymbolValue { Symbol: ParameterSymbol })
                                Synchronize(new Access(candidate, candidate.Layout, 0), statements, writeBack: false);
                    foreach (var statement in block.Statements) Rewrite(statement, statements);
                    blocks.Add(new GimpleBasicBlock(block.Label, statements.ToImmutable()));
                }
                return new GimpleFunctionDefinition(_function.Syntax, _function.Symbol, temporaries.ToImmutable(), blocks.ToImmutable(), _function.EntryLabel,
                    hasScalarReplacementApplied: true);
            }

            private bool Spend(int depth = 0)
            {
                if (depth > MaxDepth || ++_work > _options.MaxAnalysisNodes) _exhausted = true;
                return !_exhausted;
            }

            private void AddCandidate(GimplePlace root)
            {
                if (!IsAggregate(root.Type) || !Spend()) return;
                var key = Key(root);
                if (_candidates.ContainsKey(key)) return;
                if (!_layouts.TryGetValue(root.Type, out var layout))
                {
                    var leaves = 0;
                    layout = BuildLayout(root.Type, 0, ref leaves);
                    _layouts.Add(root.Type, layout);
                }
                if (layout is null || layout.LeafCount == 0) return;
                var candidate = new Candidate(root, layout);
                if (candidate.NeedsMemory)
                {
                    candidate.Cost = 2L * layout.LeafCount;
                    candidate.Growth = layout.LeafCount;
                }
                _candidates.Add(key, candidate);
            }

            private Layout? BuildLayout(QualifiedType type, int depth, ref int leaves)
            {
                if (!Spend(depth) || IsObservable(type)) return null;
                var layout = new Layout(type);
                if (type.Type is ArrayType array)
                {
                    if (array.Length is not long length || length <= 0 || length > _options.MaxFieldsPerAggregate) return null;
                    for (var i = 0; i < length; i++)
                    {
                        var child = BuildLayout(array.ElementType, depth + 1, ref leaves);
                        if (child is null || !Append(layout, null, child)) return null;
                    }
                }
                else if (type.Type is TagType { Symbol.TagKind: TagKind.Struct } tag)
                {
                    if (!tag.Symbol.IsComplete || tag.Symbol.Fields.Length == 0 || tag.Symbol.Fields.Length > _options.MaxFieldsPerAggregate) return null;
                    foreach (var field in tag.Symbol.Fields)
                    {
                        var child = BuildLayout(field.Type, depth + 1, ref leaves);
                        if (child is null || !Append(layout, field, child)) return null;
                    }
                }
                else
                {
                    if (type.Type.Kind is not (TypeKind.Builtin or TypeKind.Enum or TypeKind.Pointer) ||
                        type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void or BuiltinTypeKind.LongDouble } ||
                        ++leaves > _options.MaxFieldsPerAggregate) return null;
                    layout.Size = _target.SizeOf(type);
                    layout.Alignment = _target.AlignOf(type);
                    layout.LeafCount = 1;
                }
                var size = ((long)layout.Size + layout.Alignment - 1) / layout.Alignment * layout.Alignment;
                if (size <= 0 || size > _options.MaxAggregateSize) return null;
                layout.Size = (int)size;
                return layout;
            }

            private bool Append(Layout parent, FieldSymbol? field, Layout child)
            {
                var size = ((long)parent.Size + child.Alignment - 1) / child.Alignment * child.Alignment + child.Size;
                if (size > _options.MaxAggregateSize) return false;
                parent.Size = (int)size;
                parent.Alignment = Math.Max(parent.Alignment, child.Alignment);
                parent.LeafCount += child.LeafCount;
                parent.Children.Add((field, child));
                return true;
            }

            private void FindArrayAddresses()
            {
                var definitions = new Dictionary<GimpleTemporaryValue, (GimpleStatement? Definition, int Block, int Statement)>();
                void Define(GimplePlace? lhs, GimpleStatement definition, int block, int statement)
                {
                    if (lhs is not GimpleTemporaryValue temporary) return;
                    if (definitions.ContainsKey(temporary)) definitions[temporary] = (null, 0, 0);
                    else definitions.Add(temporary, (definition, block, statement));
                }
                for (var b = 0; b < _function.Blocks.Length; b++)
                {
                    for (var s = 0; s < _function.Blocks[b].Statements.Length; s++)
                    {
                        if (!Spend()) return;
                        var statement = _function.Blocks[b].Statements[s];
                        switch (statement)
                        {
                            case GimpleAssignStatement assign: Define(assign.Lhs, assign, b, s); break;
                            case GimpleCallStatement call: Define(call.Lhs, call, b, s); break;
                            case GimpleAsmStatement asm:
                                foreach (var output in asm.Outputs) Define(output.Target, asm, b, s);
                                break;
                        }
                    }
                }
                foreach (var pair in definitions)
                {
                    if (pair.Value.Definition is not null) _singleDefinitions.Add(pair.Key);
                    _blockIndex = pair.Value.Block;
                    _statementIndex = pair.Value.Statement;
                    if (pair.Value.Definition is GimpleAssignStatement { IsCopy: true, Op1: GimpleAddressOfExpression address } assign &&
                        address.Target.Type.Type is ArrayType array && pair.Key.Type.Type is PointerType pointer &&
                        pointer.PointeeType.Equals(array.ElementType) && Resolve(address.Target) is Access access)
                        _arrayAddresses.Add(pair.Key, new ArrayAddress(assign, access, pair.Value.Block, pair.Value.Statement));
                }
            }

            // Backward edges supply a bounded loop hint; no loop or profile analysis is required.
            private int[] GetBlockWeights()
            {
                var indices = new Dictionary<GimpleLabel, int>();
                for (var i = 0; i < _function.Blocks.Length; i++) indices[_function.Blocks[i].Label] = i;
                var deltas = new int[_function.Blocks.Length + 1];
                void Edge(int from, GimpleLabel target)
                {
                    if (indices.TryGetValue(target, out var to) && to <= from)
                    {
                        deltas[to]++;
                        deltas[from + 1]--;
                    }
                }
                for (var i = 0; i < _function.Blocks.Length; i++)
                {
                    var statements = _function.Blocks[i].Statements;
                    if (statements.Length == 0) continue;
                    switch (statements[^1])
                    {
                        case GimpleGotoStatement go: Edge(i, go.Target); break;
                        case GimpleCondStatement cond: Edge(i, cond.WhenTrue); Edge(i, cond.WhenFalse); break;
                        case GimpleSwitchStatement sw:
                            Edge(i, sw.DefaultLabel);
                            foreach (var item in sw.Cases)
                            {
                                if (!Spend()) return Array.Empty<int>();
                                Edge(i, item.Target);
                            }
                            break;
                    }
                }
                var weights = new int[_function.Blocks.Length];
                var active = 0;
                for (var i = 0; i < weights.Length; i++)
                {
                    active += deltas[i];
                    weights[i] = active > 0 ? 8 : 1;
                }
                return weights;
            }

            private void Analyze(GimpleStatement statement, int weight)
            {
                if (!Spend()) return;
                if (statement is GimpleAssignStatement { Lhs: GimpleTemporaryValue temporary } definition &&
                    _arrayAddresses.TryGetValue(temporary, out var address) && ReferenceEquals(address.Definition, definition)) return;
                var accesses = new StatementAccesses();
                switch (statement)
                {
                    case GimpleAssignStatement assign:
                        Visit(assign.Lhs, assign.IsConstructor ? AccessKind.Zero : AccessKind.Write, accesses, weight);
                        foreach (var operand in assign.Operands) Visit(operand, AccessKind.Read, accesses, weight);
                        break;
                    case GimpleCallStatement call:
                        if (call.Lhs is not null) Visit(call.Lhs, AccessKind.Write, accesses, weight);
                        Visit(call.Function, AccessKind.Read, accesses, weight);
                        foreach (var argument in call.Arguments) Visit(argument, AccessKind.Read, accesses, weight);
                        break;
                    case GimpleCondStatement cond:
                        Visit(cond.Lhs, AccessKind.Read, accesses, weight);
                        Visit(cond.Rhs, AccessKind.Read, accesses, weight);
                        break;
                    case GimpleSwitchStatement sw: Visit(sw.Expression, AccessKind.Read, accesses, weight); break;
                    case GimpleReturnStatement ret when ret.Expression is not null: Visit(ret.Expression, AccessKind.Read, accesses, weight); break;
                    case GimpleAsmStatement asm:
                        foreach (var operand in asm.Outputs)
                        {
                            if (operand.Target is not null) Visit(operand.Target, AccessKind.Reject, accesses, weight);
                            if (operand.Value is not null) Visit(operand.Value, AccessKind.Reject, accesses, weight);
                        }
                        foreach (var operand in asm.Inputs)
                            if (operand.Value is not null) Visit(operand.Value, AccessKind.Reject, accesses, weight);
                        break;
                    case GimpleDeclarationStatement:
                    case GimpleGotoStatement:
                    case GimpleNopStatement:
                        break;
                    default: _exhausted = true; break;
                }
                if (accesses.Reads.Count != 0 || accesses.Writes.Count != 0 || accesses.Zero is not null)
                    _statements.Add(statement, accesses);
            }

            private void Visit(GimpleValue value, AccessKind kind, StatementAccesses accesses, int weight, int depth = 0)
            {
                if (!Spend(depth)) return;
                if (value is GimpleTemporaryValue used && kind is AccessKind.Read or AccessKind.Reject)
                {
                    _temporaryUses.TryGetValue(used, out var count);
                    _temporaryUses[used] = count + 1;
                }
                if (value is GimpleTemporaryValue temporary && _arrayAddresses.TryGetValue(temporary, out var arrayAddress))
                {
                    arrayAddress.Access.Candidate.Rejected = true;
                    return;
                }
                var access = Resolve(value);
                if (access is not null)
                {
                    var candidate = access.Candidate;
                    if (kind == AccessKind.Reject || IsObservable(value.Type)) { candidate.Rejected = true; return; }
                    if (access.IsScalar)
                    {
                        candidate.Benefit += 2L * weight;
                        // Memory-backed loop updates need a margin for phi copies and register pressure.
                        if (kind == AccessKind.Write && weight > 1) candidate.LoopWriteCost += 5L * weight;
                        return;
                    }
                    if (kind == AccessKind.Zero)
                    {
                        accesses.Zero = access;
                        candidate.Cost += (long)weight * Math.Max(0, access.Layout.LeafCount - 1);
                        candidate.Growth += access.Layout.LeafCount;
                    }
                    else
                    {
                        (kind == AccessKind.Write ? accesses.Writes : accesses.Reads).Add(access);
                        candidate.Cost += 2L * weight * access.Layout.LeafCount;
                        candidate.Growth += access.Layout.LeafCount;
                    }
                    return;
                }
                var childKind = kind == AccessKind.Reject ? kind : AccessKind.Read;
                switch (value)
                {
                    case GimpleAddressOfExpression address: Visit(address.Target, AccessKind.Reject, accesses, weight, depth + 1); break;
                    case GimpleMemberAccessExpression member:
                        Visit(member.Expression, member.ThroughPointer ? childKind : AccessKind.Reject, accesses, weight, depth + 1);
                        break;
                    case GimpleElementAccessExpression element:
                        Visit(element.Expression, element.Expression.Type.Type is ArrayType ? AccessKind.Reject : childKind, accesses, weight, depth + 1);
                        if (element.Index is not null) Visit(element.Index, childKind, accesses, weight, depth + 1);
                        break;
                    case GimpleIndirectExpression indirect: Visit(indirect.Address, childKind, accesses, weight, depth + 1); break;
                    case GimpleConversionExpression conversion:
                        Visit(conversion.Operand, IsAggregate(conversion.Operand.Type) ? AccessKind.Reject : childKind, accesses, weight, depth + 1);
                        break;
                    case GimpleCastExpression cast: Visit(cast.Operand, IsAggregate(cast.Operand.Type) ? AccessKind.Reject : childKind, accesses, weight, depth + 1); break;
                    case GimpleUnaryExpression unary: Visit(unary.Operand, childKind, accesses, weight, depth + 1); break;
                    case GimpleBinaryExpression binary:
                        Visit(binary.Left, childKind, accesses, weight, depth + 1);
                        Visit(binary.Right, childKind, accesses, weight, depth + 1);
                        break;
                }
            }

            private Access? Resolve(GimpleValue value, int depth = 0)
            {
                if (_accesses.TryGetValue(value, out var cached)) return cached;
                if (!Spend(depth)) return null;
                Access? result = null;
                if (TryGetCandidate(value, out var candidate)) result = new Access(candidate, candidate.Layout, 0);
                else if (value is GimpleMemberAccessExpression { ThroughPointer: false, Field: not null } member)
                {
                    var parent = Resolve(member.Expression, depth + 1);
                    if (parent is not null)
                    {
                        var first = parent.FirstField;
                        foreach (var child in parent.Layout.Children)
                        {
                            if (ReferenceEquals(child.Field, member.Field)) { result = new Access(parent.Candidate, child.Layout, first); break; }
                            first += child.Layout.LeafCount;
                        }
                    }
                }
                else if (value is GimpleElementAccessExpression element &&
                         element.Index is GimpleConstantValue constant && TryIndex(constant.Value, out var index))
                {
                    Access? parent = null;
                    if (element.Expression.Type.Type is ArrayType) parent = Resolve(element.Expression, depth + 1);
                    else if (element.Expression is GimpleTemporaryValue temporary && _arrayAddresses.TryGetValue(temporary, out var address) &&
                             address.Block == _blockIndex && address.Statement < _statementIndex)
                        parent = address.Access;
                    if (parent is not null && index >= 0 && index < parent.Layout.Children.Count)
                    {
                        var child = parent.Layout.Children[(int)index].Layout;
                        result = new Access(parent.Candidate, child, parent.FirstField + (int)index * child.LeafCount);
                    }
                }
                if (result is not null && !result.Layout.Type.Equals(value.Type))
                {
                    result.Candidate.Rejected = true;
                    result = null;
                }
                _accesses[value] = result;
                return result;
            }

            private void CreateFields(Candidate candidate, GimplePlace place, Layout layout)
            {
                if (layout.Children.Count == 0)
                {
                    candidate.Fields.Add(place);
                    candidate.Replacements.Add(new GimpleTemporaryValue(_nextTemporary++, layout.Type, place.Syntax));
                    return;
                }
                for (var i = 0; i < layout.Children.Count; i++)
                {
                    var child = layout.Children[i];
                    GimplePlace field = child.Field is not null
                        ? new GimpleMemberAccessExpression(place, false, default, child.Field, child.Layout.Type, place.Syntax)
                        : new GimpleElementAccessExpression(place, new GimpleConstantValue(i, new QualifiedType(TypeCatalog.Instance.Int)), child.Layout.Type, place.Syntax);
                    CreateFields(candidate, field, child.Layout);
                }
            }

            private static bool CanDecomposeCopy(GimpleStatement statement, StatementAccesses accesses)
                => statement is GimpleAssignStatement { IsCopy: true } && accesses.Reads.Count == 1 && accesses.Writes.Count == 1 &&
                   accesses.Reads[0].Candidate.Selected && accesses.Writes[0].Candidate.Selected &&
                   accesses.Reads[0].Layout.Type.Equals(accesses.Writes[0].Layout.Type);

            private void Rewrite(GimpleStatement statement, ImmutableArray<GimpleStatement>.Builder output)
            {
                if (statement is GimpleAssignStatement { Lhs: GimpleTemporaryValue temporary } definition &&
                    _arrayAddresses.TryGetValue(temporary, out var address) && ReferenceEquals(address.Definition, definition) &&
                    address.Access.Candidate.Selected) return;
                if (statement is GimpleDeclarationStatement declaration && declaration.Symbol is not null &&
                    _candidates.TryGetValue(GimpleVariableKey.FromSymbol(declaration.Symbol), out var declared) &&
                    declared.Selected && !declared.NeedsMemory) return;

                _statements.TryGetValue(statement, out var accesses);
                if (accesses is not null)
                {
                    if (CanDecomposeCopy(statement, accesses))
                    {
                        var source = accesses.Reads[0];
                        var destination = accesses.Writes[0];
                        for (var i = 0; i < source.Layout.LeafCount; i++)
                            output.Add(GimpleAssignStatement.Single(destination.Candidate.Replacements[destination.FirstField + i], source.Candidate.Replacements[source.FirstField + i], statement.Syntax));
                        return;
                    }
                    if (accesses.Zero is Access zero && zero.Candidate.Selected)
                    {
                        if (zero.Candidate.NeedsMemory) output.Add(statement);
                        for (var i = 0; i < zero.Layout.LeafCount; i++)
                            output.Add(GimpleAssignStatement.Constructor(zero.Candidate.Replacements[zero.FirstField + i], statement.Syntax));
                        return;
                    }
                    foreach (var read in accesses.Reads) Synchronize(read, output, writeBack: true);
                }
                AppendRewritten(output, statement switch
                {
                    GimpleAssignStatement assign => assign.WithOperands((GimplePlace)Map(assign.Lhs), Map(assign.Operands)),
                    GimpleCallStatement call => call.WithOperands(call.Lhs is null ? null : (GimplePlace)Map(call.Lhs), Map(call.Function), Map(call.Arguments)),
                    GimpleCondStatement cond => cond.WithOperands(cond.Code, Map(cond.Lhs), Map(cond.Rhs)),
                    GimpleSwitchStatement sw => new GimpleSwitchStatement(Map(sw.Expression), sw.Cases, sw.DefaultLabel, sw.Syntax),
                    GimpleReturnStatement ret => new GimpleReturnStatement(ret.Function, ret.Expression is null ? null : Map(ret.Expression), ret.Syntax),
                    _ => statement,
                });
                if (accesses is not null)
                    foreach (var write in accesses.Writes) Synchronize(write, output, writeBack: false);
            }

            private void AppendRewritten(ImmutableArray<GimpleStatement>.Builder output, GimpleStatement statement)
            {
                if (statement is GimpleAssignStatement { IsCopy: true, Lhs: GimpleTemporaryValue destination, Op1: GimpleTemporaryValue source } &&
                    destination.Ordinal >= _firstReplacement && source.Ordinal < _firstReplacement &&
                    _singleDefinitions.Contains(source) && _temporaryUses.TryGetValue(source, out var uses) && uses == 1 &&
                    output.Count != 0 && output[^1] is GimpleAssignStatement previous && ReferenceEquals(previous.Lhs, source) &&
                    destination.Type.Equals(source.Type))
                {
                    output[^1] = previous.WithOperands(destination, previous.Operands);
                    return;
                }
                output.Add(statement);
            }

            // Keep scalar state authoritative between whole-object operations, including across CFG edges.
            private static void Synchronize(Access access, ImmutableArray<GimpleStatement>.Builder output, bool writeBack)
            {
                var candidate = access.Candidate;
                if (!candidate.Selected) return;
                for (var i = access.FirstField; i < access.FirstField + access.Layout.LeafCount; i++)
                    output.Add(writeBack
                        ? GimpleAssignStatement.Single(candidate.Fields[i], candidate.Replacements[i])
                        : GimpleAssignStatement.Single(candidate.Replacements[i], candidate.Fields[i]));
            }

            private ImmutableArray<GimpleValue> Map(ImmutableArray<GimpleValue> values)
            {
                var mapped = values.ToBuilder();
                for (var i = 0; i < mapped.Count; i++) mapped[i] = Map(mapped[i]);
                return mapped.ToImmutable();
            }

            private GimpleValue Map(GimpleValue value)
            {
                if (_accesses.TryGetValue(value, out var access) && access is { IsScalar: true, Candidate.Selected: true })
                    return access.Candidate.Replacements[access.FirstField];
                return value switch
                {
                    GimpleMemberAccessExpression member => new GimpleMemberAccessExpression(Map(member.Expression), member.ThroughPointer, member.NameToken, member.Field, member.Type, member.Syntax),
                    GimpleElementAccessExpression element => new GimpleElementAccessExpression(Map(element.Expression), element.Index is null ? null : Map(element.Index), element.Type, element.Syntax),
                    GimpleIndirectExpression indirect => new GimpleIndirectExpression(Map(indirect.Address), indirect.Type, indirect.Syntax),
                    GimpleAddressOfExpression address => new GimpleAddressOfExpression((GimplePlace)Map(address.Target), address.Type, address.Syntax),
                    GimpleConversionExpression conversion => new GimpleConversionExpression(Map(conversion.Operand), conversion.Type, conversion.ConversionKind, conversion.Syntax),
                    GimpleCastExpression cast => new GimpleCastExpression(Map(cast.Operand), cast.Type, cast.Syntax),
                    GimpleUnaryExpression unary => new GimpleUnaryExpression(unary.Code, Map(unary.Operand), unary.Type, unary.Syntax),
                    GimpleBinaryExpression binary => new GimpleBinaryExpression(Map(binary.Left), binary.Code, Map(binary.Right), binary.Type, binary.Syntax),
                    _ => value,
                };
            }

            private bool TryGetCandidate(GimpleValue value, out Candidate candidate)
            {
                candidate = null!;
                return value is GimpleSymbolValue or GimpleTemporaryValue && _candidates.TryGetValue(Key(value), out candidate!);
            }

            private static GimpleVariableKey Key(GimpleValue value) => value is GimpleSymbolValue symbol
                ? GimpleVariableKey.FromSymbol(symbol.Symbol) : GimpleVariableKey.FromTemporary((GimpleTemporaryValue)value);

            private static bool IsAggregate(QualifiedType type) => type.Type.Kind is TypeKind.Struct or TypeKind.Array;
            private static bool IsObservable(QualifiedType type) => (type.Qualifiers & (TypeQualifiers.Volatile | TypeQualifiers.Atomic)) != 0;

            private static bool TryIndex(object? value, out long index)
            {
                switch (value)
                {
                    case sbyte v: index = v; return true;
                    case byte v: index = v; return true;
                    case short v: index = v; return true;
                    case ushort v: index = v; return true;
                    case char v: index = v; return true;
                    case int v: index = v; return true;
                    case uint v: index = v; return true;
                    case long v: index = v; return true;
                    case ulong v when v <= long.MaxValue: index = (long)v; return true;
                    default: index = 0; return false;
                }
            }
        }
    }
}
