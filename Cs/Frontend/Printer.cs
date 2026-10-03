using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cnidaria.Cs
{
    /// <summary>Formats bound trees as a deterministic text hierarchy</summary>
    public static class BoundTreePrinter
    {
        /// <summary>Controls optional metadata included in bound tree output</summary>
        public sealed class Options
        {
            /// <summary>Includes the originating syntax kind</summary>
            public bool IncludeSyntaxKind { get; init; } = true;
            /// <summary>Includes the originating source span</summary>
            public bool IncludeSyntaxSpan { get; init; } = false;
            /// <summary>Includes expression and symbol types</summary>
            public bool IncludeTypes { get; init; } = true;
            /// <summary>Includes compile-time constant values</summary>
            public bool IncludeConstants { get; init; } = true;
            /// <summary>Includes bound conversion classifications</summary>
            public bool IncludeConversionKind { get; init; } = false;
            /// <summary>Includes error state for invalid nodes</summary>
            public bool IncludeHasErrors { get; init; } = true;
            /// <summary>Includes intrinsic binding details</summary>
            public bool IncludeIntrinsicInfo { get; init; } = false;
        }

        /// <summary>Formats a bound tree into a string</summary>
        public static string Print(BoundNode node, Options? options = null)
        {
            options ??= new Options();
            var sb = new StringBuilder(8 * 1024);
            var w = new Writer(sb, options);
            w.WriteNode(node);
            return sb.ToString();
        }

        /// <summary>Writes a formatted bound tree to standard output</summary>
        public static void PrintToConsole(BoundNode node, Options? options = null)
        {
            Console.WriteLine(Print(node, options));
        }
        /// <summary>Finds Program.Main inside namespace declarations</summary>
        public static MethodDeclarationSyntax? FindMain(CompilationUnitSyntax cu)
        {
            foreach (var m in cu.Members)
            {
                if (m is BaseNamespaceDeclarationSyntax ns)
                {
                    foreach (var nm in ns.Members)
                    {
                        if (nm is ClassDeclarationSyntax c && (c.Identifier.ValueText ?? "") == "Program")
                        {
                            foreach (var cm in c.Members)
                            {
                                if (cm is MethodDeclarationSyntax md && (md.Identifier.ValueText ?? "") == "Main")
                                    return md;
                            }
                        }
                    }
                }
            }

            return null;
        }
        /// <summary>Maintains tree prefix state while formatting nodes recursively</summary>
        private sealed class Writer
        {
            private readonly StringBuilder _sb;
            private readonly Options _opt;

            private readonly List<bool> _hasNextAtDepth = new();
            public Writer(StringBuilder sb, Options opt)
            {
                _sb = sb;
                _opt = opt;
            }
            public void WriteNode(BoundNode node)
            {
                WriteNodeCore(node, edgeLabel: null, isLast: true, isRoot: true);
            }

            /// <summary>Writes one node and recursively emits its labeled children</summary>
            private void WriteNodeCore(BoundNode? node, string? edgeLabel, bool isLast, bool isRoot)
            {
                if (!isRoot)
                {
                    EnsureLineStart();
                    WritePrefix(isLast);
                }


                if (edgeLabel != null)
                {
                    _sb.Append(edgeLabel);
                    _sb.Append(": ");
                }

                if (node is null)
                {
                    _sb.AppendLine("<null bound>");
                    return;
                }

                // Write node metadata before descending into children
                var title = GetTitle(node);
                _sb.Append(title);
                _sb.Append(FormatMeta(node));

                if (node is BoundExpression be)
                    _sb.Append(FormatExprExtras(be));

                var inline = FormatInline(node);
                if (inline.Length != 0)
                {
                    _sb.Append(' ');
                    _sb.Append(inline);
                }

                _sb.AppendLine();

                // Child order follows source and evaluation order
                var children = GetChildren(node);
                if (children.Length == 0)
                    return;

                if (!isRoot)
                    _hasNextAtDepth.Add(!isLast);

                for (int i = 0; i < children.Length; i++)
                {
                    var c = children[i];
                    bool childLast = (i == children.Length - 1);
                    WriteNodeCore(c.Node, c.Label, childLast, isRoot: false);
                }

                if (!isRoot)
                    _hasNextAtDepth.RemoveAt(_hasNextAtDepth.Count - 1);
            }
            private void EnsureLineStart()
            {
                if (_sb.Length == 0)
                    return;

                char last = _sb[_sb.Length - 1];
                if (last != '\n' && last != '\r')
                    _sb.AppendLine();
            }
            private void WritePrefix(bool isLast)
            {
                for (int i = 0; i < _hasNextAtDepth.Count; i++)
                    _sb.Append(_hasNextAtDepth[i] ? "│  " : "   ");

                _sb.Append(isLast ? "└─ " : "├─ ");
            }

            private static string GetTitle(BoundNode node)
            {
                return node switch
                {
                    BoundCompilationUnit => "BoundCompilationUnit",
                    BoundMethodBody => "BoundMethodBody",

                    BoundBlockStatement => "BoundBlockStatement",
                    BoundStatementList => "BoundStatementList",
                    BoundExpressionStatement => "BoundExpressionStatement",
                    BoundLocalDeclarationStatement => "BoundLocalDeclarationStatement",
                    BoundLockStatement => "BoundLockStatement",
                    BoundEmptyStatement => "BoundEmptyStatement",
                    BoundReturnStatement => "BoundReturnStatement",
                    BoundThrowStatement => "BoundThrowStatement",
                    BoundIfStatement => "BoundIfStatement",
                    BoundWhileStatement => "BoundWhileStatement",
                    BoundForStatement => "BoundForStatement",
                    BoundForEachStatement => "BoundForEachStatement",
                    BoundDoWhileStatement => "BoundDoWhileStatement",

                    BoundLabelStatement => "BoundLabelStatement",
                    BoundGotoStatement => "BoundGotoStatement",
                    BoundBreakStatement => "BoundBreakStatement",
                    BoundContinueStatement => "BoundContinueStatement",

                    BoundBadStatement => "BoundBadStatement",
                    BoundBadExpression => "BoundBadExpression",

                    BoundLiteralExpression => "BoundLiteralExpression",
                    BoundThisExpression => "BoundThisExpression",
                    BoundBaseExpression => "BoundBaseExpression",
                    BoundLocalExpression => "BoundLocalExpression",
                    BoundParameterExpression => "BoundParameterExpression",
                    BoundLabelExpression => "BoundLabelExpression",
                    BoundConversionExpression => "BoundConversionExpression",
                    BoundAsExpression => "BoundAsExpression",
                    BoundBinaryExpression => "BoundBinaryExpression",
                    BoundUnaryExpression => "BoundUnaryExpression",
                    BoundConditionalExpression => "BoundConditionalExpression",
                    BoundCallExpression => "BoundCallExpression",
                    BoundObjectCreationExpression => "BoundObjectCreationExpression",
                    BoundUnboundImplicitObjectCreationExpression => "BoundUnboundImplicitObjectCreationExpression",
                    BoundAssignmentExpression => "BoundAssignmentExpression",
                    BoundCompoundAssignmentExpression => "BoundCompoundAssignmentExpression",
                    BoundNullCoalescingAssignmentExpression => "BoundNullCoalescingAssignmentExpression",
                    BoundIncrementDecrementExpression => "BoundIncrementDecrementExpression",
                    BoundArrayInitializerExpression => "BoundArrayInitializerExpression",
                    BoundArrayCreationExpression => "BoundArrayCreationExpression",
                    BoundArrayElementAccessExpression => "BoundArrayElementAccessExpression",
                    BoundInlineArrayElementAccessExpression => "BoundInlineArrayElementAccessExpression",
                    BoundStackAllocArrayCreationExpression => "BoundStackAllocArrayCreationExpression",
                    BoundRefExpression => "BoundRefExpression",
                    BoundAddressOfExpression => "BoundAddressOfExpression",
                    BoundPointerIndirectionExpression => "BoundPointerIndirectionExpression",
                    BoundPointerElementAccessExpression => "BoundPointerElementAccessExpression",
                    BoundTupleExpression => "BoundTupleExpression",
                    BoundSequenceExpression => "BoundSequenceExpression",
                    BoundConditionalGotoStatement => "BoundConditionalGotoStatement",
                    BoundMemberAccessExpression => "BoundMemberAccessExpression",
                    BoundIndexerAccessExpression => "BoundIndexerAccessExpression",
                    BoundIsPatternExpression => "BoundIsPatternExpression",
                    BoundLocalFunctionStatement => "BoundLocalFunctionStatement",
                    BoundTryStatement => "BoundTryStatement",
                    BoundCatchBlock => "BoundCatchBlock",
                    BoundCheckedStatement => "BoundCheckedStatement",
                    BoundUncheckedStatement => "BoundUncheckedStatement",
                    BoundCheckedExpression => "BoundCheckedExpression",
                    BoundUncheckedExpression => "BoundUncheckedExpression",
                    BoundSizeOfExpression => "BoundSizeOfExpression",
                    BoundThrowExpression => "BoundThrowExpression",
                    BoundFixedStatement => "BoundFixedStatement",
                    BoundFixedInitializerExpression => "BoundFixedInitializerExpression",
                    BoundUnboundCollectionExpression => "BoundUnboundCollectionExpression",
                    _ => node.GetType().Name
                };
            }
            /// <summary>Pairs a child node with the edge label shown by the printer</summary>
            private readonly struct Child
            {
                public readonly string Label;
                public readonly BoundNode? Node;

                public Child(string label, BoundNode? node)
                {
                    Label = label;
                    Node = node;
                }
            }

            /// <summary>Projects each bound node kind into its structural child edges</summary>
            private static Child[] GetChildren(BoundNode node)
            {
                switch (node)
                {
                    case BoundCompilationUnit cu:
                        {
                            var list = new List<Child>(cu.Statements.Length + 2);
                            if (cu.TopLevelMethodBodyOpt != null)
                                list.Add(new Child("TopLevelMethodBody", cu.TopLevelMethodBodyOpt));

                            for (int i = 0; i < cu.Statements.Length; i++)
                                list.Add(new Child($"Statements[{i}]", cu.Statements[i]));

                            return list.ToArray();
                        }

                    case BoundMethodBody mb:
                        return new[] { new Child("Body", mb.Body) };

                    case BoundBlockStatement b:
                        {
                            var list = new Child[b.Statements.Length];
                            for (int i = 0; i < b.Statements.Length; i++)
                                list[i] = new Child($"Statements[{i}]", b.Statements[i]);
                            return list;
                        }

                    case BoundStatementList sl:
                        {
                            var list = new Child[sl.Statements.Length];
                            for (int i = 0; i < sl.Statements.Length; i++)
                                list[i] = new Child($"Statements[{i}]", sl.Statements[i]);
                            return list;
                        }

                    case BoundExpressionStatement es:
                        return new[] { new Child("Expression", es.Expression) };

                    case BoundLocalDeclarationStatement ld:
                        return new[] { new Child("Initializer", ld.Initializer) };

                    case BoundReturnStatement rs:
                        return new[] { new Child("Expression", rs.Expression) };

                    case BoundThrowStatement ts:
                        return new[] { new Child("Expression", ts.ExpressionOpt) };

                    case BoundThrowExpression te:
                        return new[] { new Child("Exception", te.Exception) };

                    case BoundConversionExpression ce:
                        return new[] { new Child("Operand", ce.Operand) };

                    case BoundAsExpression ae:
                        return new[] { new Child("Operand", ae.Operand) };

                    case BoundUnaryExpression un:
                        return new[] { new Child("Operand", un.Operand) };

                    case BoundBinaryExpression bin:
                        return new[]
                        {
                            new Child("Left", bin.Left),
                            new Child("Right", bin.Right)
                        };
                    case BoundConditionalExpression c:
                        return new[]
                        {
                            new Child("Condition", c.Condition),
                            new Child("WhenTrue", c.WhenTrue),
                            new Child("WhenFalse", c.WhenFalse),
                        };
                    case BoundAssignmentExpression asg:
                        return new[]
                        {
                            new Child("Left", asg.Left),
                            new Child("Right", asg.Right)
                        };
                    case BoundNullCoalescingAssignmentExpression nca:
                        return new[]
                        {
                            new Child("Left", nca.Left),
                            new Child("Value", nca.Value)
                        };
                    case BoundCallExpression call:
                        {
                            var list = new List<Child>(1 + call.Arguments.Length + 1);
                            list.Add(new Child("Receiver", call.ReceiverOpt));
                            for (int i = 0; i < call.Arguments.Length; i++)
                                list.Add(new Child($"Arguments[{i}]", call.Arguments[i]));
                            return list.ToArray();
                        }
                    case BoundLockStatement ls:
                        return new[]
                        {
                            new Child("Expression", ls.Expression),
                            new Child("Body", ls.Body),
                        };

                    case BoundIfStatement ifs:
                        return new[]
                        {
                            new Child("Condition", ifs.Condition),
                            new Child("Then", ifs.Then),
                            new Child("Else", ifs.ElseOpt),
                        };

                    case BoundWhileStatement wh:
                        return new[]
                        {
                            new Child("Condition", wh.Condition),
                            new Child("Body", wh.Body),
                        };

                    case BoundDoWhileStatement dw:
                        return new[]
                        {
                            new Child("Body", dw.Body),
                            new Child("Condition", dw.Condition),
                        };

                    case BoundForStatement fs:
                        {
                            var list = new List<Child>(fs.Initializers.Length + 1 + fs.Incrementors.Length + 1);

                            for (int i = 0; i < fs.Initializers.Length; i++)
                                list.Add(new Child($"Initializers[{i}]", fs.Initializers[i]));

                            list.Add(new Child("Condition", fs.ConditionOpt));

                            for (int i = 0; i < fs.Incrementors.Length; i++)
                                list.Add(new Child($"Incrementors[{i}]", fs.Incrementors[i]));

                            list.Add(new Child("Body", fs.Body));
                            return list.ToArray();
                        }

                    case BoundCompoundAssignmentExpression ca:
                        return new[]
                        {
                            new Child("Left", ca.Left),
                            new Child("Value", ca.Value),
                        };
                    case BoundArrayInitializerExpression ai:
                        {
                            var list = new Child[ai.Elements.Length];
                            for (int i = 0; i < ai.Elements.Length; i++)
                                list[i] = new Child($"Elements[{i}]", ai.Elements[i]);
                            return list;
                        }
                    case BoundArrayCreationExpression ac:
                        {
                            var list = new List<Child>(ac.DimensionSizes.Length + 1);
                            for (int i = 0; i < ac.DimensionSizes.Length; i++)
                                list.Add(new Child($"DimensionSizes[{i}]", ac.DimensionSizes[i]));
                            list.Add(new Child("Initializer", ac.InitializerOpt));
                            return list.ToArray();
                        }
                    case BoundArrayElementAccessExpression aea:
                        {
                            var list = new List<Child>(aea.Indices.Length + 1);
                            list.Add(new Child("Expression", aea.Expression));
                            for (int i = 0; i < aea.Indices.Length; i++)
                                list.Add(new Child($"Indices[{i}]", aea.Indices[i]));
                            return list.ToArray();
                        }
                    case BoundInlineArrayElementAccessExpression iae:
                        return new[]
                        {
                            new Child("Receiver", iae.Receiver),
                            new Child("Index", iae.Index)
                        };
                    case BoundStackAllocArrayCreationExpression sa:
                        {
                            var list = new List<Child>(2);
                            list.Add(new Child("Count", sa.Count));
                            list.Add(new Child("Initializer", sa.InitializerOpt));
                            return list.ToArray();
                        }
                    case BoundRefExpression re:
                        return new[] { new Child("Operand", re.Operand) };
                    case BoundAddressOfExpression ao:
                        return new[] { new Child("Operand", ao.Operand) };
                    case BoundPointerIndirectionExpression pi:
                        return new[] { new Child("Operand", pi.Operand) };
                    case BoundPointerElementAccessExpression pea:
                        return new[]
                        {
                            new Child("Expression", pea.Expression),
                            new Child("Index", pea.Index),
                        };
                    case BoundTupleExpression t:
                        {
                            var list = new Child[t.Elements.Length];
                            for (int i = 0; i < t.Elements.Length; i++)
                            {
                                var name = (i < t.ElementNames.Length) ? t.ElementNames[i] : null;
                                var label = name is null
                                    ? $"Elements[{i}]"
                                    : $"Elements[{i}] ({name})";
                                list[i] = new Child(label, t.Elements[i]);
                            }
                            return list;
                        }
                    case BoundSequenceExpression seq:
                        {
                            var list = new List<Child>(seq.SideEffects.Length + 1);
                            for (int i = 0; i < seq.SideEffects.Length; i++)
                                list.Add(new Child($"SideEffects[{i}]", seq.SideEffects[i]));
                            list.Add(new Child("Value", seq.Value));
                            return list.ToArray();
                        }
                    case BoundConditionalGotoStatement cg:
                        return new[] { new Child("Condition", cg.Condition) };
                    case BoundObjectCreationExpression oc:
                        {
                            var list = new List<Child>(oc.Arguments.Length);
                            for (int i = 0; i < oc.Arguments.Length; i++)
                                list.Add(new Child($"Arguments[{i}]", oc.Arguments[i]));
                            return list.ToArray();
                        }
                    case BoundUnboundImplicitObjectCreationExpression uioc:
                        {
                            var list = new Child[uioc.Arguments.Length];
                            for (int i = 0; i < uioc.Arguments.Length; i++)
                                list[i] = new Child($"Arguments[{i}]", uioc.Arguments[i]);
                            return list;
                        }
                    case BoundMemberAccessExpression ma:
                        {
                            return new[]
                            {
                                new Child("Receiver", ma.ReceiverOpt),
                            };
                        }
                    case BoundIndexerAccessExpression ia:
                        {
                            var list = new List<Child>(1 + ia.Arguments.Length);
                            list.Add(new Child("Receiver", ia.Receiver));
                            for (int i = 0; i < ia.Arguments.Length; i++)
                                list.Add(new Child($"Arguments[{i}]", ia.Arguments[i]));
                            return list.ToArray();
                        }

                    case BoundLocalFunctionStatement lfs:
                        return new[] { new Child("Body", lfs.Body) };

                    case BoundCheckedStatement cs:
                        return new[] { new Child("Statement", cs.Statement) };

                    case BoundUncheckedStatement us:
                        return new[] { new Child("Statement", us.Statement) };

                    case BoundCheckedExpression ce2:
                        return new[] { new Child("Expression", ce2.Expression) };

                    case BoundUncheckedExpression ue:
                        return new[] { new Child("Expression", ue.Expression) };

                    case BoundTryStatement ts:
                        {
                            var list = new List<Child>(1 + ts.CatchBlocks.Length + 1);
                            list.Add(new Child("TryBlock", ts.TryBlock));
                            for (int i = 0; i < ts.CatchBlocks.Length; i++)
                                list.Add(new Child($"CatchBlocks[{i}]", ts.CatchBlocks[i]));
                            if (ts.FinallyBlockOpt != null)
                                list.Add(new Child("FinallyBlock", ts.FinallyBlockOpt));
                            return list.ToArray();
                        }

                    case BoundCatchBlock cb:
                        return new[]
                        {
                            new Child("Filter", cb.FilterOpt),
                            new Child("Body", cb.Body),
                        };

                    case BoundIncrementDecrementExpression id:
                        return new[]
                        {
                            new Child("Target", id.Target),
                            new Child("Read", id.Read),
                            new Child("Value", id.Value),
                        };

                    case BoundIsPatternExpression ip:
                        return new[]
                        {
                            new Child("Operand", ip.Operand),
                        };

                    default:
                        return Array.Empty<Child>();
                }
            }

            /// <summary>Formats syntax and error metadata shared by all nodes</summary>
            private string FormatMeta(BoundNode node)
            {
                var parts = new List<string>(4);

                if (_opt.IncludeHasErrors)
                    parts.Add($"HasErrors={node.HasErrors}");

                if (_opt.IncludeSyntaxKind && node.Syntax is not null)
                    parts.Add($"Syntax={node.Syntax.Kind}");

                if (_opt.IncludeSyntaxSpan && node.Syntax is not null)
                {
                    var sp = node.Syntax.Span;
                    parts.Add($"Span=[{sp.Start}..{sp.End})");
                }

                if (parts.Count == 0)
                    return "";

                return "  {" + string.Join(", ", parts) + "}";
            }
            private static string FormatSymbol(Symbol? s)
            {
                if (s is null) return "<null-symbol>";
                return s switch
                {
                    MethodSymbol m => FormatMethod(m),
                    LocalSymbol l => FormatLocal(l),
                    ParameterSymbol p => FormatParameter(p),
                    NamedTypeSymbol nt => nt.Name,
                    _ => s.ToString() ?? s.GetType().Name
                };
            }
            /// <summary>Formats type, constant, conversion, and intrinsic expression metadata</summary>
            private string FormatExprExtras(BoundExpression expr)
            {
                var parts = new List<string>(2);

                if (_opt.IncludeTypes)
                    parts.Add($"Type={FormatType(expr.Type)}");

                if (_opt.IncludeConstants && expr.ConstantValueOpt.HasValue)
                    parts.Add($"Const={FormatConst(expr.ConstantValueOpt.Value)}");

                if (parts.Count == 0)
                    return "";

                return "  [" + string.Join(", ", parts) + "]";
            }

            /// <summary>Formats compact node-specific data shown on the header line</summary>
            private string FormatInline(BoundNode node)
            {
                switch (node)
                {
                    case BoundMethodBody mb:
                        return $"Method={FormatMethod(mb.Method)}";

                    case BoundLocalDeclarationStatement ld:
                        return $"Local={FormatLocal(ld.Local)}";

                    case BoundLiteralExpression lit:
                        return $"Value={FormatConst(lit.Value)}";

                    case BoundLocalExpression le:
                        return $"Local={FormatLocal(le.Local)}";

                    case BoundParameterExpression pe:
                        return $"Parameter={FormatParameter(pe.Parameter)}";

                    case BoundThrowStatement ts:
                        return $"Rethrow={(ts.ExpressionOpt is null ? "true" : "false")}";

                    case BoundConversionExpression ce:
                        {
                            var parts = new List<string>(2);
                            if (_opt.IncludeConversionKind)
                                parts.Add($"Conversion={ce.Conversion.Kind} (implicit={ce.Conversion.IsImplicit})");
                            if (ce.IsChecked)
                                parts.Add("Checked=true");
                            return parts.Count == 0 ? "" : string.Join(", ", parts);
                        }

                    case BoundAsExpression ae:
                        {
                            var parts = new List<string>(2) { "Operator=as" };
                            if (_opt.IncludeConversionKind)
                                parts.Add($"Conversion={ae.Conversion.Kind} (implicit={ae.Conversion.IsImplicit})");
                            return string.Join(", ", parts);
                        }

                    case BoundUnaryExpression un:
                        {
                            var text = $"Operator={FormatUnaryOperator(un.OperatorKind)}";
                            if (un.IsChecked) text += ", Checked=true";
                            return text;
                        }

                    case BoundBinaryExpression bin:
                        {
                            var text = $"Operator={FormatBinaryOperator(bin.OperatorKind)}";
                            if (bin.IsChecked) text += ", Checked=true";
                            return text;
                        }

                    case BoundCallExpression call:
                        {
                            var m = FormatMethod(call.Method);
                            if (_opt.IncludeIntrinsicInfo && call.Method is IntrinsicMethodSymbol im)
                                m += $" <intrinsic: {im.IntrinsicName}>";
                            return $"Method={m}";
                        }

                    case BoundObjectCreationExpression oc:
                        {
                            var ctor = oc.ConstructorOpt is null ? "<default>" : FormatMethod(oc.ConstructorOpt);
                            return $"Ctor={ctor}, Args={oc.Arguments.Length}";
                        }

                    case BoundUnboundImplicitObjectCreationExpression uioc:
                        return $"Ctor=<unbound>, Args={uioc.Arguments.Length}";

                    case BoundCompoundAssignmentExpression ca:
                        {
                            var text = $"Operator={FormatBinaryOperator(ca.OperatorKind)}";
                            if (ca.IsChecked) text += ", Checked=true";
                            return text;
                        }

                    case BoundIncrementDecrementExpression id:
                        {
                            var op = id.IsIncrement ? "++" : "--";
                            var form = id.IsPostfix ? "postfix" : "prefix";
                            var text = $"Operator={op}, Form={form}";
                            if (id.IsChecked) text += ", Checked=true";
                            return text;
                        }

                    case BoundBreakStatement bs:
                        return $"Target={FormatLabel(bs.TargetLabel)}";

                    case BoundContinueStatement cs:
                        return $"Target={FormatLabel(cs.TargetLabel)}";

                    case BoundWhileStatement wh:
                        return $"BreakLabel={FormatLabel(wh.BreakLabel)}, ContinueLabel={FormatLabel(wh.ContinueLabel)}";

                    case BoundDoWhileStatement dw:
                        return $"BreakLabel={FormatLabel(dw.BreakLabel)}, ContinueLabel={FormatLabel(dw.ContinueLabel)}";

                    case BoundForStatement fs:
                        return $"BreakLabel={FormatLabel(fs.BreakLabel)}, ContinueLabel={FormatLabel(fs.ContinueLabel)}";

                    case BoundThisExpression te:
                        return $"ContainingType={FormatType(te.ContainingType)}";

                    case BoundLabelExpression labelExpr:
                        return $"Label={FormatLabel(labelExpr.Label)}";

                    case BoundLabelStatement ls:
                        return $"Label={FormatLabel(ls.Label)}";

                    case BoundGotoStatement gs:
                        return $"Target={FormatLabel(gs.TargetLabel)}";

                    case BoundArrayInitializerExpression ai:
                        return $"Length={ai.Elements.Length}";

                    case BoundArrayCreationExpression ac:
                        return $"ElementType={FormatType(ac.ElementType)}, Rank={ac.DimensionSizes.Length}, HasInitializer={(ac.InitializerOpt != null ? "true" : "false")}";

                    case BoundArrayElementAccessExpression aea:
                        return $"Rank={aea.Indices.Length}, IsLValue={(aea.IsLValue ? "true" : "false")}";

                    case BoundInlineArrayElementAccessExpression iae:
                        return $"Field={FormatSymbol(iae.ElementField)}, Length={iae.Length}, IsLValue={(iae.IsLValue ? "true" : "false")}";

                    case BoundNullCoalescingAssignmentExpression:
                        return "Operator=??=";

                    case BoundStackAllocArrayCreationExpression sa:
                        return $"ElementType={FormatType(sa.ElementType)}";

                    case BoundRefExpression:
                        return "Kind=ref";

                    case BoundSizeOfExpression so:
                        return $"OperandType={FormatType(so.OperandType)}";

                    case BoundConditionalGotoStatement cg:
                        return $"Target={FormatLabel(cg.TargetLabel)}, JumpIfTrue={(cg.JumpIfTrue ? "true" : "false")}";

                    case BoundSequenceExpression seq:
                        return $"Locals={FormatLocals(seq.Locals)}, SideEffects={FormatCount(seq.SideEffects)}, ValueType={FormatType(seq.Value.Type)}";

                    case BoundTupleExpression t:
                        return $"Arity={t.Elements.Length}{FormatTupleNames(t.ElementNames)}";

                    case BoundMemberAccessExpression ma:
                        return $"Member={FormatSymbol(ma.Member)}, IsLValue={(ma.IsLValue ? "true" : "false")}";

                    case BoundIndexerAccessExpression ia:
                        return $"Indexer={ia.Indexer.Name}, Args={ia.Arguments.Length}, IsLValue={(ia.IsLValue ? "true" : "false")}";

                    case BoundIsPatternExpression ip:
                        {
                            var declaredLocal = ip.DeclaredLocalOpt is null
                                ? "<none>"
                                : FormatLocal(ip.DeclaredLocalOpt);
                            return $"PatternType={ip.PatternKind}, DeclaredLocal={declaredLocal}, IsDiscard={(ip.IsDiscard ? "true" : "false")}";
                        }

                    case BoundLocalFunctionStatement lfs:
                        return $"LocalFunction={FormatMethod(lfs.LocalFunction)}";

                    case BoundTryStatement ts:
                        return $"CatchBlocks={ts.CatchBlocks.Length}, HasFinally={(ts.FinallyBlockOpt != null ? "true" : "false")}";

                    case BoundCatchBlock cb:
                        return $"ExceptionType={FormatType(cb.ExceptionType)}";

                    case BoundCheckedStatement:
                        return "Context=checked";

                    case BoundUncheckedStatement:
                        return "Context=unchecked";

                    case BoundCheckedExpression:
                        return "Context=checked";

                    case BoundUncheckedExpression:
                        return "Context=unchecked";

                    default:
                        return "";
                }
            }
            private static string FormatLabel(LabelSymbol l)
                => l is null ? "<null-label>" : l.Name;
            private static string FormatUnaryOperator(BoundUnaryOperatorKind op)
                => op.ToString();
            private static string FormatBinaryOperator(BoundBinaryOperatorKind op)
                => op.ToString();

            private static string FormatType(TypeSymbol t)
                => t is null ? "<null-type>" : t.Name;

            private static string FormatLocal(LocalSymbol l)
            {
                if (l is null) return "<null-local>";

                var text = $"{l.Name}: {FormatType(l.Type)}";

                if (l.IsConst)
                {
                    text = "const " + text;
                    if (l.ConstantValueOpt.HasValue)
                        text += $" = {FormatConst(l.ConstantValueOpt.Value)}";
                }

                return text;
            }
            private static string FormatMethod(MethodSymbol m)
            {
                if (m is null) return "<null-method>";

                var containing = FormatContaining(m.ContainingSymbol);

                var name = m.Name ?? "<unnamed>";

                var typeArgs = m.TypeArguments;
                if (!typeArgs.IsDefaultOrEmpty)
                {
                    var sb = new StringBuilder();
                    sb.Append(name);
                    sb.Append('<');
                    for (int i = 0; i < typeArgs.Length; i++)
                    {
                        if (i != 0) sb.Append(", ");
                        sb.Append(FormatType(typeArgs[i]));
                    }
                    sb.Append('>');
                    name = sb.ToString();
                }

                var ret = FormatType(m.ReturnType);

                var ps = m.Parameters;
                if (ps.IsDefault) return $"{containing}{name}(): {ret}";

                var args = new string[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    args[i] = FormatParameter(ps[i]);

                return $"{containing}{name}({string.Join(", ", args)}): {ret}";
            }

            private static string FormatContaining(Symbol? s)
                => s is NamedTypeSymbol nt ? nt.Name + "." : "";

            private static string FormatParameter(ParameterSymbol p)
                => p is null ? "<null-param>" : $"{p.Name}: {FormatType(p.Type)}";

            private static string FormatConst(object? value)
            {
                if (value is null) return "null";
                if (value is string s) return Quote(s);
                if (value is char c) return $"'{EscapeChar(c)}'";
                if (value is bool b) return b ? "true" : "false";
                return value.ToString() ?? "<const>";
            }
            private static string FormatCount<T>(System.Collections.Immutable.ImmutableArray<T> arr)
                => arr.IsDefault ? "<default>" : arr.Length.ToString();

            private static string FormatLocals(System.Collections.Immutable.ImmutableArray<LocalSymbol> locals)
            {
                if (locals.IsDefault) return "<default>";
                if (locals.Length == 0) return "0";

                var take = locals.Length <= 4 ? locals.Length : 4;
                var names = new string[take];
                for (int i = 0; i < take; i++)
                    names[i] = locals[i]?.Name ?? "<null>";

                if (take == locals.Length)
                    return $"{locals.Length} ({string.Join(", ", names)})";

                return $"{locals.Length} ({string.Join(", ", names)}, …)";
            }

            private static string FormatTupleNames(System.Collections.Immutable.ImmutableArray<string?> names)
            {
                if (names.IsDefaultOrEmpty) return "";

                bool any = false;
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i] != null) { any = true; break; }
                }
                if (!any) return "";

                var take = names.Length <= 6 ? names.Length : 6;
                var arr = new string[take];
                for (int i = 0; i < take; i++)
                    arr[i] = names[i] ?? "_";

                if (take == names.Length)
                    return $" Names=({string.Join(", ", arr)})";

                return $" Names=({string.Join(", ", arr)}, …)";
            }
            private static string Quote(string s)
                => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            private static string EscapeChar(char c)
            {
                return c switch
                {
                    '\\' => "\\\\",
                    '\'' => "\\'",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => c.ToString()
                };
            }
        }
    }
}
