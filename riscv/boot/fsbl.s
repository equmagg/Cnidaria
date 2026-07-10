_start: 
    li sp, ${ZBOOT_ZS2_STACK_ADDRESS} # FSBL private stack, default 0x80010000
    li s0, ${ZBOOT_BLOCK_DEVICE_BASE} # ZS storage command block base
    # Load the SBI image from storage[0x00000000..0x00010000) to RAM at 0x80100000
    li a0, ${ZBOOT_ZS3_LOAD_ADDRESS}
    li a1, ${ZBOOT_ZS3_STORAGE_OFFSET}
    li a2, ${ZBOOT_ZS3_IMAGE_SIZE}
    jal ra, load_block
    # Load the supervisor payload from storage[0x00010000..0x00210000) to RAM at 0x80200000
    li a0, ${ZBOOT_SUPERVISOR_PAYLOAD_LOAD_ADDRESS}
    li a1, ${ZBOOT_SUPERVISOR_PAYLOAD_STORAGE_OFFSET}
    li a2, ${ZBOOT_SUPERVISOR_PAYLOAD_IMAGE_SIZE}
    jal ra, load_block
    # SBI entry ABI
    #   a0 = hart id
    #   a1 = FDT physical address
    #   a2 = supervisor payload entry point
    csrrs a0, mhartid, zero
    li a1, ${ZBOOT_DEVICE_TREE_LOAD_ADDRESS}
    li a2, ${ZBOOT_SUPERVISOR_PAYLOAD_LOAD_ADDRESS}
    # Transfer control to the SBI image loaded above
    li t0, ${ZBOOT_ZS3_LOAD_ADDRESS}
    jr t0
load_block:
    # Program the storage device
    addi sp, sp, -16
    sd ra, 0(sp)
    jal ra, virtio_block_init
    li t3, ${ZBOOT_VIRTIO_BLOCK_REQUEST_ADDRESS}
    li t4, ${ZBOOT_VIRTIO_QUEUE_DESCRIPTOR_ADDRESS}
    sw zero, 0(t3)
    sw zero, 4(t3)
    srli t0, a1, 9
    sd t0, 8(t3)
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
    sh zero, 0(t4)
    li t0, 1
    sh t0, 2(t4)
    sh zero, 4(t4)
    li t4, ${ZBOOT_VIRTIO_QUEUE_DEVICE_ADDRESS}
    sh zero, 0(t4)
    sh zero, 2(t4)
    fence rw, rw
    sw zero, 80(s0)
load_block_wait:
    lbu t1, 16(t3)
    li t2, 255
    beq t1, t2, load_block_wait
    bnez t1, zs2_panic
    lw t0, 96(s0)
    sw t0, 100(s0)
    ld ra, 0(sp)
    addi sp, sp, 16
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
    li t0, 1
    sw t0, 68(s0)
    li t0, 15
    sw t0, 112(s0)
    ret
zs2_panic:
    ebreak
    wfi
    j zs2_panic