using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class SsaDumper
    {
        public static string Dump(SsaProgram program)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < program.Methods.Length; i++)
            {
                DumpMethod(sb, program.Methods[i]);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string Dump(SsaMethod method)
        {
            var sb = new StringBuilder();
            DumpMethod(sb, method);
            return sb.ToString();
        }

        public static string FormatTree(SsaTree tree)
        {
            var sb = new StringBuilder();
            AppendTree(sb, tree);
            return sb.ToString();
        }

        private static void DumpMethod(StringBuilder sb, SsaMethod method)
        {
            var rm = method.GenTreeMethod.RuntimeMethod;
            sb.Append("ssa method ")
              .Append(method.GenTreeMethod.Module.Name)
              .Append("::")
              .Append(TypeName(rm.DeclaringType))
              .Append('.')
              .Append(rm.Name)
              .Append(" #")
              .Append(rm.MethodId)
              .AppendLine();

            if (method.InitialValues.Length != 0)
            {
                sb.Append("  initial: ");
                for (int i = 0; i < method.InitialValues.Length; i++)
                {
                    if (i != 0) sb.Append(", ");
                    sb.Append(method.InitialValues[i]);
                }
                sb.AppendLine();
            }

            if (method.InitialMemoryValues.Length != 0)
            {
                sb.Append("  initial-memory: ");
                AppendMemoryValueList(sb, method.InitialMemoryValues);
                sb.AppendLine();
            }

            if (method.SsaLocalDescriptors.Length != 0)
            {
                sb.AppendLine("  ssa descriptors:");
                for (int l = 0; l < method.SsaLocalDescriptors.Length; l++)
                {
                    var local = method.SsaLocalDescriptors[l];
                    if (local.PerSsaData.IsDefaultOrEmpty)
                        continue;
                    sb.Append("    ").Append(local.Slot).Append(':');
                    for (int ssaNum = SsaConfig.FirstSsaNumber; ssaNum < local.PerSsaData.Length; ssaNum++)
                    {
                        var descriptor = local.PerSsaData[ssaNum];
                        if (descriptor is null)
                            continue;
                        if (ssaNum != SsaConfig.FirstSsaNumber)
                            sb.Append(';');
                        sb.Append(' ').Append(descriptor);
                    }
                    sb.AppendLine();
                }
            }

            if (method.Cfg.NaturalLoops.Length != 0)
            {
                sb.Append("  loops: ");
                for (int i = 0; i < method.Cfg.NaturalLoops.Length; i++)
                {
                    if (i != 0) sb.Append(", ");
                    var loop = method.Cfg.NaturalLoops[i];
                    sb.Append('L').Append(loop.Index)
                        .Append("(H=B").Append(loop.Header)
                        .Append(", latches=").Append(loop.Latches.Length)
                        .Append(", pre=").Append(loop.Preheader >= 0 ? "B" + loop.Preheader.ToString() : "none")
                        .Append(", depth=").Append(loop.Depth)
                        .Append(", body=").Append(loop.Blocks.Length)
                        .Append(')');
                }
                sb.AppendLine();
            }

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var block = method.Blocks[b];
                sb.Append("  B").Append(block.Id)
                  .Append(" [pc ").Append(block.CfgBlock.StartPc).Append("..").Append(block.CfgBlock.EndPcExclusive).Append(']');

                if (block.CfgBlock.Successors.Length != 0)
                {
                    sb.Append(" -> ");
                    for (int i = 0; i < block.CfgBlock.Successors.Length; i++)
                    {
                        if (i != 0) sb.Append(", ");
                        var e = block.CfgBlock.Successors[i];
                        sb.Append('B').Append(e.ToBlockId).Append(':').Append(e.Kind);
                    }
                }
                sb.AppendLine();

                if (block.MemoryIn.Length != 0)
                {
                    sb.Append("    memory-in: ");
                    AppendMemoryValueList(sb, block.MemoryIn);
                    sb.AppendLine();
                }

                for (int i = 0; i < block.Phis.Length; i++)
                {
                    var phi = block.Phis[i];
                    sb.Append("    ").Append(phi.Target).Append(" = phi(");
                    for (int p = 0; p < phi.Inputs.Length; p++)
                    {
                        if (p != 0) sb.Append(", ");
                        sb.Append("B").Append(phi.Inputs[p].PredecessorBlockId).Append(':').Append(phi.Inputs[p].Value);
                    }
                    sb.AppendLine(")");
                }

                for (int i = 0; i < block.MemoryPhis.Length; i++)
                {
                    var phi = block.MemoryPhis[i];
                    sb.Append("    ").Append(phi.Target).Append(" = memory-phi(");
                    for (int p = 0; p < phi.Inputs.Length; p++)
                    {
                        if (p != 0) sb.Append(", ");
                        sb.Append("B").Append(phi.Inputs[p].PredecessorBlockId).Append(':').Append(phi.Inputs[p].Value);
                    }
                    sb.AppendLine(")");
                }

                for (int i = 0; i < block.Statements.Length; i++)
                {
                    sb.Append("    ");
                    AppendTree(sb, block.Statements[i]);
                    AppendMemoryAnnotation(sb, block.Statements[i]);
                    sb.AppendLine();
                }

                if (block.MemoryOut.Length != 0)
                {
                    sb.Append("    memory-out: ");
                    AppendMemoryValueList(sb, block.MemoryOut);
                    sb.AppendLine();
                }
            }
        }

        private static void AppendMemoryValueList(StringBuilder sb, ImmutableArray<SsaMemoryValueName> values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                sb.Append(values[i]);
            }
        }

        private static void AppendMemoryAnnotation(StringBuilder sb, SsaTree tree)
        {
            if (!tree.HasMemoryEffects)
                return;

            sb.Append("  ; mem");
            if (tree.MemoryUses.Length != 0)
            {
                sb.Append(" use=[");
                AppendMemoryValueList(sb, tree.MemoryUses);
                sb.Append(']');
            }
            if (tree.MemoryDefinitions.Length != 0)
            {
                sb.Append(" def=[");
                AppendMemoryValueList(sb, tree.MemoryDefinitions);
                sb.Append(']');
            }
        }

        private static void AppendTree(StringBuilder sb, SsaTree tree)
        {
            if (tree.Value.HasValue)
            {
                sb.Append(tree.Value.Value);
                return;
            }

            switch (tree.Kind)
            {
                case GenTreeKind.ConstI4:
                    sb.Append(tree.Source.Int32);
                    return;
                case GenTreeKind.ConstI8:
                    sb.Append(tree.Source.Int64).Append('L');
                    return;
                case GenTreeKind.ConstR4Bits:
                    sb.Append(BitConverter.Int32BitsToSingle(tree.Source.Int32).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('f');
                    return;
                case GenTreeKind.ConstR8Bits:
                    sb.Append(BitConverter.Int64BitsToDouble(tree.Source.Int64).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case GenTreeKind.ConstNull:
                    sb.Append("null");
                    return;
                case GenTreeKind.ConstString:
                    sb.Append('"').Append(Escape(tree.Source.Text ?? string.Empty)).Append('"');
                    return;
                case GenTreeKind.Local:
                    sb.Append('l').Append(tree.Source.Int32);
                    return;
                case GenTreeKind.Arg:
                    sb.Append('a').Append(tree.Source.Int32);
                    return;
                case GenTreeKind.Temp:
                    sb.Append('t').Append(tree.Source.Int32);
                    return;
                case GenTreeKind.TempAddr:
                    sb.Append("&t").Append(tree.Source.Int32);
                    return;
                case GenTreeKind.LocalAddr:
                    sb.Append("&l").Append(tree.Source.Int32);
                    return;
                case GenTreeKind.ArgAddr:
                    sb.Append("&a").Append(tree.Source.Int32);
                    return;
                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreArg:
                case GenTreeKind.StoreTemp:
                    if (tree.StoreTarget.HasValue)
                        sb.Append(tree.StoreTarget.Value);
                    else
                        AppendOriginalStoreTarget(sb, tree.Source);
                    sb.Append(" = ");
                    AppendOperandList(sb, tree);
                    return;
                case GenTreeKind.Unary:
                    sb.Append(tree.Source.SourceOp.ToString().ToLowerInvariant()).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.Binary:
                    if (tree.Operands.Length == 2)
                    {
                        sb.Append('(');
                        AppendTree(sb, tree.Operands[0]);
                        sb.Append(' ').Append(tree.Source.SourceOp).Append(' ');
                        AppendTree(sb, tree.Operands[1]);
                        sb.Append(')');
                        return;
                    }
                    break;
                case GenTreeKind.Conv:
                    sb.Append("conv.").Append(tree.Source.ConvKind).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    sb.Append(tree.Kind == GenTreeKind.VirtualCall ? "callvirt " : tree.Kind == GenTreeKind.DelegateInvoke ? "delegate_invoke " : "call ")
                      .Append(MethodName(tree.Source.Method)).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.NewObject:
                    sb.Append("newobj ").Append(MethodName(tree.Source.Method)).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.NewDelegate:
                    sb.Append("newdelegate ").Append(TypeName(tree.Source.RuntimeType)).Append(" -> ").Append(MethodName(tree.Source.Method)).Append('(');
                    AppendOperandList(sb, tree);
                    sb.Append(')');
                    return;
                case GenTreeKind.Eval:
                    sb.Append("eval ");
                    AppendOperandList(sb, tree);
                    return;
                case GenTreeKind.Branch:
                    sb.Append("br B").Append(tree.Source.TargetBlockId);
                    return;
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                    sb.Append(tree.Kind == GenTreeKind.BranchTrue ? "brtrue " : "brfalse ");
                    AppendOperandList(sb, tree);
                    sb.Append(" -> B").Append(tree.Source.TargetBlockId);
                    return;
                case GenTreeKind.Return:
                    sb.Append("ret");
                    if (tree.Operands.Length != 0)
                    {
                        sb.Append(' ');
                        AppendOperandList(sb, tree);
                    }
                    return;
                case GenTreeKind.Throw:
                    sb.Append("throw ");
                    AppendOperandList(sb, tree);
                    return;
                case GenTreeKind.Rethrow:
                    sb.Append("rethrow");
                    return;
                case GenTreeKind.EndFinally:
                    sb.Append("endfinally");
                    return;
            }

            sb.Append(tree.Kind).Append('(');
            AppendOperandList(sb, tree);
            sb.Append(')');
        }

        private static void AppendOriginalStoreTarget(StringBuilder sb, GenTree source)
        {
            switch (source.Kind)
            {
                case GenTreeKind.StoreArg:
                    sb.Append('a').Append(source.Int32);
                    return;
                case GenTreeKind.StoreLocal:
                    sb.Append('l').Append(source.Int32);
                    return;
                case GenTreeKind.StoreTemp:
                    sb.Append('t').Append(source.Int32);
                    return;
                default:
                    sb.Append("<store>");
                    return;
            }
        }

        private static void AppendOperandList(StringBuilder sb, SsaTree tree)
        {
            for (int i = 0; i < tree.Operands.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                AppendTree(sb, tree.Operands[i]);
            }
        }

        private static string TypeName(RuntimeType? type)
        {
            if (type is null) return "?";
            if (string.IsNullOrEmpty(type.Namespace)) return type.Name;
            return type.Namespace + "." + type.Name;
        }

        private static string MethodName(RuntimeMethod? method)
        {
            if (method is null) return "<method?>";
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
