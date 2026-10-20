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
    private readonly Compilation _compilation;
    private readonly IR.TypeCatalog _types = IR.TypeCatalog.Instance;
    private readonly Dictionary<string, IR.QualifiedType> _typeMap = new(StringComparer.Ordinal);
    private readonly Dictionary<FunctionSymbol, IR.FunctionSymbol> _functions = new();
    private readonly Dictionary<VariableSymbol, IR.TypedSymbol> _variables = new();
    private readonly Dictionary<LabelSymbol, IR.GimpleLabel> _labels = new();
    private readonly List<IR.GimpleNode> _members = new();
    private readonly List<IR.GimpleTemporaryValue> _temporaries = new();
    private readonly List<(IR.GimpleLabel Label, List<IR.GimpleStatement> Statements)> _blocks = new();
    private IR.FunctionSymbol _function = null!;
    private SyntaxNode _syntax;
    private int _labelOrdinal;
    private int _dataOrdinal;

    private Gimplifier(Compilation compilation)
    { _compilation = compilation; _syntax = compilation.RootSyntaxTree.Root; }

    internal static IR.GimpleTree Lower(Compilation compilation, BoundCompilationUnit root, IR.CompilationOptions options)
    {
        if (options.Target.PointerSize * 8 != compilation.Options.PointerWidth)
            throw new LoweringException(root.Syntax, "The semantic pointer width does not match the code generation target.");
        var emitter = new Gimplifier(compilation);
        foreach (BoundFunction function in root.Functions) emitter.Function(function.Symbol);
        foreach (BoundGlobal global in root.Globals)
        {
            emitter._syntax = global.Syntax;
            if (global.Symbol.Kind == SymbolKind.Constant) continue;
            var symbol = (IR.VariableSymbol)emitter.Variable(global.Symbol);
            IR.GimpleInitializer? initializer = global.Initializer is null ? null : emitter.Initializer(global.Initializer);
            emitter._members.Add(new IR.GimpleGlobalDeclaration(null, IR.StorageClass.Static,
                [new IR.GimpleVariableDeclaration(symbol, symbol.Type, IR.StorageClass.Static, initializer)]));
        }
        foreach (BoundFunction function in root.Functions)
            if (function.Body is not null) emitter.EmitFunction(function);
        FunctionSymbol? entry = root.Functions.Select(f => f.Symbol).FirstOrDefault(f => f.ContainingSymbol == compilation.Crate && f.Name == "main");
        if (entry is not null && !compilation.Options.IsLibrary) emitter.EntryPoint(entry);
        IR.Compilation context = IR.Compilation.Create("", compilation.Options.CrateName, options);
        return new IR.GimpleTree(context.GetSemanticModel(context.SyntaxTrees[0]), emitter._members.ToImmutableArray(), []);
    }

    private IR.QualifiedType Type(TypeSymbol type)
    {
        type = type.Resolve();
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
                result = _types.Builtin(IR.BuiltinTypeKind.Void);
                break;
            case TupleTypeSymbol tuple:
                return Aggregate(type, tuple.Elements.Select((t, i) => (i.ToString(CultureInfo.InvariantCulture), t)));
            case NamedTypeSymbol named when named.Kind == TypeKind.Struct:
                return Aggregate(type, named.Definition.Fields.Select(f => (f.Name, _compilation.Binding.Types.FieldType(f, named))));
            case IndirectTypeSymbol indirect when IsWide(indirect):
                TypeSymbol element = indirect.ElementType is SequenceTypeSymbol slice ? slice.ElementType : _compilation.Binding.Types.Primitives["u8"];
                return Aggregate(type, new[] { ("data", (TypeSymbol)new IndirectTypeSymbol(TypeKind.Pointer, element, indirect.IsMutable)), ("length", (TypeSymbol)_compilation.Binding.Types.Usize) });
            case IndirectTypeSymbol indirect:
                IR.QualifiedType pointee = Type(indirect.ElementType);
                result = new IR.QualifiedType(_types.PointerTo(new IR.QualifiedType(pointee.Type, indirect.IsMutable ? IR.TypeQualifiers.None : IR.TypeQualifiers.Const)));
                break;
            case SequenceTypeSymbol { Kind: TypeKind.Array, Length: ulong length } sequence:
                if (length > int.MaxValue) throw Unsupported("Arrays larger than Int32.MaxValue");
                result = new IR.QualifiedType(_types.ArrayOf(Type(sequence.ElementType), (long)length));
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
    private IR.QualifiedType Aggregate(TypeSymbol type, IEnumerable<(string Name, TypeSymbol Type)> fields)
    {
        var tag = new IR.TagSymbol(type.Name, IR.TagKind.Struct, null);
        var result = new IR.QualifiedType(new IR.TagType(tag));
        _typeMap.Add(type.Name, result);
        var members = ImmutableArray.CreateBuilder<IR.FieldSymbol>();
        foreach (var field in fields)
        {
            IR.QualifiedType fieldType = Type(field.Type);
            if (IR.GimpleTypes.IsVoid(fieldType)) throw Unsupported("Zero-sized aggregate fields");
            members.Add(new IR.FieldSymbol(field.Name, fieldType, tag, members.Count, null));
        }
        if (members.Count == 0) throw Unsupported("Zero-sized aggregates");
        tag.TryDefineFields(members.ToImmutable());
        return result;
    }
    private IR.FunctionType Signature(IEnumerable<TypeSymbol> parameters, TypeSymbol result)
        => _types.FunctionReturning(Type(result), parameters.Where(p => p.Kind != TypeKind.Unit).Select((p, i) => new IR.ParameterSymbol("arg" + i, Type(p), null)).ToImmutableArray(), true, false);
    private static string Mangle(Symbol symbol) => "_RN" + string.Concat(symbol.QualifiedName.Split("::", StringSplitOptions.None).Select(s => s.Length.ToString(CultureInfo.InvariantCulture) + s));
    private IR.FunctionSymbol Function(FunctionSymbol function)
    {
        if (_functions.TryGetValue(function, out IR.FunctionSymbol? cached)) return cached;
        _syntax = function.Declaration;
        if (function.Abi is not ("Rust" or "C")) throw Unsupported($"ABI '{function.Abi}'");
        IR.FunctionType signature = Signature(function.Parameters.Select(p => p.Type), function.ReturnType);
        var symbol = new IR.FunctionSymbol(function.ExternalName ?? (function.IsForeign ? function.Name : Mangle(function)), new IR.QualifiedType(signature),
            function.IsForeign ? IR.StorageClass.Extern : function.ExternalName is null ? IR.StorageClass.Static : IR.StorageClass.None,
            IR.FunctionSpecifiers.None, !function.IsForeign, null);
        _functions.Add(function, symbol);
        int ordinal = 0;
        foreach (ParameterSymbol parameter in function.Parameters)
            if (parameter.Type.Kind != TypeKind.Unit) _variables.Add(parameter, signature.Parameters[ordinal++]);
        return symbol;
    }
    private IR.TypedSymbol Variable(VariableSymbol variable)
    {
        if (_variables.TryGetValue(variable, out IR.TypedSymbol? symbol)) return symbol;
        symbol = new IR.VariableSymbol(variable is GlobalSymbol ? Mangle(variable) : variable.Name, Type(variable.Type),
            variable is GlobalSymbol ? IR.StorageClass.Static : IR.StorageClass.None, null);
        _variables.Add(variable, symbol);
        return symbol;
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
        _temporaries.Clear(); _blocks.Clear(); _labels.Clear(); _labelOrdinal = 0;
        Start(NewLabel());
    }
    private void Finish()
        => _members.Add(new IR.GimpleFunctionDefinition(null, _function, _temporaries.ToImmutableArray(),
            _blocks.Select(b => new IR.GimpleBasicBlock(b.Label, b.Statements.ToImmutableArray())).ToImmutableArray(), _blocks[0].Label));
    private void EmitFunction(BoundFunction function)
    {
        Begin(Function(function.Symbol));
        IR.GimpleValue result = Value(function.Body!);
        if (!Terminated) Emit(new IR.GimpleReturnStatement(_function, IR.GimpleTypes.IsVoid(result.Type) ? null : Snapshot(result)));
        Finish();
    }
    private void EntryPoint(FunctionSymbol entry)
    {
        _syntax = entry.Declaration;
        if (entry.IsForeign || entry.IsUnsafe || !entry.Parameters.IsEmpty || entry.ReturnType.Kind != TypeKind.Unit)
            throw new LoweringException(entry.Declaration, "The entry point must be a safe fn main() returning ().");
        IR.QualifiedType integer = _types.Builtin(IR.BuiltinTypeKind.Int);
        Begin(new IR.FunctionSymbol("main", new IR.QualifiedType(_types.FunctionReturning(integer, [], true, false)), IR.StorageClass.None, IR.FunctionSpecifiers.None, true, null));
        IR.FunctionSymbol target = Function(entry);
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
            case BoundLiteralExpression literal: return Constant(literal.Type, literal.Value);
            case BoundVariableExpression { Variable: GlobalSymbol { Kind: SymbolKind.Constant, ConstantValue.HasValue: true } global }:
                return Constant(global.Type, global.ConstantValue.Value);
            case BoundVariableExpression variable: return variable.Type.Kind == TypeKind.Unit ? Unit() : Symbol(variable.Variable);
            case BoundFunctionReference reference:
                if (!reference.TypeArguments.IsEmpty) throw Unsupported("Generic function instantiation");
                IR.FunctionSymbol function = Function(reference.Function);
                return new IR.GimpleSymbolValue(function, function.Type);
            case BoundBlockExpression block:
                foreach (BoundStatement statement in block.Statements) Statement(statement);
                return block.Tail is null ? Unit() : Value(block.Tail);
            case BoundUnaryExpression unary: return Unary(unary);
            case BoundBinaryExpression binary: return Arithmetic(binary);
            case BoundConversionExpression conversion: return Conversion(conversion);
            case BoundAssignmentExpression assignment:
                IR.GimpleValue assigned = Snapshot(Value(assignment.Value));
                if (assignment.Target.Type.Kind == TypeKind.Unit) return Unit();
                IR.GimplePlace destination = Place(assignment.Target);
                Emit(IR.GimpleAssignStatement.Single(destination, assigned));
                return Unit();
            case BoundCallExpression call: return Call(call);
            case BoundFieldExpression or BoundIndexExpression: return Place(expression);
            case BoundTupleExpression tuple when tuple.Elements.IsEmpty: return Unit();
            case BoundTupleExpression tuple: return Construct(tuple.Type, tuple.Elements.Select((e, i) => (i, e)), null);
            case BoundConstructionExpression construction:
                if (construction.Variant is not null) throw Unsupported("Enum construction");
                return Construct(construction.Type, construction.Fields.Select(f => (f.Field.Ordinal, f.Value)), construction.Base);
            case BoundArrayExpression array: return Array(array);
            case BoundControlFlowExpression { Kind: BoundNodeKind.Return } control:
                IR.GimpleValue? returned = control.Value is null ? null : Snapshot(Value(control.Value));
                Emit(new IR.GimpleReturnStatement(_function, returned is null || IR.GimpleTypes.IsVoid(returned.Type) ? null : returned));
                return Unit();
            case BoundPanicExpression: Panic(); return Unit();
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
                if (variable.Type.Kind == TypeKind.Unit) { if (let.Initializer is not null) _ = Value(let.Initializer); break; }
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
            case BoundVariableExpression variable: return Symbol(variable.Variable);
            case BoundUnaryExpression { Operator: SyntaxKind.StarToken } unary:
                return new IR.GimpleIndirectExpression(Snapshot(Value(unary.Operand)), Type(unary.Type));
            case BoundFieldExpression field:
                IR.GimplePlace receiver = Place(field.Receiver);
                var tag = (IR.TagType)receiver.Type.Type;
                IR.FieldSymbol member = tag.Symbol.Fields[field.Field?.Ordinal ?? field.TupleIndex];
                return new IR.GimpleMemberAccessExpression(receiver, false, default, member, member.Type);
            case BoundIndexExpression index:
                IR.GimpleValue data, length;
                if (index.Receiver is BoundUnaryExpression { Operator: SyntaxKind.StarToken, Operand.Type: IndirectTypeSymbol indirect } dereference && IsWide(indirect))
                {
                    IR.GimpleValue slice = Snapshot(Value(dereference.Operand));
                    data = Snapshot(Field(slice, 0));
                    length = Snapshot(Field(slice, 1));
                }
                else
                {
                    IR.GimplePlace array = Place(index.Receiver);
                    if (index.Receiver.Type is not SequenceTypeSymbol { Length: ulong count }) throw Unsupported("Unsized indexing");
                    data = Snapshot(new IR.GimpleAddressOfExpression(array, new IR.QualifiedType(_types.PointerTo(Type(index.Type)))));
                    length = Constant(_compilation.Binding.Types.Usize, new BigInteger(count));
                }
                IR.GimpleValue subscript = Snapshot(Value(index.Index));
                Require(IR.GimpleTreeCode.LtExpr, subscript, length);
                return new IR.GimpleElementAccessExpression(data, subscript, Type(index.Type));
            default:
                IR.GimpleValue value = Snapshot(Value(expression));
                if (value is IR.GimplePlace place) return place;
                if (IR.GimpleTypes.IsVoid(value.Type)) throw Unsupported("Address of a zero-sized value");
                var temporary = Temp(value.Type);
                Emit(IR.GimpleAssignStatement.Single(temporary, value));
                return temporary;
        }
    }
    private IR.GimpleMemberAccessExpression Field(IR.GimpleValue aggregate, int index)
    {
        IR.FieldSymbol field = ((IR.TagType)aggregate.Type.Type).Symbol.Fields[index];
        return new IR.GimpleMemberAccessExpression(aggregate, false, default, field, field.Type);
    }
    private IR.GimpleValue Construct(TypeSymbol type, IEnumerable<(int Index, BoundExpression Value)> fields, BoundExpression? baseValue)
    {
        var result = Temp(Type(type));
        var assigned = new HashSet<int>();
        foreach (var field in fields)
        {
            Emit(IR.GimpleAssignStatement.Single(Field(result, field.Index), Snapshot(Value(field.Value))));
            assigned.Add(field.Index);
        }
        if (baseValue is not null)
        {
            IR.GimpleValue source = Snapshot(Value(baseValue));
            int count = ((IR.TagType)result.Type.Type).Symbol.Fields.Length;
            for (int i = 0; i < count; i++)
                if (!assigned.Contains(i)) Emit(IR.GimpleAssignStatement.Single(Field(result, i), Snapshot(Field(source, i))));
        }
        return result;
    }
    private IR.GimpleValue Array(BoundArrayExpression array)
    {
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

    private IR.GimpleValue Constant(TypeSymbol type, object? value)
    {
        if (value is byte[] bytes) return Data(bytes, Type(type));
        if (value is string text && type is IndirectTypeSymbol { ElementType.Kind: TypeKind.String })
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            IR.QualifiedType target = Type(type);
            var result = Temp(target);
            Emit(IR.GimpleAssignStatement.Single(Field(result, 0), Snapshot(Data(utf8, Field(result, 0).Type))));
            Emit(IR.GimpleAssignStatement.Single(Field(result, 1), new IR.GimpleConstantValue(utf8.Length, Field(result, 1).Type)));
            return result;
        }
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
    private IR.GimpleInitializer Initializer(BoundExpression expression)
    {
        _syntax = expression.Syntax;
        IR.QualifiedType type = Type(expression.Type);
        if (expression.ConstantValue.HasValue)
        {
            if (expression.Type is IndirectTypeSymbol wide && IsWide(wide)) throw Unsupported("Static wide references");
            return new IR.GimpleExpressionInitializer(null, type, Constant(expression.Type, expression.ConstantValue.Value));
        }
        IEnumerable<BoundExpression> elements = expression switch
        {
            BoundTupleExpression tuple => tuple.Elements,
            BoundArrayExpression { RepeatCount: null } array => array.Elements,
            BoundArrayExpression array => Enumerable.Repeat(array.Elements[0], checked((int)((SequenceTypeSymbol)array.Type).Length!.Value)),
            BoundConstructionExpression { Variant: null, Base: null } construction => construction.Fields.OrderBy(f => f.Field.Ordinal).Select(f => f.Value),
            _ => throw Unsupported("Nonconstant static initialization")
        };
        return new IR.GimpleInitializerList(null, type, elements.Select((e, i) => new IR.GimpleInitializerListItem(null, [], Initializer(e), i)).ToImmutableArray());
    }
    private IR.GimpleValue Unary(BoundUnaryExpression unary)
    {
        if (unary.Operator == SyntaxKind.AmpersandToken)
            return new IR.GimpleAddressOfExpression(Place(unary.Operand), Type(unary.Type));
        if (unary.Operator == SyntaxKind.StarToken) return Place(unary);
        IR.GimpleValue operand = Snapshot(Value(unary.Operand));
        IR.QualifiedType type = Type(unary.Type);
        if (unary.Operator == SyntaxKind.MinusToken && unary.Type is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer)
        {
            IR.QualifiedType unsigned = Unsigned(integer);
            return Convert(Binary(IR.GimpleTreeCode.MinusExpr, new IR.GimpleConstantValue(0, unsigned), Convert(operand, unsigned), unsigned), type);
        }
        IR.GimpleTreeCode code = unary.Operator switch
        {
            SyntaxKind.MinusToken => IR.GimpleTreeCode.NegateExpr,
            SyntaxKind.BangToken when unary.Type.Kind == TypeKind.Boolean => IR.GimpleTreeCode.TruthNotExpr,
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
            SyntaxKind.SlashToken when expression.Left.Type.Kind == TypeKind.Float => IR.GimpleTreeCode.RdivExpr,
            SyntaxKind.SlashToken => IR.GimpleTreeCode.TruncDivExpr, SyntaxKind.PercentToken => IR.GimpleTreeCode.TruncModExpr,
            SyntaxKind.AmpersandToken => IR.GimpleTreeCode.BitAndExpr, SyntaxKind.PipeToken => IR.GimpleTreeCode.BitIorExpr,
            SyntaxKind.CaretToken => IR.GimpleTreeCode.BitXorExpr, SyntaxKind.LessThanLessThanToken => IR.GimpleTreeCode.LshiftExpr,
            SyntaxKind.GreaterThanGreaterThanToken => IR.GimpleTreeCode.RshiftExpr,
            SyntaxKind.EqualsEqualsToken => IR.GimpleTreeCode.EqExpr, SyntaxKind.BangEqualsToken => IR.GimpleTreeCode.NeExpr,
            SyntaxKind.LessThanToken => IR.GimpleTreeCode.LtExpr, SyntaxKind.LessThanEqualsToken => IR.GimpleTreeCode.LeExpr,
            SyntaxKind.GreaterThanToken => IR.GimpleTreeCode.GtExpr, SyntaxKind.GreaterThanEqualsToken => IR.GimpleTreeCode.GeExpr,
            _ => throw Unsupported($"Binary operator '{expression.Operator}'")
        };
        if (expression.Left.Type is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer)
        {
            if (code is IR.GimpleTreeCode.TruncDivExpr or IR.GimpleTreeCode.TruncModExpr)
            {
                Require(IR.GimpleTreeCode.NeExpr, right, new IR.GimpleConstantValue(0, right.Type));
                if (integer.IsSigned)
                {
                    var boolType = _types.Builtin(IR.BuiltinTypeKind.Bool);
                    IR.GimpleValue minimum = Constant(integer, -(BigInteger.One << (integer.BitWidth - 1)));
                    IR.GimpleValue safeLeft = Binary(IR.GimpleTreeCode.NeExpr, left, minimum, boolType);
                    IR.GimpleValue safeRight = Binary(IR.GimpleTreeCode.NeExpr, right, Constant(integer, -BigInteger.One), boolType);
                    Require(IR.GimpleTreeCode.NeExpr, Binary(IR.GimpleTreeCode.BitIorExpr, safeLeft, safeRight, boolType), new IR.GimpleConstantValue(0, boolType));
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
        else if (expression.Left.Type.Kind == TypeKind.Float && code == IR.GimpleTreeCode.TruncModExpr)
            throw Unsupported("Floating-point remainder");
        return Binary(code, left, right, resultType);
    }
    private IR.GimpleValue Conversion(BoundConversionExpression conversion)
    {
        if (conversion.Conversion is ConversionKind.Identity or ConversionKind.Never) return Value(conversion.Operand);
        if (conversion.Conversion == ConversionKind.FunctionToPointer)
            return new IR.GimpleAddressOfExpression(Place(conversion.Operand), Type(conversion.Type));
        if (conversion.Type is IndirectTypeSymbol target && IsWide(target) && conversion.Operand.Type is IndirectTypeSymbol { ElementType: SequenceTypeSymbol { Kind: TypeKind.Array, Length: ulong length } })
        {
            IR.GimpleValue array = Snapshot(Value(conversion.Operand));
            var result = Temp(Type(target));
            Emit(IR.GimpleAssignStatement.Single(Field(result, 0), Convert(array, Field(result, 0).Type)));
            Emit(IR.GimpleAssignStatement.Single(Field(result, 1), new IR.GimpleConstantValue(length, Field(result, 1).Type)));
            return result;
        }
        IR.GimpleValue value = Snapshot(Value(conversion.Operand));
        if (conversion.Type is IndirectTypeSymbol wide && IsWide(wide) && conversion.Operand.Type is IndirectTypeSymbol source && IsWide(source))
        {
            var result = Temp(Type(wide));
            Emit(IR.GimpleAssignStatement.Single(result, value));
            return result;
        }
        if (conversion.Operand.Type.Kind == TypeKind.Float && conversion.Type is PrimitiveTypeSymbol { Kind: TypeKind.Integer } integer)
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
        IR.GimpleValue function = Snapshot(Value(call.Target));
        IR.FunctionType signature = function.Type.Type is IR.FunctionType direct ? direct
            : function.Type.Type is IR.PointerType { PointeeType.Type: IR.FunctionType pointer } ? pointer
            : throw Unsupported("Call target");
        var arguments = call.Arguments.Select(a => Snapshot(Value(a))).Where(a => !IR.GimpleTypes.IsVoid(a.Type)).ToImmutableArray();
        IR.GimpleTemporaryValue? result = IR.GimpleTypes.IsVoid(signature.ReturnType) ? null : Temp(signature.ReturnType);
        Emit(new IR.GimpleCallStatement(result, function, arguments, signature, signature.ReturnType, isNoReturn: call.Type.Kind == TypeKind.Never));
        if (call.Type.Kind == TypeKind.Never) Stop();
        return (IR.GimpleValue?)result ?? Unit();
    }
    private void Require(IR.GimpleTreeCode comparison, IR.GimpleValue left, IR.GimpleValue right)
    {
        IR.GimpleLabel success = NewLabel(), failure = NewLabel();
        Emit(new IR.GimpleCondStatement(comparison, left, right, success, failure));
        Start(failure); Panic(); Start(success);
    }
    private void Panic()
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
