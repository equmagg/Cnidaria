using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal static class GenTreeDumper
    {
        private static bool TryAppendOperand(StringBuilder sb, GenTree node, int index)
        {
            if ((uint)index < (uint)node.Operands.Length)
            {
                AppendNode(sb, node.Operands[index]);
                return true;
            }

            sb.Append("<missing-op").Append(index).Append('>');
            return false;
        }

        internal static void AppendNode(StringBuilder sb, GenTree node)
        {
            switch (node.Kind)
            {
                case GenTreeKind.ConstI4:
                    sb.Append(node.Int32);
                    return;
                case GenTreeKind.ConstI8:
                    sb.Append(node.Int64).Append('L');
                    return;
                case GenTreeKind.ConstR4Bits:
                    sb.Append(BitConverter.Int32BitsToSingle(node.Int32).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('f');
                    return;
                case GenTreeKind.ConstR8Bits:
                    sb.Append(BitConverter.Int64BitsToDouble(node.Int64).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case GenTreeKind.ConstNull:
                    sb.Append("null");
                    return;
                case GenTreeKind.ConstString:
                    sb.Append('"').Append(Escape(node.Text ?? string.Empty)).Append('"');
                    return;
                case GenTreeKind.Local:
                    sb.Append('l').Append(node.Int32);
                    return;
                case GenTreeKind.LocalAddr:
                    sb.Append("&l").Append(node.Int32);
                    return;
                case GenTreeKind.Arg:
                    sb.Append('a').Append(node.Int32);
                    return;
                case GenTreeKind.ArgAddr:
                    sb.Append("&a").Append(node.Int32);
                    return;
                case GenTreeKind.Temp:
                    sb.Append('t').Append(node.Int32);
                    return;
                case GenTreeKind.TempAddr:
                    sb.Append("&t").Append(node.Int32);
                    return;
                case GenTreeKind.DefaultValue:
                    sb.Append("default(").Append(TypeName(node.RuntimeType)).Append(')');
                    return;
                case GenTreeKind.SizeOf:
                    sb.Append("sizeof(").Append(TypeName(node.RuntimeType)).Append(')');
                    return;
                case GenTreeKind.ExceptionObject:
                    sb.Append("exception");
                    return;
                case GenTreeKind.Unary:
                    sb.Append(node.SourceOp.ToString().ToLowerInvariant()).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Binary:
                    if (node.Operands.Length == 2)
                    {
                        sb.Append('(');
                        TryAppendOperand(sb, node, 0);
                        sb.Append(' ').Append(node.SourceOp).Append(' ');
                        TryAppendOperand(sb, node, 1);
                        sb.Append(')');
                        return;
                    }
                    break;
                case GenTreeKind.Conv:
                    sb.Append("conv.").Append(node.ConvKind);
                    if (node.ConvFlags != NumericConvFlags.None)
                        sb.Append('.').Append(node.ConvFlags);
                    sb.Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    sb.Append(node.Kind == GenTreeKind.VirtualCall ? "callvirt " : node.Kind == GenTreeKind.DelegateInvoke ? "delegate_invoke " : "call ");
                    sb.Append(MethodName(node.Method)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.NewObject:
                    sb.Append("newobj ").Append(MethodName(node.Method)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.NewDelegate:
                    sb.Append("newdelegate ").Append(TypeName(node.RuntimeType)).Append(" -> ").Append(MethodName(node.Method)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                    TryAppendOperand(sb, node, 0);
                    sb.Append(node.Kind == GenTreeKind.FieldAddr ? ".&" : ".");
                    sb.Append(FieldName(node.Field));
                    return;
                case GenTreeKind.StaticField:
                    sb.Append(FieldName(node.Field));
                    return;
                case GenTreeKind.StaticFieldAddr:
                    sb.Append('&').Append(FieldName(node.Field));
                    return;
                case GenTreeKind.LoadIndirect:
                    sb.Append("ldobj ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.StoreIndirect:
                    sb.Append("stobj ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.StoreLocal:
                    sb.Append('l').Append(node.Int32).Append(" = ");
                    TryAppendOperand(sb, node, 0);
                    return;
                case GenTreeKind.StoreArg:
                    sb.Append('a').Append(node.Int32).Append(" = ");
                    TryAppendOperand(sb, node, 0);
                    return;
                case GenTreeKind.StoreTemp:
                    sb.Append('t').Append(node.Int32).Append(" = ");
                    TryAppendOperand(sb, node, 0);
                    return;
                case GenTreeKind.StoreField:
                    TryAppendOperand(sb, node, 0);
                    sb.Append('.').Append(FieldName(node.Field)).Append(" = ");
                    TryAppendOperand(sb, node, 1);
                    return;
                case GenTreeKind.StoreStaticField:
                    sb.Append(FieldName(node.Field)).Append(" = ");
                    TryAppendOperand(sb, node, 0);
                    return;
                case GenTreeKind.NewArray:
                    sb.Append("newarr ").Append(TypeName(node.RuntimeType)).Append('[');
                    AppendOperands(sb, node);
                    sb.Append(']');
                    return;
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                    TryAppendOperand(sb, node, 0);
                    sb.Append('[');
                    TryAppendOperand(sb, node, 1);
                    sb.Append(']');
                    if (node.Kind == GenTreeKind.ArrayElementAddr) sb.Append(".addr");
                    return;
                case GenTreeKind.StoreArrayElement:
                    TryAppendOperand(sb, node, 0);
                    sb.Append('[');
                    TryAppendOperand(sb, node, 1);
                    sb.Append("] = ");
                    TryAppendOperand(sb, node, 2);
                    return;
                case GenTreeKind.ArrayDataRef:
                    sb.Append("arrayDataRef(");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.StaticData:
                    sb.Append("staticData(offset=").Append(node.Int32).Append(", length=").Append(node.Int64).Append(')');
                    return;
                case GenTreeKind.StackAlloc:
                    sb.Append("stackalloc(size=").Append(node.Int32).Append(", count=");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.AllocHGlobal:
                    sb.Append("alloc_hglobal(");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.FreeHGlobal:
                    sb.Append("free_hglobal(");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.PointerElementAddr:
                    sb.Append("ptrElemAddr(size=").Append(node.Int32).Append(", ");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.PointerToByRef:
                    sb.Append("ptrToByRef(");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.PointerDiff:
                    sb.Append("ptrDiff(size=").Append(node.Int32).Append(", ");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.CastClass:
                    sb.Append("castclass ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.IsInst:
                    sb.Append("isinst ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Box:
                    sb.Append("box ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.UnboxAny:
                    sb.Append("unbox.any ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Eval:
                    sb.Append("eval ");
                    AppendOperands(sb, node);
                    return;
                case GenTreeKind.Branch:
                    sb.Append("br B").Append(node.TargetBlockId).Append(" pc ").Append(node.TargetPc);
                    return;
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                    sb.Append(node.Kind == GenTreeKind.BranchTrue ? "brtrue " : "brfalse ");
                    TryAppendOperand(sb, node, 0);
                    sb.Append(" -> B").Append(node.TargetBlockId).Append(" pc ").Append(node.TargetPc);
                    return;
                case GenTreeKind.Return:
                    sb.Append("ret");
                    if (node.Operands.Length != 0)
                    {
                        sb.Append(' ');
                        AppendOperands(sb, node);
                    }
                    return;
                case GenTreeKind.Throw:
                    sb.Append("throw ");
                    AppendOperands(sb, node);
                    return;
                case GenTreeKind.Rethrow:
                    sb.Append("rethrow");
                    return;
                case GenTreeKind.EndFinally:
                    sb.Append("endfinally");
                    return;
            }

            sb.Append(node.Kind).Append('(');
            AppendOperands(sb, node);
            sb.Append(')');
        }

        private static void AppendOperands(StringBuilder sb, GenTree node)
        {
            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                AppendNode(sb, node.Operands[i]);
            }
        }

        private static string TypeName(RuntimeType? type)
        {
            if (type is null) return "?";
            if (string.IsNullOrEmpty(type.Namespace)) return type.Name;
            return $"{type.Namespace}.{type.Name}";
        }

        private static string FieldName(RuntimeField? field)
        {
            if (field is null) return "<field?>";
            return $"{TypeName(field.DeclaringType)}.{field.Name}";
        }

        private static string MethodName(RuntimeMethod? method)
        {
            if (method is null) return "<method?>";
            return $"{TypeName(method.DeclaringType)}.{method.Name}";
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
