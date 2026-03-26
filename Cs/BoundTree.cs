using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    
    public abstract class BoundNode
    {
        public abstract BoundNodeKind Kind { get; }
        public bool HasErrors { get; protected set; }
        public abstract SyntaxNode Syntax { get; }
    }
    public abstract class BoundExpression : BoundNode
    {
        public TypeSymbol Type { get; protected set; } = null!;
        public Optional<object> ConstantValueOpt { get; protected set; } = Optional<object>.None;
        public virtual bool IsLValue => false;
        public override SyntaxNode Syntax { get; }
        internal void SetHasErrors() => HasErrors = true;
        protected BoundExpression(SyntaxNode syntax) => Syntax = syntax;
    }

    public abstract class BoundStatement : BoundNode
    {
        public override SyntaxNode Syntax { get; }
        protected BoundStatement(SyntaxNode syntax) => Syntax = syntax;
        internal void SetHasErrors() => HasErrors = true;
    }
    internal sealed class BoundBadExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.BadExpression;
        public void SetType(TypeSymbol type) => Type = type;
        public BoundBadExpression(SyntaxNode syntax) : base(syntax)
        {
            HasErrors = true;
            Type = new ErrorTypeSymbol("error", containing: null, ImmutableArray<Location>.Empty);
        }
    }
    internal sealed class BoundBadStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.BadStatement;
        public BoundBadStatement(StatementSyntax syntax) : base(syntax) => HasErrors = true;
    }
    internal sealed class BoundCompilationUnit : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.CompilationUnit;
        public override SyntaxNode Syntax { get; }
        public ImmutableArray<BoundStatement> Statements { get; }
        public BoundMethodBody? TopLevelMethodBodyOpt { get; }
        public BoundCompilationUnit(
            CompilationUnitSyntax syntax,
            ImmutableArray<BoundStatement> statements,
            BoundMethodBody? topLevelMethodBodyOpt = null)
        {
            Syntax = syntax;
            Statements = statements;
            TopLevelMethodBodyOpt = topLevelMethodBodyOpt;
        }

    }
    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundStatementList : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.StatementList;
        public ImmutableArray<BoundStatement> Statements { get; }

        public BoundStatementList(SyntaxNode syntax, ImmutableArray<BoundStatement> statements)
            : base(syntax)
        {
            Statements = statements;
        }
    }
    internal sealed class BoundLiteralExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Literal;
        public object? Value { get; }

        public BoundLiteralExpression(SyntaxNode syntax, TypeSymbol type, object? value)
            : base(syntax)
        {
            Type = type;
            Value = value;
            ConstantValueOpt = new Optional<object>(value!); 
        }
    }
    internal sealed class BoundThrowExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ThrowExpression;
        public BoundExpression Exception { get; }

        public BoundThrowExpression(ThrowExpressionSyntax syntax, BoundExpression exception)
            : base(syntax)
        {
            Exception = exception;
            Type = ThrowTypeSymbol.Instance;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = exception.HasErrors;
        }

        internal void SetType(TypeSymbol type) => Type = type;
    }
    internal sealed class BoundTupleExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.TupleExpression;

        public ImmutableArray<BoundExpression> Elements { get; }
        public ImmutableArray<string?> ElementNames { get; }

        public BoundTupleExpression(
            TupleExpressionSyntax syntax,
            TupleTypeSymbol type,
            ImmutableArray<BoundExpression> elements,
            ImmutableArray<string?> elementNames,
            bool hasErrors = false)
            : base(syntax)
        {
            Type = type;
            Elements = elements;
            ElementNames = elementNames;

            HasErrors = hasErrors;

            // Tuples are never constant in C#.
            ConstantValueOpt = Optional<object>.None;

            for (int i = 0; i < elements.Length; i++)
                if (elements[i].HasErrors)
                    HasErrors = true;
        }
    }
    internal sealed class BoundArrayInitializerExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ArrayInitializer;
        public ImmutableArray<BoundExpression> Elements { get; }

        public BoundArrayInitializerExpression(
            InitializerExpressionSyntax syntax, TypeSymbol elementType, ImmutableArray<BoundExpression> elements)
            : base(syntax)
        {
            Type = elementType;
            Elements = elements;

            for (int i = 0; i < elements.Length; i++)
                if (elements[i].HasErrors)
                    HasErrors = true;
        }
    }
    internal sealed class BoundArrayCreationExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ArrayCreation;
        public TypeSymbol ElementType { get; }
        public ImmutableArray<BoundExpression> DimensionSizes { get; }
        public BoundArrayInitializerExpression? InitializerOpt { get; }
        public BoundArrayCreationExpression(
            SyntaxNode syntax,
            ArrayTypeSymbol type,
            TypeSymbol elementType,
            ImmutableArray<BoundExpression> dimensionSizes,
            BoundArrayInitializerExpression? initializerOpt)
            : base(syntax)
        {
            Type = type;
            ElementType = elementType;
            DimensionSizes = dimensionSizes.IsDefault ? ImmutableArray<BoundExpression>.Empty : dimensionSizes;
            InitializerOpt = initializerOpt;

            HasErrors = initializerOpt?.HasErrors ?? false;
            for (int i = 0; i < DimensionSizes.Length; i++)
                if (DimensionSizes[i].HasErrors)
                    HasErrors = true;
        }

        public BoundArrayCreationExpression(
            SyntaxNode syntax,
            ArrayTypeSymbol type,
            TypeSymbol elementType,
            BoundExpression count,
            BoundArrayInitializerExpression? initializerOpt)
            : this(syntax, type, elementType, ImmutableArray.Create(count), initializerOpt)
        {
        }

    }
    internal sealed class BoundArrayElementAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ArrayElementAccess;
        public BoundExpression Expression { get; }
        public ImmutableArray<BoundExpression> Indices { get; }
        public override bool IsLValue => true;
        public BoundArrayElementAccessExpression(
            SyntaxNode syntax,
            TypeSymbol elementType,
            BoundExpression expression,
            ImmutableArray<BoundExpression> indices)
            : base(syntax)
        {
            Type = elementType;
            Expression = expression;
            Indices = indices.IsDefault ? ImmutableArray<BoundExpression>.Empty : indices;

            HasErrors = expression.HasErrors;
            for (int i = 0; i < Indices.Length; i++)
                if (Indices[i].HasErrors)
                    HasErrors = true;
        }

        public BoundArrayElementAccessExpression(
            SyntaxNode syntax,
            TypeSymbol elementType,
            BoundExpression expression,
            BoundExpression index)
            : this(syntax, elementType, expression, ImmutableArray.Create(index))
        {
        }
    }
    internal sealed class BoundStackAllocArrayCreationExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.StackAllocArrayCreation;
        public TypeSymbol ElementType { get; }
        public BoundExpression Count { get; } // int32
        public BoundArrayInitializerExpression? InitializerOpt { get; }

        public BoundStackAllocArrayCreationExpression(
            SyntaxNode syntax,
            PointerTypeSymbol type,
            TypeSymbol elementType,
            BoundExpression count,
            BoundArrayInitializerExpression? initializerOpt)
            : base(syntax)
        {
            Type = type;
            ElementType = elementType;
            Count = count;
            InitializerOpt = initializerOpt;
        }
    }
    internal sealed class BoundRefExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.RefExpression;
        public BoundExpression Operand { get; }

        public BoundRefExpression(SyntaxNode syntax, TypeSymbol byRefType, BoundExpression operand)
            : base(syntax)
        {
            Type = byRefType;
            Operand = operand;
            HasErrors = operand.HasErrors;
        }
    }
    internal sealed class BoundAddressOfExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.AddressOf;
        public BoundExpression Operand { get; }

        public BoundAddressOfExpression(PrefixUnaryExpressionSyntax syntax, PointerTypeSymbol type, BoundExpression operand)
            : base(syntax)
        {
            Type = type;
            Operand = operand;
        }
    }

    internal sealed class BoundPointerIndirectionExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.PointerIndirection;
        public BoundExpression Operand { get; }
        public override bool IsLValue => true;

        public BoundPointerIndirectionExpression(SyntaxNode syntax, TypeSymbol elementType, BoundExpression operand)
            : base(syntax)
        {
            Type = elementType;
            Operand = operand;
        }
    }

    internal sealed class BoundPointerElementAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.PointerElementAccess;
        public BoundExpression Expression { get; }
        public BoundExpression Index { get; }
        public override bool IsLValue => true;

        public BoundPointerElementAccessExpression(SyntaxNode syntax, TypeSymbol elementType, BoundExpression expression, BoundExpression index)
            : base(syntax)
        {
            Type = elementType;
            Expression = expression;
            Index = index;
        }
    }
    internal sealed class BoundConversionExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Conversion;
        public BoundExpression Operand { get; }
        public Conversion Conversion { get; }
        public bool IsChecked { get; }

        public BoundConversionExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression operand, Conversion conversion, bool isChecked)
            : base(syntax)
        {
            Type = type;
            Operand = operand;
            Conversion = conversion;
            IsChecked = isChecked;

            ConstantValueOpt = ConvertConstant(operand, type, conversion, isChecked);
        }

        private static Optional<object> ConvertConstant(
            BoundExpression operand,
            TypeSymbol targetType,
            Conversion conversion,
            bool isChecked)
        {
            if (!operand.ConstantValueOpt.HasValue)
                return Optional<object>.None;

            var value = operand.ConstantValueOpt.Value;

            if (conversion.Kind == ConversionKind.Identity)
                return operand.ConstantValueOpt;

            if (conversion.Kind == ConversionKind.NullLiteral)
                return new Optional<object>(null!);

            if (conversion.Kind is ConversionKind.ImplicitNumeric
                or ConversionKind.ExplicitNumeric
                or ConversionKind.ImplicitConstant)
            {
                bool allowWrap = conversion.Kind == ConversionKind.ExplicitNumeric && !isChecked;
                var targetSpecial = targetType is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Enum
                    ? (nt.EnumUnderlyingType?.SpecialType ?? SpecialType.System_Int32)
                    : targetType.SpecialType;

                if (TryConvertNumericConstant(value!, targetSpecial, allowWrap, out var converted))
                    return new Optional<object>(converted);
            }

            return Optional<object>.None;
        }

        private static bool TryConvertNumericConstant(object value, SpecialType to, bool allowWrap, out object converted)
        {
            converted = default!;

            if (to == SpecialType.System_IntPtr)
                to = SpecialType.System_Int32;
            else if (to == SpecialType.System_UIntPtr)
                to = SpecialType.System_UInt32;

            if (IsIntegralOrChar(to))
            {
                if (allowWrap && TryToUInt64Bits(value, out var bits))
                {
                    converted = ConvertFromBits(bits, to);
                    return true;
                }

                if (TryConvertFloatingToIntegralConstant(value, to, out converted))
                    return true;

                if (!TryToDecimal(value, out var d))
                    return false;

                if (d != decimal.Truncate(d))
                    return false;

                switch (to)
                {
                    case SpecialType.System_Int8:
                        if (d < sbyte.MinValue || d > sbyte.MaxValue) return false;
                        converted = (sbyte)d; return true;

                    case SpecialType.System_UInt8:
                        if (d < byte.MinValue || d > byte.MaxValue) return false;
                        converted = (byte)d; return true;

                    case SpecialType.System_Int16:
                        if (d < short.MinValue || d > short.MaxValue) return false;
                        converted = (short)d; return true;

                    case SpecialType.System_UInt16:
                        if (d < ushort.MinValue || d > ushort.MaxValue) return false;
                        converted = (ushort)d; return true;

                    case SpecialType.System_Char:
                        if (d < char.MinValue || d > char.MaxValue) return false;
                        converted = (char)d; return true;

                    case SpecialType.System_Int32:
                        if (d < int.MinValue || d > int.MaxValue) return false;
                        converted = (int)d; return true;

                    case SpecialType.System_UInt32:
                        if (d < uint.MinValue || d > uint.MaxValue) return false;
                        converted = (uint)d; return true;

                    case SpecialType.System_Int64:
                        if (d < long.MinValue || d > long.MaxValue) return false;
                        converted = (long)d; return true;

                    case SpecialType.System_UInt64:
                        if (d < 0m || d > (decimal)ulong.MaxValue) return false;
                        converted = (ulong)d; return true;
                }
                return false;
            }
            if (to is SpecialType.System_Single or SpecialType.System_Double)
                return TryConvertToFloatingConstant(value, to, out converted);
            return false;
        }
        private static bool TryConvertToFloatingConstant(object value, SpecialType to, out object converted)
        {
            converted = default!;
            double d;

            switch (value)
            {
                case sbyte x: d = x; break;
                case byte x: d = x; break;
                case short x: d = x; break;
                case ushort x: d = x; break;
                case int x: d = x; break;
                case uint x: d = x; break;
                case long x: d = x; break;
                case ulong x: d = x; break;
                case char x: d = x; break;
                case float x: d = x; break;
                case double x: d = x; break;
                default:
                    return false;
            }

            if (to == SpecialType.System_Single)
            {
                converted = (float)d;
                return true;
            }

            if (to == SpecialType.System_Double)
            {
                converted = d;
                return true;
            }

            return false;
        }
        private static bool TryConvertFloatingToIntegralConstant(object value, SpecialType to, out object converted)
        {
            converted = default!;
            double d;

            switch (value)
            {
                case float f: d = f; break;
                case double dd: d = dd; break;
                default:
                    return false;
            }

            if (double.IsNaN(d) || double.IsInfinity(d))
                return false;

            if (d != Math.Truncate(d))
                return false;

            switch (to)
            {
                case SpecialType.System_Int8:
                    if (d < sbyte.MinValue || d > sbyte.MaxValue) return false;
                    converted = (sbyte)d; return true;

                case SpecialType.System_UInt8:
                    if (d < byte.MinValue || d > byte.MaxValue) return false;
                    converted = (byte)d; return true;

                case SpecialType.System_Int16:
                    if (d < short.MinValue || d > short.MaxValue) return false;
                    converted = (short)d; return true;

                case SpecialType.System_UInt16:
                    if (d < ushort.MinValue || d > ushort.MaxValue) return false;
                    converted = (ushort)d; return true;

                case SpecialType.System_Char:
                    if (d < char.MinValue || d > char.MaxValue) return false;
                    converted = (char)d; return true;

                case SpecialType.System_Int32:
                    if (d < int.MinValue || d > int.MaxValue) return false;
                    converted = (int)d; return true;

                case SpecialType.System_UInt32:
                    if (d < uint.MinValue || d > uint.MaxValue) return false;
                    converted = (uint)d; return true;

                case SpecialType.System_Int64:
                    if (d < long.MinValue || d > long.MaxValue) return false;
                    converted = (long)d; return true;

                case SpecialType.System_UInt64:
                    if (d < 0.0 || d > ulong.MaxValue) return false;
                    converted = (ulong)d; return true;

                default:
                    return false;
            }
        }
        private static bool IsIntegralOrChar(SpecialType t) => t is
            SpecialType.System_Int8 or SpecialType.System_UInt8 or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Char;

        private static bool TryToUInt64Bits(object v, out ulong bits)
        {
            switch (v)
            {
                case sbyte x: bits = unchecked((ulong)x); return true;
                case byte x: bits = x; return true;
                case short x: bits = unchecked((ulong)x); return true;
                case ushort x: bits = x; return true;
                case int x: bits = unchecked((ulong)x); return true;
                case uint x: bits = x; return true;
                case long x: bits = unchecked((ulong)x); return true;
                case ulong x: bits = x; return true;
                case char x: bits = x; return true;
                default:
                    bits = default;
                    return false;
            }
        }

        private static object ConvertFromBits(ulong bits, SpecialType to)
        {
            return to switch
            {
                SpecialType.System_Int8 => (sbyte)bits,
                SpecialType.System_UInt8 => (byte)bits,
                SpecialType.System_Int16 => (short)bits,
                SpecialType.System_UInt16 => (ushort)bits,
                SpecialType.System_Char => (char)bits,
                SpecialType.System_Int32 => (int)bits,
                SpecialType.System_UInt32 => (uint)bits,
                SpecialType.System_Int64 => (long)bits,
                SpecialType.System_UInt64 => bits,
                _ => throw new ArgumentOutOfRangeException(nameof(to))
            };
        }

        private static bool TryToDecimal(object v, out decimal dd)
        {
            switch (v)
            {
                case sbyte x: dd = x; return true;
                case byte x: dd = x; return true;
                case short x: dd = x; return true;
                case ushort x: dd = x; return true;
                case int x: dd = x; return true;
                case uint x: dd = x; return true;
                case long x: dd = x; return true;
                case ulong x: dd = x; return true;
                case char x: dd = x; return true;
                default:
                    dd = default;
                    return false;
            }
        }
    }
    internal sealed class BoundAsExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.AsExpression;
        public BoundExpression Operand { get; }
        public Conversion Conversion { get; }

        public BoundAsExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression operand, Conversion conversion)
            : base(syntax)
        {
            Type = type;
            Operand = operand;
            Conversion = conversion;

            ConstantValueOpt = Optional<object>.None;
            HasErrors = operand.HasErrors || !conversion.Exists;
        }
    }
    internal sealed class BoundSizeOfExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.SizeOfExpression;
        public TypeSymbol OperandType { get; }
        public BoundSizeOfExpression(SizeOfExpressionSyntax syntax, TypeSymbol resultType, TypeSymbol operandType)
        : base(syntax)
        {
            Type = resultType; // always int
            OperandType = operandType; 
            ConstantValueOpt = Optional<object>.None;
        }
    }
    internal sealed class BoundCheckedExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.CheckedExpression;
        public BoundExpression Expression { get; }

        public BoundCheckedExpression(CheckedExpressionSyntax syntax, BoundExpression expression)
            : base(syntax)
        {
            Expression = expression;
            Type = expression.Type;
            ConstantValueOpt = expression.ConstantValueOpt;
            HasErrors = expression.HasErrors;
        }
    }

    internal sealed class BoundUncheckedExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.UncheckedExpression;
        public BoundExpression Expression { get; }

        public BoundUncheckedExpression(CheckedExpressionSyntax syntax, BoundExpression expression)
            : base(syntax)
        {
            Expression = expression;
            Type = expression.Type;
            ConstantValueOpt = expression.ConstantValueOpt;
            HasErrors = expression.HasErrors;
        }
    }

    internal sealed class BoundCheckedStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.CheckedStatement;
        public BoundStatement Statement { get; }

        public BoundCheckedStatement(CheckedStatementSyntax syntax, BoundStatement statement)
            : base(syntax)
        {
            Statement = statement;
            HasErrors = statement.HasErrors;
        }
    }

    internal sealed class BoundUncheckedStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.UncheckedStatement;
        public BoundStatement Statement { get; }

        public BoundUncheckedStatement(CheckedStatementSyntax syntax, BoundStatement statement)
            : base(syntax)
        {
            Statement = statement;
            HasErrors = statement.HasErrors;
        }
    }
    internal sealed class BoundLocalExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Local;
        public override bool IsLValue => true;
        public LocalSymbol Local { get; }

        public BoundLocalExpression(SyntaxNode syntax, LocalSymbol local)
        : base(syntax)
        {
            Local = local;
            Type = local.Type is ByRefTypeSymbol br ? br.ElementType : local.Type;

            if (local.IsConst)
                ConstantValueOpt = local.ConstantValueOpt;
        }
    }
    internal sealed class BoundParameterExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Parameter;
        public override bool IsLValue => true;
        public ParameterSymbol Parameter { get; }

        public BoundParameterExpression(SyntaxNode syntax, ParameterSymbol parameter)
            : base(syntax)
        {
            Parameter = parameter;
            Type = parameter.Type is ByRefTypeSymbol br ? br.ElementType : parameter.Type;
        }
    }
    internal sealed class BoundLabelExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.LabelExpression;
        public LabelSymbol Label { get; }

        public BoundLabelExpression(ExpressionSyntax syntax, LabelSymbol label)
            : base(syntax)
        {
            Label = label;

            HasErrors = true;
            Type = new ErrorTypeSymbol("label", containing: null, locations: ImmutableArray<Location>.Empty);
        }
    }
    internal sealed class BoundExpressionStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ExpressionStatement;
        public BoundExpression Expression { get; }

        public BoundExpressionStatement(SyntaxNode syntax, BoundExpression expression)
            : base(syntax)
        {
            Expression = expression;
        }
    }
    internal sealed class BoundThisExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.This;
        public NamedTypeSymbol ContainingType { get; }

        public BoundThisExpression(ExpressionSyntax syntax, NamedTypeSymbol containingType)
            : base(syntax)
        {
            ContainingType = containingType;
            Type = containingType;
        }
    }
    internal sealed class BoundBaseExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Base;

        public NamedTypeSymbol ContainingType { get; }
        public NamedTypeSymbol BaseType { get; }

        public BoundBaseExpression(
            ExpressionSyntax syntax,
            NamedTypeSymbol containingType,
            NamedTypeSymbol baseType)
            : base(syntax)
        {
            ContainingType = containingType;
            BaseType = baseType;
            Type = baseType;
        }
    }
    internal sealed class BoundMemberAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.MemberAccess;
        public BoundExpression? ReceiverOpt { get; }
        public Symbol Member { get; }

        private readonly bool _isLValue;
        public override bool IsLValue => _isLValue;
        public BoundMemberAccessExpression(
            ExpressionSyntax syntax,
            BoundExpression? receiverOpt,
            Symbol member,
            TypeSymbol type,
            bool isLValue,
            Optional<object> constantValueOpt = default,
            bool hasErrors = false)
            : base(syntax)
        {
            ReceiverOpt = receiverOpt;
            Member = member;
            Type = type;
            _isLValue = isLValue;

            ConstantValueOpt = constantValueOpt;

            HasErrors = hasErrors;
            if (receiverOpt?.HasErrors == true)
                HasErrors = true;
        }
    }
    public sealed class BoundIndexerAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.IndexerAccess;
        public BoundExpression Receiver { get; }
        public PropertySymbol Indexer { get; }
        public ImmutableArray<BoundExpression> Arguments { get; }
        public override bool IsLValue { get; }

        public BoundIndexerAccessExpression(
            ExpressionSyntax syntax,
            BoundExpression receiver,
            PropertySymbol indexer,
            ImmutableArray<BoundExpression> arguments,
            bool isLValue,
            bool hasErrors = false)
            : base(syntax)
        {
            Receiver = receiver;
            Indexer = indexer;
            Arguments = arguments.IsDefault ? ImmutableArray<BoundExpression>.Empty : arguments;
            IsLValue = isLValue || indexer.Type is ByRefTypeSymbol;
            HasErrors = hasErrors;
            Type = indexer.Type is ByRefTypeSymbol br ? br.ElementType : indexer.Type;
        }
    }
    internal sealed class BoundReturnStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Return;
        public BoundExpression? Expression { get; }

        public BoundReturnStatement(SyntaxNode syntax, BoundExpression? expression)
            : base(syntax)
        {
            Expression = expression;
        }
    }
    internal sealed class BoundThrowStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Throw;
        public BoundExpression? ExpressionOpt { get; } // null => rethrow
        public BoundThrowStatement(SyntaxNode syntax, BoundExpression? expressionOpt)
            : base(syntax)
        {
            ExpressionOpt = expressionOpt;
            HasErrors = expressionOpt?.HasErrors ?? false;
        }
    }
    internal sealed class BoundLocalDeclarationStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.LocalDeclaration;
        public LocalSymbol Local { get; }
        public BoundExpression? Initializer { get; }
        public BoundLocalDeclarationStatement(SyntaxNode syntax, LocalSymbol local, BoundExpression? initializer)
            : base(syntax)
        {
            Local = local;
            Initializer = initializer;
        }
    }
    internal sealed class BoundEmptyStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.EmptyStatement;

        public BoundEmptyStatement(SyntaxNode syntax)
            : base(syntax)
        {
        }
    }
    internal sealed class BoundBlockStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Block;
        public ImmutableArray<BoundStatement> Statements { get; }

        public BoundBlockStatement(SyntaxNode syntax, ImmutableArray<BoundStatement> statements)
            : base(syntax)
        {
            Statements = statements;
        }
    }

    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundIfStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.If;
        public BoundExpression Condition { get; }
        public BoundStatement Then { get; }
        public BoundStatement? ElseOpt { get; }

        public BoundIfStatement(IfStatementSyntax syntax, BoundExpression condition, BoundStatement thenStatement, BoundStatement? elseOpt)
            : base(syntax)
        {
            Condition = condition;
            Then = thenStatement;
            ElseOpt = elseOpt;

            if (condition.HasErrors || thenStatement.HasErrors || (elseOpt?.HasErrors ?? false))
                HasErrors = true;
        }
    }
    internal sealed class BoundLabelStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.LabelStatement;
        public LabelSymbol Label { get; }

        public BoundLabelStatement(SyntaxNode syntax, LabelSymbol label)
            : base(syntax)
        {
            Label = label;
        }
    }

    internal sealed class BoundGotoStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Goto;
        public LabelSymbol TargetLabel { get; }

        public BoundGotoStatement(SyntaxNode syntax, LabelSymbol targetLabel)
            : base(syntax)
        {
            TargetLabel = targetLabel;
        }
    }

    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundBreakStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Break;
        public LabelSymbol TargetLabel { get; }

        public BoundBreakStatement(BreakStatementSyntax syntax, LabelSymbol targetLabel)
            : base(syntax)
        {
            TargetLabel = targetLabel;
        }
    }

    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundContinueStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Continue;
        public LabelSymbol TargetLabel { get; }

        public BoundContinueStatement(ContinueStatementSyntax syntax, LabelSymbol targetLabel)
            : base(syntax)
        {
            TargetLabel = targetLabel;
        }
    }

    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundDoWhileStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.DoWhile;
        public BoundStatement Body { get; }
        public BoundExpression Condition { get; }

        public LabelSymbol BreakLabel { get; }
        public LabelSymbol ContinueLabel { get; }

        public BoundDoWhileStatement(
            DoStatementSyntax syntax,
            BoundStatement body,
            BoundExpression condition,
            LabelSymbol breakLabel,
            LabelSymbol continueLabel)
            : base(syntax)
        {
            Body = body;
            Condition = condition;
            BreakLabel = breakLabel;
            ContinueLabel = continueLabel;

            if (body.HasErrors || condition.HasErrors)
                HasErrors = true;
        }
    }

    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundWhileStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.While;
        public BoundExpression Condition { get; }
        public BoundStatement Body { get; }
        public LabelSymbol BreakLabel { get; }
        public LabelSymbol ContinueLabel { get; }
        public BoundWhileStatement(
            WhileStatementSyntax syntax,
            BoundExpression condition,
            BoundStatement body,
            LabelSymbol breakLabel,
            LabelSymbol continueLabel)
            : base(syntax)
        {
            Condition = condition;
            Body = body;
            BreakLabel = breakLabel;
            ContinueLabel = continueLabel;

            if (condition.HasErrors || body.HasErrors)
                HasErrors = true;
        }
    }

    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundForStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.For;

        public ImmutableArray<BoundStatement> Initializers { get; }
        public BoundExpression? ConditionOpt { get; } // null == "true"
        public ImmutableArray<BoundStatement> Incrementors { get; }
        public BoundStatement Body { get; }
        public LabelSymbol BreakLabel { get; }
        public LabelSymbol ContinueLabel { get; }
        public BoundForStatement(
            ForStatementSyntax syntax,
            ImmutableArray<BoundStatement> initializers,
            BoundExpression? conditionOpt,
            ImmutableArray<BoundStatement> incrementors,
            BoundStatement body,
            LabelSymbol breakLabel,
            LabelSymbol continueLabel)
            : base(syntax)
        {
            Initializers = initializers;
            ConditionOpt = conditionOpt;
            Incrementors = incrementors;
            Body = body;
            BreakLabel = breakLabel;
            ContinueLabel = continueLabel;

            if (body.HasErrors || (conditionOpt?.HasErrors ?? false) ||
                AnyHasErrors(initializers) || AnyHasErrors(incrementors))
                HasErrors = true;
        }
        private static bool AnyHasErrors(ImmutableArray<BoundStatement> statements)
        {
            for (int i = 0; i < statements.Length; i++)
                if (statements[i].HasErrors)
                    return true;
            return false;
        }
    }
    internal enum BoundForEachEnumeratorKind : byte
    {
        Array,
        String,
        Pattern,
        Interface
    }
    /// <summary> Will disappear after lowering </summary>
    internal sealed class BoundForEachStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ForEach;

        public BoundForEachEnumeratorKind EnumeratorKind { get; }
        public LocalSymbol IterationVariable { get; }
        public BoundExpression Collection { get; }
        public TypeSymbol CollectionType { get; }

        public TypeSymbol EnumeratorType { get; }
        public TypeSymbol ElementType { get; }
        public Conversion CollectionConversion { get; }

        public MethodSymbol? GetEnumeratorMethodOpt { get; }
        public bool GetEnumeratorIsExtensionMethod { get; }
        public PropertySymbol? CurrentPropertyOpt { get; }
        public MethodSymbol? MoveNextMethodOpt { get; }
        public Conversion IterationConversion { get; }

        public BoundStatement Body { get; }
        public LabelSymbol BreakLabel { get; }
        public LabelSymbol ContinueLabel { get; }

        public BoundForEachStatement(
            ForEachStatementSyntax syntax,
            BoundForEachEnumeratorKind enumeratorKind,
            LocalSymbol iterationVariable,
            BoundExpression collection,
            TypeSymbol collectionType,
            TypeSymbol enumeratorType,
            TypeSymbol elementType,
            Conversion collectionConversion,
            MethodSymbol? getEnumeratorMethodOpt,
            bool getEnumeratorIsExtensionMethod,
            PropertySymbol? currentPropertyOpt,
            MethodSymbol? moveNextMethodOpt,
            Conversion iterationConversion,
            BoundStatement body,
            LabelSymbol breakLabel,
            LabelSymbol continueLabel)
            : base(syntax)
        {
            EnumeratorKind = enumeratorKind;
            IterationVariable = iterationVariable;
            Collection = collection;
            CollectionType = collectionType;
            EnumeratorType = enumeratorType;
            ElementType = elementType;
            CollectionConversion = collectionConversion;
            GetEnumeratorMethodOpt = getEnumeratorMethodOpt;
            GetEnumeratorIsExtensionMethod = getEnumeratorIsExtensionMethod;
            CurrentPropertyOpt = currentPropertyOpt;
            MoveNextMethodOpt = moveNextMethodOpt;
            IterationConversion = iterationConversion;
            Body = body;
            BreakLabel = breakLabel;
            ContinueLabel = continueLabel;

            HasErrors =
                collection.HasErrors ||
                body.HasErrors ||
                !collectionConversion.Exists ||
                !iterationConversion.Exists;

            if (enumeratorKind != BoundForEachEnumeratorKind.Array &&
                enumeratorKind != BoundForEachEnumeratorKind.String)
            {
                if (GetEnumeratorMethodOpt is null || CurrentPropertyOpt is null || MoveNextMethodOpt is null)
                    HasErrors = true;
            }
        }
    }
    internal sealed class BoundTryStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.TryStatement;

        public BoundBlockStatement TryBlock { get; }
        public ImmutableArray<BoundCatchBlock> CatchBlocks { get; }
        public BoundBlockStatement? FinallyBlockOpt { get; }

        public BoundTryStatement(
            TryStatementSyntax syntax,
            BoundBlockStatement tryBlock,
            ImmutableArray<BoundCatchBlock> catchBlocks,
            BoundBlockStatement? finallyBlockOpt)
            : base(syntax)
        {
            TryBlock = tryBlock;
            CatchBlocks = catchBlocks.IsDefault ? ImmutableArray<BoundCatchBlock>.Empty : catchBlocks;
            FinallyBlockOpt = finallyBlockOpt;

            HasErrors =
                tryBlock.HasErrors ||
                AnyHasErrors(CatchBlocks) ||
                (finallyBlockOpt?.HasErrors ?? false);
        }

        private static bool AnyHasErrors(ImmutableArray<BoundCatchBlock> catches)
        {
            for (int i = 0; i < catches.Length; i++)
                if (catches[i].HasErrors)
                    return true;
            return false;
        }
    }
    internal sealed class BoundCatchBlock : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.CatchBlock;
        public override SyntaxNode Syntax { get; }
        public TypeSymbol ExceptionType { get; }
        public LocalSymbol? ExceptionLocalOpt { get; }
        public BoundExpression? FilterOpt { get; }
        public BoundBlockStatement Body { get; }
        public BoundCatchBlock(
        CatchClauseSyntax syntax,
        TypeSymbol exceptionType,
        LocalSymbol? exceptionLocalOpt,
        BoundExpression? filterOpt,
        BoundBlockStatement body)
        {
            Syntax = syntax;
            ExceptionType = exceptionType;
            ExceptionLocalOpt = exceptionLocalOpt;
            FilterOpt = filterOpt;
            Body = body;

            HasErrors =
                body.HasErrors ||
                (filterOpt?.HasErrors ?? false) ||
                exceptionType.Kind == SymbolKind.Error;
        }
    }
    internal sealed class BoundMethodBody : BoundNode
    {
        public override BoundNodeKind Kind => BoundNodeKind.MethodBody;
        public override SyntaxNode Syntax { get; }

        public MethodSymbol Method { get; }
        public BoundStatement Body { get; }

        public BoundMethodBody(SyntaxNode syntax, MethodSymbol method, BoundStatement body)
        {
            Syntax = syntax;
            Method = method;
            Body = body;
        }
    }
    internal sealed class BoundLocalFunctionStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.LocalFunctionStatement;
        public LocalFunctionSymbol LocalFunction { get; }
        public BoundStatement Body { get; }

        public BoundLocalFunctionStatement(
            LocalFunctionStatementSyntax syntax,
            LocalFunctionSymbol localFunction,
            BoundStatement body)
            : base(syntax)
        {
            LocalFunction = localFunction;
            Body = body;
            if (body.HasErrors)
                HasErrors = true;
        }
    }
    internal sealed class BoundUnaryExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Unary;
        public bool IsChecked { get; }
        public BoundUnaryOperatorKind OperatorKind { get; }
        public BoundExpression Operand { get; }

        public BoundUnaryExpression(
            SyntaxNode syntax,
            BoundUnaryOperatorKind op,
            TypeSymbol type,
            BoundExpression operand,
            Optional<object> constantValueOpt,
            bool isChecked = false)
            : base(syntax)
        {
            OperatorKind = op;
            IsChecked = isChecked;
            Type = type;
            Operand = operand;
            ConstantValueOpt = constantValueOpt;
            HasErrors = operand.HasErrors;
        }
    }
    internal sealed class BoundBinaryExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Binary;
        public bool IsChecked { get; }
        public BoundBinaryOperatorKind OperatorKind { get; }
        public BoundExpression Left { get; }
        public BoundExpression Right { get; }

        public BoundBinaryExpression(
            SyntaxNode syntax,
            BoundBinaryOperatorKind op,
            TypeSymbol type,
            BoundExpression left,
            BoundExpression right,
            Optional<object> constantValueOpt,
            bool isChecked = false)
            : base(syntax)
        {
            OperatorKind = op;
            Type = type;
            Left = left;
            Right = right;
            ConstantValueOpt = constantValueOpt;
            IsChecked = isChecked;
            HasErrors = left.HasErrors || right.HasErrors;
        }
    }
    internal sealed class BoundConditionalExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Conditional;

        public BoundExpression Condition { get; }
        public BoundExpression WhenTrue { get; }
        public BoundExpression WhenFalse { get; }

        public BoundConditionalExpression(
            ConditionalExpressionSyntax syntax,
            TypeSymbol type,
            BoundExpression condition,
            BoundExpression whenTrue,
            BoundExpression whenFalse,
            Optional<object> constantValueOpt)
            : base(syntax)
        {
            Type = type;
            Condition = condition;
            WhenTrue = whenTrue;
            WhenFalse = whenFalse;
            ConstantValueOpt = constantValueOpt;
            HasErrors = condition.HasErrors || whenTrue.HasErrors || whenFalse.HasErrors;
        }
    }
    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundIncrementDecrementExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.IncrementDecrement;
        public bool IsIncrement { get; }
        public bool IsPostfix { get; }
        public bool IsChecked { get; }
        public BoundExpression Target { get; }
        public BoundExpression Read { get; }
        public BoundExpression Value { get; }
        public MethodSymbol? OperatorMethodOpt { get; }
        public bool UsesDirectOperator { get; }

        public BoundIncrementDecrementExpression(
            SyntaxNode syntax,
            BoundExpression target,
            BoundExpression read,
            BoundExpression value,
            bool isIncrement,
            bool isPostfix,
            MethodSymbol? operatorMethodOpt = null,
            bool usesDirectOperator = false,
            bool isChecked = false)
            : base(syntax)
        {
            Target = target;
            Read = read;
            Value = value;
            OperatorMethodOpt = operatorMethodOpt;
            UsesDirectOperator = usesDirectOperator;
            IsIncrement = isIncrement;
            IsPostfix = isPostfix;
            IsChecked = isChecked;
            Type = target.Type;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = target.HasErrors || read.HasErrors || value.HasErrors;
        }
    }
    /// <summary> Will dissapear after lowering </summary>
    internal sealed class BoundCompoundAssignmentExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.CompoundAssignment;
        public bool IsChecked { get; }
        public BoundExpression Left { get; }
        public BoundBinaryOperatorKind OperatorKind { get; }
        public BoundExpression Value { get; }
        public MethodSymbol? OperatorMethodOpt { get; }
        public bool UsesDirectOperator { get; }
        public BoundCompoundAssignmentExpression(
            SyntaxNode syntax,
            BoundExpression left,
            BoundBinaryOperatorKind operatorKind,
            BoundExpression value,
            MethodSymbol? operatorMethodOpt = null,
            bool usesDirectOperator = false,
            bool isChecked = false)
            : base(syntax)
        {
            Left = left;
            OperatorKind = operatorKind;
            Value = value;

            Type = left.Type;
            ConstantValueOpt = Optional<object>.None;
            IsChecked = isChecked;
            HasErrors = left.HasErrors || value.HasErrors;
            OperatorMethodOpt = operatorMethodOpt;
            UsesDirectOperator = usesDirectOperator;
        }
    }
    internal sealed class BoundNullCoalescingAssignmentExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.NullCoalescingAssignment;
        public BoundExpression Left { get; }
        public BoundExpression Value { get; }

        public BoundNullCoalescingAssignmentExpression(SyntaxNode syntax, BoundExpression left, BoundExpression value)
            : base(syntax)
        {
            Left = left;
            Value = value;

            Type = left.Type;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = left.HasErrors || value.HasErrors;
        }
    }
    internal sealed class BoundAssignmentExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Assignment;
        public BoundExpression Left { get; }
        public BoundExpression Right { get; }
        public BoundAssignmentExpression(SyntaxNode syntax, BoundExpression left, BoundExpression right)
            : base(syntax)
        {
            Left = left;
            Right = right;

            Type = left.Type;
            HasErrors = left.HasErrors || right.HasErrors;
        }
    }
    internal sealed class BoundCallExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Call;

        public BoundExpression? ReceiverOpt { get; }
        public MethodSymbol Method { get; }
        public ImmutableArray<BoundExpression> Arguments { get; }
        public override bool IsLValue => Method.ReturnType is ByRefTypeSymbol;
        public BoundCallExpression(
            SyntaxNode syntax,
            BoundExpression? receiverOpt,
            MethodSymbol method,
            ImmutableArray<BoundExpression> arguments)
            : base(syntax)
        {
            ReceiverOpt = receiverOpt;
            Method = method;
            Arguments = arguments;
            Type = method.ReturnType is ByRefTypeSymbol br ? br.ElementType : method.ReturnType;

            bool hasArgErrors = false;
            for (int i = 0; i < arguments.Length; i++)
                hasArgErrors |= arguments[i].HasErrors;

            HasErrors = (receiverOpt?.HasErrors ?? false) || hasArgErrors;
        }
    }
    internal sealed class BoundObjectCreationExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ObjectCreation;
        public MethodSymbol? ConstructorOpt { get; }
        public ImmutableArray<BoundExpression> Arguments { get; }
        public BoundObjectCreationExpression(
            SyntaxNode syntax,
            NamedTypeSymbol type, 
            MethodSymbol? constructorOpt,
            ImmutableArray<BoundExpression> arguments, 
            bool hasErrors = false) 
            : base(syntax)
        {
            Type = type;
            ConstructorOpt = constructorOpt;
            Arguments = arguments;

            bool hasArgErrors = false;
            for (int i = 0; i < arguments.Length; i++)
                hasArgErrors |= arguments[i].HasErrors;

            HasErrors = hasErrors || hasArgErrors || (constructorOpt is null && type.TypeKind != TypeKind.Struct);
            ConstantValueOpt = Optional<object>.None;
        }
    }
    internal sealed class BoundUnboundImplicitObjectCreationExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.UnboundImplicitObjectCreation;
        public ImmutableArray<BoundExpression> Arguments { get; }

        public BoundUnboundImplicitObjectCreationExpression(
            ImplicitObjectCreationExpressionSyntax syntax,
            ImmutableArray<BoundExpression> arguments)
            : base(syntax)
        {
            Arguments = arguments;
            Type = new ErrorTypeSymbol("<unbound>", containing: null, ImmutableArray<Location>.Empty);
            bool hasArgErrors = false;
            for (int i = 0; i < arguments.Length; i++)
                hasArgErrors |= arguments[i].HasErrors;
            HasErrors = hasArgErrors;
            ConstantValueOpt = Optional<object>.None;
        }
    }
    internal enum BoundFixedInitializerKind : byte
    {
        AddressOf,    // fixed (int* p = &x)
        Array,               // fixed (int* p = arr)
        String,              // fixed (char* p = str)
        GetPinnableReference // fixed (int* p = span)
    }
    internal sealed class BoundFixedInitializerExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.FixedInitializer;

        public BoundFixedInitializerKind InitializerKind { get; }
        public BoundExpression Expression { get; }
        public MethodSymbol? GetPinnableReferenceMethodOpt { get; }

        /// <summary> Pointed element type before final pointer conversion. </summary>
        public TypeSymbol ElementType { get; }

        /// <summary> Conversion from ElementType* to declared pointer type. </summary>
        public Conversion ElementPointerConversion { get; }

        public BoundFixedInitializerExpression(
            SyntaxNode syntax,
            PointerTypeSymbol declaredPointerType,
            BoundFixedInitializerKind initializerKind,
            BoundExpression expression,
            TypeSymbol elementType,
            Conversion elementPointerConversion,
            MethodSymbol? getPinnableReferenceMethodOpt = null)
            : base(syntax)
        {
            Type = declaredPointerType;
            InitializerKind = initializerKind;
            Expression = expression;
            ElementType = elementType;
            ElementPointerConversion = elementPointerConversion;
            GetPinnableReferenceMethodOpt = getPinnableReferenceMethodOpt;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = expression.HasErrors || !elementPointerConversion.Exists;
        }
    }

    internal sealed class BoundFixedStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.FixedStatement;

        public ImmutableArray<BoundLocalDeclarationStatement> Declarations { get; }
        public BoundStatement Body { get; }

        public BoundFixedStatement(
            FixedStatementSyntax syntax,
            ImmutableArray<BoundLocalDeclarationStatement> declarations,
            BoundStatement body)
            : base(syntax)
        {
            Declarations = declarations;
            Body = body;

            if (body.HasErrors)
                HasErrors = true;

            for (int i = 0; i < declarations.Length; i++)
            {
                if (declarations[i].HasErrors)
                {
                    HasErrors = true;
                    break;
                }
            }
        }
    }
    // =rewriter nodes=
    internal sealed class BoundSequenceExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Sequence;

        public ImmutableArray<LocalSymbol> Locals { get; }
        public ImmutableArray<BoundStatement> SideEffects { get; } // expression statements only
        public BoundExpression Value { get; }

        public BoundSequenceExpression(
            SyntaxNode syntax,
            ImmutableArray<LocalSymbol> locals,
            ImmutableArray<BoundStatement> sideEffects,
            BoundExpression value)
            : base(syntax)
        {
            Locals = locals;
            SideEffects = sideEffects;
            Value = value;

            Type = value.Type;
            ConstantValueOpt = Optional<object>.None;

            if (value.HasErrors)
                SetHasErrors();
            else
            {
                for (int i = 0; i < sideEffects.Length; i++)
                {
                    if (sideEffects[i].HasErrors)
                    {
                        SetHasErrors();
                        break;
                    }
                }
            }
        }
    }

    internal sealed class BoundConditionalGotoStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.ConditionalGoto;

        public BoundExpression Condition { get; }
        public LabelSymbol TargetLabel { get; }
        public bool JumpIfTrue { get; }

        public BoundConditionalGotoStatement(
            SyntaxNode syntax,
            BoundExpression condition,
            LabelSymbol targetLabel,
            bool jumpIfTrue)
            : base(syntax)
        {
            Condition = condition;
            TargetLabel = targetLabel;
            JumpIfTrue = jumpIfTrue;

            if (condition.HasErrors)
                SetHasErrors();
        }
    }
    internal sealed class BoundIsPatternExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.IsPatternExpression;

        public BoundExpression Operand { get; }
        public TypeSymbol PatternType { get; }
        public LocalSymbol? DeclaredLocalOpt { get; }
        public bool IsDiscard { get; }

        public BoundIsPatternExpression(
            SyntaxNode syntax,
            BoundExpression operand,
            TypeSymbol patternType,
            LocalSymbol? declaredLocalOpt,
            TypeSymbol boolType,
            bool isDiscard)
            : base(syntax)
        {
            Operand = operand;
            PatternType = patternType;
            DeclaredLocalOpt = declaredLocalOpt;
            IsDiscard = isDiscard;
            Type = boolType;
        }

    }
}
