_start:
    j kernel_boot_start

kernel_enter_user:
    mv t0, a0
    mv t1, a1
    li t2, ${ZKERNEL_TRAP_STACK_TOP}
    csrrw zero, sscratch, t2
    csrrs t3, sstatus, zero
    li t4, 256
    not t4, t4
    and t3, t3, t4
    li t4, 32
    or t3, t3, t4
    csrrw zero, sstatus, t3
    csrrw zero, sepc, t0
    mv sp, t1
    sret

kernel_boot_start:
    li sp, ${ZKERNEL_STACK_TOP}
    li t0, ${ZKERNEL_TRAP_STACK_TOP}
    csrrw zero, sscratch, t0
    la t0, supervisor_trap_vector
    csrrw zero, stvec, t0
    mv a0, a0
    mv a1, a1
    li t0, ${ZKERNEL_C_ENTRY_ADDRESS}
    jalr ra, t0, 0
kernel_boot_hang:
    wfi
    j kernel_boot_hang

supervisor_trap_vector:
    csrrw sp, sscratch, sp
    addi sp, sp, -1320
    addi sp, sp, -1320
    sd x0, 0(sp)
    sd x1, 8(sp)
    sd x5, 40(sp)
    csrrs t0, sscratch, zero
    sd t0, 16(sp)
    sd x3, 24(sp)
    sd x4, 32(sp)
    sd x6, 48(sp)
    sd x7, 56(sp)
    sd x8, 64(sp)
    sd x9, 72(sp)
    sd x10, 80(sp)
    sd x11, 88(sp)
    sd x12, 96(sp)
    sd x13, 104(sp)
    sd x14, 112(sp)
    sd x15, 120(sp)
    sd x16, 128(sp)
    sd x17, 136(sp)
    sd x18, 144(sp)
    sd x19, 152(sp)
    sd x20, 160(sp)
    sd x21, 168(sp)
    sd x22, 176(sp)
    sd x23, 184(sp)
    sd x24, 192(sp)
    sd x25, 200(sp)
    sd x26, 208(sp)
    sd x27, 216(sp)
    sd x28, 224(sp)
    sd x29, 232(sp)
    sd x30, 240(sp)
    sd x31, 248(sp)
    csrrs t0, sepc, zero
    sd t0, 256(sp)
    csrrs t0, sstatus, zero
    sd t0, 264(sp)
    csrrs t0, scause, zero
    sd t0, 272(sp)
    csrrs t0, stval, zero
    sd t0, 280(sp)
    csrrs t1, vtype, zero
    sd t1, 288(sp)
    csrrs t1, vl, zero
    sd t1, 296(sp)
    csrrs t1, vstart, zero
    sd t1, 304(sp)
    csrrs t1, vcsr, zero
    sd t1, 312(sp)
    csrrs t2, vlenb, zero
    slli t2, t2, 3
    addi t3, sp, 320
    vsetvli t1, zero, e8, m8
    vse8.v v0, (t3)
    add t3, t3, t2
    vse8.v v8, (t3)
    add t3, t3, t2
    vse8.v v16, (t3)
    add t3, t3, t2
    vse8.v v24, (t3)
    addi t3, sp, 2047
    addi t3, t3, 321
    fsd f0, 0(t3)
    fsd f1, 8(t3)
    fsd f2, 16(t3)
    fsd f3, 24(t3)
    fsd f4, 32(t3)
    fsd f5, 40(t3)
    fsd f6, 48(t3)
    fsd f7, 56(t3)
    fsd f8, 64(t3)
    fsd f9, 72(t3)
    fsd f10, 80(t3)
    fsd f11, 88(t3)
    fsd f12, 96(t3)
    fsd f13, 104(t3)
    fsd f14, 112(t3)
    fsd f15, 120(t3)
    fsd f16, 128(t3)
    fsd f17, 136(t3)
    fsd f18, 144(t3)
    fsd f19, 152(t3)
    fsd f20, 160(t3)
    fsd f21, 168(t3)
    fsd f22, 176(t3)
    fsd f23, 184(t3)
    fsd f24, 192(t3)
    fsd f25, 200(t3)
    fsd f26, 208(t3)
    fsd f27, 216(t3)
    fsd f28, 224(t3)
    fsd f29, 232(t3)
    fsd f30, 240(t3)
    fsd f31, 248(t3)
    csrrs t1, fcsr, zero
    sd t1, 256(t3)
    mv a0, sp
    li t0, ${ZKERNEL_TRAP_DISPATCH_ADDRESS}
    jalr ra, t0, 0
    ld t0, 256(sp)
    csrrw zero, sepc, t0
    ld t0, 264(sp)
    csrrw zero, sstatus, t0
    csrrs t2, vlenb, zero
    slli t2, t2, 3
    addi t3, sp, 320
    vsetvli t1, zero, e8, m8
    vle8.v v0, (t3)
    add t3, t3, t2
    vle8.v v8, (t3)
    add t3, t3, t2
    vle8.v v16, (t3)
    add t3, t3, t2
    vle8.v v24, (t3)
    ld t1, 288(sp)
    ld t2, 296(sp)
    vsetvl zero, t2, t1
    ld t1, 304(sp)
    csrrw zero, vstart, t1
    ld t1, 312(sp)
    csrrw zero, vcsr, t1
    addi t3, sp, 2047
    addi t3, t3, 321
    fld f0, 0(t3)
    fld f1, 8(t3)
    fld f2, 16(t3)
    fld f3, 24(t3)
    fld f4, 32(t3)
    fld f5, 40(t3)
    fld f6, 48(t3)
    fld f7, 56(t3)
    fld f8, 64(t3)
    fld f9, 72(t3)
    fld f10, 80(t3)
    fld f11, 88(t3)
    fld f12, 96(t3)
    fld f13, 104(t3)
    fld f14, 112(t3)
    fld f15, 120(t3)
    fld f16, 128(t3)
    fld f17, 136(t3)
    fld f18, 144(t3)
    fld f19, 152(t3)
    fld f20, 160(t3)
    fld f21, 168(t3)
    fld f22, 176(t3)
    fld f23, 184(t3)
    fld f24, 192(t3)
    fld f25, 200(t3)
    fld f26, 208(t3)
    fld f27, 216(t3)
    fld f28, 224(t3)
    fld f29, 232(t3)
    fld f30, 240(t3)
    fld f31, 248(t3)
    ld t1, 256(t3)
    csrrw zero, fcsr, t1
    ld x1, 8(sp)
    ld x3, 24(sp)
    ld x4, 32(sp)
    ld x8, 64(sp)
    ld x9, 72(sp)
    ld x10, 80(sp)
    ld x11, 88(sp)
    ld x12, 96(sp)
    ld x13, 104(sp)
    ld x14, 112(sp)
    ld x15, 120(sp)
    ld x16, 128(sp)
    ld x17, 136(sp)
    ld x18, 144(sp)
    ld x19, 152(sp)
    ld x20, 160(sp)
    ld x21, 168(sp)
    ld x22, 176(sp)
    ld x23, 184(sp)
    ld x24, 192(sp)
    ld x25, 200(sp)
    ld x26, 208(sp)
    ld x27, 216(sp)
    ld x28, 224(sp)
    ld x29, 232(sp)
    ld x30, 240(sp)
    ld x31, 248(sp)
    addi x5, sp, 1320
    addi x5, x5, 1320
    csrrw zero, sscratch, x5
    ld x5, 40(sp)
    ld x6, 48(sp)
    ld x7, 56(sp)
    ld x2, 16(sp)
    sret
