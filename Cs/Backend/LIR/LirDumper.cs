using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class LinearDumper
    {
        public static string Dump(GenTreeProgram program)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            var sb = new StringBuilder();
            for (int i = 0; i < program.Methods.Length; i++)
            {
                DumpMethod(sb, program.Methods[i]);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string Dump(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var sb = new StringBuilder();
            DumpMethod(sb, method);
            return sb.ToString();
        }

        public static string FormatNode(GenTree node)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            var sb = new StringBuilder();
            AppendNode(sb, node);
            return sb.ToString();
        }

        private static void DumpMethod(StringBuilder sb, GenTreeMethod method)
        {
            var rm = method.RuntimeMethod;
            sb.Append("linear method ")
              .Append(method.Module.Name)
              .Append("::")
              .Append(TypeName(rm.DeclaringType))
              .Append('.')
              .Append(rm.Name)
              .Append(" #")
              .Append(rm.MethodId)
              .AppendLine();

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                sb.Append("B").Append(block.Id).Append(" [pc ")
                  .Append(method.Cfg.Blocks[b].StartPc).Append("..").Append(method.Cfg.Blocks[b].EndPcExclusive).AppendLine(")");

                for (int n = 0; n < block.LinearNodes.Length; n++)
                {
                    sb.Append("  ");
                    AppendNode(sb, block.LinearNodes[n]);
                    sb.AppendLine();
                }
            }

            if (method.LiveIntervals.Length != 0)
            {
                sb.AppendLine("intervals:");
                for (int i = 0; i < method.LiveIntervals.Length; i++)
                    AppendInterval(sb, method.LiveIntervals[i]);
            }

            if (method.RefPositions.Length != 0)
            {
                sb.AppendLine("refpositions:");
                for (int i = 0; i < method.RefPositions.Length; i++)
                    sb.Append("  ").Append(method.RefPositions[i]).AppendLine();
            }
        }

        private static void AppendInterval(StringBuilder sb, LinearLiveInterval interval)
        {
            sb.Append("  ").Append(interval.Value).Append(" def@").Append(interval.DefinitionPosition).Append(" ranges=");
            for (int i = 0; i < interval.Ranges.Length; i++)
            {
                if (i != 0)
                    sb.Append(',');
                sb.Append(interval.Ranges[i]);
            }

            if (interval.UsePositions.Length != 0)
            {
                sb.Append(" uses=");
                for (int i = 0; i < interval.UsePositions.Length; i++)
                {
                    if (i != 0)
                        sb.Append(',');
                    sb.Append(interval.UsePositions[i]);
                }
            }
            sb.AppendLine();
        }

        private static void AppendNode(StringBuilder sb, GenTree node)
        {
            sb.Append('#').Append(node.LinearId).Append(' ');

            if (node.LinearKind is GenTreeLinearKind.Copy or GenTreeLinearKind.PhiCopy)
            {
                sb.Append(node.RegisterResult is not null ? node.RegisterResult.ToString() : "<none>")
                  .Append(node.IsPhiCopy ? " <- phi " : " <- copy ")
                  .Append(node.RegisterUses.Length == 0 ? "<missing>" : node.RegisterUses[0].ToString());
                if (node.IsPhiCopy)
                    sb.Append(" ; edge B").Append(node.LinearPhiCopyFromBlockId).Append("->B").Append(node.LinearPhiCopyToBlockId);
                return;
            }

            if (node.LinearKind == GenTreeLinearKind.GcPoll)
            {
                sb.Append("gc.poll");
                string gcLowering = node.LinearLowering.ToString();
                if (!string.IsNullOrEmpty(gcLowering))
                    sb.Append(" ; lower[").Append(gcLowering).Append(']');
                return;
            }

            if (node.RegisterResult is not null)
                sb.Append(node.RegisterResult).Append(" = ");

            AppendTreeShape(sb, node);

            string lowering = node.LinearLowering.ToString();
            if (!string.IsNullOrEmpty(lowering))
                sb.Append(" ; lower[").Append(lowering).Append(']');

            string memory = node.LinearMemoryAccess.ToString();
            if (!string.IsNullOrEmpty(memory))
                sb.Append(" ; mem[").Append(memory).Append(']');
        }

        private static void AppendTreeShape(StringBuilder sb, GenTree node)
        {
            GenTree? source = node;
            if (source is null)
            {
                sb.Append(node.Kind);
                return;
            }

            switch (source.Kind)
            {
                case GenTreeKind.ConstI4:
                    sb.Append(source.Int32);
                    return;
                case GenTreeKind.ConstI8:
                    sb.Append(source.Int64).Append('L');
                    return;
                case GenTreeKind.ConstR4Bits:
                    sb.Append(BitConverter.Int32BitsToSingle(source.Int32).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('f');
                    return;
                case GenTreeKind.ConstR8Bits:
                    sb.Append(BitConverter.Int64BitsToDouble(source.Int64).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case GenTreeKind.ConstNull:
                    sb.Append("null");
                    return;
                case GenTreeKind.ConstString:
                    sb.Append('"').Append(Escape(source.Text ?? string.Empty)).Append('"');
                    return;
                case GenTreeKind.Local:
                    sb.Append("ldloc l").Append(source.Int32);
                    return;
                case GenTreeKind.LocalAddr:
                    sb.Append("ldloca l").Append(source.Int32);
                    return;
                case GenTreeKind.Arg:
                    sb.Append("ldarg a").Append(source.Int32);
                    return;
                case GenTreeKind.ArgAddr:
                    sb.Append("ldarga a").Append(source.Int32);
                    return;
                case GenTreeKind.Temp:
                    sb.Append("ldtmp t").Append(source.Int32);
                    return;
                case GenTreeKind.TempAddr:
                    sb.Append("ldtmpa t").Append(source.Int32);
                    return;
                case GenTreeKind.ExceptionObject:
                    sb.Append("exception");
                    return;
                case GenTreeKind.DefaultValue:
                    sb.Append("default(").Append(TypeName(source.RuntimeType)).Append(')');
                    return;
                case GenTreeKind.SizeOf:
                    sb.Append("sizeof(").Append(TypeName(source.RuntimeType)).Append(')');
                    return;
                case GenTreeKind.Unary:
                    sb.Append(source.SourceOp.ToString().ToLowerInvariant()).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Binary:
                    sb.Append(source.SourceOp).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Conv:
                    sb.Append("conv.").Append(source.ConvKind);
                    if (source.ConvFlags != NumericConvFlags.None)
                        sb.Append('.').Append(source.ConvFlags);
                    sb.Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    sb.Append(source.Kind == GenTreeKind.VirtualCall ? "callvirt " : source.Kind == GenTreeKind.DelegateInvoke ? "delegate_invoke " : "call ")
                      .Append(MethodName(source.Method)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.NewDelegate:
                    sb.Append("new_delegate ").Append(MethodName(source.Method)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.DelegateCombine:
                    sb.Append("delegate_combine ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.DelegateRemove:
                    sb.Append("delegate_remove ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.NewObject:
                    sb.Append("newobj ").Append(MethodName(source.Method)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                    sb.Append(source.Kind == GenTreeKind.FieldAddr ? "fieldaddr " : "field ")
                      .Append(FieldName(source.Field)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StaticField:
                    sb.Append("static_field ").Append(FieldName(source.Field));
                    return;
                case GenTreeKind.StaticFieldAddr:
                    sb.Append("static_field_addr ").Append(FieldName(source.Field));
                    return;
                case GenTreeKind.LoadIndirect:
                    sb.Append("ldobj ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreIndirect:
                    sb.Append("stobj ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreLocal:
                    sb.Append("stloc l").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreArg:
                    sb.Append("starg a").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreTemp:
                    sb.Append("sttmp t").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreField:
                    sb.Append("stfld ").Append(FieldName(source.Field)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreStaticField:
                    sb.Append("stsfld ").Append(FieldName(source.Field)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.NewArray:
                    sb.Append("newarr ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                    sb.Append(source.Kind == GenTreeKind.ArrayElementAddr ? "arr_addr " : "arr_elem ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StoreArrayElement:
                    sb.Append("st_elem ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.ArrayDataRef:
                    sb.Append("array_data_ref ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.StaticData:
                    sb.Append("static_data offset=").Append(source.Int32).Append(" length=").Append(source.Int64);
                    return;
                case GenTreeKind.StackAlloc:
                    sb.Append("stackalloc elemSize=").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.AllocHGlobal:
                    sb.Append("alloc_hglobal ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.FreeHGlobal:
                    sb.Append("free_hglobal ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.PointerElementAddr:
                    sb.Append("ptr_elem_addr elemSize=").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.PointerToByRef:
                    sb.Append("ptr_to_byref ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.PointerDiff:
                    sb.Append("ptr_diff elemSize=").Append(source.Int32).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.CastClass:
                    sb.Append("castclass ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.IsInst:
                    sb.Append("isinst ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Box:
                    sb.Append("box ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.UnboxAny:
                    sb.Append("unbox.any ").Append(TypeName(source.RuntimeType)).Append(' ');
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Eval:
                    sb.Append("eval ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Branch:
                    sb.Append("br B").Append(source.TargetBlockId);
                    return;
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                    sb.Append(source.Kind == GenTreeKind.BranchTrue ? "brtrue " : "brfalse ");
                    AppendUses(sb, node);
                    sb.Append(" -> B").Append(source.TargetBlockId);
                    return;
                case GenTreeKind.Return:
                    sb.Append("ret ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Throw:
                    sb.Append("throw ");
                    AppendUses(sb, node);
                    return;
                case GenTreeKind.Rethrow:
                    sb.Append("rethrow");
                    return;
                case GenTreeKind.EndFinally:
                    sb.Append("endfinally");
                    return;
            }

            sb.Append(source.Kind).Append(' ');
            AppendUses(sb, node);
        }

        private static void AppendUses(StringBuilder sb, GenTree node)
        {
            if (!node.OperandFlags.IsDefaultOrEmpty)
            {
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (i != 0)
                        sb.Append(", ");
                    var flags = i < node.OperandFlags.Length ? node.OperandFlags[i] : LirOperandFlags.None;
                    if ((flags & LirOperandFlags.Contained) != 0)
                        sb.Append("contained(").Append(node.Operands[i].Kind).Append(')');
                    else if (node.Operands[i].RegisterResult is not null)
                        sb.Append(node.Operands[i].RegisterResult!);
                    else
                        sb.Append('_');
                    if ((flags & ~LirOperandFlags.Contained) != 0)
                        sb.Append(" [").Append(flags & ~LirOperandFlags.Contained).Append(']');
                }
                return;
            }

            for (int i = 0; i < node.RegisterUses.Length; i++)
            {
                if (i != 0)
                    sb.Append(", ");
                sb.Append(node.RegisterUses[i]);
            }
        }

        private static string TypeName(RuntimeType? type)
        {
            if (type is null)
                return "?";
            return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
        }

        private static string FieldName(RuntimeField? field)
        {
            if (field is null)
                return "<field?>";
            return TypeName(field.DeclaringType) + "." + field.Name;
        }

        private static string MethodName(RuntimeMethod? method)
        {
            if (method is null)
                return "<method?>";
            return TypeName(method.DeclaringType) + "." + method.Name;
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
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
