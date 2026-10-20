using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Cs;

internal static partial class RecordBinder
{
    internal static void BindBodies(Compilation compilation, RecordTypeInfo record, SemanticModel model,
        TypeBinder typeBinder, IBindingRecorder recorder, DiagnosticBag diagnostics)
    {
        if (!record.Bodies.IsDefault)
            return;
        var bodies = ImmutableArray.CreateBuilder<BoundMethodBody>();
        foreach (var (method, kind, field) in record.Members)
        {
            if (!method.IsAbstract)
                bodies.Add(new BodyBuilder(compilation, record, method).Build(kind, field, model, typeBinder, recorder, diagnostics));
        }
        record.Bodies = bodies.ToImmutable();
    }

    private sealed class BodyBuilder
    {
        private readonly Compilation _compilation;
        private readonly RecordTypeInfo _record;
        private readonly SourceMethodSymbol _method;
        private readonly SourceNamedTypeSymbol _type;
        private readonly SyntaxNode _syntax;
        private readonly NamedTypeSymbol _bool;
        private readonly NamedTypeSymbol _int;
        private readonly NamedTypeSymbol _object;
        private readonly NamedTypeSymbol _string;
        private readonly BoundThisExpression _this;
        private readonly NamedTypeSymbol? _baseRecord;
        private readonly ImmutableArray<BoundStatement>.Builder _statements = ImmutableArray.CreateBuilder<BoundStatement>();

        internal BodyBuilder(Compilation compilation, RecordTypeInfo record, SourceMethodSymbol method)
        {
            _compilation = compilation;
            _record = record;
            _method = method;
            _type = record.Type;
            _syntax = record.Syntax;
            _bool = compilation.GetSpecialType(SpecialType.System_Boolean);
            _int = compilation.GetSpecialType(SpecialType.System_Int32);
            _object = compilation.GetSpecialType(SpecialType.System_Object);
            _string = compilation.GetSpecialType(SpecialType.System_String);
            _this = new BoundThisExpression(new ThisExpressionSyntax(default), _type, isLValue: true);
            _baseRecord = _type.BaseType is NamedTypeSymbol { IsRecord: true } baseRecord ? baseRecord : null;
        }

        internal BoundMethodBody Build(RecordMemberKind kind, FieldSymbol? field, SemanticModel model,
            TypeBinder typeBinder, IBindingRecorder recorder, DiagnosticBag diagnostics)
        {
            switch (kind)
            {
                case RecordMemberKind.Get:
                    Return(Member(_this, field!));
                    break;
                case RecordMemberKind.Set:
                    Assign(Member(_this, field!), Parameter(0));
                    Return();
                    break;
                case RecordMemberKind.PrimaryConstructor:
                    BuildPrimaryConstructor(model, typeBinder, recorder, diagnostics);
                    break;
                case RecordMemberKind.CopyConstructor:
                    BuildCopyConstructor(diagnostics);
                    break;
                case RecordMemberKind.Clone:
                    Return(new BoundObjectCreationExpression(_syntax, _type,
                        _type.GetMembers().OfType<MethodSymbol>().First(IsCopyConstructor), ImmutableArray.Create<BoundExpression>(_this)));
                    break;
                case RecordMemberKind.EqualityContract:
                    Return(new BoundTypeOfExpression(_syntax, _method.ReturnType, _type));
                    break;
                case RecordMemberKind.Equals:
                    BuildEquals();
                    break;
                case RecordMemberKind.ObjectEquals:
                    if (_type.IsValueType)
                    {
                        var test = new BoundIsPatternExpression(_syntax, Parameter(0), _bool, BoundIsPatternKind.Type, _type);
                        var value = Convert(Parameter(0), _type, ConversionKind.Unboxing);
                        Return(Binary(BoundBinaryOperatorKind.LogicalAnd, test, Call(_this, FindMethod(_type, "Equals", _type)!, value)));
                    }
                    else
                        Return(Call(_this, FindMethod(_type, "Equals", _type)!,
                            new BoundAsExpression(_syntax, _type, Parameter(0), new Conversion(ConversionKind.ExplicitReference))));
                    break;
                case RecordMemberKind.BaseEquals:
                    Return(Call(_this, FindMethod(_type, "Equals", _object)!, Convert(Parameter(0), _object, ConversionKind.ImplicitReference)));
                    break;
                case RecordMemberKind.Equality:
                    var equals = Call(Parameter(0), FindMethod(_type, "Equals", _type)!, Parameter(1));
                    Return(_type.IsValueType ? equals : Binary(BoundBinaryOperatorKind.LogicalOr,
                        Binary(BoundBinaryOperatorKind.Equals, Parameter(0), Parameter(1)),
                        Binary(BoundBinaryOperatorKind.LogicalAnd, NotNull(Parameter(0)), equals)));
                    break;
                case RecordMemberKind.Inequality:
                    Return(new BoundUnaryExpression(_syntax, BoundUnaryOperatorKind.LogicalNot, _bool,
                        Call(null, FindMethod(_type, "op_Equality", new TypeSymbol[] { _type, _type })!, Parameter(0), Parameter(1)), default));
                    break;
                case RecordMemberKind.GetHashCode:
                    BuildHashCode();
                    break;
                case RecordMemberKind.Deconstruct:
                    for (int i = 0; i < _method.Parameters.Length; i++)
                        Assign(Parameter(i), Member(_this, _record.PositionalMembers[i]));
                    Return();
                    break;
                case RecordMemberKind.PrintMembers:
                    BuildPrintMembers();
                    break;
                case RecordMemberKind.ToString:
                    BuildToString();
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected record member: {kind}.");
            }
            return new BoundMethodBody(_syntax, _method, new BoundBlockStatement(_syntax, _statements.ToImmutable()));
        }

        private void BuildPrimaryConstructor(SemanticModel model, TypeBinder typeBinder, IBindingRecorder recorder, DiagnosticBag diagnostics)
        {
            if (_type.IsValueType)
                Assign(_this, Local("$zero", _type, null));
            for (int i = 0; i < _method.Parameters.Length; i++)
            {
                if (_record.PositionalMembers[i] is SourcePropertySymbol { BackingFieldOpt: FieldSymbol field } property &&
                    ReferenceEquals(property.ContainingSymbol, _type) &&
                    property.DeclaringSyntaxReferences[0].Node is ParameterSyntax)
                    Assign(Member(_this, field), Parameter(i));
            }
            _statements.AddRange(Compilation.BuildFieldInitializerStatements(_compilation, _record.Tree, model, _method, false, diagnostics));
            if (_type.IsReferenceType)
            {
                var binder = new LocalScopeBinder(typeBinder, BinderFlags.InMethod | BinderFlags.InConstructor, _method);
                var context = new BindingContext(_compilation, model, _method, recorder);
                var baseSyntax = _record.Syntax.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault();
                var args = baseSyntax?.ArgumentList ?? new ArgumentListSyntax(default, default, default);
                var token = new SyntaxToken(SyntaxKind.BaseKeyword, _syntax.Span, "base", null, Array.Empty<SyntaxTrivia>(), Array.Empty<SyntaxTrivia>());
                var initializer = new ConstructorInitializerSyntax(SyntaxKind.BaseConstructorInitializer, default, token, args);
                if (binder.BindConstructorInitializer(initializer, context, diagnostics) is BoundStatement statement)
                    _statements.Add(statement);
            }
            Return();
        }

        private void BuildCopyConstructor(DiagnosticBag diagnostics)
        {
            var baseType = (NamedTypeSymbol)_type.BaseType!;
            var constructor = baseType.GetMembers().OfType<MethodSymbol>().FirstOrDefault(m => m.IsConstructor && !m.IsStatic &&
                (_baseRecord is null ? m.Parameters.Length == 0 : m.Parameters.Length == 1 && SameType(m.Parameters[0].Type, baseType)));
            if (constructor is null)
                Error(_record, diagnostics, "CS8867", "No accessible base copy constructor was found.");
            else
                Expression(Call(BaseReceiver(baseType), constructor, _baseRecord is null ? Array.Empty<BoundExpression>() : new[] { Convert(Parameter(0), baseType, ConversionKind.ImplicitReference) }));
            foreach (var field in InstanceFields())
                Assign(Member(_this, field), Member(Parameter(0), field));
            Return();
        }

        private void BuildEquals()
        {
            BoundExpression result = Literal(true);
            if (_baseRecord is not null)
                result = Call(BaseReceiver(_baseRecord), FindMethod(_baseRecord, "Equals", _baseRecord)!, Convert(Parameter(0), _baseRecord, ConversionKind.ImplicitReference));
            else if (_type.IsReferenceType)
            {
                var contract = FindMethod(_type, "get_EqualityContract")!;
                result = Binary(BoundBinaryOperatorKind.LogicalAnd, NotNull(Parameter(0)),
                    Binary(BoundBinaryOperatorKind.Equals, Call(_this, contract), Call(Parameter(0), contract)));
            }
            foreach (var field in InstanceFields())
                result = Binary(BoundBinaryOperatorKind.LogicalAnd, result, Compare(field.Type, "Equals", Member(_this, field), Member(Parameter(0), field)));
            Return(result);
        }

        private void BuildHashCode()
        {
            BoundExpression? hash = _baseRecord is not null
                ? Call(BaseReceiver(_baseRecord), FindMethod(_baseRecord, "GetHashCode")!)
                : _type.IsReferenceType ? Compare(FindType(_compilation, "System", "Type")!, "GetHashCode", Call(_this, FindMethod(_type, "get_EqualityContract")!)) : null;
            foreach (var field in InstanceFields())
            {
                var value = Compare(field.Type, "GetHashCode", Member(_this, field));
                hash = hash is null ? value : Binary(BoundBinaryOperatorKind.Add,
                    Binary(BoundBinaryOperatorKind.Multiply, hash, Literal(-1521134295), _int), value, _int);
            }
            Return(hash ?? Literal(0));
        }

        private void BuildPrintMembers()
        {
            var printable = _record.PositionalMembers.Where(m => m is SourcePropertySymbol p && ReferenceEquals(p.ContainingSymbol, _type) && p.DeclaringSyntaxReferences[0].Node is ParameterSyntax)
                .Concat(_type.GetMembers()).Distinct().Where(m => m.DeclaredAccessibility == Accessibility.Public && m is
                FieldSymbol { IsStatic: false } or PropertySymbol { IsStatic: false, HasGet: true, Parameters.Length: 0, GetMethod.DeclaredAccessibility: Accessibility.Public }).ToArray();
            if (_baseRecord is not null)
            {
                var printed = Call(BaseReceiver(_baseRecord), FindMethod(_baseRecord, "PrintMembers", Parameter(0).Type)!, Parameter(0));
                if (printable.Length == 0)
                {
                    Return(printed);
                    return;
                }
                _statements.Add(new BoundIfStatement(_syntax, printed, Statement(Append(Parameter(0), Literal(", "))), null));
            }
            for (int i = 0; i < printable.Length; i++)
            {
                Expression(Append(Parameter(0), Literal((i == 0 ? "" : ", ") + printable[i].Name + " = ")));
                var value = Member(_this, printable[i]);
                BoundExpression text;
                if (value.Type.SpecialType == SpecialType.System_String)
                    text = value;
                else if (value.Type.IsValueType && value.Type is not TypeParameterSymbol)
                    text = Call(value, FindMethod(value.Type as NamedTypeSymbol, "ToString") ?? FindMethod(_object, "ToString")!);
                else
                {
                    var boxed = Local("$print" + i, _object, Convert(value, _object,
                        value.Type.IsReferenceType ? ConversionKind.ImplicitReference : ConversionKind.Boxing));
                    text = new BoundConditionalExpression(_syntax, _string, NotNull(boxed),
                        Call(boxed, FindMethod(_object, "ToString")!), new BoundLiteralExpression(_syntax, _string, null), default);
                }
                Expression(Append(Parameter(0), text));
            }
            Return(Literal(printable.Length != 0));
        }

        private void BuildToString()
        {
            var builderType = FindType(_compilation, "System.Text", "StringBuilder")!;
            var constructor = builderType.GetMembers().OfType<MethodSymbol>().First(m => m.IsConstructor && !m.IsStatic && m.Parameters.Length == 0);
            var builder = Local("$builder", builderType, new BoundObjectCreationExpression(_syntax, builderType, constructor, ImmutableArray<BoundExpression>.Empty));
            Expression(Append(builder, Literal(_type.Name + " { ")));
            var printed = Call(_this, FindMethod(_type, "PrintMembers", builderType)!, builder);
            _statements.Add(new BoundIfStatement(_syntax, printed, Statement(Append(builder, Literal(" "))), null));
            Expression(Append(builder, Literal("}")));
            Return(Call(builder, FindMethod(builderType, "ToString")!));
        }

        private IEnumerable<FieldSymbol> InstanceFields() => _type.GetMembers().OfType<FieldSymbol>().Where(f => !f.IsStatic && !f.IsConst);
        private BoundExpression Parameter(int index) => new BoundParameterExpression(_syntax, _method.Parameters[index]);
        private BoundExpression BaseReceiver(NamedTypeSymbol type) => new BoundBaseExpression(new BaseExpressionSyntax(default), _type, type);
        private BoundExpression Literal(bool value) => new BoundLiteralExpression(_syntax, _bool, value);
        private BoundExpression Literal(int value) => new BoundLiteralExpression(_syntax, _int, value);
        private BoundExpression Literal(string value) => new BoundLiteralExpression(_syntax, _string, value);
        private BoundExpression NotNull(BoundExpression value) => Binary(BoundBinaryOperatorKind.NotEquals, value, new BoundLiteralExpression(_syntax, value.Type, null));
        private BoundExpression Convert(BoundExpression value, TypeSymbol type, ConversionKind kind)
            => SameType(value.Type, type) ? value : new BoundConversionExpression(_syntax, type, value, new Conversion(kind), false);
        private BoundExpression Binary(BoundBinaryOperatorKind kind, BoundExpression left, BoundExpression right, TypeSymbol? type = null)
            => new BoundBinaryExpression(_syntax, kind, type ?? _bool, left, right, default);
        private BoundExpression Call(BoundExpression? receiver, MethodSymbol method, params BoundExpression[] arguments)
            => new BoundCallExpression(_syntax, receiver, method, arguments.ToImmutableArray());
        private BoundExpression Member(BoundExpression receiver, Symbol member)
            => new BoundMemberAccessExpression((ExpressionSyntax)_this.Syntax, receiver, member, member is FieldSymbol field ? field.Type : ((PropertySymbol)member).Type, isLValue: true);
        private BoundExpression Compare(TypeSymbol type, string name, params BoundExpression[] arguments)
        {
            var comparer = _compilation.ConstructNamedType(FindType(_compilation, "System.Collections.Generic", "EqualityComparer", 1)!, ImmutableArray.Create(type));
            var instance = Call(null, FindMethod(comparer, "get_Default")!);
            return Call(instance, FindMethod(comparer, name, arguments.Select(a => a.Type).ToArray())!, arguments);
        }
        private BoundExpression Append(BoundExpression builder, BoundExpression value)
            => Call(builder, FindMethod((NamedTypeSymbol)builder.Type, "Append", _string)!, value);
        private BoundExpression Local(string name, TypeSymbol type, BoundExpression? initializer)
        {
            var local = new LocalSymbol(name, _method, type, ImmutableArray<Location>.Empty);
            _statements.Add(new BoundLocalDeclarationStatement(_syntax, local, initializer));
            return new BoundLocalExpression(_syntax, local);
        }
        private BoundStatement Statement(BoundExpression expression) => new BoundExpressionStatement(_syntax, expression);
        private void Expression(BoundExpression expression) => _statements.Add(Statement(expression));
        private void Assign(BoundExpression target, BoundExpression value) => Expression(new BoundAssignmentExpression(_syntax, target, value));
        private void Return(BoundExpression? value = null) => _statements.Add(new BoundReturnStatement(_syntax, value));
    }
}
