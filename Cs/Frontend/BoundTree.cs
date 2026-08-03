using System;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{

    /// <summary>Base node produced by semantic binding</summary>
    public abstract class BoundNode
    {
        public abstract BoundNodeKind Kind { get; }
        public bool HasErrors { get; protected set; }
        public abstract SyntaxNode Syntax { get; }
    }
    /// <summary>Base node for a bound expression with a resolved type</summary>
    public abstract class BoundExpression : BoundNode
    {
        public TypeSymbol Type { get; protected set; } = null!;
        public Optional<object> ConstantValueOpt { get; protected set; } = Optional<object>.None;
        public virtual bool IsLValue => false;
        public override SyntaxNode Syntax { get; }
        internal void SetHasErrors() => HasErrors = true;
        protected BoundExpression(SyntaxNode syntax) => Syntax = syntax;
    }

    /// <summary>Base node for an executable bound statement</summary>
    public abstract class BoundStatement : BoundNode
    {
        public override SyntaxNode Syntax { get; }
        protected BoundStatement(SyntaxNode syntax) => Syntax = syntax;
        internal void SetHasErrors() => HasErrors = true;
    }
    /// <summary>Represents an expression that failed to bind</summary>
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
    /// <summary>Represents a statement that failed to bind</summary>
    internal sealed class BoundBadStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.BadStatement;
        public BoundBadStatement(StatementSyntax syntax) : base(syntax) => HasErrors = true;
    }
    /// <summary>Represents the bound contents of a source file</summary>
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
    /// <summary>Bound statement sequence for one source construct</summary>
    /// <remarks>Removed during lowering</remarks>
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
    /// <summary>Represents a literal value and its compile-time constant</summary>
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
    /// <summary>Represents an exception throw used as an expression</summary>
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
    /// <summary>Lambda awaiting a target delegate type</summary>
    internal sealed class BoundUnboundLambdaExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.UnboundLambda;

        public BoundUnboundLambdaExpression(ExpressionSyntax syntax)
            : base(syntax)
        {
            Type = UnboundLambdaTypeSymbol.Instance;
            ConstantValueOpt = Optional<object>.None;
        }
    }

    /// <summary>Represents an unresolved method overload set and optional receiver</summary>
    internal sealed class BoundMethodGroupExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.MethodGroup;

        public string Name { get; }
        public BoundExpression? ReceiverOpt { get; }
        public ImmutableArray<MethodSymbol> Methods { get; }

        public BoundMethodGroupExpression(
            ExpressionSyntax syntax,
            string name,
            BoundExpression? receiverOpt,
            ImmutableArray<MethodSymbol> methods,
            bool hasErrors = false)
            : base(syntax)
        {
            Name = name;
            ReceiverOpt = receiverOpt;
            Methods = methods.IsDefault ? ImmutableArray<MethodSymbol>.Empty : methods;
            Type = UnboundMethodGroupTypeSymbol.Instance;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = hasErrors || (receiverOpt?.HasErrors ?? false) || Methods.IsDefaultOrEmpty;
        }
    }

    /// <summary>Represents a lambda bound to a delegate invocation signature</summary>
    internal sealed class BoundLambdaExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Lambda;

        public MethodSymbol Method { get; }
        public MethodSymbol InvokeMethod { get; }
        public BoundStatement Body { get; }
        public BoundExpression? TargetOpt { get; }
        public bool IsStatic { get; }
        public bool IsAsync { get; }

        public BoundLambdaExpression(
            ExpressionSyntax syntax,
            NamedTypeSymbol delegateType,
            MethodSymbol method,
            MethodSymbol invokeMethod,
            BoundStatement body,
            bool isStatic,
            bool isAsync,
            BoundExpression? targetOpt = null)
            : base(syntax)
        {
            Type = delegateType;
            Method = method;
            InvokeMethod = invokeMethod;
            Body = body;
            TargetOpt = targetOpt;
            IsStatic = isStatic;
            IsAsync = isAsync;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = body.HasErrors || (targetOpt?.HasErrors ?? false);
        }
    }

    /// <summary>Bound allocation of storage for one captured value</summary>
    internal sealed class BoundClosureCellCreationExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ClosureCellCreation;
        public TypeSymbol ValueType { get; }
        public BoundExpression InitialValue { get; }

        public BoundClosureCellCreationExpression(SyntaxNode syntax, NamedTypeSymbol objectType, TypeSymbol valueType, BoundExpression initialValue)
            : base(syntax)
        {
            Type = objectType;
            ValueType = valueType;
            InitialValue = initialValue;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = initialValue.HasErrors;
        }
    }

    /// <summary>Bound closure object creation from captured value cells</summary>
    internal sealed class BoundClosureCreationExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ClosureCreation;
        public ImmutableArray<BoundExpression> Cells { get; }

        public BoundClosureCreationExpression(SyntaxNode syntax, NamedTypeSymbol objectType, ImmutableArray<BoundExpression> cells)
            : base(syntax)
        {
            Type = objectType;
            Cells = cells.IsDefault ? ImmutableArray<BoundExpression>.Empty : cells;
            ConstantValueOpt = Optional<object>.None;

            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i].HasErrors)
                {
                    HasErrors = true;
                    break;
                }
            }
        }
    }

    /// <summary>Bound reference to a captured value cell in a closure object</summary>
    internal sealed class BoundClosureSlotExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ClosureSlot;
        public BoundExpression Closure { get; }
        public int SlotIndex { get; }

        public BoundClosureSlotExpression(SyntaxNode syntax, NamedTypeSymbol objectType, BoundExpression closure, int slotIndex)
            : base(syntax)
        {
            Type = objectType;
            Closure = closure;
            SlotIndex = slotIndex;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = closure.HasErrors;
        }
    }

    /// <summary>Bound access to the value stored in a closure cell</summary>
    internal sealed class BoundClosureAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ClosureAccess;
        public override bool IsLValue => true;
        public BoundExpression Cell { get; }

        public BoundClosureAccessExpression(SyntaxNode syntax, TypeSymbol valueType, BoundExpression cell)
            : base(syntax)
        {
            Type = valueType is ByRefTypeSymbol br ? br.ElementType : valueType;
            Cell = cell;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = cell.HasErrors;
        }
    }

    /// <summary>Represents a tuple value with resolved element names and types</summary>
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

            ConstantValueOpt = Optional<object>.None;

            for (int i = 0; i < elements.Length; i++)
                if (elements[i].HasErrors)
                    HasErrors = true;
        }
    }
    /// <summary>Represents the ordered elements of an array initializer</summary>
    internal sealed class BoundArrayInitializerExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.ArrayInitializer;
        public ImmutableArray<BoundExpression> Elements { get; }

        public BoundArrayInitializerExpression(
            SyntaxNode syntax, TypeSymbol elementType, ImmutableArray<BoundExpression> elements)
            : base(syntax)
        {
            Type = elementType;
            Elements = elements;

            for (int i = 0; i < elements.Length; i++)
                if (elements[i].HasErrors)
                    HasErrors = true;
        }
    }
    /// <summary>Bound array creation with resolved dimensions and optional initializer</summary>
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
    /// <summary>Bound array element access by one or more indices</summary>
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
    /// <summary>Bound inline array element access</summary>
    internal sealed class BoundInlineArrayElementAccessExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.InlineArrayElementAccess;
        public BoundExpression Receiver { get; }
        public FieldSymbol ElementField { get; }
        public BoundExpression Index { get; }
        public int Length { get; }
        private readonly bool _isLValue;
        public override bool IsLValue => _isLValue;

        public BoundInlineArrayElementAccessExpression(
            SyntaxNode syntax,
            BoundExpression receiver,
            FieldSymbol elementField,
            BoundExpression index,
            int length,
            bool isLValue)
            : base(syntax)
        {
            Receiver = receiver;
            ElementField = elementField;
            Index = index;
            Length = length;
            _isLValue = isLValue;
            Type = elementField.Type;
            HasErrors = receiver.HasErrors || index.HasErrors;
        }
    }

    /// <summary>Bound stack allocation of a contiguous element buffer</summary>
    internal sealed class BoundStackAllocArrayCreationExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.StackAllocArrayCreation;
        public TypeSymbol ElementType { get; }
        /// <summary>Element count converted to a 32-bit integer</summary>
        public BoundExpression Count { get; }
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
    /// <summary>Bound reference to immutable element data in static storage</summary>
    internal sealed class BoundStaticDataExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.StaticData;

        public TypeSymbol ElementType { get; }
        public ImmutableArray<BoundExpression> Elements { get; }

        public BoundStaticDataExpression(
            SyntaxNode syntax,
            PointerTypeSymbol type,
            TypeSymbol elementType,
            ImmutableArray<BoundExpression> elements)
            : base(syntax)
        {
            Type = type;
            ElementType = elementType;
            Elements = elements.IsDefault ? ImmutableArray<BoundExpression>.Empty : elements;
        }
    }
    /// <summary>Bound collection elements for span construction</summary>
    internal sealed class BoundSpanCollectionExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.SpanCollection;
        public TypeSymbol ElementType { get; }
        public ImmutableArray<BoundExpression> Elements { get; }

        public BoundSpanCollectionExpression(
            CollectionExpressionSyntax syntax,
            NamedTypeSymbol spanType,
            TypeSymbol elementType,
            ImmutableArray<BoundExpression> elements)
            : base(syntax)
        {
            Type = spanType;
            ElementType = elementType;
            Elements = elements.IsDefault ? ImmutableArray<BoundExpression>.Empty : elements;
            ConstantValueOpt = Optional<object>.None;

            for (int i = 0; i < Elements.Length; i++)
            {
                if (Elements[i].HasErrors)
                {
                    HasErrors = true;
                    break;
                }
            }
        }
    }
    /// <summary>Bound managed reference to an assignable operand</summary>
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
    /// <summary>Bound unmanaged address of an addressable operand</summary>
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
    /// <summary>Bound function pointer load for a resolved method</summary>
    internal sealed class BoundFunctionPointerLoadExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.FunctionPointerLoad;
        public MethodSymbol Method { get; }

        public BoundFunctionPointerLoadExpression(
            PrefixUnaryExpressionSyntax syntax,
            FunctionPointerTypeSymbol type,
            MethodSymbol method)
            : base(syntax)
        {
            Type = type;
            Method = method;
        }
    }
    /// <summary>Method group awaiting a target function pointer type</summary>
    internal sealed class BoundFunctionPointerMethodGroupExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.FunctionPointerMethodGroup;
        public BoundMethodGroupExpression MethodGroup { get; }

        public BoundFunctionPointerMethodGroupExpression(
            PrefixUnaryExpressionSyntax syntax,
            BoundMethodGroupExpression methodGroup)
            : base(syntax)
        {
            MethodGroup = methodGroup;
            Type = UnboundMethodGroupTypeSymbol.Instance;
            HasErrors = methodGroup.HasErrors;
        }
    }

    /// <summary>Bound invocation through a resolved function pointer signature</summary>
    internal sealed class BoundFunctionPointerInvocationExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.FunctionPointerInvocation;
        public BoundExpression InvokedExpression { get; }
        public ImmutableArray<BoundExpression> Arguments { get; }
        public FunctionPointerTypeSymbol FunctionPointerType { get; }
        public override bool IsLValue => FunctionPointerType.ReturnRefKind != FunctionPointerRefKind.None;

        public BoundFunctionPointerInvocationExpression(
            InvocationExpressionSyntax syntax,
            BoundExpression invokedExpression,
            FunctionPointerTypeSymbol functionPointerType,
            ImmutableArray<BoundExpression> arguments)
            : base(syntax)
        {
            InvokedExpression = invokedExpression;
            FunctionPointerType = functionPointerType;
            Arguments = arguments;
            Type = functionPointerType.ReturnType;
            HasErrors = invokedExpression.HasErrors;
            for (int i = 0; i < arguments.Length; i++)
                HasErrors |= arguments[i].HasErrors;
        }
    }

    /// <summary>Bound access to the value referenced by a pointer</summary>
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

    /// <summary>Bound pointer element access at a computed index</summary>
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
    /// <summary>Bound conversion with a resolved conversion classification</summary>
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
    /// <summary>Bound safe cast that yields null for an incompatible operand</summary>
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
    /// <summary>Bound runtime type lookup for a resolved type</summary>
    internal sealed class BoundTypeOfExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.TypeOfExpression;
        public TypeSymbol OperandType { get; }
        public BoundTypeOfExpression(TypeOfExpressionSyntax syntax, TypeSymbol resultType, TypeSymbol operandType)
            : base(syntax)
        {
            Type = resultType;
            OperandType = operandType;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = resultType.Kind == SymbolKind.Error || operandType.Kind == SymbolKind.Error;
        }
    }
    /// <summary>Bound storage size query for a resolved type</summary>
    internal sealed class BoundSizeOfExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.SizeOfExpression;
        public TypeSymbol OperandType { get; }
        public BoundSizeOfExpression(SizeOfExpressionSyntax syntax, TypeSymbol resultType, TypeSymbol operandType)
        : base(syntax)
        {
            Type = resultType;
            OperandType = operandType;
            ConstantValueOpt = Optional<object>.None;
        }
    }
    /// <summary>Bound expression with checked arithmetic semantics</summary>
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

    /// <summary>Bound expression with unchecked arithmetic semantics</summary>
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

    /// <summary>Bound statement with checked arithmetic semantics</summary>
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

    /// <summary>Bound statement with unchecked arithmetic semantics</summary>
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
    /// <summary>Bound reference to a local variable</summary>
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
    /// <summary>Bound reference to a method or lambda parameter</summary>
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
    /// <summary>Represents a label value used by supported low-level constructs</summary>
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
    /// <summary>Bound expression evaluated only for side effects</summary>
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
    /// <summary>Bound reference to the current instance</summary>
    internal sealed class BoundThisExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.This;
        public NamedTypeSymbol ContainingType { get; }
        public override bool IsLValue { get; }
        public BoundThisExpression(ExpressionSyntax syntax, NamedTypeSymbol containingType, bool isLValue = false)
            : base(syntax)
        {
            ContainingType = containingType;
            Type = containingType;
            IsLValue = isLValue;
        }
    }
    /// <summary>Bound base-typed reference to the current instance</summary>
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
    /// <summary>Bound field or property access through an optional receiver</summary>
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
    /// <summary>Bound indexer access through its receiver and arguments</summary>
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
    /// <summary>Identifies the control-flow form of a yield statement</summary>
    internal enum BoundYieldStatementKind : byte { Return, Break }
    /// <summary>Represents iterator yield return or yield break</summary>
    internal sealed class BoundYieldStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Yield;

        public BoundYieldStatementKind YieldKind { get; }
        public BoundExpression? ExpressionOpt { get; }
        public TypeSymbol? ElementTypeOpt { get; }

        public BoundYieldStatement(
            YieldStatementSyntax syntax,
            BoundYieldStatementKind yieldKind,
            BoundExpression? expressionOpt,
            TypeSymbol? elementTypeOpt)
            : base(syntax)
        {
            YieldKind = yieldKind;
            ExpressionOpt = expressionOpt;
            ElementTypeOpt = elementTypeOpt;
            HasErrors = expressionOpt?.HasErrors ?? false;
        }
    }
    /// <summary>Bound return with an optional value</summary>
    internal sealed class BoundReturnStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Return;
        public BoundExpression? Expression { get; }

        public BoundReturnStatement(SyntaxNode syntax, BoundExpression? expression)
            : base(syntax)
        {
            Expression = expression;
            HasErrors = expression?.HasErrors ?? false;
        }
    }
    /// <summary>Bound throw or rethrow</summary>
    internal sealed class BoundThrowStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.Throw;
        /// <summary>Thrown expression or null for a rethrow</summary>
        public BoundExpression? ExpressionOpt { get; }
        public BoundThrowStatement(SyntaxNode syntax, BoundExpression? expressionOpt)
            : base(syntax)
        {
            ExpressionOpt = expressionOpt;
            HasErrors = expressionOpt?.HasErrors ?? false;
        }
    }
    /// <summary>Bound local declaration with an optional initializer</summary>
    internal sealed class BoundLocalDeclarationStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.LocalDeclaration;
        public LocalSymbol Local { get; }
        public BoundExpression? Initializer { get; }
        public bool IsUsing { get; }
        public BoundLocalDeclarationStatement(SyntaxNode syntax, LocalSymbol local, BoundExpression? initializer, bool isUsing = false)
            : base(syntax)
        {
            Local = local;
            Initializer = initializer;
            IsUsing = isUsing;
            HasErrors = initializer?.HasErrors ?? false;
        }
    }
    /// <summary>Bound resource lifetime with resolved disposal</summary>
    internal sealed class BoundUsingStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.UsingStatement;
        public ImmutableArray<BoundLocalDeclarationStatement> Declarations { get; }
        public BoundExpression? ExpressionOpt { get; }
        public BoundStatement Body { get; }
        public BoundUsingStatement(
            UsingStatementSyntax syntax,
            ImmutableArray<BoundLocalDeclarationStatement> declarations,
            BoundExpression? expressionOpt,
            BoundStatement body)
            : base(syntax)
        {
            Declarations = declarations.IsDefault
                ? ImmutableArray<BoundLocalDeclarationStatement>.Empty
                : declarations;
            ExpressionOpt = expressionOpt;
            Body = body;
            HasErrors = expressionOpt?.HasErrors ?? false;
            if (body.HasErrors)
                HasErrors = true;
            for (int i = 0; i < Declarations.Length; i++)
            {
                if (Declarations[i].HasErrors)
                    HasErrors = true;
            }
        }
    }
    /// <summary>Represents a statement with no runtime effect</summary>
    internal sealed class BoundEmptyStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.EmptyStatement;

        public BoundEmptyStatement(SyntaxNode syntax)
            : base(syntax)
        {
        }
    }
    /// <summary>Bound executable statement block</summary>
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

    /// <summary>Bound conditional statement with an optional alternative branch</summary>
    /// <remarks>Removed during lowering</remarks>
    internal sealed class BoundIfStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.If;
        public BoundExpression Condition { get; }
        public BoundStatement Then { get; }
        public BoundStatement? ElseOpt { get; }

        public BoundIfStatement(SyntaxNode syntax, BoundExpression condition, BoundStatement thenStatement, BoundStatement? elseOpt)
            : base(syntax)
        {
            Condition = condition;
            Then = thenStatement;
            ElseOpt = elseOpt;

            if (condition.HasErrors || thenStatement.HasErrors || (elseOpt?.HasErrors ?? false))
                HasErrors = true;
        }
    }
    /// <summary>Bound control-flow target</summary>
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

    /// <summary>Bound unconditional branch to a label</summary>
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

    /// <summary>Bound branch to the enclosing break target</summary>
    /// <remarks>Removed during lowering</remarks>
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

    /// <summary>Bound branch to the enclosing continue target</summary>
    /// <remarks>Removed during lowering</remarks>
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

    /// <summary>Bound post-test loop</summary>
    /// <remarks>Removed during lowering</remarks>
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

    /// <summary>Bound pre-test loop</summary>
    /// <remarks>Removed during lowering</remarks>
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

    /// <summary>Represents initializer, condition, increment, and body phases of a loop</summary>
    /// <remarks>Removed during lowering</remarks>
    internal sealed class BoundForStatement : BoundStatement
    {
        public override BoundNodeKind Kind => BoundNodeKind.For;

        public ImmutableArray<BoundStatement> Initializers { get; }
        /// <summary>Loop condition or null for an unconditional loop</summary>
        public BoundExpression? ConditionOpt { get; }
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
    /// <summary>Identifies the iteration strategy selected for a foreach statement</summary>
    internal enum BoundForEachEnumeratorKind : byte
    {
        Array,
        String,
        Span,
        Pattern,
        Interface
    }
    /// <summary>Bound collection iteration with a resolved enumeration strategy</summary>
    /// <remarks>Removed during lowering</remarks>
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
                enumeratorKind != BoundForEachEnumeratorKind.String &&
                enumeratorKind != BoundForEachEnumeratorKind.Span)
            {
                if (GetEnumeratorMethodOpt is null || CurrentPropertyOpt is null || MoveNextMethodOpt is null)
                    HasErrors = true;
            }
        }
    }
    /// <summary>Bound protected region with catch and optional finally handlers</summary>
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
    /// <summary>Represents one exception handler with an optional filter and local</summary>
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
    /// <summary>Bound executable body associated with a method symbol</summary>
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
    /// <summary>Bound local function declaration and body</summary>
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
    /// <summary>Bound unary operation with a resolved operator</summary>
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
    /// <summary>Bound binary operation with a resolved operator</summary>
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
    /// <summary>Bound conditional expression with two result branches</summary>
    internal sealed class BoundConditionalExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Conditional;

        public BoundExpression Condition { get; }
        public BoundExpression WhenTrue { get; }
        public BoundExpression WhenFalse { get; }

        public BoundConditionalExpression(
            SyntaxNode syntax,
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
    /// <summary>Represents the read, update, and result phases of increment or decrement</summary>
    /// <remarks>Removed during lowering</remarks>
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
    /// <summary>Represents a binary operation followed by assignment to the left operand</summary>
    /// <remarks>Removed during lowering</remarks>
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
    /// <summary>Bound null-coalescing assignment</summary>
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
    /// <summary>Bound assignment to an assignable operand</summary>
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
    /// <summary>Bound invocation of a resolved method</summary>
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
    /// <summary>Bound object creation through a resolved constructor</summary>
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
    /// <summary>Implicit object creation awaiting a target type</summary>
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
    /// <summary>Identifies a value or spread element in a collection expression</summary>
    internal enum BoundCollectionElementKind : byte
    {
        Expression,
        Spread,
    }
    /// <summary>Pairs a bound collection element with its source syntax and form</summary>
    internal readonly struct BoundCollectionElement
    {
        public BoundCollectionElementKind Kind { get; }
        public CollectionElementSyntax Syntax { get; }
        public BoundExpression Expression { get; }

        public BoundCollectionElement(
            BoundCollectionElementKind kind,
            CollectionElementSyntax syntax,
            BoundExpression expression)
        {
            Kind = kind;
            Syntax = syntax;
            Expression = expression;
        }
    }
    /// <summary>Collection elements awaiting a target collection type</summary>
    internal sealed class BoundUnboundCollectionExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.UnboundCollectionExpression;
        public ImmutableArray<BoundCollectionElement> Elements { get; }

        public BoundUnboundCollectionExpression(
            CollectionExpressionSyntax syntax,
            ImmutableArray<BoundCollectionElement> elements)
            : base(syntax)
        {
            Elements = elements.IsDefault ? ImmutableArray<BoundCollectionElement>.Empty : elements;
            Type = new ErrorTypeSymbol("<unbound collection>", containing: null, ImmutableArray<Location>.Empty);
            bool hasErrors = false;
            for (int i = 0; i < Elements.Length; i++)
                hasErrors |= Elements[i].Expression.HasErrors;
            HasErrors = hasErrors;
            ConstantValueOpt = Optional<object>.None;
        }
    }
    /// <summary>Identifies the source form used to initialize a fixed pointer</summary>
    internal enum BoundFixedInitializerKind : byte
    {
        /// <summary>Addressable value</summary>
        AddressOf,
        /// <summary>Array storage</summary>
        Array,
        /// <summary>String data</summary>
        String,
        /// <summary>Pinnable-reference pattern</summary>
        GetPinnableReference
    }
    /// <summary>Bound fixed initializer for a pinned pointer</summary>
    internal sealed class BoundFixedInitializerExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.FixedInitializer;

        public BoundFixedInitializerKind InitializerKind { get; }
        public BoundExpression Expression { get; }
        public MethodSymbol? GetPinnableReferenceMethodOpt { get; }

        /// <summary>Element type before conversion to the declared pointer type</summary>
        public TypeSymbol ElementType { get; }

        /// <summary>Conversion from the element pointer to the declared pointer type</summary>
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

    /// <summary>Bound fixed statement with pinned local resources</summary>
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

    /// <summary>Bound sequence of side effects and a final value</summary>
    internal sealed class BoundSequenceExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.Sequence;

        public ImmutableArray<LocalSymbol> Locals { get; }
        /// <summary>Ordered expression statements evaluated before the final value</summary>
        public ImmutableArray<BoundStatement> SideEffects { get; }
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

    /// <summary>Bound conditional branch to a label</summary>
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
    /// <summary>Identifies the bound form of an is-pattern test</summary>
    internal enum BoundIsPatternKind : byte
    {
        Type,
        Null,
        Constant,
    }
    /// <summary>Bound test against a resolved type, null, or constant pattern</summary>
    internal sealed class BoundIsPatternExpression : BoundExpression
    {
        public override BoundNodeKind Kind => BoundNodeKind.IsPatternExpression;

        public BoundExpression Operand { get; }
        public BoundIsPatternKind PatternKind { get; }
        public TypeSymbol? PatternTypeOpt { get; }
        public BoundExpression? ConstantOpt { get; }
        public TypeSymbol? ComparisonTypeOpt { get; }
        public LocalSymbol? DeclaredLocalOpt { get; }
        public bool IsDiscard { get; }
        public bool IsNegated { get; }
        public BoundIsPatternExpression(
            SyntaxNode syntax,
            BoundExpression operand,
            TypeSymbol boolType,
            BoundIsPatternKind patternKind,
            TypeSymbol? patternTypeOpt = null,
            BoundExpression? constantOpt = null,
            TypeSymbol? comparisonTypeOpt = null,
            LocalSymbol? declaredLocalOpt = null,
            bool isDiscard = false,
            bool isNegated = false)
            : base(syntax)
        {
            Operand = operand;
            PatternKind = patternKind;
            PatternTypeOpt = patternTypeOpt;
            ConstantOpt = constantOpt;
            ComparisonTypeOpt = comparisonTypeOpt;
            DeclaredLocalOpt = declaredLocalOpt;
            IsDiscard = isDiscard;
            IsNegated = isNegated;

            Type = boolType;
            ConstantValueOpt = Optional<object>.None;
            HasErrors = operand.HasErrors || (constantOpt?.HasErrors ?? false);
        }

    }

}
