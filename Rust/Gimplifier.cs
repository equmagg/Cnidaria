using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using IR = Cnidaria.C;

namespace Cnidaria.Rust;

internal sealed class Gimplifier
{
    // A function body with concrete type arguments; upstream and generic ones are emitted on first reference
    private sealed record Instance(FunctionSymbol Function, ImmutableArray<TypeSymbol> Arguments, IR.FunctionSymbol Symbol);
    // Instances are keyed by type names, which grow with the type tree, so polymorphic recursion is cut off by tree size as well as depth
    private const int InstanceDepthLimit = 128, InstanceSizeLimit = 1 << 16;
    private readonly Compilation _compilation;
    private readonly IR.TypeCatalog _types = IR.TypeCatalog.Instance;
    private readonly Dictionary<string, IR.QualifiedType> _typeMap = new(StringComparer.Ordinal);
    private readonly Dictionary<(FunctionSymbol Function, string Arguments), IR.FunctionSymbol> _functions = new();
    private readonly Dictionary<GlobalSymbol, IR.TypedSymbol> _globals = new();
    private readonly Dictionary<VariableSymbol, IR.TypedSymbol> _locals = new();
    private readonly Dictionary<LabelSymbol, IR.GimpleLabel> _labels = new();
    private readonly List<IR.GimpleNode> _members = new();
    private readonly List<IR.GimpleTemporaryValue> _temporaries = new();
    private readonly List<(IR.GimpleLabel Label, List<IR.GimpleStatement> Statements)> _blocks = new();
    private readonly Queue<Instance> _pending = new();
    private Dictionary<TypeParameterSymbol, TypeSymbol> _substitution = new();
    private IR.FunctionSymbol _function = null!;
    private SyntaxNode _syntax;
    private int _labelOrdinal;
    private int _dataOrdinal;
    private int _instanceOrdinal;

    private Gimplifier(Compilation compilation)
    { _compilation = compilation; _syntax = compilation.RootSyntaxTree.Root; }
    private Binder Binding => _compilation.Binding;

    internal static IR.GimpleTree Lower(Compilation compilation, BoundCompilationUnit root, IR.CompilationOptions options)
    {
        if (options.Target.PointerSize * 8 != compilation.Options.PointerWidth)
            throw new LoweringException(root.Syntax, "The semantic pointer width does not match the code generation target.");
        var emitter = new Gimplifier(compilation);
        foreach (BoundFunction function in root.Functions) if (function.Body is not null || function.Symbol.IsForeign) emitter.Function(function.Symbol, []);
        foreach (BoundGlobal global in root.Globals)
            if (global.Symbol.Kind == SymbolKind.Static) emitter.Variable(global.Symbol);
        foreach (BoundFunction function in root.Functions)
            if (function.Body is not null) emitter.EmitFunction(function, emitter.Function(function.Symbol, []), []);
        FunctionSymbol? entry = root.Functions.Select(f => f.Symbol).FirstOrDefault(f => f.ContainingSymbol == compilation.Crate && f.Name == "main");
        if (entry is not null && !compilation.Options.IsLibrary) emitter.EntryPoint(entry);
        while (emitter._pending.TryDequeue(out Instance? instance)) emitter.EmitInstance(instance);
        IR.Compilation context = IR.Compilation.Create("", compilation.Options.CrateName, options);
        return new IR.GimpleTree(context.GetSemanticModel(context.SyntaxTrees[0]), emitter._members.ToImmutableArray(), []);
    }

    // Bodies of generic instances keep their generic types; every type that drives code generation goes through here
    private TypeSymbol Concrete(TypeSymbol type)
    {
        if (_substitution.Count == 0) return type.Resolve();
        return Binding.Zonk(Binding.NormalizeIn(Binding.Types.Substitute(type, _substitution), ParamEnv.Empty));
    }
    private TypeSymbol TypeOf(BoundExpression expression) => Concrete(expression.Type);

    private IR.QualifiedType Type(TypeSymbol type)
    {
        type = Concrete(type);
        if (type is FunctionTypeSymbol { Abi: not ("Rust" or "C") } unsupported) throw Unsupported($"ABI '{unsupported.Abi}'");
        if (_typeMap.TryGetValue(type.Name, out IR.QualifiedType cached)) return cached;
        IR.QualifiedType result;
        switch (type)
        {
            case PrimitiveTypeSymbol primitive:
                IR.BuiltinTypeKind kind = primitive.Kind switch
                {
                    TypeKind.Never => IR.BuiltinTypeKind.Void,
                    TypeKind.Boolean => IR.BuiltinTypeKind.Bool,
                    TypeKind.Character => IR.BuiltinTypeKind.UnsignedInt,
                    TypeKind.Float => primitive.BitWidth == 32 ? IR.BuiltinTypeKind.Float : IR.BuiltinTypeKind.Double,
                    TypeKind.Integer => (primitive.BitWidth, primitive.IsSigned) switch
                    {
                        (8, true) => IR.BuiltinTypeKind.SignedChar, (8, false) => IR.BuiltinTypeKind.UnsignedChar,
                        (16, true) => IR.BuiltinTypeKind.Short, (16, false) => IR.BuiltinTypeKind.UnsignedShort,
                        (32, true) => IR.BuiltinTypeKind.Int, (32, false) => IR.BuiltinTypeKind.UnsignedInt,
                        (64, true) => IR.BuiltinTypeKind.LongLong, (64, false) => IR.BuiltinTypeKind.UnsignedLongLong,
                        _ => throw Unsupported($"Integer type '{type}'")
                    },
                    _ => throw Unsupported($"Type '{type}'")
                };
                result = _types.Builtin(kind);
                break;
            case TupleTypeSymbol tuple when tuple.Elements.IsEmpty:
                result = Void;
                break;
            case TupleTypeSymbol tuple:
                return Composite(type.Name, IR.TagKind.Struct, tuple.Elements.Select((t, i) => (i.ToString(CultureInfo.InvariantCulture), Type(t))), true);
            case NamedTypeSymbol named when named.Kind == TypeKind.Struct:
                return Composite(type.Name, IR.TagKind.Struct, named.Definition.Fields.Select(f => (f.Name, Type(Binding.Types.FieldType(f, named)))), true);
            case NamedTypeSymbol named when named.Kind == TypeKind.Enum && named.Definition.Variants.All(v => v.Fields.IsEmpty):
                result = named.Definition.Variants.IsEmpty ? Void : Type(Discriminant(named.Definition));
                break;
            case NamedTypeSymbol named when named.Kind == TypeKind.Enum:
                return Composite(type.Name, IR.TagKind.Struct, EnumMembers(named), true);
            case IndirectTypeSymbol indirect when IsWide(indirect):
                TypeSymbol element = indirect.ElementType is SequenceTypeSymbol slice ? slice.ElementType : Binding.Types.Primitives["u8"];
                return Composite(type.Name, IR.TagKind.Struct, [("data", Type(new IndirectTypeSymbol(TypeKind.Pointer, element, indirect.IsMutable))), ("length", Type(Binding.Types.Usize))], true);
            case IndirectTypeSymbol indirect:
                IR.QualifiedType pointee = Type(indirect.ElementType);
                result = new IR.QualifiedType(_types.PointerTo(new IR.QualifiedType(pointee.Type, indirect.IsMutable ? IR.TypeQualifiers.None : IR.TypeQualifiers.Const)));
                break;
            case SequenceTypeSymbol { Kind: TypeKind.Array, Length: ulong length } sequence:
                if (length > int.MaxValue) throw Unsupported("Arrays larger than Int32.MaxValue");
                IR.QualifiedType elementType = Type(sequence.ElementType);
                result = IR.GimpleTypes.IsVoid(elementType) ? Void : new IR.QualifiedType(_types.ArrayOf(elementType, (long)length));
                break;
            case FunctionTypeSymbol function:
                result = new IR.QualifiedType(_types.PointerTo(new IR.QualifiedType(Signature(function.ParameterTypes, function.ReturnType))));
                break;
            default: throw Unsupported($"Type '{type}'");
        }
        _typeMap.Add(type.Name, result);
        return result;
    }
    private static bool IsWide(IndirectTypeSymbol type) => type.ElementType.Kind is TypeKind.String or TypeKind.Slice;
    // An enum with fields is its discriminant followed by a union that holds the fields of the current variant
    private IEnumerable<(string Name, IR.QualifiedType Type)> EnumMembers(NamedTypeSymbol type)
    {
        yield return ("tag", Type(Discriminant(type.Definition)));
        yield return ("payload", Composite(type.Name + "$payload", IR.TagKind.Union, type.Definition.Variants.Select(v =>
            (v.Name, Composite(type.Name + "$" + v.Name, IR.TagKind.Struct, v.Fields.Select(f => (f.Name, Type(Binding.Types.FieldType(f, type)))))))));
    }
    // The tag is the smallest integer that holds every discriminant, as rustc sizes enums without a repr
    private PrimitiveTypeSymbol Discriminant(AggregateSymbol definition)
    {
        BigInteger low = definition.Variants.Min(v => v.Discriminant), high = definition.Variants.Max(v => v.Discriminant);
        bool signed = low < 0;
        int bits = new[] { 8, 16, 32 }.FirstOrDefault(b => signed ? low >= -(BigInteger.One << (b - 1)) && high < BigInteger.One << (b - 1) : high >> b == 0, 64);
        return Binding.Types.Primitives[(signed ? "i" : "u") + bits.ToString(CultureInfo.InvariantCulture)];
    }
    private static IR.GimpleValue Tag(IR.GimplePlace value) => value.Type.Type is IR.TagType ? Member(value, "tag") : value;
    private IR.QualifiedType Void => _types.Builtin(IR.BuiltinTypeKind.Void);
    private bool ZeroSized(TypeSymbol type) => Concrete(type).Kind is not (TypeKind.String or TypeKind.Slice) && IR.GimpleTypes.IsVoid(Type(type));
    // Zero-sized members are left out, so a composite of zero-sized members is zero-sized itself; a cached tag is visible to its own member types
    private IR.QualifiedType Composite(string name, IR.TagKind kind, IEnumerable<(string Name, IR.QualifiedType Type)> members, bool cache = false)
    {
        var tag = new IR.TagSymbol(name, kind, null);
        var result = new IR.QualifiedType(new IR.TagType(tag));
        if (cache) _typeMap.Add(name, result);
        var fields = ImmutableArray.CreateBuilder<IR.FieldSymbol>();
        foreach (var member in members)
            if (!IR.GimpleTypes.IsVoid(member.Type)) fields.Add(new IR.FieldSymbol(member.Name, member.Type, tag, fields.Count, null));
        if (fields.Count == 0) result = Void;
        else tag.TryDefineFields(fields.ToImmutable());
        if (cache) _typeMap[name] = result;
        return result;
    }
    private IR.FunctionType Signature(IEnumerable<TypeSymbol> parameters, TypeSymbol result)
        => _types.FunctionReturning(Type(result), parameters.Where(p => !ZeroSized(p)).Select((p, i) => new IR.ParameterSymbol("arg" + i, Type(p), null)).ToImmutableArray(), true, false);
    private static string Mangle(Symbol symbol) => "_RN" + string.Concat(symbol.QualifiedName.Split("::", StringSplitOptions.None).Select(s => s.Length.ToString(CultureInfo.InvariantCulture) + s));
    // Local functions without type arguments are emitted from the lowered crate; every other instance is queued once
    private IR.FunctionSymbol Function(FunctionSymbol function, ImmutableArray<TypeSymbol> arguments)
    {
        var key = (function, string.Join("\u001F", arguments.Select(a => a.Name)));
        if (_functions.TryGetValue(key, out IR.FunctionSymbol? cached)) return cached;
        if (function.IsIntrinsic) throw Unsupported($"Using intrinsic '{function.Name}' other than in a call");
        _syntax = function.Declaration;
        if (function.Abi is not ("Rust" or "C")) throw Unsupported($"ABI '{function.Abi}'");
        int budget = InstanceSizeLimit;
        foreach (TypeSymbol argument in arguments)
            if (!Fits(argument, InstanceDepthLimit, ref budget))
                throw new LoweringException(function.Declaration, $"Reached the recursion limit while instantiating '{function.QualifiedName}'.");
        Dictionary<TypeParameterSymbol, TypeSymbol> outer = _substitution;
        _substitution = function.AllTypeParameters.Zip(arguments).ToDictionary(p => p.First, p => p.Second);
        IR.FunctionType signature;
        try { signature = Signature(function.Parameters.Select(p => p.Type), function.ReturnType); }
        finally { _substitution = outer; }
        string name = function.ExternalName ?? (function.IsForeign ? function.Name : arguments.IsEmpty ? Mangle(function) : Mangle(function) + "$" + _instanceOrdinal++);
        var symbol = new IR.FunctionSymbol(name, new IR.QualifiedType(signature),
            function.IsForeign ? IR.StorageClass.Extern : function.ExternalName is null ? IR.StorageClass.Static : IR.StorageClass.None,
            IR.FunctionSpecifiers.None, !function.IsForeign, null);
        _functions.Add(key, symbol);
        if (!function.IsForeign && (!arguments.IsEmpty || !Binding.IsLocal(function))) _pending.Enqueue(new Instance(function, arguments, symbol));
        return symbol;
    }
    private static bool Fits(TypeSymbol type, int depth, ref int budget)
    {
        if (depth == 0 || --budget < 0) return false;
        IEnumerable<TypeSymbol> children = type.Resolve() switch
        {
            NamedTypeSymbol n => n.TypeArguments,
            TupleTypeSymbol t => t.Elements,
            SequenceTypeSymbol s => [s.ElementType],
            IndirectTypeSymbol r => [r.ElementType],
            FunctionTypeSymbol f => f.ParameterTypes.Append(f.ReturnType),
            _ => []
        };
        foreach (TypeSymbol child in children)
            if (!Fits(child, depth - 1, ref budget)) return false;
        return true;
    }
    private IR.TypedSymbol Variable(VariableSymbol variable)
    {
        // Statics are defined where first referenced, so upstream statics join this crate's data
        if (variable is GlobalSymbol global)
        {
            if (_globals.TryGetValue(global, out IR.TypedSymbol? symbol)) return symbol;
            var defined = new IR.VariableSymbol(Mangle(global), Type(global.Type), IR.StorageClass.Static, null);
            _globals.Add(global, defined);
            if (IR.GimpleTypes.IsVoid(defined.Type)) return defined;
            SyntaxNode syntax = _syntax;
            IR.GimpleInitializer? initializer = Binding.Initializer(global) is BoundExpression value ? Initializer(value) : null;
            _members.Add(new IR.GimpleGlobalDeclaration(null, IR.StorageClass.Static, [new IR.GimpleVariableDeclaration(defined, defined.Type, IR.StorageClass.Static, initializer)]));
            _syntax = syntax;
            return defined;
        }
        if (_locals.TryGetValue(variable, out IR.TypedSymbol? local)) return local;
        _locals.Add(variable, local = new IR.VariableSymbol(variable.Name, Type(variable.Type), IR.StorageClass.None, null));
        return local;
    }
    private IR.GimpleSymbolValue Symbol(VariableSymbol variable)
    {
        IR.TypedSymbol symbol = Variable(variable);
        return new IR.GimpleSymbolValue(symbol, symbol.Type);
    }
    private IR.GimpleTemporaryValue Temp(IR.QualifiedType type)
    {
        var temporary = new IR.GimpleTemporaryValue(_temporaries.Count, type);
        _temporaries.Add(temporary);
        return temporary;
    }
    private IR.GimpleValue Unit() => new IR.GimpleConstantValue(null, _types.Builtin(IR.BuiltinTypeKind.Void));
    private IR.GimpleLabel NewLabel() => new("rust_" + _labelOrdinal++);
    private IR.GimpleLabel Label(LabelSymbol label)
    {
        if (!_labels.TryGetValue(label, out IR.GimpleLabel? result)) _labels.Add(label, result = NewLabel());
        return result;
    }
    private bool Terminated => _blocks.Count != 0 && _blocks[^1].Statements.LastOrDefault()?.IsTerminator == true;
    private void Start(IR.GimpleLabel label)
    {
        if (_blocks.Count != 0 && !Terminated) Emit(new IR.GimpleGotoStatement(label));
        _blocks.Add((label, new List<IR.GimpleStatement>()));
    }
    private void Emit(IR.GimpleStatement statement)
    {
        if (_blocks.Count == 0 || Terminated) Start(NewLabel());
        _blocks[^1].Statements.Add(statement);
    }
    private void Begin(IR.FunctionSymbol function)
    {
        _function = function;
        _temporaries.Clear(); _blocks.Clear(); _labels.Clear(); _locals.Clear(); _labelOrdinal = 0;
        Start(NewLabel());
    }
    private void Finish()
        => _members.Add(new IR.GimpleFunctionDefinition(null, _function, _temporaries.ToImmutableArray(),
            _blocks.Select(b => new IR.GimpleBasicBlock(b.Label, b.Statements.ToImmutableArray())).ToImmutableArray(), _blocks[0].Label));
    private void EmitFunction(BoundFunction function, IR.FunctionSymbol symbol, ImmutableArray<TypeSymbol> arguments)
    {
        _substitution = function.Symbol.AllTypeParameters.Zip(arguments).ToDictionary(p => p.First, p => p.Second);
        Begin(symbol);
        int ordinal = 0;
        foreach (ParameterSymbol parameter in function.Symbol.Parameters)
            if (!ZeroSized(parameter.Type)) _locals.Add(parameter, symbol.FunctionType!.Parameters[ordinal++]);
        IR.GimpleValue result = Value(function.Body!);
        if (!Terminated) Emit(new IR.GimpleReturnStatement(_function, IR.GimpleTypes.IsVoid(result.Type) ? null : Snapshot(result)));
        Finish();
        _substitution = new();
    }
    private void EmitInstance(Instance instance)
    {
        FunctionSymbol function = instance.Function;
        _syntax = function.Declaration;
        if (function.Declaration.Body is not null) EmitFunction(_compilation.LoweredFunction(function), instance.Symbol, instance.Arguments);
        else if (ReferenceEquals(function.Trait, Binding.CloneTrait) && function.Name == "clone") EmitCloneShim(instance.Arguments[0], instance.Symbol);
        else throw Unsupported($"The builtin implementation of '{function.QualifiedName}' for '{instance.Arguments.FirstOrDefault()}'");
    }
    // Clone of tuples, arrays, function pointers and ! has no impl in core; it copies Copy values and clones tuples element by element
    private void EmitCloneShim(TypeSymbol self, IR.FunctionSymbol symbol)
    {
        Begin(symbol);
        IR.QualifiedType type = Type(self);
        var source = new IR.GimpleIndirectExpression(new IR.GimpleSymbolValue(symbol.FunctionType!.Parameters[0], symbol.FunctionType.Parameters[0].Type), type);
        IR.GimpleValue? result = null;
        if (Binding.IsCopy(self, ParamEnv.Empty)) result = IR.GimpleTypes.IsVoid(type) ? null : Snapshot(source);
        else if (self is TupleTypeSymbol tuple)
        {
            var clone = Binding.CloneTrait!.Methods.First(m => m.Name == "clone");
            IR.GimpleTemporaryValue? copy = IR.GimpleTypes.IsVoid(type) ? null : Temp(type);
            for (int i = 0; i < tuple.Elements.Length; i++)
            {
                var (target, arguments) = Binding.ResolveInstance(clone, [tuple.Elements[i]]);
                IR.FunctionSymbol element = Function(target, arguments);
                IR.QualifiedType pointer = element.FunctionType!.Parameters[0].Type;
                string name = i.ToString(CultureInfo.InvariantCulture);
                IR.GimpleValue address = ZeroSized(tuple.Elements[i]) ? Dangling(pointer) : Snapshot(new IR.GimpleAddressOfExpression(Member(source, name), pointer));
                IR.GimpleTemporaryValue? value = IR.GimpleTypes.IsVoid(element.FunctionType.ReturnType) ? null : Temp(element.FunctionType.ReturnType);
                Emit(new IR.GimpleCallStatement(value, new IR.GimpleSymbolValue(element, element.Type), [address], element.FunctionType, element.FunctionType.ReturnType));
                if (copy is not null && value is not null) Emit(IR.GimpleAssignStatement.Single(Member(copy, name), value));
            }
            result = copy;
        }
        else throw Unsupported($"Cloning '{self}'");
        Emit(new IR.GimpleReturnStatement(_function, result));
        Finish();
    }
    private void EntryPoint(FunctionSymbol entry)
    {
        _syntax = entry.Declaration;
        if (entry.IsForeign || entry.IsUnsafe || !entry.Parameters.IsEmpty || entry.ReturnType.Kind != TypeKind.Unit)
            throw new LoweringException(entry.Declaration, "The entry point must be a safe fn main() returning ().");
        IR.QualifiedType integer = _types.Builtin(IR.BuiltinTypeKind.Int);
        Begin(new IR.FunctionSymbol("main", new IR.QualifiedType(_types.FunctionReturning(integer, [], true, false)), IR.StorageClass.None, IR.FunctionSpecifiers.None, true, null));
        IR.FunctionSymbol target = Function(entry, []);
        Emit(new IR.GimpleCallStatement(null, new IR.GimpleSymbolValue(target, target.Type), [], target.FunctionType, target.FunctionType!.ReturnType));
        Emit(new IR.GimpleReturnStatement(_function, new IR.GimpleConstantValue(0, integer)));
        Finish();
    }
    private LoweringException Unsupported(string feature) => new(_syntax, feature + " is not supported by Rust code generation yet.");

    private IR.GimpleValue Snapshot(IR.GimpleValue value)
    {
        if (value is IR.GimpleConstantValue or IR.GimpleTemporaryValue || value is IR.GimpleSymbolValue { Symbol: IR.FunctionSymbol }) return value;
        if (IR.GimpleTypes.IsVoid(value.Type)) return value;
        IR.GimpleTemporaryValue temporary = Temp(value.Type);
        Emit(IR.GimpleAssignStatement.Single(temporary, value));
        return temporary;
    }
    private IR.GimpleValue Convert(IR.GimpleValue value, IR.QualifiedType type)
    {
        var result = Temp(type);
        Emit(IR.GimpleAssignStatement.Unary(result, IR.GimpleOperators.ConversionCode(value.Type, type), Snapshot(value)));
        return result;
    }
    private IR.GimpleValue Binary(IR.GimpleTreeCode code, IR.GimpleValue left, IR.GimpleValue right, IR.QualifiedType type)
    {
        var result = Temp(type);
        Emit(IR.GimpleAssignStatement.Binary(result, code, Snapshot(left), Snapshot(right)));
        return result;
    }
    private IR.GimpleValue Value(BoundExpression expression)
    {
        _syntax = expression.Syntax;
        switch (expression)
        {
            case BoundExpression when expression.ConstantValue.HasValue: return Constant(expression.Type, expression.ConstantValue.Value);
            case BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant, ConstantValue.HasValue: true } global }:
                return Constant(global.Type, global.ConstantValue.Value);
            case BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } constant }: return Value(Inlined(constant));
            case BoundVariableExpression variable: return ZeroSized(variable.Type) ? Unit() : Symbol(variable.Variable);
            case BoundFunctionReference reference:
            {
                ImmutableArray<TypeSymbol> concrete = reference.TypeArguments.Select(t => Binding.Zonk(Concrete(t))).ToImmutableArray();
                var (resolved, arguments) = Binding.ResolveInstance(reference.Function, concrete);
                IR.FunctionSymbol function = Function(resolved, arguments);
                return new IR.GimpleSymbolValue(function, function.Type);
            }
            case BoundBlockExpression block:
                foreach (BoundStatement statement in block.Statements) Statement(statement);
                return block.Tail is null ? Unit() : Value(block.Tail);
            case BoundUnaryExpression unary: return Unary(unary);
            case BoundBinaryExpression binary: return Arithmetic(binary);
            case BoundConversionExpression conversion: return Conversion(conversion);
            case BoundAssignmentExpression assignment:
                IR.GimpleValue assigned = Snapshot(Value(assignment.Value));
                IR.GimplePlace destination = Place(assignment.Target);
                if (!IR.GimpleTypes.IsVoid(destination.Type)) Emit(IR.GimpleAssignStatement.Single(destination, assigned));
                return Unit();
            case BoundCallExpression call: return Call(call);
            case BoundFieldExpression or BoundIndexExpression: return Place(expression);
            case BoundTupleExpression tuple when tuple.Elements.IsEmpty: return Unit();
            case BoundTupleExpression tuple: return Construct(tuple.Type, tuple.Elements.Select((e, i) => (i.ToString(CultureInfo.InvariantCulture), e)), null);
            case BoundConstructionExpression { Variant: VariantSymbol variant } construction: return Variant((NamedTypeSymbol)construction.Type, variant, construction.Fields);
            case BoundConstructionExpression construction:
                return Construct(construction.Type, construction.Fields.Select(f => (f.Field.Name, f.Value)), construction.Base);
            case BoundDiscriminantExpression discriminant: return Convert(Tag(Place(discriminant.Operand)), Type(discriminant.Type));
            case BoundArrayExpression array: return Array(array);
            case BoundControlFlowExpression { Kind: BoundNodeKind.Return } control:
                IR.GimpleValue? returned = control.Value is null ? null : Snapshot(Value(control.Value));
                Emit(new IR.GimpleReturnStatement(_function, returned is null || IR.GimpleTypes.IsVoid(returned.Type) ? null : returned));
                return Unit();
            case BoundPanicExpression: Abort(); return Unit();
            default: throw Unsupported($"Bound node '{expression.Kind}' after lowering");
        }
    }
    private void Statement(BoundStatement statement)
    {
        _syntax = statement.Syntax;
        switch (statement)
        {
            case BoundExpressionStatement expression:
                if (expression.Expression is not null) _ = Value(expression.Expression);
                break;
            case BoundLetStatement { Pattern.Variable: VariableSymbol variable } let:
                if (ZeroSized(variable.Type)) { if (let.Initializer is not null) _ = Value(let.Initializer); break; }
                IR.TypedSymbol symbol = Variable(variable);
                if (symbol is IR.VariableSymbol local) Emit(new IR.GimpleDeclarationStatement(new IR.GimpleVariableDeclaration(local, local.Type, IR.StorageClass.None)));
                if (let.Initializer is not null) Emit(IR.GimpleAssignStatement.Single(new IR.GimpleSymbolValue(symbol, symbol.Type), Snapshot(Value(let.Initializer))));
                break;
            case BoundLabelStatement label: Start(Label(label.Label)); break;
            case BoundGotoStatement { Condition: null } jump: Emit(new IR.GimpleGotoStatement(Label(jump.Label))); break;
            case BoundGotoStatement jump:
                IR.GimpleValue condition = Snapshot(Value(jump.Condition!));
                IR.GimpleLabel next = NewLabel();
                Emit(new IR.GimpleCondStatement(IR.GimpleTreeCode.NeExpr, condition, new IR.GimpleConstantValue(0, condition.Type),
                    jump.JumpIfTrue ? Label(jump.Label) : next, jump.JumpIfTrue ? next : Label(jump.Label)));
                Start(next);
                break;
            default: throw Unsupported($"Statement '{statement.Kind}' after lowering");
        }
    }

    private IR.GimplePlace Place(BoundExpression expression)
    {
        _syntax = expression.Syntax;
        switch (expression)
        {
            case BoundVariableExpression { Variable: not GlobalSymbol { Kind: SymbolKind.Constant } } variable: return Symbol(variable.Variable);
            case BoundUnaryExpression { Operator: SyntaxKind.StarToken } unary:
                return new IR.GimpleIndirectExpression(Snapshot(Value(unary.Operand)), Type(unary.Type));
            case BoundFieldExpression field:
                IR.GimplePlace receiver = Place(field.Receiver);
                if (ZeroSized(field.Type)) return Vacant();
                if (field.Field?.ContainingSymbol is VariantSymbol variant) receiver = Member(Member(receiver, "payload"), variant.Name);
                return Member(receiver, field.Name);
            case BoundIndexExpression index:
                IR.GimpleValue? data;
                IR.GimpleValue length;
                if (index.Receiver is BoundUnaryExpression { Operator: SyntaxKind.StarToken } dereference && TypeOf(dereference.Operand) is IndirectTypeSymbol indirect && IsWide(indirect))
                {
                    IR.GimpleValue slice = Snapshot(Value(dereference.Operand));
                    data = Snapshot(Member(slice, "data"));
                    length = Snapshot(Member(slice, "length"));
                }
                else
                {
                    IR.GimplePlace array = Place(index.Receiver);
                    if (TypeOf(index.Receiver) is not SequenceTypeSymbol { Length: ulong count }) throw Unsupported("Unsized indexing");
                    data = IR.GimpleTypes.IsVoid(array.Type) ? null : Snapshot(new IR.GimpleAddressOfExpression(array, new IR.QualifiedType(_types.PointerTo(Type(index.Type)))));
                    length = Constant(Binding.Types.Usize, new BigInteger(count));
                }
                IR.GimpleValue subscript = Snapshot(Value(index.Index));
                Require(IR.GimpleTreeCode.LtExpr, subscript, length, "panic_bounds_check", subscript, length);
                return data is null || ZeroSized(index.Type) ? Vacant() : new IR.GimpleElementAccessExpression(data, subscript, Type(index.Type));
            default:
                IR.GimpleValue value = Snapshot(Value(expression));
                if (value is IR.GimplePlace place) return place;
                if (IR.GimpleTypes.IsVoid(value.Type)) return Vacant();
                var temporary = Temp(value.Type);
                Emit(IR.GimpleAssignStatement.Single(temporary, value));
                return temporary;
        }
    }
    private static IR.GimpleMemberAccessExpression Member(IR.GimpleValue aggregate, string name)
    {
        IR.FieldSymbol field = ((IR.TagType)aggregate.Type.Type).Symbol.Fields.First(f => f.Name == name);
        return new IR.GimpleMemberAccessExpression(aggregate, false, default, field, field.Type);
    }
    // Zero-sized values have no storage: their places and addresses are a dangling address that is never accessed
    private IR.GimpleValue Dangling(IR.QualifiedType pointer) => Convert(new IR.GimpleConstantValue(1, Type(Binding.Types.Usize)), pointer);
    private IR.GimplePlace Vacant() => new IR.GimpleIndirectExpression(Dangling(new IR.QualifiedType(_types.PointerTo(Void))), Void);
    private IR.GimpleValue Construct(TypeSymbol type, IEnumerable<(string Name, BoundExpression Value)> fields, BoundExpression? baseValue)
    {
        IR.QualifiedType target = Type(type);
        IR.GimpleTemporaryValue? result = IR.GimpleTypes.IsVoid(target) ? null : Temp(target);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            IR.GimpleValue value = Snapshot(Value(field.Value));
            if (result is not null && !IR.GimpleTypes.IsVoid(value.Type)) Emit(IR.GimpleAssignStatement.Single(Member(result, field.Name), value));
            assigned.Add(field.Name);
        }
        if (baseValue is not null)
        {
            IR.GimpleValue source = Snapshot(Value(baseValue));
            if (result is not null)
                foreach (IR.FieldSymbol member in ((IR.TagType)target.Type).Symbol.Fields)
                    if (!assigned.Contains(member.Name)) Emit(IR.GimpleAssignStatement.Single(Member(result, member.Name), Snapshot(Member(source, member.Name))));
        }
        return (IR.GimpleValue?)result ?? Unit();
    }
    private IR.GimpleValue Variant(NamedTypeSymbol type, VariantSymbol variant, ImmutableArray<BoundFieldInitializer> fields)
    {
        IR.QualifiedType target = Type(type);
        IR.GimpleValue discriminant = Constant(Discriminant(variant.Enum), variant.Discriminant);
        if (target.Type is not IR.TagType) return discriminant;
        var result = Temp(target);
        Emit(IR.GimpleAssignStatement.Single(Member(result, "tag"), discriminant));
        foreach (BoundFieldInitializer field in fields)
        {
            IR.GimpleValue value = Snapshot(Value(field.Value));
            if (!IR.GimpleTypes.IsVoid(value.Type)) Emit(IR.GimpleAssignStatement.Single(Member(Member(Member(result, "payload"), variant.Name), field.Field.Name), value));
        }
        return result;
    }
    private IR.GimpleValue Array(BoundArrayExpression array)
    {
        if (ZeroSized(array.Type))
        {
            foreach (BoundExpression item in array.Elements) _ = Value(item);
            return Unit();
        }
        var result = Temp(Type(array.Type));
        IR.QualifiedType element = ((IR.ArrayType)result.Type.Type).ElementType;
        IR.QualifiedType usize = Type(_compilation.Binding.Types.Usize);
        IR.GimpleValue data = Snapshot(new IR.GimpleAddressOfExpression(result, new IR.QualifiedType(_types.PointerTo(element))));
        if (array.RepeatCount is null)
        {
            for (int i = 0; i < array.Elements.Length; i++)
                Emit(IR.GimpleAssignStatement.Single(new IR.GimpleElementAccessExpression(data, new IR.GimpleConstantValue(i, usize), element), Snapshot(Value(array.Elements[i]))));
        }
        else
        {
            IR.GimpleValue repeated = Snapshot(Value(array.Elements[0]));
            var index = Temp(usize);
            Emit(IR.GimpleAssignStatement.Single(index, new IR.GimpleConstantValue(0, usize)));
            IR.GimpleLabel test = NewLabel(), body = NewLabel(), end = NewLabel();
            Start(test);
            Emit(new IR.GimpleCondStatement(IR.GimpleTreeCode.LtExpr, index, new IR.GimpleConstantValue(((IR.ArrayType)result.Type.Type).Length!.Value, usize), body, end));
            Start(body);
            Emit(IR.GimpleAssignStatement.Single(new IR.GimpleElementAccessExpression(data, index, element), repeated));
            Emit(IR.GimpleAssignStatement.Binary(index, IR.GimpleTreeCode.PlusExpr, index, new IR.GimpleConstantValue(1, usize)));
            Emit(new IR.GimpleGotoStatement(test));
            Start(end);
        }
        return result;
    }

    // String literals and byte strings coerced to a slice are wide constants
    private static byte[]? Bytes(object? value, TypeSymbol type) => type is IndirectTypeSymbol wide && IsWide(wide)
        ? value switch { string text => Encoding.UTF8.GetBytes(text), byte[] bytes => bytes, _ => null } : null;
    private IR.GimpleValue Constant(TypeSymbol type, object? value)
    {
        type = Concrete(type);
        if (Bytes(value, type) is byte[] wide)
        {
            var result = Temp(Type(type));
            Emit(IR.GimpleAssignStatement.Single(Member(result, "data"), Snapshot(Data(wide, Member(result, "data").Type))));
            Emit(IR.GimpleAssignStatement.Single(Member(result, "length"), new IR.GimpleConstantValue(wide.Length, Member(result, "length").Type)));
            return result;
        }
        if (value is byte[] bytes) return Data(bytes, Type(type));
        if (value is BigInteger integer)
        {
            if (type is PrimitiveTypeSymbol { IsSigned: false }) value = (ulong)integer;
            else value = (long)integer;
        }
        if (type.Kind == TypeKind.Character && value is string character) value = char.ConvertToUtf32(character, 0);
        if (value is System.Text.Rune rune) value = rune.Value;
        if (value is bool boolean) value = boolean ? 1 : 0;
        if (value is char c) value = (int)c;
        if (type is PrimitiveTypeSymbol { Kind: TypeKind.Float, BitWidth: 32 } && value is double floating) value = (float)floating;
        if (value is not (null or int or uint or long or ulong or float or double)) throw Unsupported($"Constant value of type '{type}'");
        return new IR.GimpleConstantValue(value, Type(type));
    }
    private IR.GimpleValue Data(byte[] bytes, IR.QualifiedType pointerType)
    {
        IR.QualifiedType element = _types.Builtin(IR.BuiltinTypeKind.UnsignedChar, IR.TypeQualifiers.Const);
        var arrayType = new IR.QualifiedType(_types.ArrayOf(element, bytes.Length));
        var symbol = new IR.VariableSymbol("$rust_data" + _dataOrdinal++, arrayType, IR.StorageClass.Static, null);
        var initializer = new IR.GimpleInitializerList(null, arrayType, bytes.Select((b, i) => new IR.GimpleInitializerListItem(null, [],
            new IR.GimpleExpressionInitializer(null, element, new IR.GimpleConstantValue((int)b, element)), i)).ToImmutableArray());
        _members.Add(new IR.GimpleGlobalDeclaration(null, IR.StorageClass.Static, [new IR.GimpleVariableDeclaration(symbol, arrayType, IR.StorageClass.Static, initializer)]));
        return new IR.GimpleAddressOfExpression(new IR.GimpleSymbolValue(symbol, arrayType), pointerType);
    }
    // Static data runs no code, so constant expressions reduce to constants, addresses and lists over the members that take space
    private IR.GimpleInitializer Initializer(BoundExpression expression)
    {
        _syntax = expression.Syntax;
        IR.QualifiedType type = Type(expression.Type);
        if (Bytes(expression.ConstantValue.Value, TypeOf(expression)) is byte[] wide)
            return Members(type, f => Scalar(f.Type, f.Name == "data" ? Data(wide, f.Type) : new IR.GimpleConstantValue(wide.Length, f.Type)));
        if (expression.ConstantValue.HasValue) return Scalar(type, Constant(expression.Type, expression.ConstantValue.Value));
        switch (expression)
        {
            case BoundFunctionReference: return Scalar(type, Retype(Value(expression), type));
            case BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant } constant }: return Initializer(Inlined(constant));
            case BoundConversionExpression { Conversion: ConversionKind.ArrayToSliceReference } conversion:
                IR.GimpleInitializer pointer = Initializer(conversion.Operand);
                ulong length = ((SequenceTypeSymbol)((IndirectTypeSymbol)TypeOf(conversion.Operand)).ElementType).Length!.Value;
                return Members(type, f => f.Name == "data" ? Scalar(f.Type, Retype(((IR.GimpleExpressionInitializer)pointer).Expression, f.Type)) : Scalar(f.Type, new IR.GimpleConstantValue(length, f.Type)));
            case BoundConversionExpression conversion:
                IR.GimpleInitializer operand = Initializer(conversion.Operand);
                return operand is IR.GimpleExpressionInitializer scalar ? Scalar(type, Retype(scalar.Expression, type)) : operand;
            case BoundUnaryExpression { Operator: SyntaxKind.AmpersandToken } borrow:
                if (ZeroSized(borrow.Operand.Type)) return Scalar(type, new IR.GimpleConstantValue(1, type));
                IR.GimplePlace place = borrow.Operand is BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Static } global } ? Symbol(global) : Promote(borrow.Operand);
                return Scalar(type, new IR.GimpleAddressOfExpression(place, type));
            case BoundFieldExpression or BoundIndexExpression: return Initializer(Binding.Reduce(expression) ?? throw Unsupported("This constant access"));
            case BoundTupleExpression tuple: return Members(type, f => Initializer(tuple.Elements[int.Parse(f.Name, CultureInfo.InvariantCulture)]));
            case BoundArrayExpression { RepeatCount: not null } array when array.Elements[0].ConstantValue.Value is BigInteger { IsZero: true } or false:
                return new IR.GimpleInitializerList(null, type, []);
            case BoundArrayExpression array:
                IEnumerable<BoundExpression> elements = array.RepeatCount is null ? array.Elements : Enumerable.Repeat(array.Elements[0], checked((int)((SequenceTypeSymbol)TypeOf(array)).Length!.Value));
                return new IR.GimpleInitializerList(null, type, elements.Select((e, i) => new IR.GimpleInitializerListItem(null, [], Initializer(e), i)).ToImmutableArray());
            case BoundConstructionExpression { Variant: VariantSymbol variant } construction:
                IR.GimpleValue discriminant = Constant(Discriminant(variant.Enum), variant.Discriminant);
                if (type.Type is not IR.TagType) return Scalar(type, discriminant);
                return Members(type, f => f.Name == "tag" ? Scalar(f.Type, discriminant) : Payload(f.Type, variant, construction));
            case BoundConstructionExpression construction: return Members(type, f => Initializer(Pick(construction, f.Name)));
            default: throw Unsupported("Nonconstant static initialization");
        }
    }
    private static IR.GimpleExpressionInitializer Scalar(IR.QualifiedType type, IR.GimpleValue value) => new(null, type, value);
    private static IR.GimpleInitializerList Members(IR.QualifiedType type, Func<IR.FieldSymbol, IR.GimpleInitializer> initializer)
        => new(null, type, ((IR.TagType)type.Type).Symbol.Fields.Select(f => new IR.GimpleInitializerListItem(null, [], initializer(f))).ToImmutableArray());
    private IR.GimpleInitializer Payload(IR.QualifiedType union, VariantSymbol variant, BoundConstructionExpression construction)
    {
        ImmutableArray<IR.FieldSymbol> members = ((IR.TagType)union.Type).Symbol.Fields;
        int member = Enumerable.Range(0, members.Length).FirstOrDefault(i => members[i].Name == variant.Name, -1);
        if (member < 0) return new IR.GimpleInitializerList(null, union, []);
        return new IR.GimpleInitializerList(null, union, [new IR.GimpleInitializerListItem(null, [], Members(members[member].Type, f => Initializer(Pick(construction, f.Name))), member)]);
    }
    private static IR.GimpleValue Retype(IR.GimpleValue address, IR.QualifiedType type) => address switch
    {
        IR.GimpleAddressOfExpression addressOf => new IR.GimpleAddressOfExpression(addressOf.Target, type),
        IR.GimpleSymbolValue { Symbol: IR.FunctionSymbol } function => new IR.GimpleAddressOfExpression(function, type),
        _ => address
    };
    private BoundExpression Pick(BoundExpression aggregate, string name) => Binding.Field(aggregate, name) ?? throw Unsupported("This constant field access");
    // Every use of a const is a fresh value built from its initializer
    private BoundExpression Inlined(GlobalSymbol constant)
        => Binding.Initializer(constant) ?? throw new LoweringException(constant.Declaration, $"The constant '{constant.Name}' has no value.");
    // Shared borrows of constants live in static data, which the borrow checker relies on when it gives them the static lifetime
    private IR.GimplePlace Promote(BoundExpression expression)
    {
        IR.QualifiedType type = Type(expression.Type);
        var symbol = new IR.VariableSymbol("$rust_promoted" + _dataOrdinal++, type, IR.StorageClass.Static, null);
        SyntaxNode syntax = _syntax;
        _members.Add(new IR.GimpleGlobalDeclaration(null, IR.StorageClass.Static, [new IR.GimpleVariableDeclaration(symbol, type, IR.StorageClass.Static, Initializer(expression))]));
        _syntax = syntax;
        return new IR.GimpleSymbolValue(symbol, type);
    }
    private IR.GimpleValue Unary(BoundUnaryExpression unary)
    {
        if (unary.Operator == SyntaxKind.AmpersandToken)
        {
            if (Binder.IsConstant(unary) && !ZeroSized(unary.Operand.Type)) return new IR.GimpleAddressOfExpression(Promote(unary.Operand), Type(unary.Type));
            IR.GimplePlace place = Place(unary.Operand);
            return IR.GimpleTypes.IsVoid(place.Type) ? Dangling(Type(unary.Type)) : new IR.GimpleAddressOfExpression(place, Type(unary.Type));
        }
        if (unary.Operator == SyntaxKind.StarToken) return Place(unary);
        IR.GimpleValue operand = Snapshot(Value(unary.Operand));
        IR.QualifiedType type = Type(unary.Type);
        if (unary.Operator == SyntaxKind.MinusToken && TypeOf(unary) is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer)
        {
            IR.QualifiedType unsigned = Unsigned(integer);
            return Convert(Binary(IR.GimpleTreeCode.MinusExpr, new IR.GimpleConstantValue(0, unsigned), Convert(operand, unsigned), unsigned), type);
        }
        IR.GimpleTreeCode code = unary.Operator switch
        {
            SyntaxKind.MinusToken => IR.GimpleTreeCode.NegateExpr,
            SyntaxKind.BangToken when TypeOf(unary).Kind == TypeKind.Boolean => IR.GimpleTreeCode.TruthNotExpr,
            SyntaxKind.BangToken => IR.GimpleTreeCode.BitNotExpr,
            _ => throw Unsupported($"Unary operator '{unary.Operator}'")
        };
        var result = Temp(type);
        Emit(IR.GimpleAssignStatement.Unary(result, code, operand));
        return result;
    }
    private IR.QualifiedType Unsigned(PrimitiveTypeSymbol type) => Type(_compilation.Binding.Types.Primitives["u" + type.BitWidth]);
    private IR.GimpleValue Arithmetic(BoundBinaryExpression expression)
    {
        IR.GimpleValue left = Snapshot(Value(expression.Left));
        IR.GimpleValue right = Snapshot(Value(expression.Right));
        IR.QualifiedType resultType = Type(expression.Type);
        IR.GimpleTreeCode code = expression.Operator switch
        {
            SyntaxKind.PlusToken => IR.GimpleTreeCode.PlusExpr, SyntaxKind.MinusToken => IR.GimpleTreeCode.MinusExpr,
            SyntaxKind.StarToken => IR.GimpleTreeCode.MultExpr,
            SyntaxKind.SlashToken when TypeOf(expression.Left).Kind == TypeKind.Float => IR.GimpleTreeCode.RdivExpr,
            SyntaxKind.SlashToken => IR.GimpleTreeCode.TruncDivExpr, SyntaxKind.PercentToken => IR.GimpleTreeCode.TruncModExpr,
            SyntaxKind.AmpersandToken => IR.GimpleTreeCode.BitAndExpr, SyntaxKind.PipeToken => IR.GimpleTreeCode.BitIorExpr,
            SyntaxKind.CaretToken => IR.GimpleTreeCode.BitXorExpr, SyntaxKind.LessThanLessThanToken => IR.GimpleTreeCode.LshiftExpr,
            SyntaxKind.GreaterThanGreaterThanToken => IR.GimpleTreeCode.RshiftExpr,
            SyntaxKind.EqualsEqualsToken => IR.GimpleTreeCode.EqExpr, SyntaxKind.BangEqualsToken => IR.GimpleTreeCode.NeExpr,
            SyntaxKind.LessThanToken => IR.GimpleTreeCode.LtExpr, SyntaxKind.LessThanEqualsToken => IR.GimpleTreeCode.LeExpr,
            SyntaxKind.GreaterThanToken => IR.GimpleTreeCode.GtExpr, SyntaxKind.GreaterThanEqualsToken => IR.GimpleTreeCode.GeExpr,
            _ => throw Unsupported($"Binary operator '{expression.Operator}'")
        };
        if (TypeOf(expression.Left) is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer)
        {
            if (code is IR.GimpleTreeCode.TruncDivExpr or IR.GimpleTreeCode.TruncModExpr)
            {
                bool division = code == IR.GimpleTreeCode.TruncDivExpr;
                Require(IR.GimpleTreeCode.NeExpr, right, new IR.GimpleConstantValue(0, right.Type), division ? "panic_const_div_by_zero" : "panic_const_rem_by_zero");
                if (integer.IsSigned)
                {
                    var boolType = _types.Builtin(IR.BuiltinTypeKind.Bool);
                    IR.GimpleValue minimum = Constant(integer, -(BigInteger.One << (integer.BitWidth - 1)));
                    IR.GimpleValue safeLeft = Binary(IR.GimpleTreeCode.NeExpr, left, minimum, boolType);
                    IR.GimpleValue safeRight = Binary(IR.GimpleTreeCode.NeExpr, right, Constant(integer, -BigInteger.One), boolType);
                    Require(IR.GimpleTreeCode.NeExpr, Binary(IR.GimpleTreeCode.BitIorExpr, safeLeft, safeRight, boolType), new IR.GimpleConstantValue(0, boolType),
                        division ? "panic_const_div_overflow" : "panic_const_rem_overflow");
                }
            }
            if (code is IR.GimpleTreeCode.LshiftExpr or IR.GimpleTreeCode.RshiftExpr)
                right = Binary(IR.GimpleTreeCode.BitAndExpr, Convert(right, left.Type), new IR.GimpleConstantValue(integer.BitWidth - 1, left.Type), left.Type);
            // Unsigned operations preserve Rust's wrapping arithmetic through C optimizations
            if (code is IR.GimpleTreeCode.PlusExpr or IR.GimpleTreeCode.MinusExpr or IR.GimpleTreeCode.MultExpr or IR.GimpleTreeCode.LshiftExpr)
            {
                IR.QualifiedType unsigned = Unsigned(integer);
                return Convert(Binary(code, Convert(left, unsigned), Convert(right, unsigned), unsigned), resultType);
            }
        }
        else if (TypeOf(expression.Left).Kind == TypeKind.Float && code == IR.GimpleTreeCode.TruncModExpr)
            throw Unsupported("Floating-point remainder");
        return Binary(code, left, right, resultType);
    }
    private IR.GimpleValue Conversion(BoundConversionExpression conversion)
    {
        if (conversion.Conversion is ConversionKind.Identity or ConversionKind.Never) return Value(conversion.Operand);
        if (conversion.Conversion == ConversionKind.FunctionToPointer)
            return new IR.GimpleAddressOfExpression(Place(conversion.Operand), Type(conversion.Type));
        if (TypeOf(conversion) is IndirectTypeSymbol target && IsWide(target) && TypeOf(conversion.Operand) is IndirectTypeSymbol { ElementType: SequenceTypeSymbol { Kind: TypeKind.Array, Length: ulong length } })
        {
            IR.GimpleValue array = Snapshot(Value(conversion.Operand));
            var result = Temp(Type(target));
            Emit(IR.GimpleAssignStatement.Single(Member(result, "data"), Convert(array, Member(result, "data").Type)));
            Emit(IR.GimpleAssignStatement.Single(Member(result, "length"), new IR.GimpleConstantValue(length, Member(result, "length").Type)));
            return result;
        }
        IR.GimpleValue value = Snapshot(Value(conversion.Operand));
        if (TypeOf(conversion) is IndirectTypeSymbol wide && IsWide(wide) && TypeOf(conversion.Operand) is IndirectTypeSymbol source && IsWide(source))
        {
            var result = Temp(Type(wide));
            Emit(IR.GimpleAssignStatement.Single(result, value));
            return result;
        }
        if (TypeOf(conversion.Operand).Kind == TypeKind.Float && TypeOf(conversion) is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer)
            return SaturatingCast(value, integer);
        return Convert(value, Type(conversion.Type));
    }
    private IR.GimpleValue SaturatingCast(IR.GimpleValue value, PrimitiveTypeSymbol integer)
    {
        IR.QualifiedType type = Type(integer);
        var result = Temp(type);
        BigInteger minimum = integer.IsSigned ? -(BigInteger.One << (integer.BitWidth - 1)) : BigInteger.Zero;
        BigInteger maximum = (BigInteger.One << (integer.BitWidth - (integer.IsSigned ? 1 : 0))) - 1;
        IR.GimpleValue Float(BigInteger n) => new IR.GimpleConstantValue(value.Type.Type is IR.BuiltinType { BuiltinKind: IR.BuiltinTypeKind.Float } ? (object)(float)n : (double)n, value.Type);
        IR.GimpleLabel lower = NewLabel(), upper = NewLabel(), cast = NewLabel(), low = NewLabel(), high = NewLabel(), nan = NewLabel(), done = NewLabel();
        Emit(new IR.GimpleCondStatement(IR.GimpleTreeCode.NeExpr, value, value, nan, lower));
        Start(lower);
        Emit(new IR.GimpleCondStatement(IR.GimpleTreeCode.LeExpr, value, Float(minimum), low, upper));
        Start(upper);
        Emit(new IR.GimpleCondStatement(IR.GimpleTreeCode.GeExpr, value, Float(maximum), high, cast));
        Start(low); Emit(IR.GimpleAssignStatement.Single(result, Constant(integer, minimum))); Emit(new IR.GimpleGotoStatement(done));
        Start(high); Emit(IR.GimpleAssignStatement.Single(result, Constant(integer, maximum))); Emit(new IR.GimpleGotoStatement(done));
        Start(nan); Emit(IR.GimpleAssignStatement.Single(result, new IR.GimpleConstantValue(0, type))); Emit(new IR.GimpleGotoStatement(done));
        Start(cast); Emit(IR.GimpleAssignStatement.Single(result, Convert(value, type))); Emit(new IR.GimpleGotoStatement(done));
        Start(done);
        return result;
    }
    private IR.GimpleValue Call(BoundCallExpression call)
    {
        if (call.Target is BoundFunctionReference { Function.IsIntrinsic: true } intrinsic) return Intrinsic(intrinsic.Function, call);
        IR.GimpleValue function = Snapshot(Value(call.Target));
        IR.FunctionType signature = function.Type.Type is IR.FunctionType direct ? direct
            : function.Type.Type is IR.PointerType { PointeeType.Type: IR.FunctionType pointer } ? pointer
            : throw Unsupported("Call target");
        var arguments = call.Arguments.Select(a => Snapshot(Value(a))).Where(a => !IR.GimpleTypes.IsVoid(a.Type)).ToImmutableArray();
        IR.GimpleTemporaryValue? result = IR.GimpleTypes.IsVoid(signature.ReturnType) ? null : Temp(signature.ReturnType);
        Emit(new IR.GimpleCallStatement(result, function, arguments, signature, signature.ReturnType, isNoReturn: TypeOf(call).Kind == TypeKind.Never));
        if (TypeOf(call).Kind == TypeKind.Never) Stop();
        return (IR.GimpleValue?)result ?? Unit();
    }
    private IR.GimpleValue Intrinsic(FunctionSymbol intrinsic, BoundCallExpression call)
    {
        switch (intrinsic.Name)
        {
            case "abort" when call.Arguments.IsEmpty: Abort(); return Unit();
            default: throw Unsupported($"Intrinsic '{intrinsic.Name}'");
        }
    }
    // Failed runtime checks call the core panic entry points rustc reaches through the same lang items
    private void Require(IR.GimpleTreeCode comparison, IR.GimpleValue left, IR.GimpleValue right, string item, params IR.GimpleValue[] arguments)
    {
        IR.GimpleLabel success = NewLabel(), failure = NewLabel();
        Emit(new IR.GimpleCondStatement(comparison, left, right, success, failure));
        Start(failure);
        FunctionSymbol handler = _compilation.LangItem(item) ?? throw new LoweringException(_syntax, $"The '{item}' lang item is not defined.");
        IR.FunctionSymbol target = Function(handler, []);
        Emit(new IR.GimpleCallStatement(null, new IR.GimpleSymbolValue(target, target.Type), arguments.Select(Snapshot).ToImmutableArray(), target.FunctionType!, target.FunctionType!.ReturnType, isNoReturn: true));
        Stop();
        Start(success);
    }
    private void Abort()
    {
        var type = _types.FunctionReturning(_types.Builtin(IR.BuiltinTypeKind.Void), [], true, false);
        var abort = new IR.FunctionSymbol("abort", new IR.QualifiedType(type), IR.StorageClass.Extern, IR.FunctionSpecifiers.None, false, null);
        Emit(new IR.GimpleCallStatement(null, new IR.GimpleSymbolValue(abort, abort.Type), [], type, type.ReturnType, isNoReturn: true));
        Stop();
    }
    private void Stop()
    {
        IR.GimpleLabel stopped = NewLabel();
        Start(stopped);
        Emit(new IR.GimpleGotoStatement(stopped));
    }
}
