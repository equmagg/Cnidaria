using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class SsaSourceAnnotations
    {
        private sealed class AttachContext
        {
            public readonly SsaMethod Method;
            public readonly Dictionary<SsaSlot, SsaSlotInfo> SlotInfoBySlot;
            public readonly Dictionary<SsaSlot, GenLocalDescriptor> DescriptorBySlot;

            public AttachContext(SsaMethod method)
            {
                Method = method;
                SlotInfoBySlot = new Dictionary<SsaSlot, SsaSlotInfo>(method.Slots.Length);
                DescriptorBySlot = new Dictionary<SsaSlot, GenLocalDescriptor>();

                for (int i = 0; i < method.Slots.Length; i++)
                    SlotInfoBySlot[method.Slots[i].Slot] = method.Slots[i];

                AddDescriptors(method.GenTreeMethod.ArgDescriptors);
                AddDescriptors(method.GenTreeMethod.LocalDescriptors);
                AddDescriptors(method.GenTreeMethod.TempDescriptors);
            }

            private void AddDescriptors(ImmutableArray<GenLocalDescriptor> descriptors)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var descriptor = descriptors[i];
                    DescriptorBySlot[new SsaSlot(descriptor)] = descriptor;
                }
            }
        }
        public static void Clear(GenTreeMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    ClearTree(statements[s]);
            }
        }

        public static void Attach(SsaMethod method)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            Clear(method.GenTreeMethod);

            var context = new AttachContext(method);

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    AttachTree(context, statements[s]);
            }
        }


        private static void ClearTree(GenTree node)
        {
            node.ClearSsaAnnotation();
            for (int i = 0; i < node.Operands.Length; i++)
                ClearTree(node.Operands[i]);
        }

        private static void AttachTree(AttachContext context, SsaTree tree)
        {
            if (tree.Value.HasValue)
            {
                tree.Source.AttachSsaUse(tree.Value.Value);
                AttachDescriptor(context, tree.Source, tree.Value.Value.Slot);
            }

            for (int i = 0; i < tree.Operands.Length; i++)
                AttachTree(context, tree.Operands[i]);

            if (tree.StoreTarget.HasValue)
            {
                var target = tree.StoreTarget.Value;
                var info = GetSlotInfo(context, target.Slot);
                tree.Source.AttachSsaDefinition(target, info.Type, info.StackKind);
                AttachDescriptor(context, tree.Source, target.Slot);
            }
        }

        private static SsaSlotInfo GetSlotInfo(AttachContext context, SsaSlot slot)
        {
            if (context.SlotInfoBySlot.TryGetValue(slot, out var info))
                return info;

            return new SsaSlotInfo(
                slot,
                null,
                GenStackKind.Unknown,
                addressExposed: true,
                memoryAliased: true,
                category: GenLocalCategory.AddressExposedLocal);
        }

        private static void AttachDescriptor(AttachContext context, GenTree node, SsaSlot slot)
        {
            if (node.LocalDescriptor is not null)
                return;

            if (context.DescriptorBySlot.TryGetValue(slot, out var descriptor))
                node.LocalDescriptor = descriptor;
        }

        private static bool TryGetDescriptor(GenTreeMethod method, SsaSlot slot, out GenLocalDescriptor descriptor)
        {
            if (slot.HasLclNum)
            {
                var all = method.AllLocalDescriptors;
                if ((uint)slot.LclNum < (uint)all.Length)
                {
                    descriptor = all[slot.LclNum];
                    return descriptor.Kind switch
                    {
                        GenLocalKind.Argument => slot.Kind == SsaSlotKind.Arg,
                        GenLocalKind.Local => slot.Kind == SsaSlotKind.Local,
                        GenLocalKind.Temporary => slot.Kind == SsaSlotKind.Temp,
                        _ => false,
                    };
                }
            }

            switch (slot.Kind)
            {
                case SsaSlotKind.Arg:
                    if ((uint)slot.Index < (uint)method.ArgDescriptors.Length)
                    {
                        descriptor = method.ArgDescriptors[slot.Index];
                        return true;
                    }
                    break;
                case SsaSlotKind.Local:
                    if ((uint)slot.Index < (uint)method.LocalDescriptors.Length)
                    {
                        descriptor = method.LocalDescriptors[slot.Index];
                        return true;
                    }
                    break;
                case SsaSlotKind.Temp:
                    for (int i = 0; i < method.TempDescriptors.Length; i++)
                    {
                        if (method.TempDescriptors[i].Index == slot.Index)
                        {
                            descriptor = method.TempDescriptors[i];
                            return true;
                        }
                    }
                    break;
            }

            descriptor = null!;
            return false;
        }
    }
}
