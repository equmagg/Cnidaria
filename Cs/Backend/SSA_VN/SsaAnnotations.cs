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

            if (tree.LocalFieldBaseValue.HasValue)
            {
                tree.Source.AttachSsaLocalFieldBaseUse(tree.LocalFieldBaseValue.Value, tree.LocalField);
                AttachDescriptor(context, tree.Source, tree.LocalFieldBaseValue.Value.Slot);
            }

            tree.Source.AttachSsaMemory(tree.MemoryUses, tree.MemoryDefinitions);

            for (int i = 0; i < tree.Operands.Length; i++)
                AttachTree(context, tree.Operands[i]);

            if (tree.StoreTarget.HasValue)
            {
                var target = tree.StoreTarget.Value;
                var info = GetSlotInfo(context, target.Slot);
                tree.Source.AttachSsaDefinition(target, info.Type, info.StackKind);
                AttachDescriptor(context, tree.Source, target.Slot);
            }
            else if (tree.LocalField is not null)
            {
                tree.Source.AttachSsaLocalField(tree.LocalField);
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
            if (!context.DescriptorBySlot.TryGetValue(slot, out var descriptor))
                return;

            if (node.LocalDescriptor is not null && SsaSlotMatchesDescriptor(slot, node.LocalDescriptor))
                return;

            if (node.SsaValueName.HasValue && !node.SsaValueName.Value.Slot.Equals(slot))
                return;

            if (node.SsaStoreTargetName.HasValue && !node.SsaStoreTargetName.Value.Slot.Equals(slot))
                return;

            node.LocalDescriptor = descriptor;
        }

        private static bool SsaSlotMatchesDescriptor(SsaSlot slot, GenLocalDescriptor descriptor)
        {
            if (slot.HasLclNum)
                return slot.LclNum == descriptor.LclNum;

            return descriptor.Kind switch
            {
                GenLocalKind.Argument => slot.Kind == SsaSlotKind.Arg && slot.Index == descriptor.Index,
                GenLocalKind.Local => slot.Kind == SsaSlotKind.Local && slot.Index == descriptor.Index,
                GenLocalKind.Temporary => slot.Kind == SsaSlotKind.Temp && slot.Index == descriptor.Index,
                _ => false,
            };
        }

    }
}
