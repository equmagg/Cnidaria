_start:
    li sp, ${ZBOOT_ZS3_STACK_ADDRESS} # Temporary M mode stack, default 0x80110000
    # All non-delegated traps enter machine_trap_vector in direct mtvec mode
    la t0, machine_trap_vector
    csrrw zero, mtvec, t0
    # Delegate common synchronous traps to S-mode
    li t0, 0x0000b1ff
    li t1, 512
    not t1, t1
    and t0, t0, t1
    csrrw zero, medeleg, t0
    # Delegate supervisor software, timer, and external interrupts to S-mode
    li t0, 546
    csrrw zero, mideleg, t0
    # Allow S-mode to read the cycle/time/instret counters
    li t0, -1
    csrrw zero, mcounteren, t0
    # Enable machine timer interrupts
    li t0, 128
    csrrs zero, mie, t0
    csrrw zero, mepc, a2 # FSBL passes the supervisor entry point in a2
    li t0, ${ZBOOT_ZS3_STACK_ADDRESS}
    csrrw zero, mscratch, t0
    # Prepare mret to enter S-mode
    csrrs t0, mstatus, zero
    li t1, 6144
    not t1, t1
    and t0, t0, t1
    li t1, 2048
    or t0, t0, t1
    csrrw zero, mstatus, t0
    mret
machine_trap_vector:
    # Swap from the interrupted S-mode stack to the M-mode stack
    csrrw sp, mscratch, sp
    addi sp, sp, -128
    sd ra, 0(sp)
    sd t0, 8(sp)
    sd t1, 16(sp)
    sd t2, 24(sp)
    sd t3, 32(sp)
    sd t4, 40(sp)
    sd t5, 48(sp)
    sd t6, 56(sp)
    sd a0, 64(sp)
    sd a1, 72(sp)
    sd a2, 80(sp)
    sd a3, 88(sp)
    sd a4, 96(sp)
    sd a5, 104(sp)
    sd a6, 112(sp)
    sd a7, 120(sp)
    # mcause[63] distinguishes interrupts from synchronous exceptions
    csrrs t0, mcause, zero
    srli t1, t0, 63
    bnez t1, machine_interrupt
    # The only synchronous trap handled is ECALL from S-mode
    li t1, 9
    bne t0, t1, machine_panic
    j handle_sbi_ecall
machine_interrupt:
    # Strip the interrupt flag and accept only machine timer interrupts
    li t1, 0x7fffffffffffffff
    and t0, t0, t1
    li t1, 7
    bne t0, t1, machine_panic
    # Disable further MTIP delivery by moving mtimecmp to UINT64_MAX, then raise STIP for S-mode
    li t0, -1
    li t1, ${ZBOOT_CLINT_MTIMECMP_ADDRESS}
    sd t0, 0(t1)
    li t0, 32
    csrrs zero, mip, t0
    j trap_return
handle_sbi_ecall:
    # SBI v0.2+ calls use extension id in a7 and function id in a6
    li t0, 1
    beq a7, t0, legacy_console_putchar
    li t0, 2
    beq a7, t0, legacy_console_getchar
    li t0, 0
    beq a7, t0, legacy_set_timer
    li t0, 16
    beq a7, t0, base_extension
    li t0, 0x54494d45
    beq a7, t0, time_extension
    li t0, 0x735049
    beq a7, t0, ipi_extension
    li t0, 0x52464e43
    beq a7, t0, rfence_extension
    li t0, 0x48534d
    beq a7, t0, hsm_extension
    li t0, 0x53525354
    beq a7, t0, system_reset_extension
    li a0, -2
    li a1, 0
    j sbi_return_pair
legacy_console_putchar:
    # 16550-compatible UART transmit register
    li t0, ${ZBOOT_UART_BASE}
    sb a0, 0(t0)
    li a0, 0
    li a1, 0
    j sbi_return_pair
legacy_console_getchar:
    # Line status register bit 0 tells whether a received byte is available
    li t0, ${ZBOOT_UART_LINE_STATUS_ADDRESS}
    lbu t1, 0(t0)
    andi t1, t1, 1
    beqz t1, legacy_console_getchar_empty
    li t0, ${ZBOOT_UART_BASE}
    lbu a0, 0(t0)
    li a1, 0
    j sbi_return_pair
legacy_console_getchar_empty:
    li a0, -1
    li a1, 0
    j sbi_return_pair
legacy_set_timer:
    # Legacy set_timer(a0=time_value)
    li t0, ${ZBOOT_CLINT_MTIMECMP_ADDRESS}
    sd a0, 0(t0)
    li t0, 32
    csrrc zero, mip, t0
    li a0, 0
    li a1, 0
    j sbi_return_pair
time_extension:
    # TIME extension, function 0: set_timer(stime_value)
    bnez a6, sbi_not_supported
    li t0, ${ZBOOT_CLINT_MTIMECMP_ADDRESS}
    sd a0, 0(t0)
    li t0, 32
    csrrc zero, mip, t0
    li a0, 0
    li a1, 0
    j sbi_return_pair
ipi_extension:
    # IPI extension, function 0: send_ipi
    bnez a6, sbi_not_supported
    li a0, 0
    li a1, 0
    j sbi_return_pair
rfence_extension:
    li a0, 0
    li a1, 0
    j sbi_return_pair
hsm_extension:
    # HSM function 2: hart_get_status
    li t0, 2
    beq a6, t0, hsm_hart_status
    j sbi_not_supported
hsm_hart_status:
    # SBI_HSM_STATE_STARTED
    li a0, 0
    li a1, 0
    j sbi_return_pair
system_reset_extension:
    bnez a6, sbi_not_supported
    j machine_panic
base_extension:
    beqz a6, base_get_spec_version
    li t0, 1
    beq a6, t0, base_get_impl_id
    li t0, 2
    beq a6, t0, base_get_impl_version
    li t0, 3
    beq a6, t0, base_probe_extension
    li t0, 4
    beq a6, t0, base_get_mvendorid
    li t0, 5
    beq a6, t0, base_get_marchid
    li t0, 6
    beq a6, t0, base_get_mimpid
    j sbi_not_supported
base_get_spec_version:
    # SBI spec version 0.2
    li a0, 0
    li a1, 2
    j sbi_return_pair
base_get_impl_id:
    # Private implementation id: ASCII "ZS3"
    li a0, 0
    li a1, 0x5a5333
    j sbi_return_pair
base_get_impl_version:
    li a0, 0
    li a1, 1
    j sbi_return_pair
base_probe_extension:
    # Return 1 only for extensions implemented
    li a1, 0
    li t0, 16
    beq a0, t0, base_probe_supported
    li t0, 0x54494d45
    beq a0, t0, base_probe_supported
    li t0, 0x735049
    beq a0, t0, base_probe_supported
    li t0, 0x52464e43
    beq a0, t0, base_probe_supported
    li t0, 0x48534d
    beq a0, t0, base_probe_supported
    li t0, 0x53525354
    beq a0, t0, base_probe_supported
    j base_probe_done
base_probe_supported:
    li a1, 1
base_probe_done:
    li a0, 0
    j sbi_return_pair
base_get_mvendorid:
    csrrs a1, mvendorid, zero
    li a0, 0
    j sbi_return_pair
base_get_marchid:
    csrrs a1, marchid, zero
    li a0, 0
    j sbi_return_pair
base_get_mimpid:
    csrrs a1, mimpid, zero
    li a0, 0
    j sbi_return_pair
sbi_not_supported:
    li a0, -2
    li a1, 0
sbi_return_pair:
    # SBI return convention: a0=error, a1=value
    sd a0, 64(sp)
    sd a1, 72(sp)
    # Skip the ECALL instruction before returning to S-mode
    csrrs t0, mepc, zero
    addi t0, t0, 4
    csrrw zero, mepc, t0
    j trap_return
machine_panic:
    ebreak
    wfi
    j machine_panic
trap_return:
    ld ra, 0(sp)
    ld t0, 8(sp)
    ld t1, 16(sp)
    ld t2, 24(sp)
    ld t3, 32(sp)
    ld t4, 40(sp)
    ld t5, 48(sp)
    ld t6, 56(sp)
    ld a0, 64(sp)
    ld a1, 72(sp)
    ld a2, 80(sp)
    ld a3, 88(sp)
    ld a4, 96(sp)
    ld a5, 104(sp)
    ld a6, 112(sp)
    ld a7, 120(sp)
    addi sp, sp, 128
    # Restore the interrupted S-mode sp and keep the M-mode stack in mscratch for the next trap
    csrrw sp, mscratch, sp
    mret
