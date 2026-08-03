using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Cnidaria.RiscV
{
    public sealed class RiscVZBootLayout
    {
        public static readonly RiscVZBootLayout Default = new();
        public ulong Zs2LoadAddress { get; set; } = 0x80000000UL;
        public ulong Zs3LoadAddress { get; set; } = 0x80100000UL;
        public ulong SupervisorPayloadLoadAddress { get; set; } = 0x80200000UL;
        public ulong Zs2StackAddress { get; set; } = 0x80010000UL;
        public ulong Zs3StackAddress { get; set; } = 0x80110000UL;
        public ulong KernelLoadAddress { get; set; } = 0x80400000UL;
        public ulong KernelEntryAddress { get; set; } = 0x80400000UL;
        public ulong DeviceTreeLoadAddress { get; set; } = 0x87F00000UL;
        public ulong UBootStackAddress { get; set; } = 0x803F0000UL;
        public ulong BlockDeviceBase { get; set; } = 0x10001000UL;
        public ulong VirtioQueueDescriptorAddress { get; set; } = 0x80020000UL;
        public ulong VirtioQueueDriverAddress { get; set; } = 0x80020100UL;
        public ulong VirtioQueueDeviceAddress { get; set; } = 0x80020200UL;
        public ulong VirtioBlockRequestAddress { get; set; } = 0x80020300UL;
        public ulong FileSystemSectorBufferAddress { get; set; } = 0x80021000UL;
        public string KernelFileName { get; set; } = "KERNEL.BIN";
        public ulong UartBase { get; set; } = 0x10000000UL;
        public ulong ClintBase { get; set; } = 0x02000000UL;
        public ulong ClintMTimeCmpOffset { get; set; } = 0x4000UL;
        public int Zs3StorageOffset { get; set; } = 0x00010000;
        public int Zs3ImageSize { get; set; } = 0x00010000;
        public int SupervisorPayloadStorageOffset { get; set; } = 0x00020000;
        public int SupervisorPayloadImageSize { get; set; } = 0x00200000;
        public int KernelStorageOffset { get; set; } = 0x00400000;
        public int KernelImageSize { get; set; } = 0x01000000;
        public int KernelPartitionImageSize { get; set; } = 0x04000000;

        public ulong UartLineStatusAddress => checked(UartBase + 5UL);
        public ulong ClintMTimeCmpAddress => checked(ClintBase + ClintMTimeCmpOffset);

        public int RequiredSupervisorPayloadStorageSize => checked(SupervisorPayloadStorageOffset + SupervisorPayloadImageSize);
        public int RequiredBootChainStorageSize => checked(KernelStorageOffset + KernelPartitionImageSize);
    }


    public sealed class RiscVBootFile
    {
        public string FileName { get; }
        public byte[] Contents { get; }

        public RiscVBootFile(string fileName, byte[] contents)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Boot file name is empty.", nameof(fileName));
            FileName = fileName;
            Contents = contents ?? throw new ArgumentNullException(nameof(contents));
        }
    }

    public sealed class RiscVDeviceTreeLayout
    {
        public ulong RamBase { get; set; } = 0x80000000UL;
        public ulong RamSize { get; set; } = 128UL * 1024UL * 1024UL;
        public ulong UartBase { get; set; } = 0x10000000UL;
        public ulong UartSize { get; set; } = 0x100UL;
        public ulong ClintBase { get; set; } = 0x02000000UL;
        public ulong ClintSize { get; set; } = 0x00010000UL;
        public ulong PlicBase { get; set; } = 0x0C000000UL;
        public ulong PlicSize { get; set; } = 0x04000000UL;
        public ulong BlockDeviceBase { get; set; } = 0x10001000UL;
        public ulong BlockDeviceStride { get; set; } = 0x1000UL;
        public ulong BlockDeviceSize { get; set; } = RVMmioBlockDevice.RegisterWindowSize;
        public int BlockDeviceCount { get; set; } = 1;
        public bool KeyboardEnabled { get; set; } = true;
        public ulong KeyboardBase { get; set; } = 0x10009000UL;
        public ulong KeyboardSize { get; set; } = RVMmioKeyboard.RegisterWindowSize;
        public uint TimebaseFrequency { get; set; } = 10000000;
        public uint BootHartId { get; set; } = 0;
    }

    public sealed class RiscVKernelLayout
    {
        public ulong KernelLoadAddress { get; set; } = 0x80400000UL;
        public ulong KernelCLoadAddress { get; set; } = 0x80402000UL;
        public ulong KernelStackTop { get; set; } = 0x88000000UL;
        public ulong KernelTrapStackTop { get; set; } = 0x87F00000UL;
        public int AssemblyReserveSize { get; set; } = 0x2000;
        public ulong EnterUserAddress => checked(KernelLoadAddress + 4UL);
    }

    public static class RiscVZBoot
    {
        public static string DefaultZs2Source => ReadBootSource("fsbl.s");
        public static string DefaultZs3Source => ReadBootSource("sbi.s");
        public static string DefaultUbootSource => ReadBootSource("uboot.s");
        public static string DefaultKernelStartSource => ReadBootSource("kernel.s");

        private static string ReadBootSource(string fileName)
        {
            var asm = typeof(Cnidaria.RiscV.RiscVZBoot).Assembly;
            string resourceName = "Cnidaria.Targets.riscv.boot." + fileName;
            using (var s = asm.GetManifestResourceStream(resourceName))
            {
                if (s != null)
                {
                    using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    return r.ReadToEnd();
                }
            }

            throw new FileNotFoundException($"Boot source not found: {fileName}");
        }

        public static RiscVAssemblySettings CreateAssemblySettings(RiscVZBootLayout? layout = null)
        {
            layout ??= new RiscVZBootLayout();
            var kernelName = BuildFatShortName(layout.KernelFileName);
            ulong name0 = 0;
            for (int i = 0; i < 8; i++)
                name0 |= (ulong)kernelName[i] << (i * 8);
            int name1 = kernelName[8] | (kernelName[9] << 8);
            int name2 = kernelName[10];

            return new RiscVAssemblySettings()
                .Define("ZBOOT_ZS2_LOAD_ADDRESS", layout.Zs2LoadAddress)
                .Define("ZBOOT_ZS3_LOAD_ADDRESS", layout.Zs3LoadAddress)
                .Define("ZBOOT_SUPERVISOR_PAYLOAD_LOAD_ADDRESS", layout.SupervisorPayloadLoadAddress)
                .Define("ZBOOT_ZS2_STACK_ADDRESS", layout.Zs2StackAddress)
                .Define("ZBOOT_ZS3_STACK_ADDRESS", layout.Zs3StackAddress)
                .Define("ZBOOT_KERNEL_LOAD_ADDRESS", layout.KernelLoadAddress)
                .Define("ZBOOT_KERNEL_ENTRY_ADDRESS", layout.KernelEntryAddress)
                .Define("ZBOOT_DEVICE_TREE_LOAD_ADDRESS", layout.DeviceTreeLoadAddress)
                .Define("ZBOOT_UBOOT_STACK_ADDRESS", layout.UBootStackAddress)
                .Define("ZBOOT_BLOCK_DEVICE_BASE", layout.BlockDeviceBase)
                .Define("ZBOOT_VIRTIO_QUEUE_DESCRIPTOR_ADDRESS", layout.VirtioQueueDescriptorAddress)
                .Define("ZBOOT_VIRTIO_QUEUE_DRIVER_ADDRESS", layout.VirtioQueueDriverAddress)
                .Define("ZBOOT_VIRTIO_QUEUE_DEVICE_ADDRESS", layout.VirtioQueueDeviceAddress)
                .Define("ZBOOT_VIRTIO_BLOCK_REQUEST_ADDRESS", layout.VirtioBlockRequestAddress)
                .Define("ZBOOT_FS_SECTOR_BUFFER_ADDRESS", layout.FileSystemSectorBufferAddress)
                .Define("ZBOOT_KERNEL_NAME_0_7", name0)
                .Define("ZBOOT_KERNEL_NAME_8_9", name1)
                .Define("ZBOOT_KERNEL_NAME_10", name2)
                .Define("ZBOOT_UART_BASE", layout.UartBase)
                .Define("ZBOOT_UART_LINE_STATUS_ADDRESS", layout.UartLineStatusAddress)
                .Define("ZBOOT_CLINT_BASE", layout.ClintBase)
                .Define("ZBOOT_CLINT_MTIMECMP_ADDRESS", layout.ClintMTimeCmpAddress)
                .Define("ZBOOT_ZS3_STORAGE_OFFSET", layout.Zs3StorageOffset)
                .Define("ZBOOT_ZS3_IMAGE_SIZE", layout.Zs3ImageSize)
                .Define("ZBOOT_SUPERVISOR_PAYLOAD_STORAGE_OFFSET", layout.SupervisorPayloadStorageOffset)
                .Define("ZBOOT_SUPERVISOR_PAYLOAD_IMAGE_SIZE", layout.SupervisorPayloadImageSize)
                .Define("ZBOOT_KERNEL_STORAGE_OFFSET", layout.KernelStorageOffset)
                .Define("ZBOOT_KERNEL_IMAGE_SIZE", layout.KernelImageSize)
                .Define("ZBOOT_KERNEL_PARTITION_IMAGE_SIZE", layout.KernelPartitionImageSize);
        }
        public static void LoadDefaultBootChain(
            RiscVEmulator machine,
            RiscVZBootLayout? layout = null,
            IEnumerable<RiscVBootFile>? additionalBootFiles = null,
            byte[]? autorunSource = null)
        {
            if (machine is null)
                throw new ArgumentNullException(nameof(machine));
            layout ??= new RiscVZBootLayout();
            byte[] kernelImage = RiscVKernel.DefaultKernel;
            byte[] dtb = BuildDefaultDeviceTree(new RiscVDeviceTreeLayout
            {
                RamBase = machine.RamBase,
                RamSize = (ulong)machine.Ram.Length,
                UartBase = machine.Uart.BaseAddress,
                ClintBase = machine.Clint.BaseAddress,
                PlicBase = machine.Plic.BaseAddress,
                BlockDeviceBase = machine.BlockDevice?.BaseAddress ?? layout.BlockDeviceBase,
                BlockDeviceStride = machine.BlockDeviceStride,
                BlockDeviceCount = machine.BlockDevices.Length,
                KeyboardEnabled = machine.Keyboard != null,
                KeyboardBase = machine.Keyboard?.BaseAddress ?? 0x10009000UL,
                BootHartId = checked((uint)machine.HartId),
            });
            var assemblySettings = CreateAssemblySettings(layout);

            var bootFiles = MergeDefaultUserlandFiles(additionalBootFiles, autorunSource);
            byte[] zs2Image = RiscVAssembler
                .Assemble(DefaultZs2Source, RVTarget.Rv64GPrivileged, assemblySettings)
                .ToExecutableBytes(layout.Zs2LoadAddress);
            byte[] zs3Image = RiscVAssembler
                .Assemble(DefaultZs3Source, RVTarget.Rv64GPrivileged, assemblySettings)
                .ToExecutableBytes(layout.Zs3LoadAddress);
            byte[] ubootImage = BuildDefaultUbootImage(layout);

            LoadBootChain(
                machine,
                zs2Image,
                zs3Image,
                ubootImage,
                kernelImage,
                dtb,
                layout,
                bootFiles);
        }

        private static IReadOnlyList<RiscVBootFile> MergeDefaultUserlandFiles(
            IEnumerable<RiscVBootFile>? additionalBootFiles, byte[]? autorunSource)
        {
            var files = RiscVUserland.BuildDefaultBootFiles(autorunSource).ToList();
            if (additionalBootFiles is null)
                return files;

            foreach (var file in additionalBootFiles)
            {
                if (file is null)
                    throw new ArgumentException("Boot file collection contains null.", nameof(additionalBootFiles));
                var shortName = Encoding.ASCII.GetString(BuildFatShortName(file.FileName));
                var replaced = false;
                for (int i = 0; i < files.Count; i++)
                {
                    var existingShortName = Encoding.ASCII.GetString(BuildFatShortName(files[i].FileName));
                    if (!string.Equals(existingShortName, shortName, StringComparison.Ordinal))
                        continue;
                    files[i] = file;
                    replaced = true;
                    break;
                }
                if (!replaced)
                    files.Add(file);
            }

            return files;
        }

        public static byte[] BuildDefaultUbootImage(RiscVZBootLayout? layout = null, RVTarget? target = null)
        {
            layout ??= new RiscVZBootLayout();
            target ??= RVTarget.Rv64GPrivileged;
            return RiscVAssembler
                .Assemble(DefaultUbootSource, target, CreateAssemblySettings(layout))
                .ToExecutableBytes(layout.SupervisorPayloadLoadAddress);
        }

        public static string BuildDefaultUbootSource(RiscVZBootLayout? layout = null)
            => CreateAssemblySettings(layout).Expand(DefaultUbootSource);

        public static byte[] BuildZs2ImageFromSource(string source, RiscVZBootLayout? layout = null, RVTarget? target = null)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            layout ??= new RiscVZBootLayout();
            target ??= RVTarget.Rv64GPrivileged;
            return RiscVAssembler.Assemble(source, target, CreateAssemblySettings(layout)).ToExecutableBytes(layout.Zs2LoadAddress);
        }

        public static byte[] BuildZs3ImageFromSource(string source, RiscVZBootLayout? layout = null, RVTarget? target = null)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            layout ??= new RiscVZBootLayout();
            target ??= RVTarget.Rv64GPrivileged;
            return RiscVAssembler.Assemble(source, target, CreateAssemblySettings(layout)).ToExecutableBytes(layout.Zs3LoadAddress);
        }
        public static byte[] BuildUbootImageFromSource(string source, RiscVZBootLayout? layout = null, RVTarget? target = null)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            layout ??= new RiscVZBootLayout();
            target ??= RVTarget.Rv64GPrivileged;
            return RiscVAssembler.Assemble(source, target, CreateAssemblySettings(layout)).ToExecutableBytes(layout.SupervisorPayloadLoadAddress);
        }
        public static void LoadBootChain(
            RiscVEmulator machine,
            byte[] zs2Image,
            byte[] zs3Image,
            byte[] ubootImage,
            byte[] kernelImage,
            byte[] deviceTreeBlob,
            RiscVZBootLayout? layout = null,
            IEnumerable<RiscVBootFile>? additionalBootFiles = null)
        {
            if (machine is null)
                throw new ArgumentNullException(nameof(machine));
            if (zs2Image is null)
                throw new ArgumentNullException(nameof(zs2Image));
            if (zs3Image is null)
                throw new ArgumentNullException(nameof(zs3Image));
            if (ubootImage is null)
                throw new ArgumentNullException(nameof(ubootImage));
            if (kernelImage is null)
                throw new ArgumentNullException(nameof(kernelImage));
            if (deviceTreeBlob is null)
                throw new ArgumentNullException(nameof(deviceTreeBlob));

            layout ??= new RiscVZBootLayout();
            var block = machine.BlockDevice ?? throw new InvalidOperationException("Z-Boot requires a configured block device.");

            Array.Clear(block.Storage, 0, block.Storage.Length);
            CopyToStorage(block.Storage, layout.Zs3StorageOffset, layout.Zs3ImageSize, zs3Image, nameof(zs3Image));
            CopyToStorage(block.Storage, layout.SupervisorPayloadStorageOffset, layout.SupervisorPayloadImageSize, ubootImage, nameof(ubootImage));
            WriteKernelFat32Partition(block.Storage, layout, kernelImage, additionalBootFiles);

            machine.LoadImage(zs2Image, layout.Zs2LoadAddress, true);
            machine.LoadImage(deviceTreeBlob, layout.DeviceTreeLoadAddress, false);
        }

        public static byte[] BuildDefaultDeviceTree(RiscVDeviceTreeLayout? layout = null)
        {
            layout ??= new RiscVDeviceTreeLayout();
            var fdt = new RiscVFlattenedDeviceTreeBuilder();
            fdt.BeginNode(string.Empty);
            fdt.PropString("model", "RV64 virt machine");
            fdt.PropStringList("compatible", "riscv-virtio", "riscv,riscv-virt");
            fdt.PropU32("#address-cells", 2);
            fdt.PropU32("#size-cells", 2);

            fdt.BeginNode("cpus");
            fdt.PropU32("#address-cells", 1);
            fdt.PropU32("#size-cells", 0);
            fdt.PropU32("timebase-frequency", layout.TimebaseFrequency);
            fdt.BeginNode("cpu@0");
            fdt.PropString("device_type", "cpu");
            fdt.PropStringList("compatible", "riscv", "riscv,rv64");
            fdt.PropString("riscv,isa", "rv64imafdsu_zicsr_zifencei");
            fdt.PropString("mmu-type", "riscv,sv39");
            fdt.PropU32("reg", 0);
            fdt.PropString("status", "okay");
            fdt.BeginNode("interrupt-controller");
            fdt.PropEmpty("interrupt-controller");
            fdt.PropU32("#interrupt-cells", 1);
            fdt.PropString("compatible", "riscv,cpu-intc");
            fdt.PropU32("phandle", 1);
            fdt.EndNode();
            fdt.EndNode();
            fdt.EndNode();

            fdt.BeginNode("aliases");
            fdt.PropString("serial0", "/soc/serial@" + layout.UartBase.ToString("x", CultureInfo.InvariantCulture));
            if (layout.BlockDeviceCount > 0)
                fdt.PropString("virtio0", "/soc/virtio_mmio@" + layout.BlockDeviceBase.ToString("x", CultureInfo.InvariantCulture));
            if (layout.KeyboardEnabled)
                fdt.PropString("keyboard0", "/soc/keyboard@" + layout.KeyboardBase.ToString("x", CultureInfo.InvariantCulture));
            fdt.EndNode();

            fdt.BeginNode("chosen");
            fdt.PropString("stdout-path", "serial0:115200n8");
            fdt.PropString("bootargs", "console=ttyS0");
            fdt.PropU32("boot-hartid", layout.BootHartId);
            fdt.EndNode();

            fdt.BeginNode("memory@" + layout.RamBase.ToString("x", CultureInfo.InvariantCulture));
            fdt.PropString("device_type", "memory");
            fdt.PropReg(layout.RamBase, layout.RamSize);
            fdt.EndNode();

            fdt.BeginNode("soc");
            fdt.PropString("compatible", "simple-bus");
            fdt.PropU32("#address-cells", 2);
            fdt.PropU32("#size-cells", 2);
            fdt.PropEmpty("ranges");


            fdt.BeginNode("serial@" + layout.UartBase.ToString("x", CultureInfo.InvariantCulture));
            fdt.PropString("compatible", "ns16550a");
            fdt.PropReg(layout.UartBase, layout.UartSize);
            fdt.PropU32("clock-frequency", 1843200);
            fdt.PropU32("current-speed", 115200);
            fdt.PropU32("reg-shift", 0);
            fdt.PropU32("reg-io-width", 1);
            fdt.PropU32("interrupt-parent", 2);
            fdt.PropU32("interrupts", 10);
            fdt.EndNode();

            fdt.BeginNode("clint@" + layout.ClintBase.ToString("x", CultureInfo.InvariantCulture));
            fdt.PropStringList("compatible", "riscv,clint0", "sifive,clint0");
            fdt.PropReg(layout.ClintBase, layout.ClintSize);
            fdt.PropU32("interrupts-extended", 1, 3, 1, 7);
            fdt.EndNode();

            fdt.BeginNode("plic@" + layout.PlicBase.ToString("x", CultureInfo.InvariantCulture));
            fdt.PropStringList("compatible", "sifive,plic-1.0.0", "riscv,plic0");
            fdt.PropReg(layout.PlicBase, layout.PlicSize);
            fdt.PropEmpty("interrupt-controller");
            fdt.PropU32("#interrupt-cells", 1);
            fdt.PropU32("phandle", 2);
            fdt.PropU32("riscv,ndev", 31);
            fdt.PropU32("interrupts-extended", 1, 11, 1, 9);
            fdt.EndNode();

            if (layout.BlockDeviceCount < 0 || layout.BlockDeviceCount > RVPlic.BlockDeviceSourceCount)
                throw new ArgumentOutOfRangeException(nameof(layout));
            for (int i = 0; i < layout.BlockDeviceCount; i++)
            {
                ulong blockAddress = checked(layout.BlockDeviceBase + layout.BlockDeviceStride * (ulong)i);
                fdt.BeginNode("virtio_mmio@" + blockAddress.ToString("x", CultureInfo.InvariantCulture));
                fdt.PropString("compatible", "virtio,mmio");
                fdt.PropReg(blockAddress, layout.BlockDeviceSize);
                fdt.PropU32("interrupt-parent", 2);
                fdt.PropU32("interrupts", checked((uint)(RVPlic.BlockDeviceFirstSource + i)));
                fdt.PropEmpty("dma-coherent");
                fdt.EndNode();
            }

            if (layout.KeyboardEnabled)
            {
                fdt.BeginNode("keyboard@" + layout.KeyboardBase.ToString("x", CultureInfo.InvariantCulture));
                fdt.PropString("compatible", "riscv-virtio,keyboard-mmio");
                fdt.PropReg(layout.KeyboardBase, layout.KeyboardSize);
                fdt.PropU32("interrupt-parent", 2);
                fdt.PropU32("interrupts", RVPlic.KeyboardSource);
                fdt.EndNode();
            }

            fdt.EndNode();
            fdt.EndNode();
            return fdt.Build();
        }
        private static void WriteKernelFat32Partition(byte[] storage, RiscVZBootLayout layout, byte[] kernelImage, IEnumerable<RiscVBootFile>? additionalBootFiles)
        {
            if (storage is null)
                throw new ArgumentNullException(nameof(storage));
            if (layout is null)
                throw new ArgumentNullException(nameof(layout));
            if (kernelImage is null)
                throw new ArgumentNullException(nameof(kernelImage));
            if (kernelImage.Length > layout.KernelImageSize)
                throw new ArgumentOutOfRangeException(nameof(kernelImage));
            var files = new List<RiscVBootFile> { new RiscVBootFile(layout.KernelFileName, kernelImage) };
            if (additionalBootFiles is not null)
                files.AddRange(additionalBootFiles);
            if (files.Count == 0 || files.Count > 16)
                throw new ArgumentOutOfRangeException(nameof(additionalBootFiles));
            var shortNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var shortName = Encoding.ASCII.GetString(BuildFatShortName(file.FileName));
                if (!shortNames.Add(shortName))
                    throw new ArgumentException("Duplicate FAT boot file name: " + file.FileName, nameof(additionalBootFiles));
            }
            if ((layout.KernelStorageOffset & ((int)RVMmioBlockDevice.SectorSize - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(layout));
            if ((layout.KernelPartitionImageSize & ((int)RVMmioBlockDevice.SectorSize - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(layout));
            if (layout.KernelStorageOffset < (int)RVMmioBlockDevice.SectorSize)
                throw new ArgumentOutOfRangeException(nameof(layout));
            if (layout.KernelStorageOffset > storage.Length || layout.KernelPartitionImageSize > storage.Length - layout.KernelStorageOffset)
                throw new ArgumentOutOfRangeException(nameof(layout));
            if (RangesOverlap(layout.Zs3StorageOffset, layout.Zs3ImageSize, layout.KernelStorageOffset, layout.KernelPartitionImageSize))
                throw new InvalidOperationException("The ZS3 image storage region overlaps the kernel filesystem partition.");
            if (RangesOverlap(layout.SupervisorPayloadStorageOffset, layout.SupervisorPayloadImageSize, layout.KernelStorageOffset, layout.KernelPartitionImageSize))
                throw new InvalidOperationException("The supervisor payload storage region overlaps the kernel filesystem partition.");

            int partitionOffset = layout.KernelStorageOffset;
            int partitionSize = layout.KernelPartitionImageSize;
            Array.Clear(storage, partitionOffset, partitionSize);

            const int sectorSize = (int)RVMmioBlockDevice.SectorSize;
            const uint sectorSizeU = (uint)RVMmioBlockDevice.SectorSize;
            const int reservedSectors = 32;
            const int sectorsPerCluster = 1;
            const int fatCount = 1;
            uint partitionStartLba = checked((uint)(partitionOffset / sectorSize));
            uint totalSectors = checked((uint)(partitionSize / sectorSize));
            if (totalSectors <= reservedSectors + 4)
                throw new ArgumentOutOfRangeException(nameof(layout));

            uint fatSectors = 1;
            uint clusterCount;
            while (true)
            {
                uint dataSectors = totalSectors - (uint)reservedSectors - fatSectors * (uint)fatCount;
                clusterCount = dataSectors / (uint)sectorsPerCluster;
                uint requiredFatSectors = checked(((clusterCount + 2) * 4 + sectorSizeU - 1) / sectorSizeU);
                if (requiredFatSectors <= fatSectors)
                    break;
                fatSectors = requiredFatSectors;
                if ((uint)reservedSectors + fatSectors * (uint)fatCount >= totalSectors)
                    throw new ArgumentOutOfRangeException(nameof(layout));
            }

            var clusterMap = new List<(RiscVBootFile File, uint FirstCluster, uint ClusterCount)>();
            const uint rootCluster = 2;
            uint nextFreeCluster = 3;
            uint usedFileClusters = 0;
            foreach (var file in files)
            {
                uint fileClusters = checked((uint)Math.Max(1L, (file.Contents.Length + (long)sectorSize * sectorsPerCluster - 1) / ((long)sectorSize * sectorsPerCluster)));
                clusterMap.Add((file, nextFreeCluster, fileClusters));
                nextFreeCluster = checked(nextFreeCluster + fileClusters);
                usedFileClusters = checked(usedFileClusters + fileClusters);
            }
            if (nextFreeCluster - 1 >= clusterCount + 2)
                throw new ArgumentOutOfRangeException(nameof(kernelImage));

            WriteMbr(storage, partitionStartLba, totalSectors);
            WriteFat32BootSector(storage.AsSpan(partitionOffset, sectorSize), partitionStartLba, totalSectors, fatSectors, rootCluster);
            WriteFat32FsInfo(storage.AsSpan(partitionOffset + sectorSize, sectorSize), clusterCount - usedFileClusters - 1, nextFreeCluster);
            storage.AsSpan(partitionOffset + 6 * sectorSize, sectorSize).Clear();
            storage.AsSpan(partitionOffset, sectorSize).CopyTo(storage.AsSpan(partitionOffset + 6 * sectorSize, sectorSize));

            int fatOffset = checked(partitionOffset + reservedSectors * sectorSize);
            WriteLe32(storage, fatOffset + 0, 0x0ffffff8U);
            WriteLe32(storage, fatOffset + 4, 0xffffffffU);
            WriteLe32(storage, fatOffset + (int)rootCluster * 4, 0x0fffffffU);
            foreach (var item in clusterMap)
            {
                uint lastCluster = checked(item.FirstCluster + item.ClusterCount - 1);
                for (uint cluster = item.FirstCluster; cluster <= lastCluster; cluster++)
                {
                    uint next = cluster == lastCluster ? 0x0fffffffU : cluster + 1;
                    WriteLe32(storage, checked(fatOffset + (int)cluster * 4), next);
                }
            }

            int dataOffset = checked(fatOffset + (int)fatSectors * fatCount * sectorSize);
            int rootOffset = checked(dataOffset + ((int)rootCluster - 2) * sectorsPerCluster * sectorSize);
            for (int i = 0; i < clusterMap.Count; i++)
            {
                var item = clusterMap[i];
                WriteDirectoryEntry(storage.AsSpan(rootOffset + i * 32, 32), item.File.FileName, item.FirstCluster, checked((uint)item.File.Contents.Length));
                int fileOffset = checked(dataOffset + ((int)item.FirstCluster - 2) * sectorsPerCluster * sectorSize);
                Buffer.BlockCopy(item.File.Contents, 0, storage, fileOffset, item.File.Contents.Length);
            }
        }

        private static void WriteMbr(byte[] storage, uint partitionStartLba, uint partitionSectors)
        {
            const int entry = 446;
            storage[entry + 0] = 0x80;
            storage[entry + 1] = 0xff;
            storage[entry + 2] = 0xff;
            storage[entry + 3] = 0xff;
            storage[entry + 4] = 0x0c;
            storage[entry + 5] = 0xff;
            storage[entry + 6] = 0xff;
            storage[entry + 7] = 0xff;
            WriteLe32(storage, entry + 8, partitionStartLba);
            WriteLe32(storage, entry + 12, partitionSectors);
            storage[510] = 0x55;
            storage[511] = 0xaa;
        }

        private static void WriteFat32BootSector(Span<byte> sector, uint hiddenSectors, uint totalSectors, uint fatSectors, uint rootCluster)
        {
            sector.Clear();
            sector[0] = 0xeb;
            sector[1] = 0x58;
            sector[2] = 0x90;
            WriteAscii(sector, 3, "EMULATOR", 8);
            WriteLe16(sector, 11, 512);
            sector[13] = 1;
            WriteLe16(sector, 14, 32);
            sector[16] = 1;
            WriteLe16(sector, 17, 0);
            WriteLe16(sector, 19, 0);
            sector[21] = 0xf8;
            WriteLe16(sector, 22, 0);
            WriteLe16(sector, 24, 32);
            WriteLe16(sector, 26, 64);
            WriteLe32(sector, 28, hiddenSectors);
            WriteLe32(sector, 32, totalSectors);
            WriteLe32(sector, 36, fatSectors);
            WriteLe16(sector, 40, 0);
            WriteLe16(sector, 42, 0);
            WriteLe32(sector, 44, rootCluster);
            WriteLe16(sector, 48, 1);
            WriteLe16(sector, 50, 6);
            sector[64] = 0x80;
            sector[66] = 0x29;
            WriteLe32(sector, 67, 0x434e4944U);
            WriteAscii(sector, 71, "EMULATOR   ", 11);
            WriteAscii(sector, 82, "FAT32   ", 8);
            sector[510] = 0x55;
            sector[511] = 0xaa;
        }

        private static void WriteFat32FsInfo(Span<byte> sector, uint freeClusters, uint nextFreeCluster)
        {
            sector.Clear();
            WriteLe32(sector, 0, 0x41615252U);
            WriteLe32(sector, 484, 0x61417272U);
            WriteLe32(sector, 488, freeClusters);
            WriteLe32(sector, 492, nextFreeCluster);
            sector[508] = 0;
            sector[509] = 0;
            sector[510] = 0x55;
            sector[511] = 0xaa;
        }

        private static void WriteDirectoryEntry(Span<byte> entry, string fileName, uint firstCluster, uint fileSize)
        {
            entry.Clear();
            var shortName = BuildFatShortName(fileName);
            shortName.AsSpan().CopyTo(entry);
            entry[11] = 0x20;
            WriteLe16(entry, 20, (ushort)(firstCluster >> 16));
            WriteLe16(entry, 26, (ushort)firstCluster);
            WriteLe32(entry, 28, fileSize);
        }

        private static byte[] BuildFatShortName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Kernel file name is empty.", nameof(fileName));
            string name = fileName.Trim().ToUpperInvariant();
            int dot = name.IndexOf('.');
            string stem = dot < 0 ? name : name.Substring(0, dot);
            string ext = dot < 0 ? string.Empty : name.Substring(dot + 1);
            if (stem.Length == 0 || stem.Length > 8 || ext.Length > 3 || ext.IndexOf('.') >= 0)
                throw new ArgumentException("Kernel file name must be an 8.3 FAT name.", nameof(fileName));
            var result = new byte[11];
            Array.Fill(result, (byte)' ');
            for (int i = 0; i < stem.Length; i++)
                result[i] = FatShortNameByte(stem[i], nameof(fileName));
            for (int i = 0; i < ext.Length; i++)
                result[8 + i] = FatShortNameByte(ext[i], nameof(fileName));
            return result;
        }

        private static byte FatShortNameByte(char c, string paramName)
        {
            if (c >= 'A' && c <= 'Z')
                return (byte)c;
            if (c >= '0' && c <= '9')
                return (byte)c;
            if (c is '_' or '$' or '~' or '!' or '#' or '%' or '&' or '-' or '@' or '^' or '`' or '{' or '}' or '(' or ')')
                return (byte)c;
            throw new ArgumentException("Kernel file name contains a character that is not valid in a FAT 8.3 alias.", paramName);
        }

        private static bool RangesOverlap(int aOffset, int aLength, int bOffset, int bLength)
        {
            long aStart = aOffset;
            long aEnd = aStart + aLength;
            long bStart = bOffset;
            long bEnd = bStart + bLength;
            return aStart < bEnd && bStart < aEnd;
        }

        private static void WriteAscii(Span<byte> target, int offset, string text, int length)
        {
            for (int i = 0; i < length; i++)
                target[offset + i] = i < text.Length ? checked((byte)text[i]) : (byte)' ';
        }

        private static void WriteLe16(byte[] target, int offset, ushort value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteLe16(Span<byte> target, int offset, ushort value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteLe32(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteLe32(Span<byte> target, int offset, uint value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }

        private static void CopyToStorage(byte[] storage, int offset, int regionSize, byte[] image, string imageName)
        {
            if (offset < 0 || regionSize < 0 || offset > storage.Length || regionSize > storage.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (image.Length > regionSize)
                throw new ArgumentOutOfRangeException(imageName);

            Array.Clear(storage, offset, regionSize);
            Buffer.BlockCopy(image, 0, storage, offset, image.Length);
        }
    }

    internal sealed class RiscVFlattenedDeviceTreeBuilder
    {
        private const uint FdtMagic = 0xD00DFEED;
        private const uint FdtBeginNode = 1;
        private const uint FdtEndNode = 2;
        private const uint FdtProp = 3;
        private const uint FdtEnd = 9;

        private readonly List<byte> _structure = new List<byte>();
        private readonly List<byte> _strings = new List<byte>();
        private readonly Dictionary<string, int> _stringOffsets = new Dictionary<string, int>(StringComparer.Ordinal);

        public void BeginNode(string name)
        {
            WriteU32(_structure, FdtBeginNode);
            WriteString(_structure, name ?? string.Empty);
            Align(_structure, 4);
        }

        public void EndNode()
            => WriteU32(_structure, FdtEndNode);

        public void PropEmpty(string name)
            => Prop(name, Array.Empty<byte>());

        public void PropString(string name, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            var data = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
            Prop(name, data);
        }

        public void PropStringList(string name, params string[] values)
        {
            var data = new List<byte>();
            foreach (var value in values)
            {
                var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
                data.AddRange(bytes);
                data.Add(0);
            }
            Prop(name, data.ToArray());
        }

        public void PropU32(string name, params uint[] values)
        {
            var data = new List<byte>(values.Length * 4);
            foreach (var value in values)
                WriteU32(data, value);
            Prop(name, data.ToArray());
        }

        public void PropReg(ulong address, ulong size)
        {
            var data = new List<byte>(16);
            WriteCell64(data, address);
            WriteCell64(data, size);
            Prop("reg", data.ToArray());
        }

        public byte[] Build()
        {
            WriteU32(_structure, FdtEnd);
            var reserve = new List<byte>(16);
            WriteCell64(reserve, 0);
            WriteCell64(reserve, 0);

            const int headerSize = 40;
            var offMemRsvmap = headerSize;
            var offDtStruct = offMemRsvmap + reserve.Count;
            var offDtStrings = offDtStruct + _structure.Count;
            var totalSize = offDtStrings + _strings.Count;

            var result = new List<byte>(totalSize);
            WriteU32(result, FdtMagic);
            WriteU32(result, checked((uint)totalSize));
            WriteU32(result, checked((uint)offDtStruct));
            WriteU32(result, checked((uint)offDtStrings));
            WriteU32(result, checked((uint)offMemRsvmap));
            WriteU32(result, 17);
            WriteU32(result, 16);
            WriteU32(result, 0);
            WriteU32(result, checked((uint)_strings.Count));
            WriteU32(result, checked((uint)_structure.Count));
            result.AddRange(reserve);
            result.AddRange(_structure);
            result.AddRange(_strings);
            return result.ToArray();
        }

        private void Prop(string name, byte[] data)
        {
            var nameOffset = GetStringOffset(name);
            WriteU32(_structure, FdtProp);
            WriteU32(_structure, checked((uint)data.Length));
            WriteU32(_structure, checked((uint)nameOffset));
            _structure.AddRange(data);
            Align(_structure, 4);
        }

        private int GetStringOffset(string name)
        {
            if (_stringOffsets.TryGetValue(name, out var offset))
                return offset;
            offset = _strings.Count;
            WriteString(_strings, name);
            _stringOffsets.Add(name, offset);
            return offset;
        }

        private static void WriteCell64(List<byte> bytes, ulong value)
        {
            WriteU32(bytes, (uint)(value >> 32));
            WriteU32(bytes, (uint)value);
        }

        private static void WriteU32(List<byte> bytes, uint value)
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        private static void WriteString(List<byte> bytes, string value)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(value ?? string.Empty));
            bytes.Add(0);
        }

        private static void Align(List<byte> bytes, int alignment)
        {
            while ((bytes.Count & (alignment - 1)) != 0)
                bytes.Add(0);
        }
    }
    public struct RiscVUserlandLayout
    {
        public static readonly RiscVUserlandLayout Default = new();
        public ulong ImageBase { get; set; } = 0x00010000UL;
        public RiscVUserlandLayout(ulong imageBase)
        {
            ImageBase = imageBase;
        }
        public static bool operator ==(RiscVUserlandLayout left, RiscVUserlandLayout right)
            => left.ImageBase == right.ImageBase;
        public static bool operator !=(RiscVUserlandLayout left, RiscVUserlandLayout right)
            => !(left == right);
        public override bool Equals(object? obj)
            => obj is RiscVUserlandLayout l && this.ImageBase == l.ImageBase;
        public override int GetHashCode() => ImageBase.GetHashCode();
    }

    public static class RiscVUserland
    {
        public static string RuntimeSource => ReadUserSource("runtime.c");
        public static string DefaultInitSource => ReadUserSource("init.c");
        public static string DefaultShellSource => ReadUserSource("shell.c");
        public static string DefaultAutorunSource => ReadUserSource("autorun.c");
        public static readonly byte[] DefaultInit = BuildProgram(DefaultInitSource, "riscv/os/init.c");
        public static readonly byte[] DefaultShell = BuildProgram(DefaultShellSource, "riscv/os/shell.c");
        public static readonly byte[] DefaultAutorun = BuildProgram(DefaultAutorunSource, "riscv/os/autorun.c");

        public static ImmutableArray<RiscVBootFile> BuildDefaultBootFiles(
            byte[]? autorunSource = null)
        {
            return ImmutableArray.Create(
                    new RiscVBootFile("INIT.ELF", DefaultInit),
                    new RiscVBootFile("SHELL.ELF", DefaultShell),
                    autorunSource == null
                    ? new RiscVBootFile("AUTORUN.ELF", DefaultAutorun)
                    : new RiscVBootFile("AUTORUN.ELF", autorunSource));
        }

        public static byte[] BuildProgram(
            string source,
            string filePath = "riscv/user/program.c",
            RiscVUserlandLayout? layout = null)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("User program path is empty.", nameof(filePath));
            layout ??= new RiscVUserlandLayout();
            var program = CompileProgram(RuntimeSource + "\n" + source, filePath);
            return program.ToLinuxExecutableBytes(layout.Value.ImageBase);
        }

        private static string ReadUserSource(string fileName)
        {
            var asm = typeof(RiscVUserland).Assembly;
            string resourceName = "Cnidaria.Targets.riscv.os." + fileName;
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null)
                throw new FileNotFoundException($"User source not found: {fileName}");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private static RiscVProgram CompileProgram(string source, string filePath)
        {
            var options = new Cnidaria.C.CompilationOptions(Cnidaria.C.TargetInfo.RV64GLinux);
            var compilation = Cnidaria.C.Compilation.CreateFromSource(
                source,
                filePath: filePath,
                includeStandardHeaders: true,
                options: options);
            var diagnostics = compilation.GetDiagnostics()
                .Where(static d => d.Severity == Cnidaria.C.DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .ToArray();
            if (diagnostics.Length != 0)
                throw new InvalidOperationException($"User compilation failed: {string.Join('\n', diagnostics)}");
            var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees[0]);
            var cfg = Cnidaria.C.ControlFlowGraph.Build(semanticModel);
            var ssa = Cnidaria.C.SsaGraph.Build(cfg);
            var lir = Cnidaria.C.LirModule.Lower(ssa);
            return Cnidaria.C.RiscVCodeGenerator.Generate(lir);
        }
    }

    public static class RiscVKernel
    {
        public static string DefaultKernelSource => ReadKernelSource("kernel.c");
        public static readonly byte[] DefaultKernel = BuildDefaultKernelImage();
        private static string ReadKernelSource(string fileName)
        {
            var asm = typeof(RiscVKernel).Assembly;
            string resourceName = "Cnidaria.Targets.riscv.os." + fileName;
            using (var s = asm.GetManifestResourceStream(resourceName))
            {
                if (s != null)
                {
                    using var r = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    return r.ReadToEnd();
                }
            }

            throw new FileNotFoundException($"kernel source not found: {fileName}");
        }
        public static byte[] BuildDefaultKernelImage(RiscVKernelLayout? layout = null)
           => BuildKernelImage(RiscVZBoot.DefaultKernelStartSource, DefaultKernelSource, layout);
        public static string GetKernelDissasembly(RiscVKernelLayout? layout = null)
        {
            layout ??= new RiscVKernelLayout();
            var cProgram = CompileKernel(DefaultKernelSource);
            return RiscVDisassembler.Disassemble(cProgram);
        }
        public static byte[] BuildKernelImage(string startSource, string kernelSource, RiscVKernelLayout? layout = null)
        {
            if (startSource is null)
                throw new ArgumentNullException(nameof(startSource));
            if (kernelSource is null)
                throw new ArgumentNullException(nameof(kernelSource));

            layout ??= new RiscVKernelLayout();
            if (layout.KernelCLoadAddress <= layout.KernelLoadAddress)
                throw new ArgumentOutOfRangeException(nameof(layout));
            if (layout.KernelCLoadAddress - layout.KernelLoadAddress != (ulong)layout.AssemblyReserveSize)
                throw new ArgumentOutOfRangeException(nameof(layout));

            var cProgram = CompileKernel(kernelSource);
            var cExternalSymbols = new Dictionary<string, ulong>(StringComparer.Ordinal)
            {
                ["kernel_enter_user"] = layout.EnterUserAddress,
            };
            var cImage = cProgram.LinkFlat(layout.KernelCLoadAddress, cExternalSymbols);
            var cEntry = ResolveRequiredSymbol(cImage, "kernel_main");
            var trapDispatch = ResolveRequiredSymbol(cImage, "kernel_trap_dispatch");

            var startSettings = new RiscVAssemblySettings()
                .Define("ZKERNEL_STACK_TOP", layout.KernelStackTop)
                .Define("ZKERNEL_TRAP_STACK_TOP", layout.KernelTrapStackTop)
                .Define("ZKERNEL_C_ENTRY_ADDRESS", cEntry)
                .Define("ZKERNEL_TRAP_DISPATCH_ADDRESS", trapDispatch);
            var startImage = RiscVAssembler
                .Assemble(startSource, RVTarget.Rv64GPrivileged, startSettings)
                .LinkFlat(layout.KernelLoadAddress);

            if (startImage.Bytes.Length > layout.AssemblyReserveSize)
                throw new InvalidOperationException("kernel assembly prologue exceeds its reserved image region.");

            var image = new byte[checked(layout.AssemblyReserveSize + cImage.Bytes.Length)];
            Copy(startImage.Bytes, image, 0);
            Copy(cImage.Bytes, image, layout.AssemblyReserveSize);
            return image;
        }

        private static RiscVProgram CompileKernel(string source)
        {
            var options = new Cnidaria.C.CompilationOptions(Cnidaria.C.TargetInfo.RiscV64.WithFeatures(
                TargetArchitectureFeatures.RiscVG | 
                TargetArchitectureFeatures.RiscVPrivileged | 
                TargetArchitectureFeatures.RiscVV | 
                TargetArchitectureFeatures.RiscVB));
            var compilation = Cnidaria.C.Compilation.CreateFromSource(
                source,
                filePath: "riscv/os/kernel.c",
                includeStandardHeaders: false,
                options: options);
            var diagnostics = compilation.GetDiagnostics()
                .Where(static d => d.Severity == Cnidaria.C.DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .ToArray();
            if (diagnostics.Length != 0)
                throw new InvalidOperationException($"kernel C compilation failed: {string.Join('\n', diagnostics)}");
            var semanticModel = compilation.GetSemanticModel(compilation.SyntaxTrees[0]);
            var cfg = Cnidaria.C.ControlFlowGraph.Build(semanticModel);
            var ssa = Cnidaria.C.SsaGraph.Build(cfg);
            var lir = Cnidaria.C.LirModule.Lower(ssa);
            return Cnidaria.C.RiscVCodeGenerator.Generate(lir);
        }

        private static ulong ResolveRequiredSymbol(RVLinkedImage image, string name)
        {
            if (image.SymbolAddresses.TryGetValue(name, out var address))
                return address;
            throw new InvalidOperationException("kernel symbol was not emitted: " + name);
        }

        private static void Copy(ImmutableArray<byte> source, byte[] destination, int offset)
        {
            for (int i = 0; i < source.Length; i++)
                destination[offset + i] = source[i];
        }
    }
}
