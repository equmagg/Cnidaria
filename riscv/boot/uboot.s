_start:
    li sp, ${ZBOOT_UBOOT_STACK_ADDRESS}
    mv s1, a0
    mv s2, a1
    li s0, ${ZBOOT_BLOCK_DEVICE_BASE}
    jal ra, virtio_block_init
    jal ra, load_kernel_image
    mv a0, s1
    mv a1, s2
    li t0, ${ZBOOT_KERNEL_ENTRY_ADDRESS}
    jr t0

load_kernel_image:
    addi sp, sp, -16
    sd ra, 0(sp)
    li a0, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    li a1, 0
    li a2, 512
    jal ra, disk_read_lba
    li t0, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    lhu t1, 510(t0)
    li t2, 0xaa55
    beq t1, t2, mbr_signature_ok
    j uboot_panic
mbr_signature_ok:
    addi t0, t0, 446
    li t3, 4
mbr_partition_loop:
    lbu t1, 4(t0)
    li t2, 0x0b
    beq t1, t2, mbr_partition_found
    li t2, 0x0c
    beq t1, t2, mbr_partition_found
    addi t0, t0, 16
    addi t3, t3, -1
    bnez t3, mbr_partition_loop
    j uboot_panic
mbr_partition_found:
    lbu s3, 8(t0)
    lbu t1, 9(t0)
    slli t1, t1, 8
    or s3, s3, t1
    lbu t1, 10(t0)
    slli t1, t1, 16
    or s3, s3, t1
    lbu t1, 11(t0)
    slli t1, t1, 24
    or s3, s3, t1
    bnez s3, mbr_partition_nonzero
    j uboot_panic
mbr_partition_nonzero:
    li a0, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    mv a1, s3
    li a2, 512
    jal ra, disk_read_lba
    li t0, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    lhu t1, 510(t0)
    li t2, 0xaa55
    beq t1, t2, fat_boot_signature_ok
    j uboot_panic
fat_boot_signature_ok:
    lbu t1, 11(t0)
    lbu t2, 12(t0)
    slli t2, t2, 8
    or t1, t1, t2
    li t2, 512
    beq t1, t2, fat_sector_size_ok
    j uboot_panic
fat_sector_size_ok:
    lbu s6, 13(t0)
    bnez s6, fat_spc_ok
    j uboot_panic
fat_spc_ok:
    lhu t1, 14(t0)
    lbu t2, 16(t0)
    lbu t3, 17(t0)
    lbu t4, 18(t0)
    slli t4, t4, 8
    or t3, t3, t4
    beqz t3, fat_root_entries_ok
    j uboot_panic
fat_root_entries_ok:
    lhu t3, 22(t0)
    beqz t3, fat_uses_fat32_size
    j uboot_panic
fat_uses_fat32_size:
    lwu s9, 36(t0)
    bnez s9, fat_size_ok
    j uboot_panic
fat_size_ok:
    lwu s7, 44(t0)
    li t3, 2
    bgeu s7, t3, fat_root_cluster_ok
    j uboot_panic
fat_root_cluster_ok:
    add s4, s3, t1
    mul t3, t2, s9
    add s5, s4, t3
    slli s8, s6, 9
    mv a0, s7
    jal ra, find_kernel_in_directory
    bnez a0, kernel_file_found
    j uboot_panic
kernel_file_found:
    jal ra, load_file_cluster_chain
    ld ra, 0(sp)
    addi sp, sp, 16
    ret

find_kernel_in_directory:
    addi sp, sp, -16
    sd ra, 0(sp)
    mv s10, a0
find_dir_cluster_loop:
    li s11, 0
find_dir_sector_loop:
    bltu s11, s6, find_dir_sector_read
    mv a0, s10
    jal ra, next_fat_cluster
    mv s10, a0
    li t0, 0x0ffffff8
    bltu s10, t0, find_dir_next_cluster_not_eoc
    li a0, 0
    li a1, 0
    j find_dir_return
find_dir_next_cluster_not_eoc:
    li t0, 2
    bgeu s10, t0, find_dir_cluster_loop
    li a0, 0
    li a1, 0
    j find_dir_return
find_dir_sector_read:
    mv a0, s10
    mv a1, s11
    jal ra, cluster_sector_lba
    mv a1, a0
    li a0, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    li a2, 512
    jal ra, disk_read_lba
    li t0, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    li t6, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    addi t6, t6, 512
find_dir_entry_loop:
    lbu t1, 0(t0)
    bnez t1, find_dir_entry_not_end
    li a0, 0
    li a1, 0
    j find_dir_return
find_dir_entry_not_end:
    li t2, 0xe5
    beq t1, t2, find_dir_next_entry
    lbu t1, 11(t0)
    andi t2, t1, 15
    li t3, 15
    beq t2, t3, find_dir_next_entry
    andi t2, t1, 8
    bnez t2, find_dir_next_entry
    ld t2, 0(t0)
    li t3, ${ZBOOT_KERNEL_NAME_0_7}
    bne t2, t3, find_dir_next_entry
    lhu t2, 8(t0)
    li t3, ${ZBOOT_KERNEL_NAME_8_9}
    bne t2, t3, find_dir_next_entry
    lbu t2, 10(t0)
    li t3, ${ZBOOT_KERNEL_NAME_10}
    bne t2, t3, find_dir_next_entry
    lhu t1, 20(t0)
    slli t1, t1, 16
    lhu t2, 26(t0)
    or a0, t1, t2
    lwu a1, 28(t0)
    j find_dir_return
find_dir_next_entry:
    addi t0, t0, 32
    bltu t0, t6, find_dir_entry_loop
    addi s11, s11, 1
    j find_dir_sector_loop
find_dir_return:
    ld ra, 0(sp)
    addi sp, sp, 16
    ret

load_file_cluster_chain:
    addi sp, sp, -32
    sd ra, 0(sp)
    mv s10, a0
    mv s11, a1
    li t0, ${ZBOOT_KERNEL_IMAGE_SIZE}
    bgeu t0, s11, load_file_size_ok
    j uboot_panic
load_file_size_ok:
    li t5, ${ZBOOT_KERNEL_LOAD_ADDRESS}
    sd t5, 8(sp)
load_file_cluster_loop:
    beqz s11, load_file_done
    li t0, 2
    bgeu s10, t0, load_file_cluster_min_ok
    j uboot_panic
load_file_cluster_min_ok:
    li t0, 0x0ffffff8
    bltu s10, t0, load_file_cluster_not_eoc
    j uboot_panic
load_file_cluster_not_eoc:
    mv t0, s8
    bgeu s11, t0, load_file_read_size_ready
    mv t0, s11
load_file_read_size_ready:
    addi t1, t0, 511
    srli t1, t1, 9
    slli t2, t1, 9
    sd t2, 16(sp)
    mv a0, s10
    li a1, 0
    jal ra, cluster_sector_lba
    mv a1, a0
    ld a0, 8(sp)
    ld a2, 16(sp)
    jal ra, disk_read_lba
    ld t5, 8(sp)
    ld t2, 16(sp)
    add t5, t5, t2
    sd t5, 8(sp)
    bgeu s11, t2, load_file_subtract_remaining
    li s11, 0
    j load_file_cluster_loop
load_file_subtract_remaining:
    sub s11, s11, t2
    beqz s11, load_file_done
    mv a0, s10
    jal ra, next_fat_cluster
    mv s10, a0
    j load_file_cluster_loop
load_file_done:
    ld ra, 0(sp)
    addi sp, sp, 32
    ret

cluster_sector_lba:
    addi a0, a0, -2
    mul a0, a0, s6
    add a0, a0, a1
    add a0, a0, s5
    ret

next_fat_cluster:
    addi sp, sp, -16
    sd ra, 0(sp)
    slli t0, a0, 2
    srli t1, t0, 9
    andi t2, t0, 511
    sd t2, 8(sp)
    add a1, s4, t1
    li a0, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    li a2, 512
    jal ra, disk_read_lba
    ld t2, 8(sp)
    li t3, ${ZBOOT_FS_SECTOR_BUFFER_ADDRESS}
    add t3, t3, t2
    lwu a0, 0(t3)
    li t0, 0x0fffffff
    and a0, a0, t0
    ld ra, 0(sp)
    addi sp, sp, 16
    ret

disk_read_lba:
    li t3, ${ZBOOT_VIRTIO_BLOCK_REQUEST_ADDRESS}
    li t4, ${ZBOOT_VIRTIO_QUEUE_DESCRIPTOR_ADDRESS}
    sw zero, 0(t3)
    sw zero, 4(t3)
    sd a1, 8(t3)
    li t0, 255
    sb t0, 16(t3)
    sd t3, 0(t4)
    li t0, 16
    sw t0, 8(t4)
    li t0, 1
    sh t0, 12(t4)
    sh t0, 14(t4)
    sd a0, 16(t4)
    sw a2, 24(t4)
    li t0, 3
    sh t0, 28(t4)
    li t0, 2
    sh t0, 30(t4)
    addi t5, t3, 16
    sd t5, 32(t4)
    li t0, 1
    sw t0, 40(t4)
    li t0, 2
    sh t0, 44(t4)
    sh zero, 46(t4)
    li t4, ${ZBOOT_VIRTIO_QUEUE_DRIVER_ADDRESS}
    lhu t0, 2(t4)
    andi t1, t0, 7
    slli t1, t1, 1
    add t2, t4, t1
    sh zero, 4(t2)
    addi t0, t0, 1
    sh t0, 2(t4)
    fence rw, rw
    sw zero, 80(s0)
disk_read_lba_wait:
    lbu t1, 16(t3)
    li t2, 255
    beq t1, t2, disk_read_lba_wait
    beqz t1, disk_read_lba_ok
    j uboot_panic
disk_read_lba_ok:
    lw t0, 96(s0)
    sw t0, 100(s0)
    ret

virtio_block_init:
    sw zero, 112(s0)
    li t0, 1
    sw t0, 112(s0)
    li t0, 3
    sw t0, 112(s0)
    sw zero, 36(s0)
    sw zero, 32(s0)
    li t0, 1
    sw t0, 36(s0)
    sw t0, 32(s0)
    li t0, 11
    sw t0, 112(s0)
    sw zero, 48(s0)
    li t0, 8
    sw t0, 56(s0)
    li t0, ${ZBOOT_VIRTIO_QUEUE_DESCRIPTOR_ADDRESS}
    sw t0, 128(s0)
    srli t0, t0, 32
    sw t0, 132(s0)
    li t0, ${ZBOOT_VIRTIO_QUEUE_DRIVER_ADDRESS}
    sw t0, 144(s0)
    srli t0, t0, 32
    sw t0, 148(s0)
    li t0, ${ZBOOT_VIRTIO_QUEUE_DEVICE_ADDRESS}
    sw t0, 160(s0)
    srli t0, t0, 32
    sw t0, 164(s0)
    li t0, ${ZBOOT_VIRTIO_QUEUE_DRIVER_ADDRESS}
    sh zero, 0(t0)
    sh zero, 2(t0)
    sh zero, 4(t0)
    li t0, ${ZBOOT_VIRTIO_QUEUE_DEVICE_ADDRESS}
    sh zero, 0(t0)
    sh zero, 2(t0)
    li t0, 1
    sw t0, 68(s0)
    li t0, 15
    sw t0, 112(s0)
    ret

uboot_panic:
    ebreak
    wfi
    j uboot_panic
