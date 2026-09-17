#ifndef __RISCV_VECTOR_H
#define __RISCV_VECTOR_H

#include <stddef.h>
#include <stdint.h>

#ifdef __riscv_vector

/* One declaration set per element width and length multiplier, expanded at the end of this header */

typedef __rvv_bool1_t vbool1_t;
typedef __rvv_bool2_t vbool2_t;
typedef __rvv_bool4_t vbool4_t;
typedef __rvv_bool8_t vbool8_t;
typedef __rvv_bool16_t vbool16_t;
typedef __rvv_bool32_t vbool32_t;
typedef __rvv_bool64_t vbool64_t;

typedef __rvv_int8mf8_t vint8mf8_t;
typedef __rvv_uint8mf8_t vuint8mf8_t;
typedef __rvv_int8mf4_t vint8mf4_t;
typedef __rvv_uint8mf4_t vuint8mf4_t;
typedef __rvv_int8mf2_t vint8mf2_t;
typedef __rvv_uint8mf2_t vuint8mf2_t;
typedef __rvv_int8m1_t vint8m1_t;
typedef __rvv_uint8m1_t vuint8m1_t;
typedef __rvv_int8m2_t vint8m2_t;
typedef __rvv_uint8m2_t vuint8m2_t;
typedef __rvv_int8m4_t vint8m4_t;
typedef __rvv_uint8m4_t vuint8m4_t;
typedef __rvv_int8m8_t vint8m8_t;
typedef __rvv_uint8m8_t vuint8m8_t;
typedef __rvv_int16mf4_t vint16mf4_t;
typedef __rvv_uint16mf4_t vuint16mf4_t;
typedef __rvv_int16mf2_t vint16mf2_t;
typedef __rvv_uint16mf2_t vuint16mf2_t;
typedef __rvv_int16m1_t vint16m1_t;
typedef __rvv_uint16m1_t vuint16m1_t;
typedef __rvv_int16m2_t vint16m2_t;
typedef __rvv_uint16m2_t vuint16m2_t;
typedef __rvv_int16m4_t vint16m4_t;
typedef __rvv_uint16m4_t vuint16m4_t;
typedef __rvv_int16m8_t vint16m8_t;
typedef __rvv_uint16m8_t vuint16m8_t;
typedef __rvv_int32mf2_t vint32mf2_t;
typedef __rvv_uint32mf2_t vuint32mf2_t;
typedef __rvv_float32mf2_t vfloat32mf2_t;
typedef __rvv_int32m1_t vint32m1_t;
typedef __rvv_uint32m1_t vuint32m1_t;
typedef __rvv_float32m1_t vfloat32m1_t;
typedef __rvv_int32m2_t vint32m2_t;
typedef __rvv_uint32m2_t vuint32m2_t;
typedef __rvv_float32m2_t vfloat32m2_t;
typedef __rvv_int32m4_t vint32m4_t;
typedef __rvv_uint32m4_t vuint32m4_t;
typedef __rvv_float32m4_t vfloat32m4_t;
typedef __rvv_int32m8_t vint32m8_t;
typedef __rvv_uint32m8_t vuint32m8_t;
typedef __rvv_float32m8_t vfloat32m8_t;
typedef __rvv_int64m1_t vint64m1_t;
typedef __rvv_uint64m1_t vuint64m1_t;
typedef __rvv_float64m1_t vfloat64m1_t;
typedef __rvv_int64m2_t vint64m2_t;
typedef __rvv_uint64m2_t vuint64m2_t;
typedef __rvv_float64m2_t vfloat64m2_t;
typedef __rvv_int64m4_t vint64m4_t;
typedef __rvv_uint64m4_t vuint64m4_t;
typedef __rvv_float64m4_t vfloat64m4_t;
typedef __rvv_int64m8_t vint64m8_t;
typedef __rvv_uint64m8_t vuint64m8_t;
typedef __rvv_float64m8_t vfloat64m8_t;

typedef __rvv_uint8mf8x2_t vuint8mf8x2_t;
typedef __rvv_int8mf8x2_t vint8mf8x2_t;
typedef __rvv_uint8mf8x3_t vuint8mf8x3_t;
typedef __rvv_int8mf8x3_t vint8mf8x3_t;
typedef __rvv_uint8mf8x4_t vuint8mf8x4_t;
typedef __rvv_int8mf8x4_t vint8mf8x4_t;
typedef __rvv_uint8mf8x5_t vuint8mf8x5_t;
typedef __rvv_int8mf8x5_t vint8mf8x5_t;
typedef __rvv_uint8mf8x6_t vuint8mf8x6_t;
typedef __rvv_int8mf8x6_t vint8mf8x6_t;
typedef __rvv_uint8mf8x7_t vuint8mf8x7_t;
typedef __rvv_int8mf8x7_t vint8mf8x7_t;
typedef __rvv_uint8mf8x8_t vuint8mf8x8_t;
typedef __rvv_int8mf8x8_t vint8mf8x8_t;
typedef __rvv_uint8mf4x2_t vuint8mf4x2_t;
typedef __rvv_int8mf4x2_t vint8mf4x2_t;
typedef __rvv_uint8mf4x3_t vuint8mf4x3_t;
typedef __rvv_int8mf4x3_t vint8mf4x3_t;
typedef __rvv_uint8mf4x4_t vuint8mf4x4_t;
typedef __rvv_int8mf4x4_t vint8mf4x4_t;
typedef __rvv_uint8mf4x5_t vuint8mf4x5_t;
typedef __rvv_int8mf4x5_t vint8mf4x5_t;
typedef __rvv_uint8mf4x6_t vuint8mf4x6_t;
typedef __rvv_int8mf4x6_t vint8mf4x6_t;
typedef __rvv_uint8mf4x7_t vuint8mf4x7_t;
typedef __rvv_int8mf4x7_t vint8mf4x7_t;
typedef __rvv_uint8mf4x8_t vuint8mf4x8_t;
typedef __rvv_int8mf4x8_t vint8mf4x8_t;
typedef __rvv_uint8mf2x2_t vuint8mf2x2_t;
typedef __rvv_int8mf2x2_t vint8mf2x2_t;
typedef __rvv_uint8mf2x3_t vuint8mf2x3_t;
typedef __rvv_int8mf2x3_t vint8mf2x3_t;
typedef __rvv_uint8mf2x4_t vuint8mf2x4_t;
typedef __rvv_int8mf2x4_t vint8mf2x4_t;
typedef __rvv_uint8mf2x5_t vuint8mf2x5_t;
typedef __rvv_int8mf2x5_t vint8mf2x5_t;
typedef __rvv_uint8mf2x6_t vuint8mf2x6_t;
typedef __rvv_int8mf2x6_t vint8mf2x6_t;
typedef __rvv_uint8mf2x7_t vuint8mf2x7_t;
typedef __rvv_int8mf2x7_t vint8mf2x7_t;
typedef __rvv_uint8mf2x8_t vuint8mf2x8_t;
typedef __rvv_int8mf2x8_t vint8mf2x8_t;
typedef __rvv_uint8m1x2_t vuint8m1x2_t;
typedef __rvv_int8m1x2_t vint8m1x2_t;
typedef __rvv_uint8m1x3_t vuint8m1x3_t;
typedef __rvv_int8m1x3_t vint8m1x3_t;
typedef __rvv_uint8m1x4_t vuint8m1x4_t;
typedef __rvv_int8m1x4_t vint8m1x4_t;
typedef __rvv_uint8m1x5_t vuint8m1x5_t;
typedef __rvv_int8m1x5_t vint8m1x5_t;
typedef __rvv_uint8m1x6_t vuint8m1x6_t;
typedef __rvv_int8m1x6_t vint8m1x6_t;
typedef __rvv_uint8m1x7_t vuint8m1x7_t;
typedef __rvv_int8m1x7_t vint8m1x7_t;
typedef __rvv_uint8m1x8_t vuint8m1x8_t;
typedef __rvv_int8m1x8_t vint8m1x8_t;
typedef __rvv_uint8m2x2_t vuint8m2x2_t;
typedef __rvv_int8m2x2_t vint8m2x2_t;
typedef __rvv_uint8m2x3_t vuint8m2x3_t;
typedef __rvv_int8m2x3_t vint8m2x3_t;
typedef __rvv_uint8m2x4_t vuint8m2x4_t;
typedef __rvv_int8m2x4_t vint8m2x4_t;
typedef __rvv_uint8m4x2_t vuint8m4x2_t;
typedef __rvv_int8m4x2_t vint8m4x2_t;
typedef __rvv_uint16mf4x2_t vuint16mf4x2_t;
typedef __rvv_int16mf4x2_t vint16mf4x2_t;
typedef __rvv_uint16mf4x3_t vuint16mf4x3_t;
typedef __rvv_int16mf4x3_t vint16mf4x3_t;
typedef __rvv_uint16mf4x4_t vuint16mf4x4_t;
typedef __rvv_int16mf4x4_t vint16mf4x4_t;
typedef __rvv_uint16mf4x5_t vuint16mf4x5_t;
typedef __rvv_int16mf4x5_t vint16mf4x5_t;
typedef __rvv_uint16mf4x6_t vuint16mf4x6_t;
typedef __rvv_int16mf4x6_t vint16mf4x6_t;
typedef __rvv_uint16mf4x7_t vuint16mf4x7_t;
typedef __rvv_int16mf4x7_t vint16mf4x7_t;
typedef __rvv_uint16mf4x8_t vuint16mf4x8_t;
typedef __rvv_int16mf4x8_t vint16mf4x8_t;
typedef __rvv_uint16mf2x2_t vuint16mf2x2_t;
typedef __rvv_int16mf2x2_t vint16mf2x2_t;
typedef __rvv_uint16mf2x3_t vuint16mf2x3_t;
typedef __rvv_int16mf2x3_t vint16mf2x3_t;
typedef __rvv_uint16mf2x4_t vuint16mf2x4_t;
typedef __rvv_int16mf2x4_t vint16mf2x4_t;
typedef __rvv_uint16mf2x5_t vuint16mf2x5_t;
typedef __rvv_int16mf2x5_t vint16mf2x5_t;
typedef __rvv_uint16mf2x6_t vuint16mf2x6_t;
typedef __rvv_int16mf2x6_t vint16mf2x6_t;
typedef __rvv_uint16mf2x7_t vuint16mf2x7_t;
typedef __rvv_int16mf2x7_t vint16mf2x7_t;
typedef __rvv_uint16mf2x8_t vuint16mf2x8_t;
typedef __rvv_int16mf2x8_t vint16mf2x8_t;
typedef __rvv_uint16m1x2_t vuint16m1x2_t;
typedef __rvv_int16m1x2_t vint16m1x2_t;
typedef __rvv_uint16m1x3_t vuint16m1x3_t;
typedef __rvv_int16m1x3_t vint16m1x3_t;
typedef __rvv_uint16m1x4_t vuint16m1x4_t;
typedef __rvv_int16m1x4_t vint16m1x4_t;
typedef __rvv_uint16m1x5_t vuint16m1x5_t;
typedef __rvv_int16m1x5_t vint16m1x5_t;
typedef __rvv_uint16m1x6_t vuint16m1x6_t;
typedef __rvv_int16m1x6_t vint16m1x6_t;
typedef __rvv_uint16m1x7_t vuint16m1x7_t;
typedef __rvv_int16m1x7_t vint16m1x7_t;
typedef __rvv_uint16m1x8_t vuint16m1x8_t;
typedef __rvv_int16m1x8_t vint16m1x8_t;
typedef __rvv_uint16m2x2_t vuint16m2x2_t;
typedef __rvv_int16m2x2_t vint16m2x2_t;
typedef __rvv_uint16m2x3_t vuint16m2x3_t;
typedef __rvv_int16m2x3_t vint16m2x3_t;
typedef __rvv_uint16m2x4_t vuint16m2x4_t;
typedef __rvv_int16m2x4_t vint16m2x4_t;
typedef __rvv_uint16m4x2_t vuint16m4x2_t;
typedef __rvv_int16m4x2_t vint16m4x2_t;
typedef __rvv_uint32mf2x2_t vuint32mf2x2_t;
typedef __rvv_int32mf2x2_t vint32mf2x2_t;
typedef __rvv_float32mf2x2_t vfloat32mf2x2_t;
typedef __rvv_uint32mf2x3_t vuint32mf2x3_t;
typedef __rvv_int32mf2x3_t vint32mf2x3_t;
typedef __rvv_float32mf2x3_t vfloat32mf2x3_t;
typedef __rvv_uint32mf2x4_t vuint32mf2x4_t;
typedef __rvv_int32mf2x4_t vint32mf2x4_t;
typedef __rvv_float32mf2x4_t vfloat32mf2x4_t;
typedef __rvv_uint32mf2x5_t vuint32mf2x5_t;
typedef __rvv_int32mf2x5_t vint32mf2x5_t;
typedef __rvv_float32mf2x5_t vfloat32mf2x5_t;
typedef __rvv_uint32mf2x6_t vuint32mf2x6_t;
typedef __rvv_int32mf2x6_t vint32mf2x6_t;
typedef __rvv_float32mf2x6_t vfloat32mf2x6_t;
typedef __rvv_uint32mf2x7_t vuint32mf2x7_t;
typedef __rvv_int32mf2x7_t vint32mf2x7_t;
typedef __rvv_float32mf2x7_t vfloat32mf2x7_t;
typedef __rvv_uint32mf2x8_t vuint32mf2x8_t;
typedef __rvv_int32mf2x8_t vint32mf2x8_t;
typedef __rvv_float32mf2x8_t vfloat32mf2x8_t;
typedef __rvv_uint32m1x2_t vuint32m1x2_t;
typedef __rvv_int32m1x2_t vint32m1x2_t;
typedef __rvv_float32m1x2_t vfloat32m1x2_t;
typedef __rvv_uint32m1x3_t vuint32m1x3_t;
typedef __rvv_int32m1x3_t vint32m1x3_t;
typedef __rvv_float32m1x3_t vfloat32m1x3_t;
typedef __rvv_uint32m1x4_t vuint32m1x4_t;
typedef __rvv_int32m1x4_t vint32m1x4_t;
typedef __rvv_float32m1x4_t vfloat32m1x4_t;
typedef __rvv_uint32m1x5_t vuint32m1x5_t;
typedef __rvv_int32m1x5_t vint32m1x5_t;
typedef __rvv_float32m1x5_t vfloat32m1x5_t;
typedef __rvv_uint32m1x6_t vuint32m1x6_t;
typedef __rvv_int32m1x6_t vint32m1x6_t;
typedef __rvv_float32m1x6_t vfloat32m1x6_t;
typedef __rvv_uint32m1x7_t vuint32m1x7_t;
typedef __rvv_int32m1x7_t vint32m1x7_t;
typedef __rvv_float32m1x7_t vfloat32m1x7_t;
typedef __rvv_uint32m1x8_t vuint32m1x8_t;
typedef __rvv_int32m1x8_t vint32m1x8_t;
typedef __rvv_float32m1x8_t vfloat32m1x8_t;
typedef __rvv_uint32m2x2_t vuint32m2x2_t;
typedef __rvv_int32m2x2_t vint32m2x2_t;
typedef __rvv_float32m2x2_t vfloat32m2x2_t;
typedef __rvv_uint32m2x3_t vuint32m2x3_t;
typedef __rvv_int32m2x3_t vint32m2x3_t;
typedef __rvv_float32m2x3_t vfloat32m2x3_t;
typedef __rvv_uint32m2x4_t vuint32m2x4_t;
typedef __rvv_int32m2x4_t vint32m2x4_t;
typedef __rvv_float32m2x4_t vfloat32m2x4_t;
typedef __rvv_uint32m4x2_t vuint32m4x2_t;
typedef __rvv_int32m4x2_t vint32m4x2_t;
typedef __rvv_float32m4x2_t vfloat32m4x2_t;
typedef __rvv_uint64m1x2_t vuint64m1x2_t;
typedef __rvv_int64m1x2_t vint64m1x2_t;
typedef __rvv_float64m1x2_t vfloat64m1x2_t;
typedef __rvv_uint64m1x3_t vuint64m1x3_t;
typedef __rvv_int64m1x3_t vint64m1x3_t;
typedef __rvv_float64m1x3_t vfloat64m1x3_t;
typedef __rvv_uint64m1x4_t vuint64m1x4_t;
typedef __rvv_int64m1x4_t vint64m1x4_t;
typedef __rvv_float64m1x4_t vfloat64m1x4_t;
typedef __rvv_uint64m1x5_t vuint64m1x5_t;
typedef __rvv_int64m1x5_t vint64m1x5_t;
typedef __rvv_float64m1x5_t vfloat64m1x5_t;
typedef __rvv_uint64m1x6_t vuint64m1x6_t;
typedef __rvv_int64m1x6_t vint64m1x6_t;
typedef __rvv_float64m1x6_t vfloat64m1x6_t;
typedef __rvv_uint64m1x7_t vuint64m1x7_t;
typedef __rvv_int64m1x7_t vint64m1x7_t;
typedef __rvv_float64m1x7_t vfloat64m1x7_t;
typedef __rvv_uint64m1x8_t vuint64m1x8_t;
typedef __rvv_int64m1x8_t vint64m1x8_t;
typedef __rvv_float64m1x8_t vfloat64m1x8_t;
typedef __rvv_uint64m2x2_t vuint64m2x2_t;
typedef __rvv_int64m2x2_t vint64m2x2_t;
typedef __rvv_float64m2x2_t vfloat64m2x2_t;
typedef __rvv_uint64m2x3_t vuint64m2x3_t;
typedef __rvv_int64m2x3_t vint64m2x3_t;
typedef __rvv_float64m2x3_t vfloat64m2x3_t;
typedef __rvv_uint64m2x4_t vuint64m2x4_t;
typedef __rvv_int64m2x4_t vint64m2x4_t;
typedef __rvv_float64m2x4_t vfloat64m2x4_t;
typedef __rvv_uint64m4x2_t vuint64m4x2_t;
typedef __rvv_int64m4x2_t vint64m4x2_t;
typedef __rvv_float64m4x2_t vfloat64m4x2_t;

#define _RVV_OP(T, S, E, MASK, OP, F, X) \
    T __riscv_##OP##_##F##_##E(T vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_m(MASK vm, T vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_mu(MASK vm, T vd, T vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tu(T vd, T vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tum(MASK vm, T vd, T vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tumu(MASK vm, T vd, T vs2, X vs1, size_t vl);

#define _RVV_OPI(T, E, MASK, OP) \
    T __riscv_##OP##_vi_##E(T vs2, int imm5, size_t vl); \
    T __riscv_##OP##_vi_##E##_m(MASK vm, T vs2, int imm5, size_t vl); \
    T __riscv_##OP##_vi_##E##_mu(MASK vm, T vd, T vs2, int imm5, size_t vl); \
    T __riscv_##OP##_vi_##E##_tu(T vd, T vs2, int imm5, size_t vl); \
    T __riscv_##OP##_vi_##E##_tum(MASK vm, T vd, T vs2, int imm5, size_t vl); \
    T __riscv_##OP##_vi_##E##_tumu(MASK vm, T vd, T vs2, int imm5, size_t vl);

/* One source and one destination, which need not be shaped alike */
#define _RVV_UNARY(T, E, MASK, OP, F, X) \
    T __riscv_##OP##_##F##_##E(X vs2, size_t vl); \
    T __riscv_##OP##_##F##_##E##_m(MASK vm, X vs2, size_t vl); \
    T __riscv_##OP##_##F##_##E##_mu(MASK vm, T vd, X vs2, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tu(T vd, X vs2, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tum(MASK vm, T vd, X vs2, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tumu(MASK vm, T vd, X vs2, size_t vl);

/* Two sources whose first is shaped unlike the destination, as widening and narrowing are */
#define _RVV_OP2(T, E, MASK, OP, F, V2, X) \
    T __riscv_##OP##_##F##_##E(V2 vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_m(MASK vm, V2 vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_mu(MASK vm, T vd, V2 vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tu(T vd, V2 vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tum(MASK vm, T vd, V2 vs2, X vs1, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tumu(MASK vm, T vd, V2 vs2, X vs1, size_t vl);

#define _RVV_OPI2(T, E, MASK, OP, F, V2) \
    T __riscv_##OP##_##F##_##E(V2 vs2, size_t uimm5, size_t vl); \
    T __riscv_##OP##_##F##_##E##_m(MASK vm, V2 vs2, size_t uimm5, size_t vl); \
    T __riscv_##OP##_##F##_##E##_mu(MASK vm, T vd, V2 vs2, size_t uimm5, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tu(T vd, V2 vs2, size_t uimm5, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tum(MASK vm, T vd, V2 vs2, size_t uimm5, size_t vl); \
    T __riscv_##OP##_##F##_##E##_tumu(MASK vm, T vd, V2 vs2, size_t uimm5, size_t vl);

/* A widening accumulator reads the wide destination and two narrow sources */
#define _RVV_WACC(T, E, S, OP, V2) \
    T __riscv_##OP##_vv_##E(T vd, V2 vs1, V2 vs2, size_t vl); \
    T __riscv_##OP##_vx_##E(T vd, S rs1, V2 vs2, size_t vl);

/* A float accumulator takes its scalar in a float register */
#define _RVV_FACC(T, S, E, OP) \
    T __riscv_##OP##_vv_##E(T vd, T vs1, T vs2, size_t vl); \
    T __riscv_##OP##_vf_##E(T vd, S rs1, T vs2, size_t vl);

/* The carry and borrow forms, which read and write a mask beside the elements */
#define _RVV_CARRY(T, S, E, MASK, B) \
    T __riscv_vadc_vvm_##E(T vs2, T vs1, MASK v0, size_t vl); \
    T __riscv_vadc_vxm_##E(T vs2, S rs1, MASK v0, size_t vl); \
    T __riscv_vadc_vim_##E(T vs2, int simm5, MASK v0, size_t vl); \
    T __riscv_vsbc_vvm_##E(T vs2, T vs1, MASK v0, size_t vl); \
    T __riscv_vsbc_vxm_##E(T vs2, S rs1, MASK v0, size_t vl); \
    MASK __riscv_vmadc_vv_##E##_b##B(T vs2, T vs1, size_t vl); \
    MASK __riscv_vmadc_vx_##E##_b##B(T vs2, S rs1, size_t vl); \
    MASK __riscv_vmadc_vi_##E##_b##B(T vs2, int simm5, size_t vl); \
    MASK __riscv_vmsbc_vv_##E##_b##B(T vs2, T vs1, size_t vl); \
    MASK __riscv_vmsbc_vx_##E##_b##B(T vs2, S rs1, size_t vl);

/* Everything that works a mask register a bit at a time */
#define _RVV_MASKOPS(MASK, B) \
    MASK __riscv_vmand_mm_b##B(MASK vs2, MASK vs1, size_t vl); \
    MASK __riscv_vmnand_mm_b##B(MASK vs2, MASK vs1, size_t vl); \
    MASK __riscv_vmandn_mm_b##B(MASK vs2, MASK vs1, size_t vl); \
    MASK __riscv_vmxor_mm_b##B(MASK vs2, MASK vs1, size_t vl); \
    MASK __riscv_vmor_mm_b##B(MASK vs2, MASK vs1, size_t vl); \
    MASK __riscv_vmnor_mm_b##B(MASK vs2, MASK vs1, size_t vl); \
    MASK __riscv_vmorn_mm_b##B(MASK vs2, MASK vs1, size_t vl); \
    MASK __riscv_vmxnor_mm_b##B(MASK vs2, MASK vs1, size_t vl); \
    MASK __riscv_vmsbf_m_b##B(MASK vs2, size_t vl); \
    MASK __riscv_vmsbf_m_b##B##_m(MASK vm, MASK vs2, size_t vl); \
    MASK __riscv_vmsif_m_b##B(MASK vs2, size_t vl); \
    MASK __riscv_vmsif_m_b##B##_m(MASK vm, MASK vs2, size_t vl); \
    MASK __riscv_vmsof_m_b##B(MASK vs2, size_t vl); \
    MASK __riscv_vmsof_m_b##B##_m(MASK vm, MASK vs2, size_t vl); \
    unsigned long __riscv_vcpop_m_b##B(MASK vs2, size_t vl); \
    unsigned long __riscv_vcpop_m_b##B##_m(MASK vm, MASK vs2, size_t vl); \
    long __riscv_vfirst_m_b##B(MASK vs2, size_t vl); \
    long __riscv_vfirst_m_b##B##_m(MASK vm, MASK vs2, size_t vl); \
    MASK __riscv_vlm_v_b##B(const unsigned char *rs1, size_t vl); \
    void __riscv_vsm_v_b##B(unsigned char *rs1, MASK vs3, size_t vl);

#define _RVV_CMP(T, E, MASK, B, OP, F, X) \
    MASK __riscv_##OP##_##F##_##E##_b##B(T vs2, X vs1, size_t vl); \
    MASK __riscv_##OP##_##F##_##E##_b##B##_m(MASK vm, T vs2, X vs1, size_t vl);

#define _RVV_CMPI(T, E, MASK, B, OP) \
    MASK __riscv_##OP##_vi_##E##_b##B(T vs2, int imm5, size_t vl); \
    MASK __riscv_##OP##_vi_##E##_b##B##_m(MASK vm, T vs2, int imm5, size_t vl);

#define _RVV_ACC(T, S, E, OP) \
    T __riscv_##OP##_vv_##E(T vd, T vs1, T vs2, size_t vl); \
    T __riscv_##OP##_vx_##E(T vd, S rs1, T vs2, size_t vl);

#define _RVV_RED(T, E, T1, E1, OP) \
    T1 __riscv_##OP##_vs_##E##_##E1(T vs2, T1 vs1, size_t vl);

#define _RVV_COMMON(T, S, E, MASK, W, M) \
    size_t __riscv_vsetvl_e##W##M(size_t avl); \
    size_t __riscv_vsetvlmax_e##W##M(void); \
    T __riscv_vle##W##_v_##E(const S *rs1, size_t vl); \
    void __riscv_vse##W##_v_##E(S *rs1, T vs3, size_t vl); \
    T __riscv_vmv_v_v_##E(T vs1, size_t vl); \
    T __riscv_vmv_v_x_##E(S rs1, size_t vl); \
    T __riscv_vmerge_vvm_##E(T vs2, T vs1, MASK vm, size_t vl); \
    T __riscv_vmerge_vxm_##E(T vs2, S rs1, MASK vm, size_t vl); \
    T __riscv_vlse##W##_v_##E(const S *rs1, long rs2, size_t vl); \
    void __riscv_vsse##W##_v_##E(S *rs1, long rs2, T vs3, size_t vl); \
    T __riscv_vcompress_vm_##E(T vs2, MASK vs1, size_t vl); \
    T __riscv_vslideup_vx_##E(T vd, T vs2, size_t rs1, size_t vl); \
    T __riscv_vslideup_vi_##E(T vd, T vs2, size_t uimm5, size_t vl); \
    _RVV_OP(T, S, E, MASK, vslidedown, vx, size_t) \
    _RVV_OPI2(T, E, MASK, vslidedown, vi, T)

#define _RVV_INT(T, S, E, MASK, B, K) \
    T __riscv_vmv_v_i_##E(int simm5, size_t vl); \
    T __riscv_vmerge_vim_##E(T vs2, int simm5, MASK vm, size_t vl); \
    S __riscv_vmv_x_s_##E##_##K(T vs2); \
    T __riscv_vmv_s_x_##E(S rs1, size_t vl); \
    _RVV_OP(T, S, E, MASK, vadd, vv, T) \
    _RVV_OP(T, S, E, MASK, vadd, vx, S) \
    _RVV_OP(T, S, E, MASK, vsub, vv, T) \
    _RVV_OP(T, S, E, MASK, vsub, vx, S) \
    _RVV_OP(T, S, E, MASK, vand, vv, T) \
    _RVV_OP(T, S, E, MASK, vand, vx, S) \
    _RVV_OP(T, S, E, MASK, vor, vv, T) \
    _RVV_OP(T, S, E, MASK, vor, vx, S) \
    _RVV_OP(T, S, E, MASK, vxor, vv, T) \
    _RVV_OP(T, S, E, MASK, vxor, vx, S) \
    _RVV_OP(T, S, E, MASK, vmul, vv, T) \
    _RVV_OP(T, S, E, MASK, vmul, vx, S) \
    _RVV_OP(T, S, E, MASK, vsll, vv, T) \
    _RVV_OP(T, S, E, MASK, vsll, vx, S) \
    _RVV_OP(T, S, E, MASK, vsrl, vv, T) \
    _RVV_OP(T, S, E, MASK, vsrl, vx, S) \
    _RVV_OP(T, S, E, MASK, vsra, vv, T) \
    _RVV_OP(T, S, E, MASK, vsra, vx, S) \
    _RVV_OP(T, S, E, MASK, vrgather, vv, T) \
    _RVV_OP(T, S, E, MASK, vrgather, vx, S) \
    _RVV_OP(T, S, E, MASK, vrsub, vx, S) \
    _RVV_OPI(T, E, MASK, vadd) \
    _RVV_OPI(T, E, MASK, vand) \
    _RVV_OPI(T, E, MASK, vor) \
    _RVV_OPI(T, E, MASK, vxor) \
    _RVV_OPI(T, E, MASK, vrsub) \
    _RVV_OPI(T, E, MASK, vsll) \
    _RVV_OPI(T, E, MASK, vsrl) \
    _RVV_OPI(T, E, MASK, vsra) \
    _RVV_OPI(T, E, MASK, vrgather) \
    _RVV_ACC(T, S, E, vmacc) \
    _RVV_ACC(T, S, E, vnmsac) \
    _RVV_ACC(T, S, E, vmadd) \
    _RVV_ACC(T, S, E, vnmsub) \
    _RVV_CMP(T, E, MASK, B, vmseq, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmsne, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmsltu, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmslt, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmsleu, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmsle, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmseq, vx, S) \
    _RVV_CMP(T, E, MASK, B, vmsne, vx, S) \
    _RVV_CMP(T, E, MASK, B, vmsltu, vx, S) \
    _RVV_CMP(T, E, MASK, B, vmslt, vx, S) \
    _RVV_CMP(T, E, MASK, B, vmsleu, vx, S) \
    _RVV_CMP(T, E, MASK, B, vmsle, vx, S) \
    _RVV_CMP(T, E, MASK, B, vmsgtu, vx, S) \
    _RVV_CMP(T, E, MASK, B, vmsgt, vx, S) \
    _RVV_CMPI(T, E, MASK, B, vmseq) \
    _RVV_CMPI(T, E, MASK, B, vmsne) \
    _RVV_CMPI(T, E, MASK, B, vmsleu) \
    _RVV_CMPI(T, E, MASK, B, vmsle) \
    _RVV_CMPI(T, E, MASK, B, vmsgtu) \
    _RVV_CMPI(T, E, MASK, B, vmsgt) \
    _RVV_CARRY(T, S, E, MASK, B) \
    T __riscv_vid_v_##E(size_t vl); \
    T __riscv_viota_m_##E(MASK vs2, size_t vl); \
    _RVV_OP(T, S, E, MASK, vslide1up, vx, S) \
    _RVV_OP(T, S, E, MASK, vslide1down, vx, S)

#define _RVV_SIGNED(T, S, E, MASK, T1, E1) \
    _RVV_OP(T, S, E, MASK, vmulh, vv, T) \
    _RVV_OP(T, S, E, MASK, vmulh, vx, S) \
    _RVV_OP(T, S, E, MASK, vmulhsu, vv, T) \
    _RVV_OP(T, S, E, MASK, vmulhsu, vx, S) \
    _RVV_OP(T, S, E, MASK, vdiv, vv, T) \
    _RVV_OP(T, S, E, MASK, vdiv, vx, S) \
    _RVV_OP(T, S, E, MASK, vrem, vv, T) \
    _RVV_OP(T, S, E, MASK, vrem, vx, S) \
    _RVV_OP(T, S, E, MASK, vmin, vv, T) \
    _RVV_OP(T, S, E, MASK, vmin, vx, S) \
    _RVV_OP(T, S, E, MASK, vmax, vv, T) \
    _RVV_OP(T, S, E, MASK, vmax, vx, S) \
    _RVV_RED(T, E, T1, E1, vredsum) \
    _RVV_RED(T, E, T1, E1, vredand) \
    _RVV_RED(T, E, T1, E1, vredor) \
    _RVV_RED(T, E, T1, E1, vredxor) \
    _RVV_RED(T, E, T1, E1, vredmin) \
    _RVV_RED(T, E, T1, E1, vredmax) \
    _RVV_OP(T, S, E, MASK, vsadd, vv, T) \
    _RVV_OP(T, S, E, MASK, vsadd, vx, S) \
    _RVV_OPI(T, E, MASK, vsadd) \
    _RVV_OP(T, S, E, MASK, vssub, vv, T) \
    _RVV_OP(T, S, E, MASK, vssub, vx, S) \
    _RVV_OP(T, S, E, MASK, vaadd, vv, T) \
    _RVV_OP(T, S, E, MASK, vaadd, vx, S) \
    _RVV_OP(T, S, E, MASK, vasub, vv, T) \
    _RVV_OP(T, S, E, MASK, vasub, vx, S) \
    _RVV_OP(T, S, E, MASK, vsmul, vv, T) \
    _RVV_OP(T, S, E, MASK, vsmul, vx, S) \
    _RVV_OP(T, S, E, MASK, vssra, vv, T) \
    _RVV_OP(T, S, E, MASK, vssra, vx, size_t) \
    _RVV_OPI2(T, E, MASK, vssra, vi, T)

#define _RVV_UNSIGNED(T, S, E, MASK, T1, E1) \
    _RVV_OP(T, S, E, MASK, vmulhu, vv, T) \
    _RVV_OP(T, S, E, MASK, vmulhu, vx, S) \
    _RVV_OP(T, S, E, MASK, vdivu, vv, T) \
    _RVV_OP(T, S, E, MASK, vdivu, vx, S) \
    _RVV_OP(T, S, E, MASK, vremu, vv, T) \
    _RVV_OP(T, S, E, MASK, vremu, vx, S) \
    _RVV_OP(T, S, E, MASK, vminu, vv, T) \
    _RVV_OP(T, S, E, MASK, vminu, vx, S) \
    _RVV_OP(T, S, E, MASK, vmaxu, vv, T) \
    _RVV_OP(T, S, E, MASK, vmaxu, vx, S) \
    _RVV_RED(T, E, T1, E1, vredsum) \
    _RVV_RED(T, E, T1, E1, vredand) \
    _RVV_RED(T, E, T1, E1, vredor) \
    _RVV_RED(T, E, T1, E1, vredxor) \
    _RVV_RED(T, E, T1, E1, vredminu) \
    _RVV_RED(T, E, T1, E1, vredmaxu) \
    _RVV_OP(T, S, E, MASK, vsaddu, vv, T) \
    _RVV_OP(T, S, E, MASK, vsaddu, vx, S) \
    _RVV_OPI(T, E, MASK, vsaddu) \
    _RVV_OP(T, S, E, MASK, vssubu, vv, T) \
    _RVV_OP(T, S, E, MASK, vssubu, vx, S) \
    _RVV_OP(T, S, E, MASK, vaaddu, vv, T) \
    _RVV_OP(T, S, E, MASK, vaaddu, vx, S) \
    _RVV_OP(T, S, E, MASK, vasubu, vv, T) \
    _RVV_OP(T, S, E, MASK, vasubu, vx, S) \
    _RVV_OP(T, S, E, MASK, vssrl, vv, T) \
    _RVV_OP(T, S, E, MASK, vssrl, vx, size_t) \
    _RVV_OPI2(T, E, MASK, vssrl, vi, T) \
    _RVV_OP(T, S, E, MASK, vandn, vv, T) \
    _RVV_OP(T, S, E, MASK, vandn, vx, S) \
    _RVV_OP(T, S, E, MASK, vrol, vv, T) \
    _RVV_OP(T, S, E, MASK, vrol, vx, size_t) \
    _RVV_OP(T, S, E, MASK, vror, vv, T) \
    _RVV_OP(T, S, E, MASK, vror, vx, size_t) \
    _RVV_OPI2(T, E, MASK, vror, vi, T) \
    _RVV_UNARY(T, E, MASK, vbrev, v, T) \
    _RVV_UNARY(T, E, MASK, vbrev8, v, T) \
    _RVV_UNARY(T, E, MASK, vrev8, v, T) \
    _RVV_UNARY(T, E, MASK, vclz, v, T) \
    _RVV_UNARY(T, E, MASK, vctz, v, T) \
    _RVV_UNARY(T, E, MASK, vcpop, v, T)

#define _RVV_FLOAT(T, S, E, MASK, B, T1, E1, TI, EI, TU, EU) \
    _RVV_FACC(T, S, E, vfmacc) \
    _RVV_FACC(T, S, E, vfnmacc) \
    _RVV_FACC(T, S, E, vfmsac) \
    _RVV_FACC(T, S, E, vfnmsac) \
    _RVV_FACC(T, S, E, vfmadd) \
    _RVV_FACC(T, S, E, vfnmadd) \
    _RVV_FACC(T, S, E, vfmsub) \
    _RVV_FACC(T, S, E, vfnmsub) \
    _RVV_UNARY(T, E, MASK, vfsqrt, v, T) \
    _RVV_UNARY(TU, EU, MASK, vfclass, v, T) \
    _RVV_UNARY(TI, EI, MASK, vfcvt_x_f, v, T) \
    _RVV_UNARY(TU, EU, MASK, vfcvt_xu_f, v, T) \
    _RVV_UNARY(TI, EI, MASK, vfcvt_rtz_x_f, v, T) \
    _RVV_UNARY(TU, EU, MASK, vfcvt_rtz_xu_f, v, T) \
    _RVV_UNARY(T, E, MASK, vfcvt_f_x, v, TI) \
    _RVV_UNARY(T, E, MASK, vfcvt_f_xu, v, TU) \
    _RVV_RED(T, E, T1, E1, vfredusum) \
    _RVV_RED(T, E, T1, E1, vfredosum) \
    _RVV_RED(T, E, T1, E1, vfredmin) \
    _RVV_RED(T, E, T1, E1, vfredmax) \
    _RVV_OP(T, S, E, MASK, vfslide1up, vf, S) \
    _RVV_OP(T, S, E, MASK, vfslide1down, vf, S) \
    T __riscv_vfmerge_vfm_##E(T vs2, S rs1, MASK v0, size_t vl); \
    T __riscv_vfmv_v_f_##E(S rs1, size_t vl); \
    T __riscv_vfmv_s_f_##E(S rs1, size_t vl); \
    S __riscv_vfmv_f_s_##E(T vs2); \
    _RVV_OP(T, S, E, MASK, vfadd, vv, T) \
    _RVV_OP(T, S, E, MASK, vfadd, vf, S) \
    _RVV_OP(T, S, E, MASK, vfsub, vv, T) \
    _RVV_OP(T, S, E, MASK, vfsub, vf, S) \
    _RVV_OP(T, S, E, MASK, vfmul, vv, T) \
    _RVV_OP(T, S, E, MASK, vfmul, vf, S) \
    _RVV_OP(T, S, E, MASK, vfdiv, vv, T) \
    _RVV_OP(T, S, E, MASK, vfdiv, vf, S) \
    _RVV_OP(T, S, E, MASK, vfmin, vv, T) \
    _RVV_OP(T, S, E, MASK, vfmin, vf, S) \
    _RVV_OP(T, S, E, MASK, vfmax, vv, T) \
    _RVV_OP(T, S, E, MASK, vfmax, vf, S) \
    _RVV_OP(T, S, E, MASK, vfsgnj, vv, T) \
    _RVV_OP(T, S, E, MASK, vfsgnj, vf, S) \
    _RVV_OP(T, S, E, MASK, vfsgnjn, vv, T) \
    _RVV_OP(T, S, E, MASK, vfsgnjn, vf, S) \
    _RVV_OP(T, S, E, MASK, vfsgnjx, vv, T) \
    _RVV_OP(T, S, E, MASK, vfsgnjx, vf, S) \
    _RVV_OP(T, S, E, MASK, vfrsub, vf, S) \
    _RVV_OP(T, S, E, MASK, vfrdiv, vf, S) \
    _RVV_CMP(T, E, MASK, B, vmfeq, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmfne, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmflt, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmfle, vv, T) \
    _RVV_CMP(T, E, MASK, B, vmfeq, vf, S) \
    _RVV_CMP(T, E, MASK, B, vmfne, vf, S) \
    _RVV_CMP(T, E, MASK, B, vmflt, vf, S) \
    _RVV_CMP(T, E, MASK, B, vmfle, vf, S) \
    _RVV_CMP(T, E, MASK, B, vmfgt, vf, S) \
    _RVV_CMP(T, E, MASK, B, vmfge, vf, S)


_RVV_COMMON(vint8mf8_t, signed char, i8mf8, vbool64_t, 8, mf8)
_RVV_INT(vint8mf8_t, signed char, i8mf8, vbool64_t, 64, i8)
_RVV_SIGNED(vint8mf8_t, signed char, i8mf8, vbool64_t, vint8m1_t, i8m1)
_RVV_COMMON(vuint8mf8_t, unsigned char, u8mf8, vbool64_t, 8, mf8)
_RVV_INT(vuint8mf8_t, unsigned char, u8mf8, vbool64_t, 64, u8)
_RVV_UNSIGNED(vuint8mf8_t, unsigned char, u8mf8, vbool64_t, vuint8m1_t, u8m1)

_RVV_COMMON(vint8mf4_t, signed char, i8mf4, vbool32_t, 8, mf4)
_RVV_INT(vint8mf4_t, signed char, i8mf4, vbool32_t, 32, i8)
_RVV_SIGNED(vint8mf4_t, signed char, i8mf4, vbool32_t, vint8m1_t, i8m1)
_RVV_COMMON(vuint8mf4_t, unsigned char, u8mf4, vbool32_t, 8, mf4)
_RVV_INT(vuint8mf4_t, unsigned char, u8mf4, vbool32_t, 32, u8)
_RVV_UNSIGNED(vuint8mf4_t, unsigned char, u8mf4, vbool32_t, vuint8m1_t, u8m1)

_RVV_COMMON(vint8mf2_t, signed char, i8mf2, vbool16_t, 8, mf2)
_RVV_INT(vint8mf2_t, signed char, i8mf2, vbool16_t, 16, i8)
_RVV_SIGNED(vint8mf2_t, signed char, i8mf2, vbool16_t, vint8m1_t, i8m1)
_RVV_COMMON(vuint8mf2_t, unsigned char, u8mf2, vbool16_t, 8, mf2)
_RVV_INT(vuint8mf2_t, unsigned char, u8mf2, vbool16_t, 16, u8)
_RVV_UNSIGNED(vuint8mf2_t, unsigned char, u8mf2, vbool16_t, vuint8m1_t, u8m1)

_RVV_COMMON(vint8m1_t, signed char, i8m1, vbool8_t, 8, m1)
_RVV_INT(vint8m1_t, signed char, i8m1, vbool8_t, 8, i8)
_RVV_SIGNED(vint8m1_t, signed char, i8m1, vbool8_t, vint8m1_t, i8m1)
_RVV_COMMON(vuint8m1_t, unsigned char, u8m1, vbool8_t, 8, m1)
_RVV_INT(vuint8m1_t, unsigned char, u8m1, vbool8_t, 8, u8)
_RVV_UNSIGNED(vuint8m1_t, unsigned char, u8m1, vbool8_t, vuint8m1_t, u8m1)

_RVV_COMMON(vint8m2_t, signed char, i8m2, vbool4_t, 8, m2)
_RVV_INT(vint8m2_t, signed char, i8m2, vbool4_t, 4, i8)
_RVV_SIGNED(vint8m2_t, signed char, i8m2, vbool4_t, vint8m1_t, i8m1)
_RVV_COMMON(vuint8m2_t, unsigned char, u8m2, vbool4_t, 8, m2)
_RVV_INT(vuint8m2_t, unsigned char, u8m2, vbool4_t, 4, u8)
_RVV_UNSIGNED(vuint8m2_t, unsigned char, u8m2, vbool4_t, vuint8m1_t, u8m1)

_RVV_COMMON(vint8m4_t, signed char, i8m4, vbool2_t, 8, m4)
_RVV_INT(vint8m4_t, signed char, i8m4, vbool2_t, 2, i8)
_RVV_SIGNED(vint8m4_t, signed char, i8m4, vbool2_t, vint8m1_t, i8m1)
_RVV_COMMON(vuint8m4_t, unsigned char, u8m4, vbool2_t, 8, m4)
_RVV_INT(vuint8m4_t, unsigned char, u8m4, vbool2_t, 2, u8)
_RVV_UNSIGNED(vuint8m4_t, unsigned char, u8m4, vbool2_t, vuint8m1_t, u8m1)

_RVV_COMMON(vint8m8_t, signed char, i8m8, vbool1_t, 8, m8)
_RVV_INT(vint8m8_t, signed char, i8m8, vbool1_t, 1, i8)
_RVV_SIGNED(vint8m8_t, signed char, i8m8, vbool1_t, vint8m1_t, i8m1)
_RVV_COMMON(vuint8m8_t, unsigned char, u8m8, vbool1_t, 8, m8)
_RVV_INT(vuint8m8_t, unsigned char, u8m8, vbool1_t, 1, u8)
_RVV_UNSIGNED(vuint8m8_t, unsigned char, u8m8, vbool1_t, vuint8m1_t, u8m1)

_RVV_COMMON(vint16mf4_t, short, i16mf4, vbool64_t, 16, mf4)
_RVV_INT(vint16mf4_t, short, i16mf4, vbool64_t, 64, i16)
_RVV_SIGNED(vint16mf4_t, short, i16mf4, vbool64_t, vint16m1_t, i16m1)
_RVV_COMMON(vuint16mf4_t, unsigned short, u16mf4, vbool64_t, 16, mf4)
_RVV_INT(vuint16mf4_t, unsigned short, u16mf4, vbool64_t, 64, u16)
_RVV_UNSIGNED(vuint16mf4_t, unsigned short, u16mf4, vbool64_t, vuint16m1_t, u16m1)

_RVV_COMMON(vint16mf2_t, short, i16mf2, vbool32_t, 16, mf2)
_RVV_INT(vint16mf2_t, short, i16mf2, vbool32_t, 32, i16)
_RVV_SIGNED(vint16mf2_t, short, i16mf2, vbool32_t, vint16m1_t, i16m1)
_RVV_COMMON(vuint16mf2_t, unsigned short, u16mf2, vbool32_t, 16, mf2)
_RVV_INT(vuint16mf2_t, unsigned short, u16mf2, vbool32_t, 32, u16)
_RVV_UNSIGNED(vuint16mf2_t, unsigned short, u16mf2, vbool32_t, vuint16m1_t, u16m1)

_RVV_COMMON(vint16m1_t, short, i16m1, vbool16_t, 16, m1)
_RVV_INT(vint16m1_t, short, i16m1, vbool16_t, 16, i16)
_RVV_SIGNED(vint16m1_t, short, i16m1, vbool16_t, vint16m1_t, i16m1)
_RVV_COMMON(vuint16m1_t, unsigned short, u16m1, vbool16_t, 16, m1)
_RVV_INT(vuint16m1_t, unsigned short, u16m1, vbool16_t, 16, u16)
_RVV_UNSIGNED(vuint16m1_t, unsigned short, u16m1, vbool16_t, vuint16m1_t, u16m1)

_RVV_COMMON(vint16m2_t, short, i16m2, vbool8_t, 16, m2)
_RVV_INT(vint16m2_t, short, i16m2, vbool8_t, 8, i16)
_RVV_SIGNED(vint16m2_t, short, i16m2, vbool8_t, vint16m1_t, i16m1)
_RVV_COMMON(vuint16m2_t, unsigned short, u16m2, vbool8_t, 16, m2)
_RVV_INT(vuint16m2_t, unsigned short, u16m2, vbool8_t, 8, u16)
_RVV_UNSIGNED(vuint16m2_t, unsigned short, u16m2, vbool8_t, vuint16m1_t, u16m1)

_RVV_COMMON(vint16m4_t, short, i16m4, vbool4_t, 16, m4)
_RVV_INT(vint16m4_t, short, i16m4, vbool4_t, 4, i16)
_RVV_SIGNED(vint16m4_t, short, i16m4, vbool4_t, vint16m1_t, i16m1)
_RVV_COMMON(vuint16m4_t, unsigned short, u16m4, vbool4_t, 16, m4)
_RVV_INT(vuint16m4_t, unsigned short, u16m4, vbool4_t, 4, u16)
_RVV_UNSIGNED(vuint16m4_t, unsigned short, u16m4, vbool4_t, vuint16m1_t, u16m1)

_RVV_COMMON(vint16m8_t, short, i16m8, vbool2_t, 16, m8)
_RVV_INT(vint16m8_t, short, i16m8, vbool2_t, 2, i16)
_RVV_SIGNED(vint16m8_t, short, i16m8, vbool2_t, vint16m1_t, i16m1)
_RVV_COMMON(vuint16m8_t, unsigned short, u16m8, vbool2_t, 16, m8)
_RVV_INT(vuint16m8_t, unsigned short, u16m8, vbool2_t, 2, u16)
_RVV_UNSIGNED(vuint16m8_t, unsigned short, u16m8, vbool2_t, vuint16m1_t, u16m1)

_RVV_COMMON(vint32mf2_t, int, i32mf2, vbool64_t, 32, mf2)
_RVV_INT(vint32mf2_t, int, i32mf2, vbool64_t, 64, i32)
_RVV_SIGNED(vint32mf2_t, int, i32mf2, vbool64_t, vint32m1_t, i32m1)
_RVV_COMMON(vuint32mf2_t, unsigned int, u32mf2, vbool64_t, 32, mf2)
_RVV_INT(vuint32mf2_t, unsigned int, u32mf2, vbool64_t, 64, u32)
_RVV_UNSIGNED(vuint32mf2_t, unsigned int, u32mf2, vbool64_t, vuint32m1_t, u32m1)
_RVV_COMMON(vfloat32mf2_t, float, f32mf2, vbool64_t, 32, mf2)
_RVV_FLOAT(vfloat32mf2_t, float, f32mf2, vbool64_t, 64, vfloat32m1_t, f32m1, vint32mf2_t, i32mf2, vuint32mf2_t, u32mf2)

_RVV_COMMON(vint32m1_t, int, i32m1, vbool32_t, 32, m1)
_RVV_INT(vint32m1_t, int, i32m1, vbool32_t, 32, i32)
_RVV_SIGNED(vint32m1_t, int, i32m1, vbool32_t, vint32m1_t, i32m1)
_RVV_COMMON(vuint32m1_t, unsigned int, u32m1, vbool32_t, 32, m1)
_RVV_INT(vuint32m1_t, unsigned int, u32m1, vbool32_t, 32, u32)
_RVV_UNSIGNED(vuint32m1_t, unsigned int, u32m1, vbool32_t, vuint32m1_t, u32m1)
_RVV_COMMON(vfloat32m1_t, float, f32m1, vbool32_t, 32, m1)
_RVV_FLOAT(vfloat32m1_t, float, f32m1, vbool32_t, 32, vfloat32m1_t, f32m1, vint32m1_t, i32m1, vuint32m1_t, u32m1)

_RVV_COMMON(vint32m2_t, int, i32m2, vbool16_t, 32, m2)
_RVV_INT(vint32m2_t, int, i32m2, vbool16_t, 16, i32)
_RVV_SIGNED(vint32m2_t, int, i32m2, vbool16_t, vint32m1_t, i32m1)
_RVV_COMMON(vuint32m2_t, unsigned int, u32m2, vbool16_t, 32, m2)
_RVV_INT(vuint32m2_t, unsigned int, u32m2, vbool16_t, 16, u32)
_RVV_UNSIGNED(vuint32m2_t, unsigned int, u32m2, vbool16_t, vuint32m1_t, u32m1)
_RVV_COMMON(vfloat32m2_t, float, f32m2, vbool16_t, 32, m2)
_RVV_FLOAT(vfloat32m2_t, float, f32m2, vbool16_t, 16, vfloat32m1_t, f32m1, vint32m2_t, i32m2, vuint32m2_t, u32m2)

_RVV_COMMON(vint32m4_t, int, i32m4, vbool8_t, 32, m4)
_RVV_INT(vint32m4_t, int, i32m4, vbool8_t, 8, i32)
_RVV_SIGNED(vint32m4_t, int, i32m4, vbool8_t, vint32m1_t, i32m1)
_RVV_COMMON(vuint32m4_t, unsigned int, u32m4, vbool8_t, 32, m4)
_RVV_INT(vuint32m4_t, unsigned int, u32m4, vbool8_t, 8, u32)
_RVV_UNSIGNED(vuint32m4_t, unsigned int, u32m4, vbool8_t, vuint32m1_t, u32m1)
_RVV_COMMON(vfloat32m4_t, float, f32m4, vbool8_t, 32, m4)
_RVV_FLOAT(vfloat32m4_t, float, f32m4, vbool8_t, 8, vfloat32m1_t, f32m1, vint32m4_t, i32m4, vuint32m4_t, u32m4)

_RVV_COMMON(vint32m8_t, int, i32m8, vbool4_t, 32, m8)
_RVV_INT(vint32m8_t, int, i32m8, vbool4_t, 4, i32)
_RVV_SIGNED(vint32m8_t, int, i32m8, vbool4_t, vint32m1_t, i32m1)
_RVV_COMMON(vuint32m8_t, unsigned int, u32m8, vbool4_t, 32, m8)
_RVV_INT(vuint32m8_t, unsigned int, u32m8, vbool4_t, 4, u32)
_RVV_UNSIGNED(vuint32m8_t, unsigned int, u32m8, vbool4_t, vuint32m1_t, u32m1)
_RVV_COMMON(vfloat32m8_t, float, f32m8, vbool4_t, 32, m8)
_RVV_FLOAT(vfloat32m8_t, float, f32m8, vbool4_t, 4, vfloat32m1_t, f32m1, vint32m8_t, i32m8, vuint32m8_t, u32m8)

_RVV_COMMON(vint64m1_t, int64_t, i64m1, vbool64_t, 64, m1)
_RVV_INT(vint64m1_t, int64_t, i64m1, vbool64_t, 64, i64)
_RVV_SIGNED(vint64m1_t, int64_t, i64m1, vbool64_t, vint64m1_t, i64m1)
_RVV_COMMON(vuint64m1_t, uint64_t, u64m1, vbool64_t, 64, m1)
_RVV_INT(vuint64m1_t, uint64_t, u64m1, vbool64_t, 64, u64)
_RVV_UNSIGNED(vuint64m1_t, uint64_t, u64m1, vbool64_t, vuint64m1_t, u64m1)
_RVV_COMMON(vfloat64m1_t, double, f64m1, vbool64_t, 64, m1)
_RVV_FLOAT(vfloat64m1_t, double, f64m1, vbool64_t, 64, vfloat64m1_t, f64m1, vint64m1_t, i64m1, vuint64m1_t, u64m1)

_RVV_COMMON(vint64m2_t, int64_t, i64m2, vbool32_t, 64, m2)
_RVV_INT(vint64m2_t, int64_t, i64m2, vbool32_t, 32, i64)
_RVV_SIGNED(vint64m2_t, int64_t, i64m2, vbool32_t, vint64m1_t, i64m1)
_RVV_COMMON(vuint64m2_t, uint64_t, u64m2, vbool32_t, 64, m2)
_RVV_INT(vuint64m2_t, uint64_t, u64m2, vbool32_t, 32, u64)
_RVV_UNSIGNED(vuint64m2_t, uint64_t, u64m2, vbool32_t, vuint64m1_t, u64m1)
_RVV_COMMON(vfloat64m2_t, double, f64m2, vbool32_t, 64, m2)
_RVV_FLOAT(vfloat64m2_t, double, f64m2, vbool32_t, 32, vfloat64m1_t, f64m1, vint64m2_t, i64m2, vuint64m2_t, u64m2)

_RVV_COMMON(vint64m4_t, int64_t, i64m4, vbool16_t, 64, m4)
_RVV_INT(vint64m4_t, int64_t, i64m4, vbool16_t, 16, i64)
_RVV_SIGNED(vint64m4_t, int64_t, i64m4, vbool16_t, vint64m1_t, i64m1)
_RVV_COMMON(vuint64m4_t, uint64_t, u64m4, vbool16_t, 64, m4)
_RVV_INT(vuint64m4_t, uint64_t, u64m4, vbool16_t, 16, u64)
_RVV_UNSIGNED(vuint64m4_t, uint64_t, u64m4, vbool16_t, vuint64m1_t, u64m1)
_RVV_COMMON(vfloat64m4_t, double, f64m4, vbool16_t, 64, m4)
_RVV_FLOAT(vfloat64m4_t, double, f64m4, vbool16_t, 16, vfloat64m1_t, f64m1, vint64m4_t, i64m4, vuint64m4_t, u64m4)

_RVV_COMMON(vint64m8_t, int64_t, i64m8, vbool8_t, 64, m8)
_RVV_INT(vint64m8_t, int64_t, i64m8, vbool8_t, 8, i64)
_RVV_SIGNED(vint64m8_t, int64_t, i64m8, vbool8_t, vint64m1_t, i64m1)
_RVV_COMMON(vuint64m8_t, uint64_t, u64m8, vbool8_t, 64, m8)
_RVV_INT(vuint64m8_t, uint64_t, u64m8, vbool8_t, 8, u64)
_RVV_UNSIGNED(vuint64m8_t, uint64_t, u64m8, vbool8_t, vuint64m1_t, u64m1)
_RVV_COMMON(vfloat64m8_t, double, f64m8, vbool8_t, 64, m8)
_RVV_FLOAT(vfloat64m8_t, double, f64m8, vbool8_t, 8, vfloat64m1_t, f64m1, vint64m8_t, i64m8, vuint64m8_t, u64m8)

/* The mask registers, which every ratio of element width to group size shares */
_RVV_MASKOPS(vbool1_t, 1)
_RVV_MASKOPS(vbool2_t, 2)
_RVV_MASKOPS(vbool4_t, 4)
_RVV_MASKOPS(vbool8_t, 8)
_RVV_MASKOPS(vbool16_t, 16)
_RVV_MASKOPS(vbool32_t, 32)
_RVV_MASKOPS(vbool64_t, 64)

/* The widening arithmetic, whose destination holds twice the element its sources do */
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwaddu, vv, vuint8mf8_t, vuint8mf8_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwaddu, vx, vuint8mf8_t, unsigned char)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwsubu, vv, vuint8mf8_t, vuint8mf8_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwsubu, vx, vuint8mf8_t, unsigned char)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwmulu, vv, vuint8mf8_t, vuint8mf8_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwmulu, vx, vuint8mf8_t, unsigned char)
_RVV_WACC(vuint16mf4_t, u16mf4, unsigned char, vwmaccu, vuint8mf8_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwadd, vv, vint8mf8_t, vint8mf8_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwadd, vx, vint8mf8_t, signed char)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwsub, vv, vint8mf8_t, vint8mf8_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwsub, vx, vint8mf8_t, signed char)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwmul, vv, vint8mf8_t, vint8mf8_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwmul, vx, vint8mf8_t, signed char)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwmulsu, vv, vint8mf8_t, vuint8mf8_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwmulsu, vx, vint8mf8_t, unsigned char)
_RVV_WACC(vint16mf4_t, i16mf4, signed char, vwmacc, vint8mf8_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwaddu, vv, vuint8mf4_t, vuint8mf4_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwaddu, vx, vuint8mf4_t, unsigned char)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwsubu, vv, vuint8mf4_t, vuint8mf4_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwsubu, vx, vuint8mf4_t, unsigned char)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwmulu, vv, vuint8mf4_t, vuint8mf4_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwmulu, vx, vuint8mf4_t, unsigned char)
_RVV_WACC(vuint16mf2_t, u16mf2, unsigned char, vwmaccu, vuint8mf4_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwadd, vv, vint8mf4_t, vint8mf4_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwadd, vx, vint8mf4_t, signed char)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwsub, vv, vint8mf4_t, vint8mf4_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwsub, vx, vint8mf4_t, signed char)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwmul, vv, vint8mf4_t, vint8mf4_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwmul, vx, vint8mf4_t, signed char)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwmulsu, vv, vint8mf4_t, vuint8mf4_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwmulsu, vx, vint8mf4_t, unsigned char)
_RVV_WACC(vint16mf2_t, i16mf2, signed char, vwmacc, vint8mf4_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwaddu, vv, vuint8mf2_t, vuint8mf2_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwaddu, vx, vuint8mf2_t, unsigned char)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwsubu, vv, vuint8mf2_t, vuint8mf2_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwsubu, vx, vuint8mf2_t, unsigned char)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwmulu, vv, vuint8mf2_t, vuint8mf2_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwmulu, vx, vuint8mf2_t, unsigned char)
_RVV_WACC(vuint16m1_t, u16m1, unsigned char, vwmaccu, vuint8mf2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwadd, vv, vint8mf2_t, vint8mf2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwadd, vx, vint8mf2_t, signed char)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwsub, vv, vint8mf2_t, vint8mf2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwsub, vx, vint8mf2_t, signed char)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwmul, vv, vint8mf2_t, vint8mf2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwmul, vx, vint8mf2_t, signed char)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwmulsu, vv, vint8mf2_t, vuint8mf2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwmulsu, vx, vint8mf2_t, unsigned char)
_RVV_WACC(vint16m1_t, i16m1, signed char, vwmacc, vint8mf2_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwaddu, vv, vuint8m1_t, vuint8m1_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwaddu, vx, vuint8m1_t, unsigned char)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwsubu, vv, vuint8m1_t, vuint8m1_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwsubu, vx, vuint8m1_t, unsigned char)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwmulu, vv, vuint8m1_t, vuint8m1_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwmulu, vx, vuint8m1_t, unsigned char)
_RVV_WACC(vuint16m2_t, u16m2, unsigned char, vwmaccu, vuint8m1_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwadd, vv, vint8m1_t, vint8m1_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwadd, vx, vint8m1_t, signed char)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwsub, vv, vint8m1_t, vint8m1_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwsub, vx, vint8m1_t, signed char)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwmul, vv, vint8m1_t, vint8m1_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwmul, vx, vint8m1_t, signed char)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwmulsu, vv, vint8m1_t, vuint8m1_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwmulsu, vx, vint8m1_t, unsigned char)
_RVV_WACC(vint16m2_t, i16m2, signed char, vwmacc, vint8m1_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwaddu, vv, vuint8m2_t, vuint8m2_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwaddu, vx, vuint8m2_t, unsigned char)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwsubu, vv, vuint8m2_t, vuint8m2_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwsubu, vx, vuint8m2_t, unsigned char)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwmulu, vv, vuint8m2_t, vuint8m2_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwmulu, vx, vuint8m2_t, unsigned char)
_RVV_WACC(vuint16m4_t, u16m4, unsigned char, vwmaccu, vuint8m2_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwadd, vv, vint8m2_t, vint8m2_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwadd, vx, vint8m2_t, signed char)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwsub, vv, vint8m2_t, vint8m2_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwsub, vx, vint8m2_t, signed char)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwmul, vv, vint8m2_t, vint8m2_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwmul, vx, vint8m2_t, signed char)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwmulsu, vv, vint8m2_t, vuint8m2_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwmulsu, vx, vint8m2_t, unsigned char)
_RVV_WACC(vint16m4_t, i16m4, signed char, vwmacc, vint8m2_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwaddu, vv, vuint8m4_t, vuint8m4_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwaddu, vx, vuint8m4_t, unsigned char)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwsubu, vv, vuint8m4_t, vuint8m4_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwsubu, vx, vuint8m4_t, unsigned char)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwmulu, vv, vuint8m4_t, vuint8m4_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwmulu, vx, vuint8m4_t, unsigned char)
_RVV_WACC(vuint16m8_t, u16m8, unsigned char, vwmaccu, vuint8m4_t)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwadd, vv, vint8m4_t, vint8m4_t)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwadd, vx, vint8m4_t, signed char)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwsub, vv, vint8m4_t, vint8m4_t)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwsub, vx, vint8m4_t, signed char)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwmul, vv, vint8m4_t, vint8m4_t)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwmul, vx, vint8m4_t, signed char)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwmulsu, vv, vint8m4_t, vuint8m4_t)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwmulsu, vx, vint8m4_t, unsigned char)
_RVV_WACC(vint16m8_t, i16m8, signed char, vwmacc, vint8m4_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwaddu, vv, vuint16mf4_t, vuint16mf4_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwaddu, vx, vuint16mf4_t, unsigned short)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwsubu, vv, vuint16mf4_t, vuint16mf4_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwsubu, vx, vuint16mf4_t, unsigned short)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwmulu, vv, vuint16mf4_t, vuint16mf4_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwmulu, vx, vuint16mf4_t, unsigned short)
_RVV_WACC(vuint32mf2_t, u32mf2, unsigned short, vwmaccu, vuint16mf4_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwadd, vv, vint16mf4_t, vint16mf4_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwadd, vx, vint16mf4_t, short)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwsub, vv, vint16mf4_t, vint16mf4_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwsub, vx, vint16mf4_t, short)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwmul, vv, vint16mf4_t, vint16mf4_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwmul, vx, vint16mf4_t, short)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwmulsu, vv, vint16mf4_t, vuint16mf4_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwmulsu, vx, vint16mf4_t, unsigned short)
_RVV_WACC(vint32mf2_t, i32mf2, short, vwmacc, vint16mf4_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwaddu, vv, vuint16mf2_t, vuint16mf2_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwaddu, vx, vuint16mf2_t, unsigned short)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwsubu, vv, vuint16mf2_t, vuint16mf2_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwsubu, vx, vuint16mf2_t, unsigned short)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwmulu, vv, vuint16mf2_t, vuint16mf2_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwmulu, vx, vuint16mf2_t, unsigned short)
_RVV_WACC(vuint32m1_t, u32m1, unsigned short, vwmaccu, vuint16mf2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwadd, vv, vint16mf2_t, vint16mf2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwadd, vx, vint16mf2_t, short)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwsub, vv, vint16mf2_t, vint16mf2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwsub, vx, vint16mf2_t, short)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwmul, vv, vint16mf2_t, vint16mf2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwmul, vx, vint16mf2_t, short)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwmulsu, vv, vint16mf2_t, vuint16mf2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwmulsu, vx, vint16mf2_t, unsigned short)
_RVV_WACC(vint32m1_t, i32m1, short, vwmacc, vint16mf2_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwaddu, vv, vuint16m1_t, vuint16m1_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwaddu, vx, vuint16m1_t, unsigned short)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwsubu, vv, vuint16m1_t, vuint16m1_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwsubu, vx, vuint16m1_t, unsigned short)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwmulu, vv, vuint16m1_t, vuint16m1_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwmulu, vx, vuint16m1_t, unsigned short)
_RVV_WACC(vuint32m2_t, u32m2, unsigned short, vwmaccu, vuint16m1_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwadd, vv, vint16m1_t, vint16m1_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwadd, vx, vint16m1_t, short)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwsub, vv, vint16m1_t, vint16m1_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwsub, vx, vint16m1_t, short)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwmul, vv, vint16m1_t, vint16m1_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwmul, vx, vint16m1_t, short)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwmulsu, vv, vint16m1_t, vuint16m1_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwmulsu, vx, vint16m1_t, unsigned short)
_RVV_WACC(vint32m2_t, i32m2, short, vwmacc, vint16m1_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwaddu, vv, vuint16m2_t, vuint16m2_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwaddu, vx, vuint16m2_t, unsigned short)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwsubu, vv, vuint16m2_t, vuint16m2_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwsubu, vx, vuint16m2_t, unsigned short)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwmulu, vv, vuint16m2_t, vuint16m2_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwmulu, vx, vuint16m2_t, unsigned short)
_RVV_WACC(vuint32m4_t, u32m4, unsigned short, vwmaccu, vuint16m2_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwadd, vv, vint16m2_t, vint16m2_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwadd, vx, vint16m2_t, short)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwsub, vv, vint16m2_t, vint16m2_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwsub, vx, vint16m2_t, short)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwmul, vv, vint16m2_t, vint16m2_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwmul, vx, vint16m2_t, short)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwmulsu, vv, vint16m2_t, vuint16m2_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwmulsu, vx, vint16m2_t, unsigned short)
_RVV_WACC(vint32m4_t, i32m4, short, vwmacc, vint16m2_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwaddu, vv, vuint16m4_t, vuint16m4_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwaddu, vx, vuint16m4_t, unsigned short)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwsubu, vv, vuint16m4_t, vuint16m4_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwsubu, vx, vuint16m4_t, unsigned short)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwmulu, vv, vuint16m4_t, vuint16m4_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwmulu, vx, vuint16m4_t, unsigned short)
_RVV_WACC(vuint32m8_t, u32m8, unsigned short, vwmaccu, vuint16m4_t)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwadd, vv, vint16m4_t, vint16m4_t)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwadd, vx, vint16m4_t, short)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwsub, vv, vint16m4_t, vint16m4_t)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwsub, vx, vint16m4_t, short)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwmul, vv, vint16m4_t, vint16m4_t)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwmul, vx, vint16m4_t, short)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwmulsu, vv, vint16m4_t, vuint16m4_t)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwmulsu, vx, vint16m4_t, unsigned short)
_RVV_WACC(vint32m8_t, i32m8, short, vwmacc, vint16m4_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwaddu, vv, vuint32mf2_t, vuint32mf2_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwaddu, vx, vuint32mf2_t, unsigned int)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwsubu, vv, vuint32mf2_t, vuint32mf2_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwsubu, vx, vuint32mf2_t, unsigned int)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwmulu, vv, vuint32mf2_t, vuint32mf2_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwmulu, vx, vuint32mf2_t, unsigned int)
_RVV_WACC(vuint64m1_t, u64m1, unsigned int, vwmaccu, vuint32mf2_t)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwadd, vv, vint32mf2_t, vint32mf2_t)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwadd, vx, vint32mf2_t, int)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwsub, vv, vint32mf2_t, vint32mf2_t)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwsub, vx, vint32mf2_t, int)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwmul, vv, vint32mf2_t, vint32mf2_t)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwmul, vx, vint32mf2_t, int)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwmulsu, vv, vint32mf2_t, vuint32mf2_t)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwmulsu, vx, vint32mf2_t, unsigned int)
_RVV_WACC(vint64m1_t, i64m1, int, vwmacc, vint32mf2_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwaddu, vv, vuint32m1_t, vuint32m1_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwaddu, vx, vuint32m1_t, unsigned int)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwsubu, vv, vuint32m1_t, vuint32m1_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwsubu, vx, vuint32m1_t, unsigned int)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwmulu, vv, vuint32m1_t, vuint32m1_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwmulu, vx, vuint32m1_t, unsigned int)
_RVV_WACC(vuint64m2_t, u64m2, unsigned int, vwmaccu, vuint32m1_t)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwadd, vv, vint32m1_t, vint32m1_t)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwadd, vx, vint32m1_t, int)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwsub, vv, vint32m1_t, vint32m1_t)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwsub, vx, vint32m1_t, int)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwmul, vv, vint32m1_t, vint32m1_t)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwmul, vx, vint32m1_t, int)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwmulsu, vv, vint32m1_t, vuint32m1_t)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwmulsu, vx, vint32m1_t, unsigned int)
_RVV_WACC(vint64m2_t, i64m2, int, vwmacc, vint32m1_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwaddu, vv, vuint32m2_t, vuint32m2_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwaddu, vx, vuint32m2_t, unsigned int)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwsubu, vv, vuint32m2_t, vuint32m2_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwsubu, vx, vuint32m2_t, unsigned int)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwmulu, vv, vuint32m2_t, vuint32m2_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwmulu, vx, vuint32m2_t, unsigned int)
_RVV_WACC(vuint64m4_t, u64m4, unsigned int, vwmaccu, vuint32m2_t)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwadd, vv, vint32m2_t, vint32m2_t)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwadd, vx, vint32m2_t, int)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwsub, vv, vint32m2_t, vint32m2_t)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwsub, vx, vint32m2_t, int)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwmul, vv, vint32m2_t, vint32m2_t)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwmul, vx, vint32m2_t, int)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwmulsu, vv, vint32m2_t, vuint32m2_t)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwmulsu, vx, vint32m2_t, unsigned int)
_RVV_WACC(vint64m4_t, i64m4, int, vwmacc, vint32m2_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwaddu, vv, vuint32m4_t, vuint32m4_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwaddu, vx, vuint32m4_t, unsigned int)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwsubu, vv, vuint32m4_t, vuint32m4_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwsubu, vx, vuint32m4_t, unsigned int)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwmulu, vv, vuint32m4_t, vuint32m4_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwmulu, vx, vuint32m4_t, unsigned int)
_RVV_WACC(vuint64m8_t, u64m8, unsigned int, vwmaccu, vuint32m4_t)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwadd, vv, vint32m4_t, vint32m4_t)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwadd, vx, vint32m4_t, int)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwsub, vv, vint32m4_t, vint32m4_t)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwsub, vx, vint32m4_t, int)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwmul, vv, vint32m4_t, vint32m4_t)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwmul, vx, vint32m4_t, int)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwmulsu, vv, vint32m4_t, vuint32m4_t)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwmulsu, vx, vint32m4_t, unsigned int)
_RVV_WACC(vint64m8_t, i64m8, int, vwmacc, vint32m4_t)

/* The narrowing shifts and the clipping ones, whose source holds twice the element */
_RVV_OP2(vuint8mf8_t, u8mf8, vbool64_t, vnsrl, wv, vuint16mf4_t, vuint8mf8_t)
_RVV_OP2(vuint8mf8_t, u8mf8, vbool64_t, vnsrl, wx, vuint16mf4_t, size_t)
_RVV_OPI2(vuint8mf8_t, u8mf8, vbool64_t, vnsrl, wi, vuint16mf4_t)
_RVV_OP2(vuint8mf8_t, u8mf8, vbool64_t, vnclipu, wv, vuint16mf4_t, vuint8mf8_t)
_RVV_OP2(vuint8mf8_t, u8mf8, vbool64_t, vnclipu, wx, vuint16mf4_t, size_t)
_RVV_OPI2(vuint8mf8_t, u8mf8, vbool64_t, vnclipu, wi, vuint16mf4_t)
_RVV_OP2(vint8mf8_t, i8mf8, vbool64_t, vnsra, wv, vint16mf4_t, vuint8mf8_t)
_RVV_OP2(vint8mf8_t, i8mf8, vbool64_t, vnsra, wx, vint16mf4_t, size_t)
_RVV_OPI2(vint8mf8_t, i8mf8, vbool64_t, vnsra, wi, vint16mf4_t)
_RVV_OP2(vint8mf8_t, i8mf8, vbool64_t, vnclip, wv, vint16mf4_t, vuint8mf8_t)
_RVV_OP2(vint8mf8_t, i8mf8, vbool64_t, vnclip, wx, vint16mf4_t, size_t)
_RVV_OPI2(vint8mf8_t, i8mf8, vbool64_t, vnclip, wi, vint16mf4_t)
_RVV_OP2(vuint8mf4_t, u8mf4, vbool32_t, vnsrl, wv, vuint16mf2_t, vuint8mf4_t)
_RVV_OP2(vuint8mf4_t, u8mf4, vbool32_t, vnsrl, wx, vuint16mf2_t, size_t)
_RVV_OPI2(vuint8mf4_t, u8mf4, vbool32_t, vnsrl, wi, vuint16mf2_t)
_RVV_OP2(vuint8mf4_t, u8mf4, vbool32_t, vnclipu, wv, vuint16mf2_t, vuint8mf4_t)
_RVV_OP2(vuint8mf4_t, u8mf4, vbool32_t, vnclipu, wx, vuint16mf2_t, size_t)
_RVV_OPI2(vuint8mf4_t, u8mf4, vbool32_t, vnclipu, wi, vuint16mf2_t)
_RVV_OP2(vint8mf4_t, i8mf4, vbool32_t, vnsra, wv, vint16mf2_t, vuint8mf4_t)
_RVV_OP2(vint8mf4_t, i8mf4, vbool32_t, vnsra, wx, vint16mf2_t, size_t)
_RVV_OPI2(vint8mf4_t, i8mf4, vbool32_t, vnsra, wi, vint16mf2_t)
_RVV_OP2(vint8mf4_t, i8mf4, vbool32_t, vnclip, wv, vint16mf2_t, vuint8mf4_t)
_RVV_OP2(vint8mf4_t, i8mf4, vbool32_t, vnclip, wx, vint16mf2_t, size_t)
_RVV_OPI2(vint8mf4_t, i8mf4, vbool32_t, vnclip, wi, vint16mf2_t)
_RVV_OP2(vuint8mf2_t, u8mf2, vbool16_t, vnsrl, wv, vuint16m1_t, vuint8mf2_t)
_RVV_OP2(vuint8mf2_t, u8mf2, vbool16_t, vnsrl, wx, vuint16m1_t, size_t)
_RVV_OPI2(vuint8mf2_t, u8mf2, vbool16_t, vnsrl, wi, vuint16m1_t)
_RVV_OP2(vuint8mf2_t, u8mf2, vbool16_t, vnclipu, wv, vuint16m1_t, vuint8mf2_t)
_RVV_OP2(vuint8mf2_t, u8mf2, vbool16_t, vnclipu, wx, vuint16m1_t, size_t)
_RVV_OPI2(vuint8mf2_t, u8mf2, vbool16_t, vnclipu, wi, vuint16m1_t)
_RVV_OP2(vint8mf2_t, i8mf2, vbool16_t, vnsra, wv, vint16m1_t, vuint8mf2_t)
_RVV_OP2(vint8mf2_t, i8mf2, vbool16_t, vnsra, wx, vint16m1_t, size_t)
_RVV_OPI2(vint8mf2_t, i8mf2, vbool16_t, vnsra, wi, vint16m1_t)
_RVV_OP2(vint8mf2_t, i8mf2, vbool16_t, vnclip, wv, vint16m1_t, vuint8mf2_t)
_RVV_OP2(vint8mf2_t, i8mf2, vbool16_t, vnclip, wx, vint16m1_t, size_t)
_RVV_OPI2(vint8mf2_t, i8mf2, vbool16_t, vnclip, wi, vint16m1_t)
_RVV_OP2(vuint8m1_t, u8m1, vbool8_t, vnsrl, wv, vuint16m2_t, vuint8m1_t)
_RVV_OP2(vuint8m1_t, u8m1, vbool8_t, vnsrl, wx, vuint16m2_t, size_t)
_RVV_OPI2(vuint8m1_t, u8m1, vbool8_t, vnsrl, wi, vuint16m2_t)
_RVV_OP2(vuint8m1_t, u8m1, vbool8_t, vnclipu, wv, vuint16m2_t, vuint8m1_t)
_RVV_OP2(vuint8m1_t, u8m1, vbool8_t, vnclipu, wx, vuint16m2_t, size_t)
_RVV_OPI2(vuint8m1_t, u8m1, vbool8_t, vnclipu, wi, vuint16m2_t)
_RVV_OP2(vint8m1_t, i8m1, vbool8_t, vnsra, wv, vint16m2_t, vuint8m1_t)
_RVV_OP2(vint8m1_t, i8m1, vbool8_t, vnsra, wx, vint16m2_t, size_t)
_RVV_OPI2(vint8m1_t, i8m1, vbool8_t, vnsra, wi, vint16m2_t)
_RVV_OP2(vint8m1_t, i8m1, vbool8_t, vnclip, wv, vint16m2_t, vuint8m1_t)
_RVV_OP2(vint8m1_t, i8m1, vbool8_t, vnclip, wx, vint16m2_t, size_t)
_RVV_OPI2(vint8m1_t, i8m1, vbool8_t, vnclip, wi, vint16m2_t)
_RVV_OP2(vuint8m2_t, u8m2, vbool4_t, vnsrl, wv, vuint16m4_t, vuint8m2_t)
_RVV_OP2(vuint8m2_t, u8m2, vbool4_t, vnsrl, wx, vuint16m4_t, size_t)
_RVV_OPI2(vuint8m2_t, u8m2, vbool4_t, vnsrl, wi, vuint16m4_t)
_RVV_OP2(vuint8m2_t, u8m2, vbool4_t, vnclipu, wv, vuint16m4_t, vuint8m2_t)
_RVV_OP2(vuint8m2_t, u8m2, vbool4_t, vnclipu, wx, vuint16m4_t, size_t)
_RVV_OPI2(vuint8m2_t, u8m2, vbool4_t, vnclipu, wi, vuint16m4_t)
_RVV_OP2(vint8m2_t, i8m2, vbool4_t, vnsra, wv, vint16m4_t, vuint8m2_t)
_RVV_OP2(vint8m2_t, i8m2, vbool4_t, vnsra, wx, vint16m4_t, size_t)
_RVV_OPI2(vint8m2_t, i8m2, vbool4_t, vnsra, wi, vint16m4_t)
_RVV_OP2(vint8m2_t, i8m2, vbool4_t, vnclip, wv, vint16m4_t, vuint8m2_t)
_RVV_OP2(vint8m2_t, i8m2, vbool4_t, vnclip, wx, vint16m4_t, size_t)
_RVV_OPI2(vint8m2_t, i8m2, vbool4_t, vnclip, wi, vint16m4_t)
_RVV_OP2(vuint8m4_t, u8m4, vbool2_t, vnsrl, wv, vuint16m8_t, vuint8m4_t)
_RVV_OP2(vuint8m4_t, u8m4, vbool2_t, vnsrl, wx, vuint16m8_t, size_t)
_RVV_OPI2(vuint8m4_t, u8m4, vbool2_t, vnsrl, wi, vuint16m8_t)
_RVV_OP2(vuint8m4_t, u8m4, vbool2_t, vnclipu, wv, vuint16m8_t, vuint8m4_t)
_RVV_OP2(vuint8m4_t, u8m4, vbool2_t, vnclipu, wx, vuint16m8_t, size_t)
_RVV_OPI2(vuint8m4_t, u8m4, vbool2_t, vnclipu, wi, vuint16m8_t)
_RVV_OP2(vint8m4_t, i8m4, vbool2_t, vnsra, wv, vint16m8_t, vuint8m4_t)
_RVV_OP2(vint8m4_t, i8m4, vbool2_t, vnsra, wx, vint16m8_t, size_t)
_RVV_OPI2(vint8m4_t, i8m4, vbool2_t, vnsra, wi, vint16m8_t)
_RVV_OP2(vint8m4_t, i8m4, vbool2_t, vnclip, wv, vint16m8_t, vuint8m4_t)
_RVV_OP2(vint8m4_t, i8m4, vbool2_t, vnclip, wx, vint16m8_t, size_t)
_RVV_OPI2(vint8m4_t, i8m4, vbool2_t, vnclip, wi, vint16m8_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vnsrl, wv, vuint32mf2_t, vuint16mf4_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vnsrl, wx, vuint32mf2_t, size_t)
_RVV_OPI2(vuint16mf4_t, u16mf4, vbool64_t, vnsrl, wi, vuint32mf2_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vnclipu, wv, vuint32mf2_t, vuint16mf4_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vnclipu, wx, vuint32mf2_t, size_t)
_RVV_OPI2(vuint16mf4_t, u16mf4, vbool64_t, vnclipu, wi, vuint32mf2_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vnsra, wv, vint32mf2_t, vuint16mf4_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vnsra, wx, vint32mf2_t, size_t)
_RVV_OPI2(vint16mf4_t, i16mf4, vbool64_t, vnsra, wi, vint32mf2_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vnclip, wv, vint32mf2_t, vuint16mf4_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vnclip, wx, vint32mf2_t, size_t)
_RVV_OPI2(vint16mf4_t, i16mf4, vbool64_t, vnclip, wi, vint32mf2_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vnsrl, wv, vuint32m1_t, vuint16mf2_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vnsrl, wx, vuint32m1_t, size_t)
_RVV_OPI2(vuint16mf2_t, u16mf2, vbool32_t, vnsrl, wi, vuint32m1_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vnclipu, wv, vuint32m1_t, vuint16mf2_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vnclipu, wx, vuint32m1_t, size_t)
_RVV_OPI2(vuint16mf2_t, u16mf2, vbool32_t, vnclipu, wi, vuint32m1_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vnsra, wv, vint32m1_t, vuint16mf2_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vnsra, wx, vint32m1_t, size_t)
_RVV_OPI2(vint16mf2_t, i16mf2, vbool32_t, vnsra, wi, vint32m1_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vnclip, wv, vint32m1_t, vuint16mf2_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vnclip, wx, vint32m1_t, size_t)
_RVV_OPI2(vint16mf2_t, i16mf2, vbool32_t, vnclip, wi, vint32m1_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vnsrl, wv, vuint32m2_t, vuint16m1_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vnsrl, wx, vuint32m2_t, size_t)
_RVV_OPI2(vuint16m1_t, u16m1, vbool16_t, vnsrl, wi, vuint32m2_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vnclipu, wv, vuint32m2_t, vuint16m1_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vnclipu, wx, vuint32m2_t, size_t)
_RVV_OPI2(vuint16m1_t, u16m1, vbool16_t, vnclipu, wi, vuint32m2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vnsra, wv, vint32m2_t, vuint16m1_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vnsra, wx, vint32m2_t, size_t)
_RVV_OPI2(vint16m1_t, i16m1, vbool16_t, vnsra, wi, vint32m2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vnclip, wv, vint32m2_t, vuint16m1_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vnclip, wx, vint32m2_t, size_t)
_RVV_OPI2(vint16m1_t, i16m1, vbool16_t, vnclip, wi, vint32m2_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vnsrl, wv, vuint32m4_t, vuint16m2_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vnsrl, wx, vuint32m4_t, size_t)
_RVV_OPI2(vuint16m2_t, u16m2, vbool8_t, vnsrl, wi, vuint32m4_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vnclipu, wv, vuint32m4_t, vuint16m2_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vnclipu, wx, vuint32m4_t, size_t)
_RVV_OPI2(vuint16m2_t, u16m2, vbool8_t, vnclipu, wi, vuint32m4_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vnsra, wv, vint32m4_t, vuint16m2_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vnsra, wx, vint32m4_t, size_t)
_RVV_OPI2(vint16m2_t, i16m2, vbool8_t, vnsra, wi, vint32m4_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vnclip, wv, vint32m4_t, vuint16m2_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vnclip, wx, vint32m4_t, size_t)
_RVV_OPI2(vint16m2_t, i16m2, vbool8_t, vnclip, wi, vint32m4_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vnsrl, wv, vuint32m8_t, vuint16m4_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vnsrl, wx, vuint32m8_t, size_t)
_RVV_OPI2(vuint16m4_t, u16m4, vbool4_t, vnsrl, wi, vuint32m8_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vnclipu, wv, vuint32m8_t, vuint16m4_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vnclipu, wx, vuint32m8_t, size_t)
_RVV_OPI2(vuint16m4_t, u16m4, vbool4_t, vnclipu, wi, vuint32m8_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vnsra, wv, vint32m8_t, vuint16m4_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vnsra, wx, vint32m8_t, size_t)
_RVV_OPI2(vint16m4_t, i16m4, vbool4_t, vnsra, wi, vint32m8_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vnclip, wv, vint32m8_t, vuint16m4_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vnclip, wx, vint32m8_t, size_t)
_RVV_OPI2(vint16m4_t, i16m4, vbool4_t, vnclip, wi, vint32m8_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vnsrl, wv, vuint64m1_t, vuint32mf2_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vnsrl, wx, vuint64m1_t, size_t)
_RVV_OPI2(vuint32mf2_t, u32mf2, vbool64_t, vnsrl, wi, vuint64m1_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vnclipu, wv, vuint64m1_t, vuint32mf2_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vnclipu, wx, vuint64m1_t, size_t)
_RVV_OPI2(vuint32mf2_t, u32mf2, vbool64_t, vnclipu, wi, vuint64m1_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vnsra, wv, vint64m1_t, vuint32mf2_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vnsra, wx, vint64m1_t, size_t)
_RVV_OPI2(vint32mf2_t, i32mf2, vbool64_t, vnsra, wi, vint64m1_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vnclip, wv, vint64m1_t, vuint32mf2_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vnclip, wx, vint64m1_t, size_t)
_RVV_OPI2(vint32mf2_t, i32mf2, vbool64_t, vnclip, wi, vint64m1_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vnsrl, wv, vuint64m2_t, vuint32m1_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vnsrl, wx, vuint64m2_t, size_t)
_RVV_OPI2(vuint32m1_t, u32m1, vbool32_t, vnsrl, wi, vuint64m2_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vnclipu, wv, vuint64m2_t, vuint32m1_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vnclipu, wx, vuint64m2_t, size_t)
_RVV_OPI2(vuint32m1_t, u32m1, vbool32_t, vnclipu, wi, vuint64m2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vnsra, wv, vint64m2_t, vuint32m1_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vnsra, wx, vint64m2_t, size_t)
_RVV_OPI2(vint32m1_t, i32m1, vbool32_t, vnsra, wi, vint64m2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vnclip, wv, vint64m2_t, vuint32m1_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vnclip, wx, vint64m2_t, size_t)
_RVV_OPI2(vint32m1_t, i32m1, vbool32_t, vnclip, wi, vint64m2_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vnsrl, wv, vuint64m4_t, vuint32m2_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vnsrl, wx, vuint64m4_t, size_t)
_RVV_OPI2(vuint32m2_t, u32m2, vbool16_t, vnsrl, wi, vuint64m4_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vnclipu, wv, vuint64m4_t, vuint32m2_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vnclipu, wx, vuint64m4_t, size_t)
_RVV_OPI2(vuint32m2_t, u32m2, vbool16_t, vnclipu, wi, vuint64m4_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vnsra, wv, vint64m4_t, vuint32m2_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vnsra, wx, vint64m4_t, size_t)
_RVV_OPI2(vint32m2_t, i32m2, vbool16_t, vnsra, wi, vint64m4_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vnclip, wv, vint64m4_t, vuint32m2_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vnclip, wx, vint64m4_t, size_t)
_RVV_OPI2(vint32m2_t, i32m2, vbool16_t, vnclip, wi, vint64m4_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vnsrl, wv, vuint64m8_t, vuint32m4_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vnsrl, wx, vuint64m8_t, size_t)
_RVV_OPI2(vuint32m4_t, u32m4, vbool8_t, vnsrl, wi, vuint64m8_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vnclipu, wv, vuint64m8_t, vuint32m4_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vnclipu, wx, vuint64m8_t, size_t)
_RVV_OPI2(vuint32m4_t, u32m4, vbool8_t, vnclipu, wi, vuint64m8_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vnsra, wv, vint64m8_t, vuint32m4_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vnsra, wx, vint64m8_t, size_t)
_RVV_OPI2(vint32m4_t, i32m4, vbool8_t, vnsra, wi, vint64m8_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vnclip, wv, vint64m8_t, vuint32m4_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vnclip, wx, vint64m8_t, size_t)
_RVV_OPI2(vint32m4_t, i32m4, vbool8_t, vnclip, wi, vint64m8_t)

/* The extensions, which reach a wider element out of a narrower one */
_RVV_UNARY(vuint16mf4_t, u16mf4, vbool64_t, vzext, vf2, vuint8mf8_t)
_RVV_UNARY(vint16mf4_t, i16mf4, vbool64_t, vsext, vf2, vint8mf8_t)
_RVV_UNARY(vuint16mf2_t, u16mf2, vbool32_t, vzext, vf2, vuint8mf4_t)
_RVV_UNARY(vint16mf2_t, i16mf2, vbool32_t, vsext, vf2, vint8mf4_t)
_RVV_UNARY(vuint16m1_t, u16m1, vbool16_t, vzext, vf2, vuint8mf2_t)
_RVV_UNARY(vint16m1_t, i16m1, vbool16_t, vsext, vf2, vint8mf2_t)
_RVV_UNARY(vuint16m2_t, u16m2, vbool8_t, vzext, vf2, vuint8m1_t)
_RVV_UNARY(vint16m2_t, i16m2, vbool8_t, vsext, vf2, vint8m1_t)
_RVV_UNARY(vuint16m4_t, u16m4, vbool4_t, vzext, vf2, vuint8m2_t)
_RVV_UNARY(vint16m4_t, i16m4, vbool4_t, vsext, vf2, vint8m2_t)
_RVV_UNARY(vuint16m8_t, u16m8, vbool2_t, vzext, vf2, vuint8m4_t)
_RVV_UNARY(vint16m8_t, i16m8, vbool2_t, vsext, vf2, vint8m4_t)
_RVV_UNARY(vuint32mf2_t, u32mf2, vbool64_t, vzext, vf2, vuint16mf4_t)
_RVV_UNARY(vint32mf2_t, i32mf2, vbool64_t, vsext, vf2, vint16mf4_t)
_RVV_UNARY(vuint32m1_t, u32m1, vbool32_t, vzext, vf2, vuint16mf2_t)
_RVV_UNARY(vint32m1_t, i32m1, vbool32_t, vsext, vf2, vint16mf2_t)
_RVV_UNARY(vuint32m2_t, u32m2, vbool16_t, vzext, vf2, vuint16m1_t)
_RVV_UNARY(vint32m2_t, i32m2, vbool16_t, vsext, vf2, vint16m1_t)
_RVV_UNARY(vuint32m4_t, u32m4, vbool8_t, vzext, vf2, vuint16m2_t)
_RVV_UNARY(vint32m4_t, i32m4, vbool8_t, vsext, vf2, vint16m2_t)
_RVV_UNARY(vuint32m8_t, u32m8, vbool4_t, vzext, vf2, vuint16m4_t)
_RVV_UNARY(vint32m8_t, i32m8, vbool4_t, vsext, vf2, vint16m4_t)
_RVV_UNARY(vuint64m1_t, u64m1, vbool64_t, vzext, vf2, vuint32mf2_t)
_RVV_UNARY(vint64m1_t, i64m1, vbool64_t, vsext, vf2, vint32mf2_t)
_RVV_UNARY(vuint64m2_t, u64m2, vbool32_t, vzext, vf2, vuint32m1_t)
_RVV_UNARY(vint64m2_t, i64m2, vbool32_t, vsext, vf2, vint32m1_t)
_RVV_UNARY(vuint64m4_t, u64m4, vbool16_t, vzext, vf2, vuint32m2_t)
_RVV_UNARY(vint64m4_t, i64m4, vbool16_t, vsext, vf2, vint32m2_t)
_RVV_UNARY(vuint64m8_t, u64m8, vbool8_t, vzext, vf2, vuint32m4_t)
_RVV_UNARY(vint64m8_t, i64m8, vbool8_t, vsext, vf2, vint32m4_t)
_RVV_UNARY(vuint32mf2_t, u32mf2, vbool64_t, vzext, vf4, vuint8mf8_t)
_RVV_UNARY(vint32mf2_t, i32mf2, vbool64_t, vsext, vf4, vint8mf8_t)
_RVV_UNARY(vuint32m1_t, u32m1, vbool32_t, vzext, vf4, vuint8mf4_t)
_RVV_UNARY(vint32m1_t, i32m1, vbool32_t, vsext, vf4, vint8mf4_t)
_RVV_UNARY(vuint32m2_t, u32m2, vbool16_t, vzext, vf4, vuint8mf2_t)
_RVV_UNARY(vint32m2_t, i32m2, vbool16_t, vsext, vf4, vint8mf2_t)
_RVV_UNARY(vuint32m4_t, u32m4, vbool8_t, vzext, vf4, vuint8m1_t)
_RVV_UNARY(vint32m4_t, i32m4, vbool8_t, vsext, vf4, vint8m1_t)
_RVV_UNARY(vuint32m8_t, u32m8, vbool4_t, vzext, vf4, vuint8m2_t)
_RVV_UNARY(vint32m8_t, i32m8, vbool4_t, vsext, vf4, vint8m2_t)
_RVV_UNARY(vuint64m1_t, u64m1, vbool64_t, vzext, vf4, vuint16mf4_t)
_RVV_UNARY(vint64m1_t, i64m1, vbool64_t, vsext, vf4, vint16mf4_t)
_RVV_UNARY(vuint64m2_t, u64m2, vbool32_t, vzext, vf4, vuint16mf2_t)
_RVV_UNARY(vint64m2_t, i64m2, vbool32_t, vsext, vf4, vint16mf2_t)
_RVV_UNARY(vuint64m4_t, u64m4, vbool16_t, vzext, vf4, vuint16m1_t)
_RVV_UNARY(vint64m4_t, i64m4, vbool16_t, vsext, vf4, vint16m1_t)
_RVV_UNARY(vuint64m8_t, u64m8, vbool8_t, vzext, vf4, vuint16m2_t)
_RVV_UNARY(vint64m8_t, i64m8, vbool8_t, vsext, vf4, vint16m2_t)
_RVV_UNARY(vuint64m1_t, u64m1, vbool64_t, vzext, vf8, vuint8mf8_t)
_RVV_UNARY(vint64m1_t, i64m1, vbool64_t, vsext, vf8, vint8mf8_t)
_RVV_UNARY(vuint64m2_t, u64m2, vbool32_t, vzext, vf8, vuint8mf4_t)
_RVV_UNARY(vint64m2_t, i64m2, vbool32_t, vsext, vf8, vint8mf4_t)
_RVV_UNARY(vuint64m4_t, u64m4, vbool16_t, vzext, vf8, vuint8mf2_t)
_RVV_UNARY(vint64m4_t, i64m4, vbool16_t, vsext, vf8, vint8mf2_t)
_RVV_UNARY(vuint64m8_t, u64m8, vbool8_t, vzext, vf8, vuint8m1_t)
_RVV_UNARY(vint64m8_t, i64m8, vbool8_t, vsext, vf8, vint8m1_t)


/* A widening form whose second source is already wide */
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwaddu, wv, vuint16mf4_t, vuint8mf8_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwaddu, wx, vuint16mf4_t, unsigned char)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwsubu, wv, vuint16mf4_t, vuint8mf8_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwsubu, wx, vuint16mf4_t, unsigned char)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwadd, wv, vint16mf4_t, vint8mf8_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwadd, wx, vint16mf4_t, signed char)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwsub, wv, vint16mf4_t, vint8mf8_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vwsub, wx, vint16mf4_t, signed char)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwaddu, wv, vuint16mf2_t, vuint8mf4_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwaddu, wx, vuint16mf2_t, unsigned char)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwsubu, wv, vuint16mf2_t, vuint8mf4_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwsubu, wx, vuint16mf2_t, unsigned char)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwadd, wv, vint16mf2_t, vint8mf4_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwadd, wx, vint16mf2_t, signed char)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwsub, wv, vint16mf2_t, vint8mf4_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vwsub, wx, vint16mf2_t, signed char)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwaddu, wv, vuint16m1_t, vuint8mf2_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwaddu, wx, vuint16m1_t, unsigned char)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwsubu, wv, vuint16m1_t, vuint8mf2_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwsubu, wx, vuint16m1_t, unsigned char)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwadd, wv, vint16m1_t, vint8mf2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwadd, wx, vint16m1_t, signed char)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwsub, wv, vint16m1_t, vint8mf2_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vwsub, wx, vint16m1_t, signed char)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwaddu, wv, vuint16m2_t, vuint8m1_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwaddu, wx, vuint16m2_t, unsigned char)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwsubu, wv, vuint16m2_t, vuint8m1_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwsubu, wx, vuint16m2_t, unsigned char)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwadd, wv, vint16m2_t, vint8m1_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwadd, wx, vint16m2_t, signed char)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwsub, wv, vint16m2_t, vint8m1_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vwsub, wx, vint16m2_t, signed char)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwaddu, wv, vuint16m4_t, vuint8m2_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwaddu, wx, vuint16m4_t, unsigned char)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwsubu, wv, vuint16m4_t, vuint8m2_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwsubu, wx, vuint16m4_t, unsigned char)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwadd, wv, vint16m4_t, vint8m2_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwadd, wx, vint16m4_t, signed char)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwsub, wv, vint16m4_t, vint8m2_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vwsub, wx, vint16m4_t, signed char)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwaddu, wv, vuint16m8_t, vuint8m4_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwaddu, wx, vuint16m8_t, unsigned char)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwsubu, wv, vuint16m8_t, vuint8m4_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwsubu, wx, vuint16m8_t, unsigned char)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwadd, wv, vint16m8_t, vint8m4_t)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwadd, wx, vint16m8_t, signed char)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwsub, wv, vint16m8_t, vint8m4_t)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vwsub, wx, vint16m8_t, signed char)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwaddu, wv, vuint32mf2_t, vuint16mf4_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwaddu, wx, vuint32mf2_t, unsigned short)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwsubu, wv, vuint32mf2_t, vuint16mf4_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwsubu, wx, vuint32mf2_t, unsigned short)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwadd, wv, vint32mf2_t, vint16mf4_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwadd, wx, vint32mf2_t, short)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwsub, wv, vint32mf2_t, vint16mf4_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vwsub, wx, vint32mf2_t, short)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwaddu, wv, vuint32m1_t, vuint16mf2_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwaddu, wx, vuint32m1_t, unsigned short)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwsubu, wv, vuint32m1_t, vuint16mf2_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwsubu, wx, vuint32m1_t, unsigned short)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwadd, wv, vint32m1_t, vint16mf2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwadd, wx, vint32m1_t, short)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwsub, wv, vint32m1_t, vint16mf2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vwsub, wx, vint32m1_t, short)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwaddu, wv, vuint32m2_t, vuint16m1_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwaddu, wx, vuint32m2_t, unsigned short)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwsubu, wv, vuint32m2_t, vuint16m1_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwsubu, wx, vuint32m2_t, unsigned short)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwadd, wv, vint32m2_t, vint16m1_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwadd, wx, vint32m2_t, short)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwsub, wv, vint32m2_t, vint16m1_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vwsub, wx, vint32m2_t, short)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwaddu, wv, vuint32m4_t, vuint16m2_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwaddu, wx, vuint32m4_t, unsigned short)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwsubu, wv, vuint32m4_t, vuint16m2_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwsubu, wx, vuint32m4_t, unsigned short)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwadd, wv, vint32m4_t, vint16m2_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwadd, wx, vint32m4_t, short)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwsub, wv, vint32m4_t, vint16m2_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vwsub, wx, vint32m4_t, short)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwaddu, wv, vuint32m8_t, vuint16m4_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwaddu, wx, vuint32m8_t, unsigned short)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwsubu, wv, vuint32m8_t, vuint16m4_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwsubu, wx, vuint32m8_t, unsigned short)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwadd, wv, vint32m8_t, vint16m4_t)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwadd, wx, vint32m8_t, short)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwsub, wv, vint32m8_t, vint16m4_t)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vwsub, wx, vint32m8_t, short)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwaddu, wv, vuint64m1_t, vuint32mf2_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwaddu, wx, vuint64m1_t, unsigned int)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwsubu, wv, vuint64m1_t, vuint32mf2_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwsubu, wx, vuint64m1_t, unsigned int)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwadd, wv, vint64m1_t, vint32mf2_t)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwadd, wx, vint64m1_t, int)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwsub, wv, vint64m1_t, vint32mf2_t)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vwsub, wx, vint64m1_t, int)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwaddu, wv, vuint64m2_t, vuint32m1_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwaddu, wx, vuint64m2_t, unsigned int)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwsubu, wv, vuint64m2_t, vuint32m1_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwsubu, wx, vuint64m2_t, unsigned int)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwadd, wv, vint64m2_t, vint32m1_t)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwadd, wx, vint64m2_t, int)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwsub, wv, vint64m2_t, vint32m1_t)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vwsub, wx, vint64m2_t, int)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwaddu, wv, vuint64m4_t, vuint32m2_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwaddu, wx, vuint64m4_t, unsigned int)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwsubu, wv, vuint64m4_t, vuint32m2_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwsubu, wx, vuint64m4_t, unsigned int)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwadd, wv, vint64m4_t, vint32m2_t)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwadd, wx, vint64m4_t, int)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwsub, wv, vint64m4_t, vint32m2_t)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vwsub, wx, vint64m4_t, int)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwaddu, wv, vuint64m8_t, vuint32m4_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwaddu, wx, vuint64m8_t, unsigned int)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwsubu, wv, vuint64m8_t, vuint32m4_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwsubu, wx, vuint64m8_t, unsigned int)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwadd, wv, vint64m8_t, vint32m4_t)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwadd, wx, vint64m8_t, int)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwsub, wv, vint64m8_t, vint32m4_t)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vwsub, wx, vint64m8_t, int)

/* The multiply accumulate forms that mix a signed source with an unsigned one */
vint16mf4_t __riscv_vwmaccsu_vv_i16mf4(vint16mf4_t vd, vuint8mf8_t vs1, vint8mf8_t vs2, size_t vl);
vint16mf4_t __riscv_vwmaccsu_vx_i16mf4(vint16mf4_t vd, unsigned char rs1, vint8mf8_t vs2, size_t vl);
vint16mf4_t __riscv_vwmaccus_vx_i16mf4(vint16mf4_t vd, signed char rs1, vuint8mf8_t vs2, size_t vl);
vint16mf2_t __riscv_vwmaccsu_vv_i16mf2(vint16mf2_t vd, vuint8mf4_t vs1, vint8mf4_t vs2, size_t vl);
vint16mf2_t __riscv_vwmaccsu_vx_i16mf2(vint16mf2_t vd, unsigned char rs1, vint8mf4_t vs2, size_t vl);
vint16mf2_t __riscv_vwmaccus_vx_i16mf2(vint16mf2_t vd, signed char rs1, vuint8mf4_t vs2, size_t vl);
vint16m1_t __riscv_vwmaccsu_vv_i16m1(vint16m1_t vd, vuint8mf2_t vs1, vint8mf2_t vs2, size_t vl);
vint16m1_t __riscv_vwmaccsu_vx_i16m1(vint16m1_t vd, unsigned char rs1, vint8mf2_t vs2, size_t vl);
vint16m1_t __riscv_vwmaccus_vx_i16m1(vint16m1_t vd, signed char rs1, vuint8mf2_t vs2, size_t vl);
vint16m2_t __riscv_vwmaccsu_vv_i16m2(vint16m2_t vd, vuint8m1_t vs1, vint8m1_t vs2, size_t vl);
vint16m2_t __riscv_vwmaccsu_vx_i16m2(vint16m2_t vd, unsigned char rs1, vint8m1_t vs2, size_t vl);
vint16m2_t __riscv_vwmaccus_vx_i16m2(vint16m2_t vd, signed char rs1, vuint8m1_t vs2, size_t vl);
vint16m4_t __riscv_vwmaccsu_vv_i16m4(vint16m4_t vd, vuint8m2_t vs1, vint8m2_t vs2, size_t vl);
vint16m4_t __riscv_vwmaccsu_vx_i16m4(vint16m4_t vd, unsigned char rs1, vint8m2_t vs2, size_t vl);
vint16m4_t __riscv_vwmaccus_vx_i16m4(vint16m4_t vd, signed char rs1, vuint8m2_t vs2, size_t vl);
vint16m8_t __riscv_vwmaccsu_vv_i16m8(vint16m8_t vd, vuint8m4_t vs1, vint8m4_t vs2, size_t vl);
vint16m8_t __riscv_vwmaccsu_vx_i16m8(vint16m8_t vd, unsigned char rs1, vint8m4_t vs2, size_t vl);
vint16m8_t __riscv_vwmaccus_vx_i16m8(vint16m8_t vd, signed char rs1, vuint8m4_t vs2, size_t vl);
vint32mf2_t __riscv_vwmaccsu_vv_i32mf2(vint32mf2_t vd, vuint16mf4_t vs1, vint16mf4_t vs2, size_t vl);
vint32mf2_t __riscv_vwmaccsu_vx_i32mf2(vint32mf2_t vd, unsigned short rs1, vint16mf4_t vs2, size_t vl);
vint32mf2_t __riscv_vwmaccus_vx_i32mf2(vint32mf2_t vd, short rs1, vuint16mf4_t vs2, size_t vl);
vint32m1_t __riscv_vwmaccsu_vv_i32m1(vint32m1_t vd, vuint16mf2_t vs1, vint16mf2_t vs2, size_t vl);
vint32m1_t __riscv_vwmaccsu_vx_i32m1(vint32m1_t vd, unsigned short rs1, vint16mf2_t vs2, size_t vl);
vint32m1_t __riscv_vwmaccus_vx_i32m1(vint32m1_t vd, short rs1, vuint16mf2_t vs2, size_t vl);
vint32m2_t __riscv_vwmaccsu_vv_i32m2(vint32m2_t vd, vuint16m1_t vs1, vint16m1_t vs2, size_t vl);
vint32m2_t __riscv_vwmaccsu_vx_i32m2(vint32m2_t vd, unsigned short rs1, vint16m1_t vs2, size_t vl);
vint32m2_t __riscv_vwmaccus_vx_i32m2(vint32m2_t vd, short rs1, vuint16m1_t vs2, size_t vl);
vint32m4_t __riscv_vwmaccsu_vv_i32m4(vint32m4_t vd, vuint16m2_t vs1, vint16m2_t vs2, size_t vl);
vint32m4_t __riscv_vwmaccsu_vx_i32m4(vint32m4_t vd, unsigned short rs1, vint16m2_t vs2, size_t vl);
vint32m4_t __riscv_vwmaccus_vx_i32m4(vint32m4_t vd, short rs1, vuint16m2_t vs2, size_t vl);
vint32m8_t __riscv_vwmaccsu_vv_i32m8(vint32m8_t vd, vuint16m4_t vs1, vint16m4_t vs2, size_t vl);
vint32m8_t __riscv_vwmaccsu_vx_i32m8(vint32m8_t vd, unsigned short rs1, vint16m4_t vs2, size_t vl);
vint32m8_t __riscv_vwmaccus_vx_i32m8(vint32m8_t vd, short rs1, vuint16m4_t vs2, size_t vl);
vint64m1_t __riscv_vwmaccsu_vv_i64m1(vint64m1_t vd, vuint32mf2_t vs1, vint32mf2_t vs2, size_t vl);
vint64m1_t __riscv_vwmaccsu_vx_i64m1(vint64m1_t vd, unsigned int rs1, vint32mf2_t vs2, size_t vl);
vint64m1_t __riscv_vwmaccus_vx_i64m1(vint64m1_t vd, int rs1, vuint32mf2_t vs2, size_t vl);
vint64m2_t __riscv_vwmaccsu_vv_i64m2(vint64m2_t vd, vuint32m1_t vs1, vint32m1_t vs2, size_t vl);
vint64m2_t __riscv_vwmaccsu_vx_i64m2(vint64m2_t vd, unsigned int rs1, vint32m1_t vs2, size_t vl);
vint64m2_t __riscv_vwmaccus_vx_i64m2(vint64m2_t vd, int rs1, vuint32m1_t vs2, size_t vl);
vint64m4_t __riscv_vwmaccsu_vv_i64m4(vint64m4_t vd, vuint32m2_t vs1, vint32m2_t vs2, size_t vl);
vint64m4_t __riscv_vwmaccsu_vx_i64m4(vint64m4_t vd, unsigned int rs1, vint32m2_t vs2, size_t vl);
vint64m4_t __riscv_vwmaccus_vx_i64m4(vint64m4_t vd, int rs1, vuint32m2_t vs2, size_t vl);
vint64m8_t __riscv_vwmaccsu_vv_i64m8(vint64m8_t vd, vuint32m4_t vs1, vint32m4_t vs2, size_t vl);
vint64m8_t __riscv_vwmaccsu_vx_i64m8(vint64m8_t vd, unsigned int rs1, vint32m4_t vs2, size_t vl);
vint64m8_t __riscv_vwmaccus_vx_i64m8(vint64m8_t vd, int rs1, vuint32m4_t vs2, size_t vl);

/* The widening reductions, which fold onto one register of twice the element */
vuint16m1_t __riscv_vwredsumu_vs_u8mf8_u16m1(vuint8mf8_t vs2, vuint16m1_t vs1, size_t vl);
vint16m1_t __riscv_vwredsum_vs_i8mf8_i16m1(vint8mf8_t vs2, vint16m1_t vs1, size_t vl);
vuint16m1_t __riscv_vwredsumu_vs_u8mf4_u16m1(vuint8mf4_t vs2, vuint16m1_t vs1, size_t vl);
vint16m1_t __riscv_vwredsum_vs_i8mf4_i16m1(vint8mf4_t vs2, vint16m1_t vs1, size_t vl);
vuint16m1_t __riscv_vwredsumu_vs_u8mf2_u16m1(vuint8mf2_t vs2, vuint16m1_t vs1, size_t vl);
vint16m1_t __riscv_vwredsum_vs_i8mf2_i16m1(vint8mf2_t vs2, vint16m1_t vs1, size_t vl);
vuint16m1_t __riscv_vwredsumu_vs_u8m1_u16m1(vuint8m1_t vs2, vuint16m1_t vs1, size_t vl);
vint16m1_t __riscv_vwredsum_vs_i8m1_i16m1(vint8m1_t vs2, vint16m1_t vs1, size_t vl);
vuint16m1_t __riscv_vwredsumu_vs_u8m2_u16m1(vuint8m2_t vs2, vuint16m1_t vs1, size_t vl);
vint16m1_t __riscv_vwredsum_vs_i8m2_i16m1(vint8m2_t vs2, vint16m1_t vs1, size_t vl);
vuint16m1_t __riscv_vwredsumu_vs_u8m4_u16m1(vuint8m4_t vs2, vuint16m1_t vs1, size_t vl);
vint16m1_t __riscv_vwredsum_vs_i8m4_i16m1(vint8m4_t vs2, vint16m1_t vs1, size_t vl);
vuint16m1_t __riscv_vwredsumu_vs_u8m8_u16m1(vuint8m8_t vs2, vuint16m1_t vs1, size_t vl);
vint16m1_t __riscv_vwredsum_vs_i8m8_i16m1(vint8m8_t vs2, vint16m1_t vs1, size_t vl);
vuint32m1_t __riscv_vwredsumu_vs_u16mf4_u32m1(vuint16mf4_t vs2, vuint32m1_t vs1, size_t vl);
vint32m1_t __riscv_vwredsum_vs_i16mf4_i32m1(vint16mf4_t vs2, vint32m1_t vs1, size_t vl);
vuint32m1_t __riscv_vwredsumu_vs_u16mf2_u32m1(vuint16mf2_t vs2, vuint32m1_t vs1, size_t vl);
vint32m1_t __riscv_vwredsum_vs_i16mf2_i32m1(vint16mf2_t vs2, vint32m1_t vs1, size_t vl);
vuint32m1_t __riscv_vwredsumu_vs_u16m1_u32m1(vuint16m1_t vs2, vuint32m1_t vs1, size_t vl);
vint32m1_t __riscv_vwredsum_vs_i16m1_i32m1(vint16m1_t vs2, vint32m1_t vs1, size_t vl);
vuint32m1_t __riscv_vwredsumu_vs_u16m2_u32m1(vuint16m2_t vs2, vuint32m1_t vs1, size_t vl);
vint32m1_t __riscv_vwredsum_vs_i16m2_i32m1(vint16m2_t vs2, vint32m1_t vs1, size_t vl);
vuint32m1_t __riscv_vwredsumu_vs_u16m4_u32m1(vuint16m4_t vs2, vuint32m1_t vs1, size_t vl);
vint32m1_t __riscv_vwredsum_vs_i16m4_i32m1(vint16m4_t vs2, vint32m1_t vs1, size_t vl);
vuint32m1_t __riscv_vwredsumu_vs_u16m8_u32m1(vuint16m8_t vs2, vuint32m1_t vs1, size_t vl);
vint32m1_t __riscv_vwredsum_vs_i16m8_i32m1(vint16m8_t vs2, vint32m1_t vs1, size_t vl);
vuint64m1_t __riscv_vwredsumu_vs_u32mf2_u64m1(vuint32mf2_t vs2, vuint64m1_t vs1, size_t vl);
vint64m1_t __riscv_vwredsum_vs_i32mf2_i64m1(vint32mf2_t vs2, vint64m1_t vs1, size_t vl);
vuint64m1_t __riscv_vwredsumu_vs_u32m1_u64m1(vuint32m1_t vs2, vuint64m1_t vs1, size_t vl);
vint64m1_t __riscv_vwredsum_vs_i32m1_i64m1(vint32m1_t vs2, vint64m1_t vs1, size_t vl);
vuint64m1_t __riscv_vwredsumu_vs_u32m2_u64m1(vuint32m2_t vs2, vuint64m1_t vs1, size_t vl);
vint64m1_t __riscv_vwredsum_vs_i32m2_i64m1(vint32m2_t vs2, vint64m1_t vs1, size_t vl);
vuint64m1_t __riscv_vwredsumu_vs_u32m4_u64m1(vuint32m4_t vs2, vuint64m1_t vs1, size_t vl);
vint64m1_t __riscv_vwredsum_vs_i32m4_i64m1(vint32m4_t vs2, vint64m1_t vs1, size_t vl);
vuint64m1_t __riscv_vwredsumu_vs_u32m8_u64m1(vuint32m8_t vs2, vuint64m1_t vs1, size_t vl);
vint64m1_t __riscv_vwredsum_vs_i32m8_i64m1(vint32m8_t vs2, vint64m1_t vs1, size_t vl);

/* The widening shift left the vector bit manipulation adds */
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwsll, vv, vuint8mf8_t, vuint8mf8_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vwsll, vx, vuint8mf8_t, size_t)
_RVV_OPI2(vuint16mf4_t, u16mf4, vbool64_t, vwsll, vi, vuint8mf8_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwsll, vv, vuint8mf4_t, vuint8mf4_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vwsll, vx, vuint8mf4_t, size_t)
_RVV_OPI2(vuint16mf2_t, u16mf2, vbool32_t, vwsll, vi, vuint8mf4_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwsll, vv, vuint8mf2_t, vuint8mf2_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vwsll, vx, vuint8mf2_t, size_t)
_RVV_OPI2(vuint16m1_t, u16m1, vbool16_t, vwsll, vi, vuint8mf2_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwsll, vv, vuint8m1_t, vuint8m1_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vwsll, vx, vuint8m1_t, size_t)
_RVV_OPI2(vuint16m2_t, u16m2, vbool8_t, vwsll, vi, vuint8m1_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwsll, vv, vuint8m2_t, vuint8m2_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vwsll, vx, vuint8m2_t, size_t)
_RVV_OPI2(vuint16m4_t, u16m4, vbool4_t, vwsll, vi, vuint8m2_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwsll, vv, vuint8m4_t, vuint8m4_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vwsll, vx, vuint8m4_t, size_t)
_RVV_OPI2(vuint16m8_t, u16m8, vbool2_t, vwsll, vi, vuint8m4_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwsll, vv, vuint16mf4_t, vuint16mf4_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vwsll, vx, vuint16mf4_t, size_t)
_RVV_OPI2(vuint32mf2_t, u32mf2, vbool64_t, vwsll, vi, vuint16mf4_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwsll, vv, vuint16mf2_t, vuint16mf2_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vwsll, vx, vuint16mf2_t, size_t)
_RVV_OPI2(vuint32m1_t, u32m1, vbool32_t, vwsll, vi, vuint16mf2_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwsll, vv, vuint16m1_t, vuint16m1_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vwsll, vx, vuint16m1_t, size_t)
_RVV_OPI2(vuint32m2_t, u32m2, vbool16_t, vwsll, vi, vuint16m1_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwsll, vv, vuint16m2_t, vuint16m2_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vwsll, vx, vuint16m2_t, size_t)
_RVV_OPI2(vuint32m4_t, u32m4, vbool8_t, vwsll, vi, vuint16m2_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwsll, vv, vuint16m4_t, vuint16m4_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vwsll, vx, vuint16m4_t, size_t)
_RVV_OPI2(vuint32m8_t, u32m8, vbool4_t, vwsll, vi, vuint16m4_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwsll, vv, vuint32mf2_t, vuint32mf2_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vwsll, vx, vuint32mf2_t, size_t)
_RVV_OPI2(vuint64m1_t, u64m1, vbool64_t, vwsll, vi, vuint32mf2_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwsll, vv, vuint32m1_t, vuint32m1_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vwsll, vx, vuint32m1_t, size_t)
_RVV_OPI2(vuint64m2_t, u64m2, vbool32_t, vwsll, vi, vuint32m1_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwsll, vv, vuint32m2_t, vuint32m2_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vwsll, vx, vuint32m2_t, size_t)
_RVV_OPI2(vuint64m4_t, u64m4, vbool16_t, vwsll, vi, vuint32m2_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwsll, vv, vuint32m4_t, vuint32m4_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vwsll, vx, vuint32m4_t, size_t)
_RVV_OPI2(vuint64m8_t, u64m8, vbool8_t, vwsll, vi, vuint32m4_t)

/* A gather whose indices are always sixteen bits wide */
_RVV_OP2(vuint8mf8_t, u8mf8, vbool64_t, vrgatherei16, vv, vuint8mf8_t, vuint16mf4_t)
_RVV_OP2(vint8mf8_t, i8mf8, vbool64_t, vrgatherei16, vv, vint8mf8_t, vuint16mf4_t)
_RVV_OP2(vuint8mf4_t, u8mf4, vbool32_t, vrgatherei16, vv, vuint8mf4_t, vuint16mf2_t)
_RVV_OP2(vint8mf4_t, i8mf4, vbool32_t, vrgatherei16, vv, vint8mf4_t, vuint16mf2_t)
_RVV_OP2(vuint8mf2_t, u8mf2, vbool16_t, vrgatherei16, vv, vuint8mf2_t, vuint16m1_t)
_RVV_OP2(vint8mf2_t, i8mf2, vbool16_t, vrgatherei16, vv, vint8mf2_t, vuint16m1_t)
_RVV_OP2(vuint8m1_t, u8m1, vbool8_t, vrgatherei16, vv, vuint8m1_t, vuint16m2_t)
_RVV_OP2(vint8m1_t, i8m1, vbool8_t, vrgatherei16, vv, vint8m1_t, vuint16m2_t)
_RVV_OP2(vuint8m2_t, u8m2, vbool4_t, vrgatherei16, vv, vuint8m2_t, vuint16m4_t)
_RVV_OP2(vint8m2_t, i8m2, vbool4_t, vrgatherei16, vv, vint8m2_t, vuint16m4_t)
_RVV_OP2(vuint8m4_t, u8m4, vbool2_t, vrgatherei16, vv, vuint8m4_t, vuint16m8_t)
_RVV_OP2(vint8m4_t, i8m4, vbool2_t, vrgatherei16, vv, vint8m4_t, vuint16m8_t)
_RVV_OP2(vuint16mf4_t, u16mf4, vbool64_t, vrgatherei16, vv, vuint16mf4_t, vuint16mf4_t)
_RVV_OP2(vint16mf4_t, i16mf4, vbool64_t, vrgatherei16, vv, vint16mf4_t, vuint16mf4_t)
_RVV_OP2(vuint16mf2_t, u16mf2, vbool32_t, vrgatherei16, vv, vuint16mf2_t, vuint16mf2_t)
_RVV_OP2(vint16mf2_t, i16mf2, vbool32_t, vrgatherei16, vv, vint16mf2_t, vuint16mf2_t)
_RVV_OP2(vuint16m1_t, u16m1, vbool16_t, vrgatherei16, vv, vuint16m1_t, vuint16m1_t)
_RVV_OP2(vint16m1_t, i16m1, vbool16_t, vrgatherei16, vv, vint16m1_t, vuint16m1_t)
_RVV_OP2(vuint16m2_t, u16m2, vbool8_t, vrgatherei16, vv, vuint16m2_t, vuint16m2_t)
_RVV_OP2(vint16m2_t, i16m2, vbool8_t, vrgatherei16, vv, vint16m2_t, vuint16m2_t)
_RVV_OP2(vuint16m4_t, u16m4, vbool4_t, vrgatherei16, vv, vuint16m4_t, vuint16m4_t)
_RVV_OP2(vint16m4_t, i16m4, vbool4_t, vrgatherei16, vv, vint16m4_t, vuint16m4_t)
_RVV_OP2(vuint16m8_t, u16m8, vbool2_t, vrgatherei16, vv, vuint16m8_t, vuint16m8_t)
_RVV_OP2(vint16m8_t, i16m8, vbool2_t, vrgatherei16, vv, vint16m8_t, vuint16m8_t)
_RVV_OP2(vuint32mf2_t, u32mf2, vbool64_t, vrgatherei16, vv, vuint32mf2_t, vuint16mf4_t)
_RVV_OP2(vint32mf2_t, i32mf2, vbool64_t, vrgatherei16, vv, vint32mf2_t, vuint16mf4_t)
_RVV_OP2(vfloat32mf2_t, f32mf2, vbool64_t, vrgatherei16, vv, vfloat32mf2_t, vuint16mf4_t)
_RVV_OP2(vuint32m1_t, u32m1, vbool32_t, vrgatherei16, vv, vuint32m1_t, vuint16mf2_t)
_RVV_OP2(vint32m1_t, i32m1, vbool32_t, vrgatherei16, vv, vint32m1_t, vuint16mf2_t)
_RVV_OP2(vfloat32m1_t, f32m1, vbool32_t, vrgatherei16, vv, vfloat32m1_t, vuint16mf2_t)
_RVV_OP2(vuint32m2_t, u32m2, vbool16_t, vrgatherei16, vv, vuint32m2_t, vuint16m1_t)
_RVV_OP2(vint32m2_t, i32m2, vbool16_t, vrgatherei16, vv, vint32m2_t, vuint16m1_t)
_RVV_OP2(vfloat32m2_t, f32m2, vbool16_t, vrgatherei16, vv, vfloat32m2_t, vuint16m1_t)
_RVV_OP2(vuint32m4_t, u32m4, vbool8_t, vrgatherei16, vv, vuint32m4_t, vuint16m2_t)
_RVV_OP2(vint32m4_t, i32m4, vbool8_t, vrgatherei16, vv, vint32m4_t, vuint16m2_t)
_RVV_OP2(vfloat32m4_t, f32m4, vbool8_t, vrgatherei16, vv, vfloat32m4_t, vuint16m2_t)
_RVV_OP2(vuint32m8_t, u32m8, vbool4_t, vrgatherei16, vv, vuint32m8_t, vuint16m4_t)
_RVV_OP2(vint32m8_t, i32m8, vbool4_t, vrgatherei16, vv, vint32m8_t, vuint16m4_t)
_RVV_OP2(vfloat32m8_t, f32m8, vbool4_t, vrgatherei16, vv, vfloat32m8_t, vuint16m4_t)
_RVV_OP2(vuint64m1_t, u64m1, vbool64_t, vrgatherei16, vv, vuint64m1_t, vuint16mf4_t)
_RVV_OP2(vint64m1_t, i64m1, vbool64_t, vrgatherei16, vv, vint64m1_t, vuint16mf4_t)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vrgatherei16, vv, vfloat64m1_t, vuint16mf4_t)
_RVV_OP2(vuint64m2_t, u64m2, vbool32_t, vrgatherei16, vv, vuint64m2_t, vuint16mf2_t)
_RVV_OP2(vint64m2_t, i64m2, vbool32_t, vrgatherei16, vv, vint64m2_t, vuint16mf2_t)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vrgatherei16, vv, vfloat64m2_t, vuint16mf2_t)
_RVV_OP2(vuint64m4_t, u64m4, vbool16_t, vrgatherei16, vv, vuint64m4_t, vuint16m1_t)
_RVV_OP2(vint64m4_t, i64m4, vbool16_t, vrgatherei16, vv, vint64m4_t, vuint16m1_t)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vrgatherei16, vv, vfloat64m4_t, vuint16m1_t)
_RVV_OP2(vuint64m8_t, u64m8, vbool8_t, vrgatherei16, vv, vuint64m8_t, vuint16m2_t)
_RVV_OP2(vint64m8_t, i64m8, vbool8_t, vrgatherei16, vv, vint64m8_t, vuint16m2_t)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vrgatherei16, vv, vfloat64m8_t, vuint16m2_t)

/* The carry and borrow masks that read a carry in as well */
vbool64_t __riscv_vmadc_vvm_u8mf8_b64(vuint8mf8_t vs2, vuint8mf8_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vxm_u8mf8_b64(vuint8mf8_t vs2, unsigned char rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vim_u8mf8_b64(vuint8mf8_t vs2, int simm5, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vvm_u8mf8_b64(vuint8mf8_t vs2, vuint8mf8_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vxm_u8mf8_b64(vuint8mf8_t vs2, unsigned char rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vvm_i8mf8_b64(vint8mf8_t vs2, vint8mf8_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vxm_i8mf8_b64(vint8mf8_t vs2, signed char rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vim_i8mf8_b64(vint8mf8_t vs2, int simm5, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vvm_i8mf8_b64(vint8mf8_t vs2, vint8mf8_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vxm_i8mf8_b64(vint8mf8_t vs2, signed char rs1, vbool64_t v0, size_t vl);
vbool32_t __riscv_vmadc_vvm_u8mf4_b32(vuint8mf4_t vs2, vuint8mf4_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vxm_u8mf4_b32(vuint8mf4_t vs2, unsigned char rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vim_u8mf4_b32(vuint8mf4_t vs2, int simm5, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vvm_u8mf4_b32(vuint8mf4_t vs2, vuint8mf4_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vxm_u8mf4_b32(vuint8mf4_t vs2, unsigned char rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vvm_i8mf4_b32(vint8mf4_t vs2, vint8mf4_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vxm_i8mf4_b32(vint8mf4_t vs2, signed char rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vim_i8mf4_b32(vint8mf4_t vs2, int simm5, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vvm_i8mf4_b32(vint8mf4_t vs2, vint8mf4_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vxm_i8mf4_b32(vint8mf4_t vs2, signed char rs1, vbool32_t v0, size_t vl);
vbool16_t __riscv_vmadc_vvm_u8mf2_b16(vuint8mf2_t vs2, vuint8mf2_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vxm_u8mf2_b16(vuint8mf2_t vs2, unsigned char rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vim_u8mf2_b16(vuint8mf2_t vs2, int simm5, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vvm_u8mf2_b16(vuint8mf2_t vs2, vuint8mf2_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vxm_u8mf2_b16(vuint8mf2_t vs2, unsigned char rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vvm_i8mf2_b16(vint8mf2_t vs2, vint8mf2_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vxm_i8mf2_b16(vint8mf2_t vs2, signed char rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vim_i8mf2_b16(vint8mf2_t vs2, int simm5, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vvm_i8mf2_b16(vint8mf2_t vs2, vint8mf2_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vxm_i8mf2_b16(vint8mf2_t vs2, signed char rs1, vbool16_t v0, size_t vl);
vbool8_t __riscv_vmadc_vvm_u8m1_b8(vuint8m1_t vs2, vuint8m1_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vxm_u8m1_b8(vuint8m1_t vs2, unsigned char rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vim_u8m1_b8(vuint8m1_t vs2, int simm5, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vvm_u8m1_b8(vuint8m1_t vs2, vuint8m1_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vxm_u8m1_b8(vuint8m1_t vs2, unsigned char rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vvm_i8m1_b8(vint8m1_t vs2, vint8m1_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vxm_i8m1_b8(vint8m1_t vs2, signed char rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vim_i8m1_b8(vint8m1_t vs2, int simm5, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vvm_i8m1_b8(vint8m1_t vs2, vint8m1_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vxm_i8m1_b8(vint8m1_t vs2, signed char rs1, vbool8_t v0, size_t vl);
vbool4_t __riscv_vmadc_vvm_u8m2_b4(vuint8m2_t vs2, vuint8m2_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vxm_u8m2_b4(vuint8m2_t vs2, unsigned char rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vim_u8m2_b4(vuint8m2_t vs2, int simm5, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vvm_u8m2_b4(vuint8m2_t vs2, vuint8m2_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vxm_u8m2_b4(vuint8m2_t vs2, unsigned char rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vvm_i8m2_b4(vint8m2_t vs2, vint8m2_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vxm_i8m2_b4(vint8m2_t vs2, signed char rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vim_i8m2_b4(vint8m2_t vs2, int simm5, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vvm_i8m2_b4(vint8m2_t vs2, vint8m2_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vxm_i8m2_b4(vint8m2_t vs2, signed char rs1, vbool4_t v0, size_t vl);
vbool2_t __riscv_vmadc_vvm_u8m4_b2(vuint8m4_t vs2, vuint8m4_t vs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vxm_u8m4_b2(vuint8m4_t vs2, unsigned char rs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vim_u8m4_b2(vuint8m4_t vs2, int simm5, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmsbc_vvm_u8m4_b2(vuint8m4_t vs2, vuint8m4_t vs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmsbc_vxm_u8m4_b2(vuint8m4_t vs2, unsigned char rs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vvm_i8m4_b2(vint8m4_t vs2, vint8m4_t vs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vxm_i8m4_b2(vint8m4_t vs2, signed char rs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vim_i8m4_b2(vint8m4_t vs2, int simm5, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmsbc_vvm_i8m4_b2(vint8m4_t vs2, vint8m4_t vs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmsbc_vxm_i8m4_b2(vint8m4_t vs2, signed char rs1, vbool2_t v0, size_t vl);
vbool1_t __riscv_vmadc_vvm_u8m8_b1(vuint8m8_t vs2, vuint8m8_t vs1, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmadc_vxm_u8m8_b1(vuint8m8_t vs2, unsigned char rs1, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmadc_vim_u8m8_b1(vuint8m8_t vs2, int simm5, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmsbc_vvm_u8m8_b1(vuint8m8_t vs2, vuint8m8_t vs1, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmsbc_vxm_u8m8_b1(vuint8m8_t vs2, unsigned char rs1, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmadc_vvm_i8m8_b1(vint8m8_t vs2, vint8m8_t vs1, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmadc_vxm_i8m8_b1(vint8m8_t vs2, signed char rs1, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmadc_vim_i8m8_b1(vint8m8_t vs2, int simm5, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmsbc_vvm_i8m8_b1(vint8m8_t vs2, vint8m8_t vs1, vbool1_t v0, size_t vl);
vbool1_t __riscv_vmsbc_vxm_i8m8_b1(vint8m8_t vs2, signed char rs1, vbool1_t v0, size_t vl);
vbool64_t __riscv_vmadc_vvm_u16mf4_b64(vuint16mf4_t vs2, vuint16mf4_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vxm_u16mf4_b64(vuint16mf4_t vs2, unsigned short rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vim_u16mf4_b64(vuint16mf4_t vs2, int simm5, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vvm_u16mf4_b64(vuint16mf4_t vs2, vuint16mf4_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vxm_u16mf4_b64(vuint16mf4_t vs2, unsigned short rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vvm_i16mf4_b64(vint16mf4_t vs2, vint16mf4_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vxm_i16mf4_b64(vint16mf4_t vs2, short rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vim_i16mf4_b64(vint16mf4_t vs2, int simm5, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vvm_i16mf4_b64(vint16mf4_t vs2, vint16mf4_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vxm_i16mf4_b64(vint16mf4_t vs2, short rs1, vbool64_t v0, size_t vl);
vbool32_t __riscv_vmadc_vvm_u16mf2_b32(vuint16mf2_t vs2, vuint16mf2_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vxm_u16mf2_b32(vuint16mf2_t vs2, unsigned short rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vim_u16mf2_b32(vuint16mf2_t vs2, int simm5, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vvm_u16mf2_b32(vuint16mf2_t vs2, vuint16mf2_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vxm_u16mf2_b32(vuint16mf2_t vs2, unsigned short rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vvm_i16mf2_b32(vint16mf2_t vs2, vint16mf2_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vxm_i16mf2_b32(vint16mf2_t vs2, short rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vim_i16mf2_b32(vint16mf2_t vs2, int simm5, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vvm_i16mf2_b32(vint16mf2_t vs2, vint16mf2_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vxm_i16mf2_b32(vint16mf2_t vs2, short rs1, vbool32_t v0, size_t vl);
vbool16_t __riscv_vmadc_vvm_u16m1_b16(vuint16m1_t vs2, vuint16m1_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vxm_u16m1_b16(vuint16m1_t vs2, unsigned short rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vim_u16m1_b16(vuint16m1_t vs2, int simm5, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vvm_u16m1_b16(vuint16m1_t vs2, vuint16m1_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vxm_u16m1_b16(vuint16m1_t vs2, unsigned short rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vvm_i16m1_b16(vint16m1_t vs2, vint16m1_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vxm_i16m1_b16(vint16m1_t vs2, short rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vim_i16m1_b16(vint16m1_t vs2, int simm5, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vvm_i16m1_b16(vint16m1_t vs2, vint16m1_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vxm_i16m1_b16(vint16m1_t vs2, short rs1, vbool16_t v0, size_t vl);
vbool8_t __riscv_vmadc_vvm_u16m2_b8(vuint16m2_t vs2, vuint16m2_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vxm_u16m2_b8(vuint16m2_t vs2, unsigned short rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vim_u16m2_b8(vuint16m2_t vs2, int simm5, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vvm_u16m2_b8(vuint16m2_t vs2, vuint16m2_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vxm_u16m2_b8(vuint16m2_t vs2, unsigned short rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vvm_i16m2_b8(vint16m2_t vs2, vint16m2_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vxm_i16m2_b8(vint16m2_t vs2, short rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vim_i16m2_b8(vint16m2_t vs2, int simm5, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vvm_i16m2_b8(vint16m2_t vs2, vint16m2_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vxm_i16m2_b8(vint16m2_t vs2, short rs1, vbool8_t v0, size_t vl);
vbool4_t __riscv_vmadc_vvm_u16m4_b4(vuint16m4_t vs2, vuint16m4_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vxm_u16m4_b4(vuint16m4_t vs2, unsigned short rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vim_u16m4_b4(vuint16m4_t vs2, int simm5, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vvm_u16m4_b4(vuint16m4_t vs2, vuint16m4_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vxm_u16m4_b4(vuint16m4_t vs2, unsigned short rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vvm_i16m4_b4(vint16m4_t vs2, vint16m4_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vxm_i16m4_b4(vint16m4_t vs2, short rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vim_i16m4_b4(vint16m4_t vs2, int simm5, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vvm_i16m4_b4(vint16m4_t vs2, vint16m4_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vxm_i16m4_b4(vint16m4_t vs2, short rs1, vbool4_t v0, size_t vl);
vbool2_t __riscv_vmadc_vvm_u16m8_b2(vuint16m8_t vs2, vuint16m8_t vs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vxm_u16m8_b2(vuint16m8_t vs2, unsigned short rs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vim_u16m8_b2(vuint16m8_t vs2, int simm5, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmsbc_vvm_u16m8_b2(vuint16m8_t vs2, vuint16m8_t vs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmsbc_vxm_u16m8_b2(vuint16m8_t vs2, unsigned short rs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vvm_i16m8_b2(vint16m8_t vs2, vint16m8_t vs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vxm_i16m8_b2(vint16m8_t vs2, short rs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmadc_vim_i16m8_b2(vint16m8_t vs2, int simm5, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmsbc_vvm_i16m8_b2(vint16m8_t vs2, vint16m8_t vs1, vbool2_t v0, size_t vl);
vbool2_t __riscv_vmsbc_vxm_i16m8_b2(vint16m8_t vs2, short rs1, vbool2_t v0, size_t vl);
vbool64_t __riscv_vmadc_vvm_u32mf2_b64(vuint32mf2_t vs2, vuint32mf2_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vxm_u32mf2_b64(vuint32mf2_t vs2, unsigned int rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vim_u32mf2_b64(vuint32mf2_t vs2, int simm5, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vvm_u32mf2_b64(vuint32mf2_t vs2, vuint32mf2_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vxm_u32mf2_b64(vuint32mf2_t vs2, unsigned int rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vvm_i32mf2_b64(vint32mf2_t vs2, vint32mf2_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vxm_i32mf2_b64(vint32mf2_t vs2, int rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vim_i32mf2_b64(vint32mf2_t vs2, int simm5, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vvm_i32mf2_b64(vint32mf2_t vs2, vint32mf2_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vxm_i32mf2_b64(vint32mf2_t vs2, int rs1, vbool64_t v0, size_t vl);
vbool32_t __riscv_vmadc_vvm_u32m1_b32(vuint32m1_t vs2, vuint32m1_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vxm_u32m1_b32(vuint32m1_t vs2, unsigned int rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vim_u32m1_b32(vuint32m1_t vs2, int simm5, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vvm_u32m1_b32(vuint32m1_t vs2, vuint32m1_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vxm_u32m1_b32(vuint32m1_t vs2, unsigned int rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vvm_i32m1_b32(vint32m1_t vs2, vint32m1_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vxm_i32m1_b32(vint32m1_t vs2, int rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vim_i32m1_b32(vint32m1_t vs2, int simm5, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vvm_i32m1_b32(vint32m1_t vs2, vint32m1_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vxm_i32m1_b32(vint32m1_t vs2, int rs1, vbool32_t v0, size_t vl);
vbool16_t __riscv_vmadc_vvm_u32m2_b16(vuint32m2_t vs2, vuint32m2_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vxm_u32m2_b16(vuint32m2_t vs2, unsigned int rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vim_u32m2_b16(vuint32m2_t vs2, int simm5, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vvm_u32m2_b16(vuint32m2_t vs2, vuint32m2_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vxm_u32m2_b16(vuint32m2_t vs2, unsigned int rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vvm_i32m2_b16(vint32m2_t vs2, vint32m2_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vxm_i32m2_b16(vint32m2_t vs2, int rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vim_i32m2_b16(vint32m2_t vs2, int simm5, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vvm_i32m2_b16(vint32m2_t vs2, vint32m2_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vxm_i32m2_b16(vint32m2_t vs2, int rs1, vbool16_t v0, size_t vl);
vbool8_t __riscv_vmadc_vvm_u32m4_b8(vuint32m4_t vs2, vuint32m4_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vxm_u32m4_b8(vuint32m4_t vs2, unsigned int rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vim_u32m4_b8(vuint32m4_t vs2, int simm5, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vvm_u32m4_b8(vuint32m4_t vs2, vuint32m4_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vxm_u32m4_b8(vuint32m4_t vs2, unsigned int rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vvm_i32m4_b8(vint32m4_t vs2, vint32m4_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vxm_i32m4_b8(vint32m4_t vs2, int rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vim_i32m4_b8(vint32m4_t vs2, int simm5, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vvm_i32m4_b8(vint32m4_t vs2, vint32m4_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vxm_i32m4_b8(vint32m4_t vs2, int rs1, vbool8_t v0, size_t vl);
vbool4_t __riscv_vmadc_vvm_u32m8_b4(vuint32m8_t vs2, vuint32m8_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vxm_u32m8_b4(vuint32m8_t vs2, unsigned int rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vim_u32m8_b4(vuint32m8_t vs2, int simm5, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vvm_u32m8_b4(vuint32m8_t vs2, vuint32m8_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vxm_u32m8_b4(vuint32m8_t vs2, unsigned int rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vvm_i32m8_b4(vint32m8_t vs2, vint32m8_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vxm_i32m8_b4(vint32m8_t vs2, int rs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmadc_vim_i32m8_b4(vint32m8_t vs2, int simm5, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vvm_i32m8_b4(vint32m8_t vs2, vint32m8_t vs1, vbool4_t v0, size_t vl);
vbool4_t __riscv_vmsbc_vxm_i32m8_b4(vint32m8_t vs2, int rs1, vbool4_t v0, size_t vl);
vbool64_t __riscv_vmadc_vvm_u64m1_b64(vuint64m1_t vs2, vuint64m1_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vxm_u64m1_b64(vuint64m1_t vs2, uint64_t rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vim_u64m1_b64(vuint64m1_t vs2, int simm5, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vvm_u64m1_b64(vuint64m1_t vs2, vuint64m1_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vxm_u64m1_b64(vuint64m1_t vs2, uint64_t rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vvm_i64m1_b64(vint64m1_t vs2, vint64m1_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vxm_i64m1_b64(vint64m1_t vs2, int64_t rs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmadc_vim_i64m1_b64(vint64m1_t vs2, int simm5, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vvm_i64m1_b64(vint64m1_t vs2, vint64m1_t vs1, vbool64_t v0, size_t vl);
vbool64_t __riscv_vmsbc_vxm_i64m1_b64(vint64m1_t vs2, int64_t rs1, vbool64_t v0, size_t vl);
vbool32_t __riscv_vmadc_vvm_u64m2_b32(vuint64m2_t vs2, vuint64m2_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vxm_u64m2_b32(vuint64m2_t vs2, uint64_t rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vim_u64m2_b32(vuint64m2_t vs2, int simm5, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vvm_u64m2_b32(vuint64m2_t vs2, vuint64m2_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vxm_u64m2_b32(vuint64m2_t vs2, uint64_t rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vvm_i64m2_b32(vint64m2_t vs2, vint64m2_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vxm_i64m2_b32(vint64m2_t vs2, int64_t rs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmadc_vim_i64m2_b32(vint64m2_t vs2, int simm5, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vvm_i64m2_b32(vint64m2_t vs2, vint64m2_t vs1, vbool32_t v0, size_t vl);
vbool32_t __riscv_vmsbc_vxm_i64m2_b32(vint64m2_t vs2, int64_t rs1, vbool32_t v0, size_t vl);
vbool16_t __riscv_vmadc_vvm_u64m4_b16(vuint64m4_t vs2, vuint64m4_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vxm_u64m4_b16(vuint64m4_t vs2, uint64_t rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vim_u64m4_b16(vuint64m4_t vs2, int simm5, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vvm_u64m4_b16(vuint64m4_t vs2, vuint64m4_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vxm_u64m4_b16(vuint64m4_t vs2, uint64_t rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vvm_i64m4_b16(vint64m4_t vs2, vint64m4_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vxm_i64m4_b16(vint64m4_t vs2, int64_t rs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmadc_vim_i64m4_b16(vint64m4_t vs2, int simm5, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vvm_i64m4_b16(vint64m4_t vs2, vint64m4_t vs1, vbool16_t v0, size_t vl);
vbool16_t __riscv_vmsbc_vxm_i64m4_b16(vint64m4_t vs2, int64_t rs1, vbool16_t v0, size_t vl);
vbool8_t __riscv_vmadc_vvm_u64m8_b8(vuint64m8_t vs2, vuint64m8_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vxm_u64m8_b8(vuint64m8_t vs2, uint64_t rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vim_u64m8_b8(vuint64m8_t vs2, int simm5, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vvm_u64m8_b8(vuint64m8_t vs2, vuint64m8_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vxm_u64m8_b8(vuint64m8_t vs2, uint64_t rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vvm_i64m8_b8(vint64m8_t vs2, vint64m8_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vxm_i64m8_b8(vint64m8_t vs2, int64_t rs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmadc_vim_i64m8_b8(vint64m8_t vs2, int simm5, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vvm_i64m8_b8(vint64m8_t vs2, vint64m8_t vs1, vbool8_t v0, size_t vl);
vbool8_t __riscv_vmsbc_vxm_i64m8_b8(vint64m8_t vs2, int64_t rs1, vbool8_t v0, size_t vl);

/* The widening float arithmetic */
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwadd, vv, vfloat32mf2_t, vfloat32mf2_t)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwadd, vf, vfloat32mf2_t, float)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwadd, wv, vfloat64m1_t, vfloat32mf2_t)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwadd, wf, vfloat64m1_t, float)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwsub, vv, vfloat32mf2_t, vfloat32mf2_t)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwsub, vf, vfloat32mf2_t, float)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwsub, wv, vfloat64m1_t, vfloat32mf2_t)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwsub, wf, vfloat64m1_t, float)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwmul, vv, vfloat32mf2_t, vfloat32mf2_t)
_RVV_OP2(vfloat64m1_t, f64m1, vbool64_t, vfwmul, vf, vfloat32mf2_t, float)
vfloat64m1_t __riscv_vfwmacc_vv_f64m1(vfloat64m1_t vd, vfloat32mf2_t vs1, vfloat32mf2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwmacc_vf_f64m1(vfloat64m1_t vd, float rs1, vfloat32mf2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwnmacc_vv_f64m1(vfloat64m1_t vd, vfloat32mf2_t vs1, vfloat32mf2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwnmacc_vf_f64m1(vfloat64m1_t vd, float rs1, vfloat32mf2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwmsac_vv_f64m1(vfloat64m1_t vd, vfloat32mf2_t vs1, vfloat32mf2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwmsac_vf_f64m1(vfloat64m1_t vd, float rs1, vfloat32mf2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwnmsac_vv_f64m1(vfloat64m1_t vd, vfloat32mf2_t vs1, vfloat32mf2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwnmsac_vf_f64m1(vfloat64m1_t vd, float rs1, vfloat32mf2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwredusum_vs_f32mf2_f64m1(vfloat32mf2_t vs2, vfloat64m1_t vs1, size_t vl);
vfloat64m1_t __riscv_vfwredosum_vs_f32mf2_f64m1(vfloat32mf2_t vs2, vfloat64m1_t vs1, size_t vl);
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwadd, vv, vfloat32m1_t, vfloat32m1_t)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwadd, vf, vfloat32m1_t, float)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwadd, wv, vfloat64m2_t, vfloat32m1_t)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwadd, wf, vfloat64m2_t, float)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwsub, vv, vfloat32m1_t, vfloat32m1_t)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwsub, vf, vfloat32m1_t, float)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwsub, wv, vfloat64m2_t, vfloat32m1_t)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwsub, wf, vfloat64m2_t, float)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwmul, vv, vfloat32m1_t, vfloat32m1_t)
_RVV_OP2(vfloat64m2_t, f64m2, vbool32_t, vfwmul, vf, vfloat32m1_t, float)
vfloat64m2_t __riscv_vfwmacc_vv_f64m2(vfloat64m2_t vd, vfloat32m1_t vs1, vfloat32m1_t vs2, size_t vl);
vfloat64m2_t __riscv_vfwmacc_vf_f64m2(vfloat64m2_t vd, float rs1, vfloat32m1_t vs2, size_t vl);
vfloat64m2_t __riscv_vfwnmacc_vv_f64m2(vfloat64m2_t vd, vfloat32m1_t vs1, vfloat32m1_t vs2, size_t vl);
vfloat64m2_t __riscv_vfwnmacc_vf_f64m2(vfloat64m2_t vd, float rs1, vfloat32m1_t vs2, size_t vl);
vfloat64m2_t __riscv_vfwmsac_vv_f64m2(vfloat64m2_t vd, vfloat32m1_t vs1, vfloat32m1_t vs2, size_t vl);
vfloat64m2_t __riscv_vfwmsac_vf_f64m2(vfloat64m2_t vd, float rs1, vfloat32m1_t vs2, size_t vl);
vfloat64m2_t __riscv_vfwnmsac_vv_f64m2(vfloat64m2_t vd, vfloat32m1_t vs1, vfloat32m1_t vs2, size_t vl);
vfloat64m2_t __riscv_vfwnmsac_vf_f64m2(vfloat64m2_t vd, float rs1, vfloat32m1_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwredusum_vs_f32m1_f64m1(vfloat32m1_t vs2, vfloat64m1_t vs1, size_t vl);
vfloat64m1_t __riscv_vfwredosum_vs_f32m1_f64m1(vfloat32m1_t vs2, vfloat64m1_t vs1, size_t vl);
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwadd, vv, vfloat32m2_t, vfloat32m2_t)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwadd, vf, vfloat32m2_t, float)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwadd, wv, vfloat64m4_t, vfloat32m2_t)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwadd, wf, vfloat64m4_t, float)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwsub, vv, vfloat32m2_t, vfloat32m2_t)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwsub, vf, vfloat32m2_t, float)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwsub, wv, vfloat64m4_t, vfloat32m2_t)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwsub, wf, vfloat64m4_t, float)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwmul, vv, vfloat32m2_t, vfloat32m2_t)
_RVV_OP2(vfloat64m4_t, f64m4, vbool16_t, vfwmul, vf, vfloat32m2_t, float)
vfloat64m4_t __riscv_vfwmacc_vv_f64m4(vfloat64m4_t vd, vfloat32m2_t vs1, vfloat32m2_t vs2, size_t vl);
vfloat64m4_t __riscv_vfwmacc_vf_f64m4(vfloat64m4_t vd, float rs1, vfloat32m2_t vs2, size_t vl);
vfloat64m4_t __riscv_vfwnmacc_vv_f64m4(vfloat64m4_t vd, vfloat32m2_t vs1, vfloat32m2_t vs2, size_t vl);
vfloat64m4_t __riscv_vfwnmacc_vf_f64m4(vfloat64m4_t vd, float rs1, vfloat32m2_t vs2, size_t vl);
vfloat64m4_t __riscv_vfwmsac_vv_f64m4(vfloat64m4_t vd, vfloat32m2_t vs1, vfloat32m2_t vs2, size_t vl);
vfloat64m4_t __riscv_vfwmsac_vf_f64m4(vfloat64m4_t vd, float rs1, vfloat32m2_t vs2, size_t vl);
vfloat64m4_t __riscv_vfwnmsac_vv_f64m4(vfloat64m4_t vd, vfloat32m2_t vs1, vfloat32m2_t vs2, size_t vl);
vfloat64m4_t __riscv_vfwnmsac_vf_f64m4(vfloat64m4_t vd, float rs1, vfloat32m2_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwredusum_vs_f32m2_f64m1(vfloat32m2_t vs2, vfloat64m1_t vs1, size_t vl);
vfloat64m1_t __riscv_vfwredosum_vs_f32m2_f64m1(vfloat32m2_t vs2, vfloat64m1_t vs1, size_t vl);
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwadd, vv, vfloat32m4_t, vfloat32m4_t)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwadd, vf, vfloat32m4_t, float)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwadd, wv, vfloat64m8_t, vfloat32m4_t)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwadd, wf, vfloat64m8_t, float)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwsub, vv, vfloat32m4_t, vfloat32m4_t)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwsub, vf, vfloat32m4_t, float)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwsub, wv, vfloat64m8_t, vfloat32m4_t)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwsub, wf, vfloat64m8_t, float)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwmul, vv, vfloat32m4_t, vfloat32m4_t)
_RVV_OP2(vfloat64m8_t, f64m8, vbool8_t, vfwmul, vf, vfloat32m4_t, float)
vfloat64m8_t __riscv_vfwmacc_vv_f64m8(vfloat64m8_t vd, vfloat32m4_t vs1, vfloat32m4_t vs2, size_t vl);
vfloat64m8_t __riscv_vfwmacc_vf_f64m8(vfloat64m8_t vd, float rs1, vfloat32m4_t vs2, size_t vl);
vfloat64m8_t __riscv_vfwnmacc_vv_f64m8(vfloat64m8_t vd, vfloat32m4_t vs1, vfloat32m4_t vs2, size_t vl);
vfloat64m8_t __riscv_vfwnmacc_vf_f64m8(vfloat64m8_t vd, float rs1, vfloat32m4_t vs2, size_t vl);
vfloat64m8_t __riscv_vfwmsac_vv_f64m8(vfloat64m8_t vd, vfloat32m4_t vs1, vfloat32m4_t vs2, size_t vl);
vfloat64m8_t __riscv_vfwmsac_vf_f64m8(vfloat64m8_t vd, float rs1, vfloat32m4_t vs2, size_t vl);
vfloat64m8_t __riscv_vfwnmsac_vv_f64m8(vfloat64m8_t vd, vfloat32m4_t vs1, vfloat32m4_t vs2, size_t vl);
vfloat64m8_t __riscv_vfwnmsac_vf_f64m8(vfloat64m8_t vd, float rs1, vfloat32m4_t vs2, size_t vl);
vfloat64m1_t __riscv_vfwredusum_vs_f32m4_f64m1(vfloat32m4_t vs2, vfloat64m1_t vs1, size_t vl);
vfloat64m1_t __riscv_vfwredosum_vs_f32m4_f64m1(vfloat32m4_t vs2, vfloat64m1_t vs1, size_t vl);

/* The widening and narrowing float conversions */
_RVV_UNARY(vuint64m1_t, u64m1, vbool64_t, vfwcvt_xu_f, v, vfloat32mf2_t)
_RVV_UNARY(vint64m1_t, i64m1, vbool64_t, vfwcvt_x_f, v, vfloat32mf2_t)
_RVV_UNARY(vuint64m1_t, u64m1, vbool64_t, vfwcvt_rtz_xu_f, v, vfloat32mf2_t)
_RVV_UNARY(vint64m1_t, i64m1, vbool64_t, vfwcvt_rtz_x_f, v, vfloat32mf2_t)
_RVV_UNARY(vfloat64m1_t, f64m1, vbool64_t, vfwcvt_f_xu, v, vuint32mf2_t)
_RVV_UNARY(vfloat64m1_t, f64m1, vbool64_t, vfwcvt_f_x, v, vint32mf2_t)
_RVV_UNARY(vfloat64m1_t, f64m1, vbool64_t, vfwcvt_f_f, v, vfloat32mf2_t)
_RVV_UNARY(vuint64m2_t, u64m2, vbool32_t, vfwcvt_xu_f, v, vfloat32m1_t)
_RVV_UNARY(vint64m2_t, i64m2, vbool32_t, vfwcvt_x_f, v, vfloat32m1_t)
_RVV_UNARY(vuint64m2_t, u64m2, vbool32_t, vfwcvt_rtz_xu_f, v, vfloat32m1_t)
_RVV_UNARY(vint64m2_t, i64m2, vbool32_t, vfwcvt_rtz_x_f, v, vfloat32m1_t)
_RVV_UNARY(vfloat64m2_t, f64m2, vbool32_t, vfwcvt_f_xu, v, vuint32m1_t)
_RVV_UNARY(vfloat64m2_t, f64m2, vbool32_t, vfwcvt_f_x, v, vint32m1_t)
_RVV_UNARY(vfloat64m2_t, f64m2, vbool32_t, vfwcvt_f_f, v, vfloat32m1_t)
_RVV_UNARY(vuint64m4_t, u64m4, vbool16_t, vfwcvt_xu_f, v, vfloat32m2_t)
_RVV_UNARY(vint64m4_t, i64m4, vbool16_t, vfwcvt_x_f, v, vfloat32m2_t)
_RVV_UNARY(vuint64m4_t, u64m4, vbool16_t, vfwcvt_rtz_xu_f, v, vfloat32m2_t)
_RVV_UNARY(vint64m4_t, i64m4, vbool16_t, vfwcvt_rtz_x_f, v, vfloat32m2_t)
_RVV_UNARY(vfloat64m4_t, f64m4, vbool16_t, vfwcvt_f_xu, v, vuint32m2_t)
_RVV_UNARY(vfloat64m4_t, f64m4, vbool16_t, vfwcvt_f_x, v, vint32m2_t)
_RVV_UNARY(vfloat64m4_t, f64m4, vbool16_t, vfwcvt_f_f, v, vfloat32m2_t)
_RVV_UNARY(vuint64m8_t, u64m8, vbool8_t, vfwcvt_xu_f, v, vfloat32m4_t)
_RVV_UNARY(vint64m8_t, i64m8, vbool8_t, vfwcvt_x_f, v, vfloat32m4_t)
_RVV_UNARY(vuint64m8_t, u64m8, vbool8_t, vfwcvt_rtz_xu_f, v, vfloat32m4_t)
_RVV_UNARY(vint64m8_t, i64m8, vbool8_t, vfwcvt_rtz_x_f, v, vfloat32m4_t)
_RVV_UNARY(vfloat64m8_t, f64m8, vbool8_t, vfwcvt_f_xu, v, vuint32m4_t)
_RVV_UNARY(vfloat64m8_t, f64m8, vbool8_t, vfwcvt_f_x, v, vint32m4_t)
_RVV_UNARY(vfloat64m8_t, f64m8, vbool8_t, vfwcvt_f_f, v, vfloat32m4_t)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfwcvt_f_xu, v, vuint16mf4_t)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfwcvt_f_x, v, vint16mf4_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfwcvt_f_xu, v, vuint16mf2_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfwcvt_f_x, v, vint16mf2_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfwcvt_f_xu, v, vuint16m1_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfwcvt_f_x, v, vint16m1_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfwcvt_f_xu, v, vuint16m2_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfwcvt_f_x, v, vint16m2_t)
_RVV_UNARY(vfloat32m8_t, f32m8, vbool4_t, vfwcvt_f_xu, v, vuint16m4_t)
_RVV_UNARY(vfloat32m8_t, f32m8, vbool4_t, vfwcvt_f_x, v, vint16m4_t)
_RVV_UNARY(vuint16mf4_t, u16mf4, vbool64_t, vfncvt_xu_f, w, vfloat32mf2_t)
_RVV_UNARY(vint16mf4_t, i16mf4, vbool64_t, vfncvt_x_f, w, vfloat32mf2_t)
_RVV_UNARY(vuint16mf4_t, u16mf4, vbool64_t, vfncvt_rtz_xu_f, w, vfloat32mf2_t)
_RVV_UNARY(vint16mf4_t, i16mf4, vbool64_t, vfncvt_rtz_x_f, w, vfloat32mf2_t)
_RVV_UNARY(vuint16mf2_t, u16mf2, vbool32_t, vfncvt_xu_f, w, vfloat32m1_t)
_RVV_UNARY(vint16mf2_t, i16mf2, vbool32_t, vfncvt_x_f, w, vfloat32m1_t)
_RVV_UNARY(vuint16mf2_t, u16mf2, vbool32_t, vfncvt_rtz_xu_f, w, vfloat32m1_t)
_RVV_UNARY(vint16mf2_t, i16mf2, vbool32_t, vfncvt_rtz_x_f, w, vfloat32m1_t)
_RVV_UNARY(vuint16m1_t, u16m1, vbool16_t, vfncvt_xu_f, w, vfloat32m2_t)
_RVV_UNARY(vint16m1_t, i16m1, vbool16_t, vfncvt_x_f, w, vfloat32m2_t)
_RVV_UNARY(vuint16m1_t, u16m1, vbool16_t, vfncvt_rtz_xu_f, w, vfloat32m2_t)
_RVV_UNARY(vint16m1_t, i16m1, vbool16_t, vfncvt_rtz_x_f, w, vfloat32m2_t)
_RVV_UNARY(vuint16m2_t, u16m2, vbool8_t, vfncvt_xu_f, w, vfloat32m4_t)
_RVV_UNARY(vint16m2_t, i16m2, vbool8_t, vfncvt_x_f, w, vfloat32m4_t)
_RVV_UNARY(vuint16m2_t, u16m2, vbool8_t, vfncvt_rtz_xu_f, w, vfloat32m4_t)
_RVV_UNARY(vint16m2_t, i16m2, vbool8_t, vfncvt_rtz_x_f, w, vfloat32m4_t)
_RVV_UNARY(vuint16m4_t, u16m4, vbool4_t, vfncvt_xu_f, w, vfloat32m8_t)
_RVV_UNARY(vint16m4_t, i16m4, vbool4_t, vfncvt_x_f, w, vfloat32m8_t)
_RVV_UNARY(vuint16m4_t, u16m4, vbool4_t, vfncvt_rtz_xu_f, w, vfloat32m8_t)
_RVV_UNARY(vint16m4_t, i16m4, vbool4_t, vfncvt_rtz_x_f, w, vfloat32m8_t)
_RVV_UNARY(vuint32mf2_t, u32mf2, vbool64_t, vfncvt_xu_f, w, vfloat64m1_t)
_RVV_UNARY(vint32mf2_t, i32mf2, vbool64_t, vfncvt_x_f, w, vfloat64m1_t)
_RVV_UNARY(vuint32mf2_t, u32mf2, vbool64_t, vfncvt_rtz_xu_f, w, vfloat64m1_t)
_RVV_UNARY(vint32mf2_t, i32mf2, vbool64_t, vfncvt_rtz_x_f, w, vfloat64m1_t)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfncvt_f_xu, w, vuint64m1_t)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfncvt_f_x, w, vint64m1_t)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfncvt_f_f, w, vfloat64m1_t)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfncvt_rod_f_f, w, vfloat64m1_t)
_RVV_UNARY(vuint32m1_t, u32m1, vbool32_t, vfncvt_xu_f, w, vfloat64m2_t)
_RVV_UNARY(vint32m1_t, i32m1, vbool32_t, vfncvt_x_f, w, vfloat64m2_t)
_RVV_UNARY(vuint32m1_t, u32m1, vbool32_t, vfncvt_rtz_xu_f, w, vfloat64m2_t)
_RVV_UNARY(vint32m1_t, i32m1, vbool32_t, vfncvt_rtz_x_f, w, vfloat64m2_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfncvt_f_xu, w, vuint64m2_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfncvt_f_x, w, vint64m2_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfncvt_f_f, w, vfloat64m2_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfncvt_rod_f_f, w, vfloat64m2_t)
_RVV_UNARY(vuint32m2_t, u32m2, vbool16_t, vfncvt_xu_f, w, vfloat64m4_t)
_RVV_UNARY(vint32m2_t, i32m2, vbool16_t, vfncvt_x_f, w, vfloat64m4_t)
_RVV_UNARY(vuint32m2_t, u32m2, vbool16_t, vfncvt_rtz_xu_f, w, vfloat64m4_t)
_RVV_UNARY(vint32m2_t, i32m2, vbool16_t, vfncvt_rtz_x_f, w, vfloat64m4_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfncvt_f_xu, w, vuint64m4_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfncvt_f_x, w, vint64m4_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfncvt_f_f, w, vfloat64m4_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfncvt_rod_f_f, w, vfloat64m4_t)
_RVV_UNARY(vuint32m4_t, u32m4, vbool8_t, vfncvt_xu_f, w, vfloat64m8_t)
_RVV_UNARY(vint32m4_t, i32m4, vbool8_t, vfncvt_x_f, w, vfloat64m8_t)
_RVV_UNARY(vuint32m4_t, u32m4, vbool8_t, vfncvt_rtz_xu_f, w, vfloat64m8_t)
_RVV_UNARY(vint32m4_t, i32m4, vbool8_t, vfncvt_rtz_x_f, w, vfloat64m8_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfncvt_f_xu, w, vuint64m8_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfncvt_f_x, w, vint64m8_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfncvt_f_f, w, vfloat64m8_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfncvt_rod_f_f, w, vfloat64m8_t)

/* The reciprocal estimates */
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfrsqrt7, v, vfloat32mf2_t)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfrec7, v, vfloat32mf2_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfrsqrt7, v, vfloat32m1_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfrec7, v, vfloat32m1_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfrsqrt7, v, vfloat32m2_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfrec7, v, vfloat32m2_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfrsqrt7, v, vfloat32m4_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfrec7, v, vfloat32m4_t)
_RVV_UNARY(vfloat32m8_t, f32m8, vbool4_t, vfrsqrt7, v, vfloat32m8_t)
_RVV_UNARY(vfloat32m8_t, f32m8, vbool4_t, vfrec7, v, vfloat32m8_t)
_RVV_UNARY(vfloat64m1_t, f64m1, vbool64_t, vfrsqrt7, v, vfloat64m1_t)
_RVV_UNARY(vfloat64m1_t, f64m1, vbool64_t, vfrec7, v, vfloat64m1_t)
_RVV_UNARY(vfloat64m2_t, f64m2, vbool32_t, vfrsqrt7, v, vfloat64m2_t)
_RVV_UNARY(vfloat64m2_t, f64m2, vbool32_t, vfrec7, v, vfloat64m2_t)
_RVV_UNARY(vfloat64m4_t, f64m4, vbool16_t, vfrsqrt7, v, vfloat64m4_t)
_RVV_UNARY(vfloat64m4_t, f64m4, vbool16_t, vfrec7, v, vfloat64m4_t)
_RVV_UNARY(vfloat64m8_t, f64m8, vbool8_t, vfrsqrt7, v, vfloat64m8_t)
_RVV_UNARY(vfloat64m8_t, f64m8, vbool8_t, vfrec7, v, vfloat64m8_t)

/* Indexed, fault only first and whole register memory */
vuint8mf8_t __riscv_vle8ff_v_u8mf8(const unsigned char * rs1, size_t *new_vl, size_t vl);
vuint8mf8_t __riscv_vluxei8_v_u8mf8(const unsigned char * rs1, vuint8mf8_t rs2, size_t vl);
vuint8mf8_t __riscv_vloxei8_v_u8mf8(const unsigned char * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_u8mf8(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8_t vs3, size_t vl);
void __riscv_vsoxei8_v_u8mf8(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8_t vs3, size_t vl);
vuint8mf8_t __riscv_vluxei16_v_u8mf8(const unsigned char * rs1, vuint16mf4_t rs2, size_t vl);
vuint8mf8_t __riscv_vloxei16_v_u8mf8(const unsigned char * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_u8mf8(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8_t vs3, size_t vl);
void __riscv_vsoxei16_v_u8mf8(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8_t vs3, size_t vl);
vuint8mf8_t __riscv_vluxei32_v_u8mf8(const unsigned char * rs1, vuint32mf2_t rs2, size_t vl);
vuint8mf8_t __riscv_vloxei32_v_u8mf8(const unsigned char * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_u8mf8(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8_t vs3, size_t vl);
void __riscv_vsoxei32_v_u8mf8(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8_t vs3, size_t vl);
vuint8mf8_t __riscv_vluxei64_v_u8mf8(const unsigned char * rs1, vuint64m1_t rs2, size_t vl);
vuint8mf8_t __riscv_vloxei64_v_u8mf8(const unsigned char * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_u8mf8(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8_t vs3, size_t vl);
void __riscv_vsoxei64_v_u8mf8(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8_t vs3, size_t vl);
vint8mf8_t __riscv_vle8ff_v_i8mf8(const signed char * rs1, size_t *new_vl, size_t vl);
vint8mf8_t __riscv_vluxei8_v_i8mf8(const signed char * rs1, vuint8mf8_t rs2, size_t vl);
vint8mf8_t __riscv_vloxei8_v_i8mf8(const signed char * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_i8mf8(signed char *rs1, vuint8mf8_t rs2, vint8mf8_t vs3, size_t vl);
void __riscv_vsoxei8_v_i8mf8(signed char *rs1, vuint8mf8_t rs2, vint8mf8_t vs3, size_t vl);
vint8mf8_t __riscv_vluxei16_v_i8mf8(const signed char * rs1, vuint16mf4_t rs2, size_t vl);
vint8mf8_t __riscv_vloxei16_v_i8mf8(const signed char * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_i8mf8(signed char *rs1, vuint16mf4_t rs2, vint8mf8_t vs3, size_t vl);
void __riscv_vsoxei16_v_i8mf8(signed char *rs1, vuint16mf4_t rs2, vint8mf8_t vs3, size_t vl);
vint8mf8_t __riscv_vluxei32_v_i8mf8(const signed char * rs1, vuint32mf2_t rs2, size_t vl);
vint8mf8_t __riscv_vloxei32_v_i8mf8(const signed char * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_i8mf8(signed char *rs1, vuint32mf2_t rs2, vint8mf8_t vs3, size_t vl);
void __riscv_vsoxei32_v_i8mf8(signed char *rs1, vuint32mf2_t rs2, vint8mf8_t vs3, size_t vl);
vint8mf8_t __riscv_vluxei64_v_i8mf8(const signed char * rs1, vuint64m1_t rs2, size_t vl);
vint8mf8_t __riscv_vloxei64_v_i8mf8(const signed char * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_i8mf8(signed char *rs1, vuint64m1_t rs2, vint8mf8_t vs3, size_t vl);
void __riscv_vsoxei64_v_i8mf8(signed char *rs1, vuint64m1_t rs2, vint8mf8_t vs3, size_t vl);
vuint8mf4_t __riscv_vle8ff_v_u8mf4(const unsigned char * rs1, size_t *new_vl, size_t vl);
vuint8mf4_t __riscv_vluxei8_v_u8mf4(const unsigned char * rs1, vuint8mf4_t rs2, size_t vl);
vuint8mf4_t __riscv_vloxei8_v_u8mf4(const unsigned char * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_u8mf4(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4_t vs3, size_t vl);
void __riscv_vsoxei8_v_u8mf4(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4_t vs3, size_t vl);
vuint8mf4_t __riscv_vluxei16_v_u8mf4(const unsigned char * rs1, vuint16mf2_t rs2, size_t vl);
vuint8mf4_t __riscv_vloxei16_v_u8mf4(const unsigned char * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_u8mf4(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4_t vs3, size_t vl);
void __riscv_vsoxei16_v_u8mf4(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4_t vs3, size_t vl);
vuint8mf4_t __riscv_vluxei32_v_u8mf4(const unsigned char * rs1, vuint32m1_t rs2, size_t vl);
vuint8mf4_t __riscv_vloxei32_v_u8mf4(const unsigned char * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_u8mf4(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4_t vs3, size_t vl);
void __riscv_vsoxei32_v_u8mf4(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4_t vs3, size_t vl);
vuint8mf4_t __riscv_vluxei64_v_u8mf4(const unsigned char * rs1, vuint64m2_t rs2, size_t vl);
vuint8mf4_t __riscv_vloxei64_v_u8mf4(const unsigned char * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_u8mf4(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4_t vs3, size_t vl);
void __riscv_vsoxei64_v_u8mf4(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4_t vs3, size_t vl);
vint8mf4_t __riscv_vle8ff_v_i8mf4(const signed char * rs1, size_t *new_vl, size_t vl);
vint8mf4_t __riscv_vluxei8_v_i8mf4(const signed char * rs1, vuint8mf4_t rs2, size_t vl);
vint8mf4_t __riscv_vloxei8_v_i8mf4(const signed char * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_i8mf4(signed char *rs1, vuint8mf4_t rs2, vint8mf4_t vs3, size_t vl);
void __riscv_vsoxei8_v_i8mf4(signed char *rs1, vuint8mf4_t rs2, vint8mf4_t vs3, size_t vl);
vint8mf4_t __riscv_vluxei16_v_i8mf4(const signed char * rs1, vuint16mf2_t rs2, size_t vl);
vint8mf4_t __riscv_vloxei16_v_i8mf4(const signed char * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_i8mf4(signed char *rs1, vuint16mf2_t rs2, vint8mf4_t vs3, size_t vl);
void __riscv_vsoxei16_v_i8mf4(signed char *rs1, vuint16mf2_t rs2, vint8mf4_t vs3, size_t vl);
vint8mf4_t __riscv_vluxei32_v_i8mf4(const signed char * rs1, vuint32m1_t rs2, size_t vl);
vint8mf4_t __riscv_vloxei32_v_i8mf4(const signed char * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_i8mf4(signed char *rs1, vuint32m1_t rs2, vint8mf4_t vs3, size_t vl);
void __riscv_vsoxei32_v_i8mf4(signed char *rs1, vuint32m1_t rs2, vint8mf4_t vs3, size_t vl);
vint8mf4_t __riscv_vluxei64_v_i8mf4(const signed char * rs1, vuint64m2_t rs2, size_t vl);
vint8mf4_t __riscv_vloxei64_v_i8mf4(const signed char * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_i8mf4(signed char *rs1, vuint64m2_t rs2, vint8mf4_t vs3, size_t vl);
void __riscv_vsoxei64_v_i8mf4(signed char *rs1, vuint64m2_t rs2, vint8mf4_t vs3, size_t vl);
vuint8mf2_t __riscv_vle8ff_v_u8mf2(const unsigned char * rs1, size_t *new_vl, size_t vl);
vuint8mf2_t __riscv_vluxei8_v_u8mf2(const unsigned char * rs1, vuint8mf2_t rs2, size_t vl);
vuint8mf2_t __riscv_vloxei8_v_u8mf2(const unsigned char * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_u8mf2(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2_t vs3, size_t vl);
void __riscv_vsoxei8_v_u8mf2(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2_t vs3, size_t vl);
vuint8mf2_t __riscv_vluxei16_v_u8mf2(const unsigned char * rs1, vuint16m1_t rs2, size_t vl);
vuint8mf2_t __riscv_vloxei16_v_u8mf2(const unsigned char * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_u8mf2(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2_t vs3, size_t vl);
void __riscv_vsoxei16_v_u8mf2(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2_t vs3, size_t vl);
vuint8mf2_t __riscv_vluxei32_v_u8mf2(const unsigned char * rs1, vuint32m2_t rs2, size_t vl);
vuint8mf2_t __riscv_vloxei32_v_u8mf2(const unsigned char * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_u8mf2(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2_t vs3, size_t vl);
void __riscv_vsoxei32_v_u8mf2(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2_t vs3, size_t vl);
vuint8mf2_t __riscv_vluxei64_v_u8mf2(const unsigned char * rs1, vuint64m4_t rs2, size_t vl);
vuint8mf2_t __riscv_vloxei64_v_u8mf2(const unsigned char * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_u8mf2(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2_t vs3, size_t vl);
void __riscv_vsoxei64_v_u8mf2(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2_t vs3, size_t vl);
vint8mf2_t __riscv_vle8ff_v_i8mf2(const signed char * rs1, size_t *new_vl, size_t vl);
vint8mf2_t __riscv_vluxei8_v_i8mf2(const signed char * rs1, vuint8mf2_t rs2, size_t vl);
vint8mf2_t __riscv_vloxei8_v_i8mf2(const signed char * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_i8mf2(signed char *rs1, vuint8mf2_t rs2, vint8mf2_t vs3, size_t vl);
void __riscv_vsoxei8_v_i8mf2(signed char *rs1, vuint8mf2_t rs2, vint8mf2_t vs3, size_t vl);
vint8mf2_t __riscv_vluxei16_v_i8mf2(const signed char * rs1, vuint16m1_t rs2, size_t vl);
vint8mf2_t __riscv_vloxei16_v_i8mf2(const signed char * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_i8mf2(signed char *rs1, vuint16m1_t rs2, vint8mf2_t vs3, size_t vl);
void __riscv_vsoxei16_v_i8mf2(signed char *rs1, vuint16m1_t rs2, vint8mf2_t vs3, size_t vl);
vint8mf2_t __riscv_vluxei32_v_i8mf2(const signed char * rs1, vuint32m2_t rs2, size_t vl);
vint8mf2_t __riscv_vloxei32_v_i8mf2(const signed char * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_i8mf2(signed char *rs1, vuint32m2_t rs2, vint8mf2_t vs3, size_t vl);
void __riscv_vsoxei32_v_i8mf2(signed char *rs1, vuint32m2_t rs2, vint8mf2_t vs3, size_t vl);
vint8mf2_t __riscv_vluxei64_v_i8mf2(const signed char * rs1, vuint64m4_t rs2, size_t vl);
vint8mf2_t __riscv_vloxei64_v_i8mf2(const signed char * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_i8mf2(signed char *rs1, vuint64m4_t rs2, vint8mf2_t vs3, size_t vl);
void __riscv_vsoxei64_v_i8mf2(signed char *rs1, vuint64m4_t rs2, vint8mf2_t vs3, size_t vl);
vuint8m1_t __riscv_vle8ff_v_u8m1(const unsigned char * rs1, size_t *new_vl, size_t vl);
vuint8m1_t __riscv_vluxei8_v_u8m1(const unsigned char * rs1, vuint8m1_t rs2, size_t vl);
vuint8m1_t __riscv_vloxei8_v_u8m1(const unsigned char * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_u8m1(unsigned char *rs1, vuint8m1_t rs2, vuint8m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_u8m1(unsigned char *rs1, vuint8m1_t rs2, vuint8m1_t vs3, size_t vl);
vuint8m1_t __riscv_vluxei16_v_u8m1(const unsigned char * rs1, vuint16m2_t rs2, size_t vl);
vuint8m1_t __riscv_vloxei16_v_u8m1(const unsigned char * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_u8m1(unsigned char *rs1, vuint16m2_t rs2, vuint8m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_u8m1(unsigned char *rs1, vuint16m2_t rs2, vuint8m1_t vs3, size_t vl);
vuint8m1_t __riscv_vluxei32_v_u8m1(const unsigned char * rs1, vuint32m4_t rs2, size_t vl);
vuint8m1_t __riscv_vloxei32_v_u8m1(const unsigned char * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_u8m1(unsigned char *rs1, vuint32m4_t rs2, vuint8m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_u8m1(unsigned char *rs1, vuint32m4_t rs2, vuint8m1_t vs3, size_t vl);
vuint8m1_t __riscv_vluxei64_v_u8m1(const unsigned char * rs1, vuint64m8_t rs2, size_t vl);
vuint8m1_t __riscv_vloxei64_v_u8m1(const unsigned char * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_u8m1(unsigned char *rs1, vuint64m8_t rs2, vuint8m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_u8m1(unsigned char *rs1, vuint64m8_t rs2, vuint8m1_t vs3, size_t vl);
vuint8m1_t __riscv_vl1re8_v_u8m1(const unsigned char * rs1);
void __riscv_vs1r_v_u8m1(unsigned char *rs1, vuint8m1_t vs3);
vint8m1_t __riscv_vle8ff_v_i8m1(const signed char * rs1, size_t *new_vl, size_t vl);
vint8m1_t __riscv_vluxei8_v_i8m1(const signed char * rs1, vuint8m1_t rs2, size_t vl);
vint8m1_t __riscv_vloxei8_v_i8m1(const signed char * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_i8m1(signed char *rs1, vuint8m1_t rs2, vint8m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_i8m1(signed char *rs1, vuint8m1_t rs2, vint8m1_t vs3, size_t vl);
vint8m1_t __riscv_vluxei16_v_i8m1(const signed char * rs1, vuint16m2_t rs2, size_t vl);
vint8m1_t __riscv_vloxei16_v_i8m1(const signed char * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_i8m1(signed char *rs1, vuint16m2_t rs2, vint8m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_i8m1(signed char *rs1, vuint16m2_t rs2, vint8m1_t vs3, size_t vl);
vint8m1_t __riscv_vluxei32_v_i8m1(const signed char * rs1, vuint32m4_t rs2, size_t vl);
vint8m1_t __riscv_vloxei32_v_i8m1(const signed char * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_i8m1(signed char *rs1, vuint32m4_t rs2, vint8m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_i8m1(signed char *rs1, vuint32m4_t rs2, vint8m1_t vs3, size_t vl);
vint8m1_t __riscv_vluxei64_v_i8m1(const signed char * rs1, vuint64m8_t rs2, size_t vl);
vint8m1_t __riscv_vloxei64_v_i8m1(const signed char * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_i8m1(signed char *rs1, vuint64m8_t rs2, vint8m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_i8m1(signed char *rs1, vuint64m8_t rs2, vint8m1_t vs3, size_t vl);
vint8m1_t __riscv_vl1re8_v_i8m1(const signed char * rs1);
void __riscv_vs1r_v_i8m1(signed char *rs1, vint8m1_t vs3);
vuint8m2_t __riscv_vle8ff_v_u8m2(const unsigned char * rs1, size_t *new_vl, size_t vl);
vuint8m2_t __riscv_vluxei8_v_u8m2(const unsigned char * rs1, vuint8m2_t rs2, size_t vl);
vuint8m2_t __riscv_vloxei8_v_u8m2(const unsigned char * rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxei8_v_u8m2(unsigned char *rs1, vuint8m2_t rs2, vuint8m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_u8m2(unsigned char *rs1, vuint8m2_t rs2, vuint8m2_t vs3, size_t vl);
vuint8m2_t __riscv_vluxei16_v_u8m2(const unsigned char * rs1, vuint16m4_t rs2, size_t vl);
vuint8m2_t __riscv_vloxei16_v_u8m2(const unsigned char * rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxei16_v_u8m2(unsigned char *rs1, vuint16m4_t rs2, vuint8m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_u8m2(unsigned char *rs1, vuint16m4_t rs2, vuint8m2_t vs3, size_t vl);
vuint8m2_t __riscv_vluxei32_v_u8m2(const unsigned char * rs1, vuint32m8_t rs2, size_t vl);
vuint8m2_t __riscv_vloxei32_v_u8m2(const unsigned char * rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxei32_v_u8m2(unsigned char *rs1, vuint32m8_t rs2, vuint8m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_u8m2(unsigned char *rs1, vuint32m8_t rs2, vuint8m2_t vs3, size_t vl);
vuint8m2_t __riscv_vl2re8_v_u8m2(const unsigned char * rs1);
void __riscv_vs2r_v_u8m2(unsigned char *rs1, vuint8m2_t vs3);
vint8m2_t __riscv_vle8ff_v_i8m2(const signed char * rs1, size_t *new_vl, size_t vl);
vint8m2_t __riscv_vluxei8_v_i8m2(const signed char * rs1, vuint8m2_t rs2, size_t vl);
vint8m2_t __riscv_vloxei8_v_i8m2(const signed char * rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxei8_v_i8m2(signed char *rs1, vuint8m2_t rs2, vint8m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_i8m2(signed char *rs1, vuint8m2_t rs2, vint8m2_t vs3, size_t vl);
vint8m2_t __riscv_vluxei16_v_i8m2(const signed char * rs1, vuint16m4_t rs2, size_t vl);
vint8m2_t __riscv_vloxei16_v_i8m2(const signed char * rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxei16_v_i8m2(signed char *rs1, vuint16m4_t rs2, vint8m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_i8m2(signed char *rs1, vuint16m4_t rs2, vint8m2_t vs3, size_t vl);
vint8m2_t __riscv_vluxei32_v_i8m2(const signed char * rs1, vuint32m8_t rs2, size_t vl);
vint8m2_t __riscv_vloxei32_v_i8m2(const signed char * rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxei32_v_i8m2(signed char *rs1, vuint32m8_t rs2, vint8m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_i8m2(signed char *rs1, vuint32m8_t rs2, vint8m2_t vs3, size_t vl);
vint8m2_t __riscv_vl2re8_v_i8m2(const signed char * rs1);
void __riscv_vs2r_v_i8m2(signed char *rs1, vint8m2_t vs3);
vuint8m4_t __riscv_vle8ff_v_u8m4(const unsigned char * rs1, size_t *new_vl, size_t vl);
vuint8m4_t __riscv_vluxei8_v_u8m4(const unsigned char * rs1, vuint8m4_t rs2, size_t vl);
vuint8m4_t __riscv_vloxei8_v_u8m4(const unsigned char * rs1, vuint8m4_t rs2, size_t vl);
void __riscv_vsuxei8_v_u8m4(unsigned char *rs1, vuint8m4_t rs2, vuint8m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_u8m4(unsigned char *rs1, vuint8m4_t rs2, vuint8m4_t vs3, size_t vl);
vuint8m4_t __riscv_vluxei16_v_u8m4(const unsigned char * rs1, vuint16m8_t rs2, size_t vl);
vuint8m4_t __riscv_vloxei16_v_u8m4(const unsigned char * rs1, vuint16m8_t rs2, size_t vl);
void __riscv_vsuxei16_v_u8m4(unsigned char *rs1, vuint16m8_t rs2, vuint8m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_u8m4(unsigned char *rs1, vuint16m8_t rs2, vuint8m4_t vs3, size_t vl);
vuint8m4_t __riscv_vl4re8_v_u8m4(const unsigned char * rs1);
void __riscv_vs4r_v_u8m4(unsigned char *rs1, vuint8m4_t vs3);
vint8m4_t __riscv_vle8ff_v_i8m4(const signed char * rs1, size_t *new_vl, size_t vl);
vint8m4_t __riscv_vluxei8_v_i8m4(const signed char * rs1, vuint8m4_t rs2, size_t vl);
vint8m4_t __riscv_vloxei8_v_i8m4(const signed char * rs1, vuint8m4_t rs2, size_t vl);
void __riscv_vsuxei8_v_i8m4(signed char *rs1, vuint8m4_t rs2, vint8m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_i8m4(signed char *rs1, vuint8m4_t rs2, vint8m4_t vs3, size_t vl);
vint8m4_t __riscv_vluxei16_v_i8m4(const signed char * rs1, vuint16m8_t rs2, size_t vl);
vint8m4_t __riscv_vloxei16_v_i8m4(const signed char * rs1, vuint16m8_t rs2, size_t vl);
void __riscv_vsuxei16_v_i8m4(signed char *rs1, vuint16m8_t rs2, vint8m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_i8m4(signed char *rs1, vuint16m8_t rs2, vint8m4_t vs3, size_t vl);
vint8m4_t __riscv_vl4re8_v_i8m4(const signed char * rs1);
void __riscv_vs4r_v_i8m4(signed char *rs1, vint8m4_t vs3);
vuint8m8_t __riscv_vle8ff_v_u8m8(const unsigned char * rs1, size_t *new_vl, size_t vl);
vuint8m8_t __riscv_vluxei8_v_u8m8(const unsigned char * rs1, vuint8m8_t rs2, size_t vl);
vuint8m8_t __riscv_vloxei8_v_u8m8(const unsigned char * rs1, vuint8m8_t rs2, size_t vl);
void __riscv_vsuxei8_v_u8m8(unsigned char *rs1, vuint8m8_t rs2, vuint8m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_u8m8(unsigned char *rs1, vuint8m8_t rs2, vuint8m8_t vs3, size_t vl);
vuint8m8_t __riscv_vl8re8_v_u8m8(const unsigned char * rs1);
void __riscv_vs8r_v_u8m8(unsigned char *rs1, vuint8m8_t vs3);
vint8m8_t __riscv_vle8ff_v_i8m8(const signed char * rs1, size_t *new_vl, size_t vl);
vint8m8_t __riscv_vluxei8_v_i8m8(const signed char * rs1, vuint8m8_t rs2, size_t vl);
vint8m8_t __riscv_vloxei8_v_i8m8(const signed char * rs1, vuint8m8_t rs2, size_t vl);
void __riscv_vsuxei8_v_i8m8(signed char *rs1, vuint8m8_t rs2, vint8m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_i8m8(signed char *rs1, vuint8m8_t rs2, vint8m8_t vs3, size_t vl);
vint8m8_t __riscv_vl8re8_v_i8m8(const signed char * rs1);
void __riscv_vs8r_v_i8m8(signed char *rs1, vint8m8_t vs3);
vuint16mf4_t __riscv_vle16ff_v_u16mf4(const unsigned short * rs1, size_t *new_vl, size_t vl);
vuint16mf4_t __riscv_vluxei8_v_u16mf4(const unsigned short * rs1, vuint8mf8_t rs2, size_t vl);
vuint16mf4_t __riscv_vloxei8_v_u16mf4(const unsigned short * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_u16mf4(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4_t vs3, size_t vl);
void __riscv_vsoxei8_v_u16mf4(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4_t vs3, size_t vl);
vuint16mf4_t __riscv_vluxei16_v_u16mf4(const unsigned short * rs1, vuint16mf4_t rs2, size_t vl);
vuint16mf4_t __riscv_vloxei16_v_u16mf4(const unsigned short * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_u16mf4(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4_t vs3, size_t vl);
void __riscv_vsoxei16_v_u16mf4(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4_t vs3, size_t vl);
vuint16mf4_t __riscv_vluxei32_v_u16mf4(const unsigned short * rs1, vuint32mf2_t rs2, size_t vl);
vuint16mf4_t __riscv_vloxei32_v_u16mf4(const unsigned short * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_u16mf4(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4_t vs3, size_t vl);
void __riscv_vsoxei32_v_u16mf4(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4_t vs3, size_t vl);
vuint16mf4_t __riscv_vluxei64_v_u16mf4(const unsigned short * rs1, vuint64m1_t rs2, size_t vl);
vuint16mf4_t __riscv_vloxei64_v_u16mf4(const unsigned short * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_u16mf4(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4_t vs3, size_t vl);
void __riscv_vsoxei64_v_u16mf4(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4_t vs3, size_t vl);
vint16mf4_t __riscv_vle16ff_v_i16mf4(const short * rs1, size_t *new_vl, size_t vl);
vint16mf4_t __riscv_vluxei8_v_i16mf4(const short * rs1, vuint8mf8_t rs2, size_t vl);
vint16mf4_t __riscv_vloxei8_v_i16mf4(const short * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_i16mf4(short *rs1, vuint8mf8_t rs2, vint16mf4_t vs3, size_t vl);
void __riscv_vsoxei8_v_i16mf4(short *rs1, vuint8mf8_t rs2, vint16mf4_t vs3, size_t vl);
vint16mf4_t __riscv_vluxei16_v_i16mf4(const short * rs1, vuint16mf4_t rs2, size_t vl);
vint16mf4_t __riscv_vloxei16_v_i16mf4(const short * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_i16mf4(short *rs1, vuint16mf4_t rs2, vint16mf4_t vs3, size_t vl);
void __riscv_vsoxei16_v_i16mf4(short *rs1, vuint16mf4_t rs2, vint16mf4_t vs3, size_t vl);
vint16mf4_t __riscv_vluxei32_v_i16mf4(const short * rs1, vuint32mf2_t rs2, size_t vl);
vint16mf4_t __riscv_vloxei32_v_i16mf4(const short * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_i16mf4(short *rs1, vuint32mf2_t rs2, vint16mf4_t vs3, size_t vl);
void __riscv_vsoxei32_v_i16mf4(short *rs1, vuint32mf2_t rs2, vint16mf4_t vs3, size_t vl);
vint16mf4_t __riscv_vluxei64_v_i16mf4(const short * rs1, vuint64m1_t rs2, size_t vl);
vint16mf4_t __riscv_vloxei64_v_i16mf4(const short * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_i16mf4(short *rs1, vuint64m1_t rs2, vint16mf4_t vs3, size_t vl);
void __riscv_vsoxei64_v_i16mf4(short *rs1, vuint64m1_t rs2, vint16mf4_t vs3, size_t vl);
vuint16mf2_t __riscv_vle16ff_v_u16mf2(const unsigned short * rs1, size_t *new_vl, size_t vl);
vuint16mf2_t __riscv_vluxei8_v_u16mf2(const unsigned short * rs1, vuint8mf4_t rs2, size_t vl);
vuint16mf2_t __riscv_vloxei8_v_u16mf2(const unsigned short * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_u16mf2(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2_t vs3, size_t vl);
void __riscv_vsoxei8_v_u16mf2(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2_t vs3, size_t vl);
vuint16mf2_t __riscv_vluxei16_v_u16mf2(const unsigned short * rs1, vuint16mf2_t rs2, size_t vl);
vuint16mf2_t __riscv_vloxei16_v_u16mf2(const unsigned short * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_u16mf2(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2_t vs3, size_t vl);
void __riscv_vsoxei16_v_u16mf2(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2_t vs3, size_t vl);
vuint16mf2_t __riscv_vluxei32_v_u16mf2(const unsigned short * rs1, vuint32m1_t rs2, size_t vl);
vuint16mf2_t __riscv_vloxei32_v_u16mf2(const unsigned short * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_u16mf2(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2_t vs3, size_t vl);
void __riscv_vsoxei32_v_u16mf2(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2_t vs3, size_t vl);
vuint16mf2_t __riscv_vluxei64_v_u16mf2(const unsigned short * rs1, vuint64m2_t rs2, size_t vl);
vuint16mf2_t __riscv_vloxei64_v_u16mf2(const unsigned short * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_u16mf2(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2_t vs3, size_t vl);
void __riscv_vsoxei64_v_u16mf2(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2_t vs3, size_t vl);
vint16mf2_t __riscv_vle16ff_v_i16mf2(const short * rs1, size_t *new_vl, size_t vl);
vint16mf2_t __riscv_vluxei8_v_i16mf2(const short * rs1, vuint8mf4_t rs2, size_t vl);
vint16mf2_t __riscv_vloxei8_v_i16mf2(const short * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_i16mf2(short *rs1, vuint8mf4_t rs2, vint16mf2_t vs3, size_t vl);
void __riscv_vsoxei8_v_i16mf2(short *rs1, vuint8mf4_t rs2, vint16mf2_t vs3, size_t vl);
vint16mf2_t __riscv_vluxei16_v_i16mf2(const short * rs1, vuint16mf2_t rs2, size_t vl);
vint16mf2_t __riscv_vloxei16_v_i16mf2(const short * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_i16mf2(short *rs1, vuint16mf2_t rs2, vint16mf2_t vs3, size_t vl);
void __riscv_vsoxei16_v_i16mf2(short *rs1, vuint16mf2_t rs2, vint16mf2_t vs3, size_t vl);
vint16mf2_t __riscv_vluxei32_v_i16mf2(const short * rs1, vuint32m1_t rs2, size_t vl);
vint16mf2_t __riscv_vloxei32_v_i16mf2(const short * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_i16mf2(short *rs1, vuint32m1_t rs2, vint16mf2_t vs3, size_t vl);
void __riscv_vsoxei32_v_i16mf2(short *rs1, vuint32m1_t rs2, vint16mf2_t vs3, size_t vl);
vint16mf2_t __riscv_vluxei64_v_i16mf2(const short * rs1, vuint64m2_t rs2, size_t vl);
vint16mf2_t __riscv_vloxei64_v_i16mf2(const short * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_i16mf2(short *rs1, vuint64m2_t rs2, vint16mf2_t vs3, size_t vl);
void __riscv_vsoxei64_v_i16mf2(short *rs1, vuint64m2_t rs2, vint16mf2_t vs3, size_t vl);
vuint16m1_t __riscv_vle16ff_v_u16m1(const unsigned short * rs1, size_t *new_vl, size_t vl);
vuint16m1_t __riscv_vluxei8_v_u16m1(const unsigned short * rs1, vuint8mf2_t rs2, size_t vl);
vuint16m1_t __riscv_vloxei8_v_u16m1(const unsigned short * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_u16m1(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_u16m1(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1_t vs3, size_t vl);
vuint16m1_t __riscv_vluxei16_v_u16m1(const unsigned short * rs1, vuint16m1_t rs2, size_t vl);
vuint16m1_t __riscv_vloxei16_v_u16m1(const unsigned short * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_u16m1(unsigned short *rs1, vuint16m1_t rs2, vuint16m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_u16m1(unsigned short *rs1, vuint16m1_t rs2, vuint16m1_t vs3, size_t vl);
vuint16m1_t __riscv_vluxei32_v_u16m1(const unsigned short * rs1, vuint32m2_t rs2, size_t vl);
vuint16m1_t __riscv_vloxei32_v_u16m1(const unsigned short * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_u16m1(unsigned short *rs1, vuint32m2_t rs2, vuint16m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_u16m1(unsigned short *rs1, vuint32m2_t rs2, vuint16m1_t vs3, size_t vl);
vuint16m1_t __riscv_vluxei64_v_u16m1(const unsigned short * rs1, vuint64m4_t rs2, size_t vl);
vuint16m1_t __riscv_vloxei64_v_u16m1(const unsigned short * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_u16m1(unsigned short *rs1, vuint64m4_t rs2, vuint16m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_u16m1(unsigned short *rs1, vuint64m4_t rs2, vuint16m1_t vs3, size_t vl);
vuint16m1_t __riscv_vl1re16_v_u16m1(const unsigned short * rs1);
void __riscv_vs1r_v_u16m1(unsigned short *rs1, vuint16m1_t vs3);
vint16m1_t __riscv_vle16ff_v_i16m1(const short * rs1, size_t *new_vl, size_t vl);
vint16m1_t __riscv_vluxei8_v_i16m1(const short * rs1, vuint8mf2_t rs2, size_t vl);
vint16m1_t __riscv_vloxei8_v_i16m1(const short * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_i16m1(short *rs1, vuint8mf2_t rs2, vint16m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_i16m1(short *rs1, vuint8mf2_t rs2, vint16m1_t vs3, size_t vl);
vint16m1_t __riscv_vluxei16_v_i16m1(const short * rs1, vuint16m1_t rs2, size_t vl);
vint16m1_t __riscv_vloxei16_v_i16m1(const short * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_i16m1(short *rs1, vuint16m1_t rs2, vint16m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_i16m1(short *rs1, vuint16m1_t rs2, vint16m1_t vs3, size_t vl);
vint16m1_t __riscv_vluxei32_v_i16m1(const short * rs1, vuint32m2_t rs2, size_t vl);
vint16m1_t __riscv_vloxei32_v_i16m1(const short * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_i16m1(short *rs1, vuint32m2_t rs2, vint16m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_i16m1(short *rs1, vuint32m2_t rs2, vint16m1_t vs3, size_t vl);
vint16m1_t __riscv_vluxei64_v_i16m1(const short * rs1, vuint64m4_t rs2, size_t vl);
vint16m1_t __riscv_vloxei64_v_i16m1(const short * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_i16m1(short *rs1, vuint64m4_t rs2, vint16m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_i16m1(short *rs1, vuint64m4_t rs2, vint16m1_t vs3, size_t vl);
vint16m1_t __riscv_vl1re16_v_i16m1(const short * rs1);
void __riscv_vs1r_v_i16m1(short *rs1, vint16m1_t vs3);
vuint16m2_t __riscv_vle16ff_v_u16m2(const unsigned short * rs1, size_t *new_vl, size_t vl);
vuint16m2_t __riscv_vluxei8_v_u16m2(const unsigned short * rs1, vuint8m1_t rs2, size_t vl);
vuint16m2_t __riscv_vloxei8_v_u16m2(const unsigned short * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_u16m2(unsigned short *rs1, vuint8m1_t rs2, vuint16m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_u16m2(unsigned short *rs1, vuint8m1_t rs2, vuint16m2_t vs3, size_t vl);
vuint16m2_t __riscv_vluxei16_v_u16m2(const unsigned short * rs1, vuint16m2_t rs2, size_t vl);
vuint16m2_t __riscv_vloxei16_v_u16m2(const unsigned short * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_u16m2(unsigned short *rs1, vuint16m2_t rs2, vuint16m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_u16m2(unsigned short *rs1, vuint16m2_t rs2, vuint16m2_t vs3, size_t vl);
vuint16m2_t __riscv_vluxei32_v_u16m2(const unsigned short * rs1, vuint32m4_t rs2, size_t vl);
vuint16m2_t __riscv_vloxei32_v_u16m2(const unsigned short * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_u16m2(unsigned short *rs1, vuint32m4_t rs2, vuint16m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_u16m2(unsigned short *rs1, vuint32m4_t rs2, vuint16m2_t vs3, size_t vl);
vuint16m2_t __riscv_vluxei64_v_u16m2(const unsigned short * rs1, vuint64m8_t rs2, size_t vl);
vuint16m2_t __riscv_vloxei64_v_u16m2(const unsigned short * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_u16m2(unsigned short *rs1, vuint64m8_t rs2, vuint16m2_t vs3, size_t vl);
void __riscv_vsoxei64_v_u16m2(unsigned short *rs1, vuint64m8_t rs2, vuint16m2_t vs3, size_t vl);
vuint16m2_t __riscv_vl2re16_v_u16m2(const unsigned short * rs1);
void __riscv_vs2r_v_u16m2(unsigned short *rs1, vuint16m2_t vs3);
vint16m2_t __riscv_vle16ff_v_i16m2(const short * rs1, size_t *new_vl, size_t vl);
vint16m2_t __riscv_vluxei8_v_i16m2(const short * rs1, vuint8m1_t rs2, size_t vl);
vint16m2_t __riscv_vloxei8_v_i16m2(const short * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_i16m2(short *rs1, vuint8m1_t rs2, vint16m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_i16m2(short *rs1, vuint8m1_t rs2, vint16m2_t vs3, size_t vl);
vint16m2_t __riscv_vluxei16_v_i16m2(const short * rs1, vuint16m2_t rs2, size_t vl);
vint16m2_t __riscv_vloxei16_v_i16m2(const short * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_i16m2(short *rs1, vuint16m2_t rs2, vint16m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_i16m2(short *rs1, vuint16m2_t rs2, vint16m2_t vs3, size_t vl);
vint16m2_t __riscv_vluxei32_v_i16m2(const short * rs1, vuint32m4_t rs2, size_t vl);
vint16m2_t __riscv_vloxei32_v_i16m2(const short * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_i16m2(short *rs1, vuint32m4_t rs2, vint16m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_i16m2(short *rs1, vuint32m4_t rs2, vint16m2_t vs3, size_t vl);
vint16m2_t __riscv_vluxei64_v_i16m2(const short * rs1, vuint64m8_t rs2, size_t vl);
vint16m2_t __riscv_vloxei64_v_i16m2(const short * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_i16m2(short *rs1, vuint64m8_t rs2, vint16m2_t vs3, size_t vl);
void __riscv_vsoxei64_v_i16m2(short *rs1, vuint64m8_t rs2, vint16m2_t vs3, size_t vl);
vint16m2_t __riscv_vl2re16_v_i16m2(const short * rs1);
void __riscv_vs2r_v_i16m2(short *rs1, vint16m2_t vs3);
vuint16m4_t __riscv_vle16ff_v_u16m4(const unsigned short * rs1, size_t *new_vl, size_t vl);
vuint16m4_t __riscv_vluxei8_v_u16m4(const unsigned short * rs1, vuint8m2_t rs2, size_t vl);
vuint16m4_t __riscv_vloxei8_v_u16m4(const unsigned short * rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxei8_v_u16m4(unsigned short *rs1, vuint8m2_t rs2, vuint16m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_u16m4(unsigned short *rs1, vuint8m2_t rs2, vuint16m4_t vs3, size_t vl);
vuint16m4_t __riscv_vluxei16_v_u16m4(const unsigned short * rs1, vuint16m4_t rs2, size_t vl);
vuint16m4_t __riscv_vloxei16_v_u16m4(const unsigned short * rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxei16_v_u16m4(unsigned short *rs1, vuint16m4_t rs2, vuint16m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_u16m4(unsigned short *rs1, vuint16m4_t rs2, vuint16m4_t vs3, size_t vl);
vuint16m4_t __riscv_vluxei32_v_u16m4(const unsigned short * rs1, vuint32m8_t rs2, size_t vl);
vuint16m4_t __riscv_vloxei32_v_u16m4(const unsigned short * rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxei32_v_u16m4(unsigned short *rs1, vuint32m8_t rs2, vuint16m4_t vs3, size_t vl);
void __riscv_vsoxei32_v_u16m4(unsigned short *rs1, vuint32m8_t rs2, vuint16m4_t vs3, size_t vl);
vuint16m4_t __riscv_vl4re16_v_u16m4(const unsigned short * rs1);
void __riscv_vs4r_v_u16m4(unsigned short *rs1, vuint16m4_t vs3);
vint16m4_t __riscv_vle16ff_v_i16m4(const short * rs1, size_t *new_vl, size_t vl);
vint16m4_t __riscv_vluxei8_v_i16m4(const short * rs1, vuint8m2_t rs2, size_t vl);
vint16m4_t __riscv_vloxei8_v_i16m4(const short * rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxei8_v_i16m4(short *rs1, vuint8m2_t rs2, vint16m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_i16m4(short *rs1, vuint8m2_t rs2, vint16m4_t vs3, size_t vl);
vint16m4_t __riscv_vluxei16_v_i16m4(const short * rs1, vuint16m4_t rs2, size_t vl);
vint16m4_t __riscv_vloxei16_v_i16m4(const short * rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxei16_v_i16m4(short *rs1, vuint16m4_t rs2, vint16m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_i16m4(short *rs1, vuint16m4_t rs2, vint16m4_t vs3, size_t vl);
vint16m4_t __riscv_vluxei32_v_i16m4(const short * rs1, vuint32m8_t rs2, size_t vl);
vint16m4_t __riscv_vloxei32_v_i16m4(const short * rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxei32_v_i16m4(short *rs1, vuint32m8_t rs2, vint16m4_t vs3, size_t vl);
void __riscv_vsoxei32_v_i16m4(short *rs1, vuint32m8_t rs2, vint16m4_t vs3, size_t vl);
vint16m4_t __riscv_vl4re16_v_i16m4(const short * rs1);
void __riscv_vs4r_v_i16m4(short *rs1, vint16m4_t vs3);
vuint16m8_t __riscv_vle16ff_v_u16m8(const unsigned short * rs1, size_t *new_vl, size_t vl);
vuint16m8_t __riscv_vluxei8_v_u16m8(const unsigned short * rs1, vuint8m4_t rs2, size_t vl);
vuint16m8_t __riscv_vloxei8_v_u16m8(const unsigned short * rs1, vuint8m4_t rs2, size_t vl);
void __riscv_vsuxei8_v_u16m8(unsigned short *rs1, vuint8m4_t rs2, vuint16m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_u16m8(unsigned short *rs1, vuint8m4_t rs2, vuint16m8_t vs3, size_t vl);
vuint16m8_t __riscv_vluxei16_v_u16m8(const unsigned short * rs1, vuint16m8_t rs2, size_t vl);
vuint16m8_t __riscv_vloxei16_v_u16m8(const unsigned short * rs1, vuint16m8_t rs2, size_t vl);
void __riscv_vsuxei16_v_u16m8(unsigned short *rs1, vuint16m8_t rs2, vuint16m8_t vs3, size_t vl);
void __riscv_vsoxei16_v_u16m8(unsigned short *rs1, vuint16m8_t rs2, vuint16m8_t vs3, size_t vl);
vuint16m8_t __riscv_vl8re16_v_u16m8(const unsigned short * rs1);
void __riscv_vs8r_v_u16m8(unsigned short *rs1, vuint16m8_t vs3);
vint16m8_t __riscv_vle16ff_v_i16m8(const short * rs1, size_t *new_vl, size_t vl);
vint16m8_t __riscv_vluxei8_v_i16m8(const short * rs1, vuint8m4_t rs2, size_t vl);
vint16m8_t __riscv_vloxei8_v_i16m8(const short * rs1, vuint8m4_t rs2, size_t vl);
void __riscv_vsuxei8_v_i16m8(short *rs1, vuint8m4_t rs2, vint16m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_i16m8(short *rs1, vuint8m4_t rs2, vint16m8_t vs3, size_t vl);
vint16m8_t __riscv_vluxei16_v_i16m8(const short * rs1, vuint16m8_t rs2, size_t vl);
vint16m8_t __riscv_vloxei16_v_i16m8(const short * rs1, vuint16m8_t rs2, size_t vl);
void __riscv_vsuxei16_v_i16m8(short *rs1, vuint16m8_t rs2, vint16m8_t vs3, size_t vl);
void __riscv_vsoxei16_v_i16m8(short *rs1, vuint16m8_t rs2, vint16m8_t vs3, size_t vl);
vint16m8_t __riscv_vl8re16_v_i16m8(const short * rs1);
void __riscv_vs8r_v_i16m8(short *rs1, vint16m8_t vs3);
vuint32mf2_t __riscv_vle32ff_v_u32mf2(const unsigned int * rs1, size_t *new_vl, size_t vl);
vuint32mf2_t __riscv_vluxei8_v_u32mf2(const unsigned int * rs1, vuint8mf8_t rs2, size_t vl);
vuint32mf2_t __riscv_vloxei8_v_u32mf2(const unsigned int * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_u32mf2(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2_t vs3, size_t vl);
void __riscv_vsoxei8_v_u32mf2(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2_t vs3, size_t vl);
vuint32mf2_t __riscv_vluxei16_v_u32mf2(const unsigned int * rs1, vuint16mf4_t rs2, size_t vl);
vuint32mf2_t __riscv_vloxei16_v_u32mf2(const unsigned int * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_u32mf2(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2_t vs3, size_t vl);
void __riscv_vsoxei16_v_u32mf2(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2_t vs3, size_t vl);
vuint32mf2_t __riscv_vluxei32_v_u32mf2(const unsigned int * rs1, vuint32mf2_t rs2, size_t vl);
vuint32mf2_t __riscv_vloxei32_v_u32mf2(const unsigned int * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_u32mf2(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2_t vs3, size_t vl);
void __riscv_vsoxei32_v_u32mf2(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2_t vs3, size_t vl);
vuint32mf2_t __riscv_vluxei64_v_u32mf2(const unsigned int * rs1, vuint64m1_t rs2, size_t vl);
vuint32mf2_t __riscv_vloxei64_v_u32mf2(const unsigned int * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_u32mf2(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2_t vs3, size_t vl);
void __riscv_vsoxei64_v_u32mf2(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2_t vs3, size_t vl);
vint32mf2_t __riscv_vle32ff_v_i32mf2(const int * rs1, size_t *new_vl, size_t vl);
vint32mf2_t __riscv_vluxei8_v_i32mf2(const int * rs1, vuint8mf8_t rs2, size_t vl);
vint32mf2_t __riscv_vloxei8_v_i32mf2(const int * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_i32mf2(int *rs1, vuint8mf8_t rs2, vint32mf2_t vs3, size_t vl);
void __riscv_vsoxei8_v_i32mf2(int *rs1, vuint8mf8_t rs2, vint32mf2_t vs3, size_t vl);
vint32mf2_t __riscv_vluxei16_v_i32mf2(const int * rs1, vuint16mf4_t rs2, size_t vl);
vint32mf2_t __riscv_vloxei16_v_i32mf2(const int * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_i32mf2(int *rs1, vuint16mf4_t rs2, vint32mf2_t vs3, size_t vl);
void __riscv_vsoxei16_v_i32mf2(int *rs1, vuint16mf4_t rs2, vint32mf2_t vs3, size_t vl);
vint32mf2_t __riscv_vluxei32_v_i32mf2(const int * rs1, vuint32mf2_t rs2, size_t vl);
vint32mf2_t __riscv_vloxei32_v_i32mf2(const int * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_i32mf2(int *rs1, vuint32mf2_t rs2, vint32mf2_t vs3, size_t vl);
void __riscv_vsoxei32_v_i32mf2(int *rs1, vuint32mf2_t rs2, vint32mf2_t vs3, size_t vl);
vint32mf2_t __riscv_vluxei64_v_i32mf2(const int * rs1, vuint64m1_t rs2, size_t vl);
vint32mf2_t __riscv_vloxei64_v_i32mf2(const int * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_i32mf2(int *rs1, vuint64m1_t rs2, vint32mf2_t vs3, size_t vl);
void __riscv_vsoxei64_v_i32mf2(int *rs1, vuint64m1_t rs2, vint32mf2_t vs3, size_t vl);
vfloat32mf2_t __riscv_vle32ff_v_f32mf2(const float * rs1, size_t *new_vl, size_t vl);
vfloat32mf2_t __riscv_vluxei8_v_f32mf2(const float * rs1, vuint8mf8_t rs2, size_t vl);
vfloat32mf2_t __riscv_vloxei8_v_f32mf2(const float * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_f32mf2(float *rs1, vuint8mf8_t rs2, vfloat32mf2_t vs3, size_t vl);
void __riscv_vsoxei8_v_f32mf2(float *rs1, vuint8mf8_t rs2, vfloat32mf2_t vs3, size_t vl);
vfloat32mf2_t __riscv_vluxei16_v_f32mf2(const float * rs1, vuint16mf4_t rs2, size_t vl);
vfloat32mf2_t __riscv_vloxei16_v_f32mf2(const float * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_f32mf2(float *rs1, vuint16mf4_t rs2, vfloat32mf2_t vs3, size_t vl);
void __riscv_vsoxei16_v_f32mf2(float *rs1, vuint16mf4_t rs2, vfloat32mf2_t vs3, size_t vl);
vfloat32mf2_t __riscv_vluxei32_v_f32mf2(const float * rs1, vuint32mf2_t rs2, size_t vl);
vfloat32mf2_t __riscv_vloxei32_v_f32mf2(const float * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_f32mf2(float *rs1, vuint32mf2_t rs2, vfloat32mf2_t vs3, size_t vl);
void __riscv_vsoxei32_v_f32mf2(float *rs1, vuint32mf2_t rs2, vfloat32mf2_t vs3, size_t vl);
vfloat32mf2_t __riscv_vluxei64_v_f32mf2(const float * rs1, vuint64m1_t rs2, size_t vl);
vfloat32mf2_t __riscv_vloxei64_v_f32mf2(const float * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_f32mf2(float *rs1, vuint64m1_t rs2, vfloat32mf2_t vs3, size_t vl);
void __riscv_vsoxei64_v_f32mf2(float *rs1, vuint64m1_t rs2, vfloat32mf2_t vs3, size_t vl);
vuint32m1_t __riscv_vle32ff_v_u32m1(const unsigned int * rs1, size_t *new_vl, size_t vl);
vuint32m1_t __riscv_vluxei8_v_u32m1(const unsigned int * rs1, vuint8mf4_t rs2, size_t vl);
vuint32m1_t __riscv_vloxei8_v_u32m1(const unsigned int * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_u32m1(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_u32m1(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1_t vs3, size_t vl);
vuint32m1_t __riscv_vluxei16_v_u32m1(const unsigned int * rs1, vuint16mf2_t rs2, size_t vl);
vuint32m1_t __riscv_vloxei16_v_u32m1(const unsigned int * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_u32m1(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_u32m1(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1_t vs3, size_t vl);
vuint32m1_t __riscv_vluxei32_v_u32m1(const unsigned int * rs1, vuint32m1_t rs2, size_t vl);
vuint32m1_t __riscv_vloxei32_v_u32m1(const unsigned int * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_u32m1(unsigned int *rs1, vuint32m1_t rs2, vuint32m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_u32m1(unsigned int *rs1, vuint32m1_t rs2, vuint32m1_t vs3, size_t vl);
vuint32m1_t __riscv_vluxei64_v_u32m1(const unsigned int * rs1, vuint64m2_t rs2, size_t vl);
vuint32m1_t __riscv_vloxei64_v_u32m1(const unsigned int * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_u32m1(unsigned int *rs1, vuint64m2_t rs2, vuint32m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_u32m1(unsigned int *rs1, vuint64m2_t rs2, vuint32m1_t vs3, size_t vl);
vuint32m1_t __riscv_vl1re32_v_u32m1(const unsigned int * rs1);
void __riscv_vs1r_v_u32m1(unsigned int *rs1, vuint32m1_t vs3);
vint32m1_t __riscv_vle32ff_v_i32m1(const int * rs1, size_t *new_vl, size_t vl);
vint32m1_t __riscv_vluxei8_v_i32m1(const int * rs1, vuint8mf4_t rs2, size_t vl);
vint32m1_t __riscv_vloxei8_v_i32m1(const int * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_i32m1(int *rs1, vuint8mf4_t rs2, vint32m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_i32m1(int *rs1, vuint8mf4_t rs2, vint32m1_t vs3, size_t vl);
vint32m1_t __riscv_vluxei16_v_i32m1(const int * rs1, vuint16mf2_t rs2, size_t vl);
vint32m1_t __riscv_vloxei16_v_i32m1(const int * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_i32m1(int *rs1, vuint16mf2_t rs2, vint32m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_i32m1(int *rs1, vuint16mf2_t rs2, vint32m1_t vs3, size_t vl);
vint32m1_t __riscv_vluxei32_v_i32m1(const int * rs1, vuint32m1_t rs2, size_t vl);
vint32m1_t __riscv_vloxei32_v_i32m1(const int * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_i32m1(int *rs1, vuint32m1_t rs2, vint32m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_i32m1(int *rs1, vuint32m1_t rs2, vint32m1_t vs3, size_t vl);
vint32m1_t __riscv_vluxei64_v_i32m1(const int * rs1, vuint64m2_t rs2, size_t vl);
vint32m1_t __riscv_vloxei64_v_i32m1(const int * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_i32m1(int *rs1, vuint64m2_t rs2, vint32m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_i32m1(int *rs1, vuint64m2_t rs2, vint32m1_t vs3, size_t vl);
vint32m1_t __riscv_vl1re32_v_i32m1(const int * rs1);
void __riscv_vs1r_v_i32m1(int *rs1, vint32m1_t vs3);
vfloat32m1_t __riscv_vle32ff_v_f32m1(const float * rs1, size_t *new_vl, size_t vl);
vfloat32m1_t __riscv_vluxei8_v_f32m1(const float * rs1, vuint8mf4_t rs2, size_t vl);
vfloat32m1_t __riscv_vloxei8_v_f32m1(const float * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_f32m1(float *rs1, vuint8mf4_t rs2, vfloat32m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_f32m1(float *rs1, vuint8mf4_t rs2, vfloat32m1_t vs3, size_t vl);
vfloat32m1_t __riscv_vluxei16_v_f32m1(const float * rs1, vuint16mf2_t rs2, size_t vl);
vfloat32m1_t __riscv_vloxei16_v_f32m1(const float * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_f32m1(float *rs1, vuint16mf2_t rs2, vfloat32m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_f32m1(float *rs1, vuint16mf2_t rs2, vfloat32m1_t vs3, size_t vl);
vfloat32m1_t __riscv_vluxei32_v_f32m1(const float * rs1, vuint32m1_t rs2, size_t vl);
vfloat32m1_t __riscv_vloxei32_v_f32m1(const float * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_f32m1(float *rs1, vuint32m1_t rs2, vfloat32m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_f32m1(float *rs1, vuint32m1_t rs2, vfloat32m1_t vs3, size_t vl);
vfloat32m1_t __riscv_vluxei64_v_f32m1(const float * rs1, vuint64m2_t rs2, size_t vl);
vfloat32m1_t __riscv_vloxei64_v_f32m1(const float * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_f32m1(float *rs1, vuint64m2_t rs2, vfloat32m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_f32m1(float *rs1, vuint64m2_t rs2, vfloat32m1_t vs3, size_t vl);
vfloat32m1_t __riscv_vl1re32_v_f32m1(const float * rs1);
void __riscv_vs1r_v_f32m1(float *rs1, vfloat32m1_t vs3);
vuint32m2_t __riscv_vle32ff_v_u32m2(const unsigned int * rs1, size_t *new_vl, size_t vl);
vuint32m2_t __riscv_vluxei8_v_u32m2(const unsigned int * rs1, vuint8mf2_t rs2, size_t vl);
vuint32m2_t __riscv_vloxei8_v_u32m2(const unsigned int * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_u32m2(unsigned int *rs1, vuint8mf2_t rs2, vuint32m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_u32m2(unsigned int *rs1, vuint8mf2_t rs2, vuint32m2_t vs3, size_t vl);
vuint32m2_t __riscv_vluxei16_v_u32m2(const unsigned int * rs1, vuint16m1_t rs2, size_t vl);
vuint32m2_t __riscv_vloxei16_v_u32m2(const unsigned int * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_u32m2(unsigned int *rs1, vuint16m1_t rs2, vuint32m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_u32m2(unsigned int *rs1, vuint16m1_t rs2, vuint32m2_t vs3, size_t vl);
vuint32m2_t __riscv_vluxei32_v_u32m2(const unsigned int * rs1, vuint32m2_t rs2, size_t vl);
vuint32m2_t __riscv_vloxei32_v_u32m2(const unsigned int * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_u32m2(unsigned int *rs1, vuint32m2_t rs2, vuint32m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_u32m2(unsigned int *rs1, vuint32m2_t rs2, vuint32m2_t vs3, size_t vl);
vuint32m2_t __riscv_vluxei64_v_u32m2(const unsigned int * rs1, vuint64m4_t rs2, size_t vl);
vuint32m2_t __riscv_vloxei64_v_u32m2(const unsigned int * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_u32m2(unsigned int *rs1, vuint64m4_t rs2, vuint32m2_t vs3, size_t vl);
void __riscv_vsoxei64_v_u32m2(unsigned int *rs1, vuint64m4_t rs2, vuint32m2_t vs3, size_t vl);
vuint32m2_t __riscv_vl2re32_v_u32m2(const unsigned int * rs1);
void __riscv_vs2r_v_u32m2(unsigned int *rs1, vuint32m2_t vs3);
vint32m2_t __riscv_vle32ff_v_i32m2(const int * rs1, size_t *new_vl, size_t vl);
vint32m2_t __riscv_vluxei8_v_i32m2(const int * rs1, vuint8mf2_t rs2, size_t vl);
vint32m2_t __riscv_vloxei8_v_i32m2(const int * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_i32m2(int *rs1, vuint8mf2_t rs2, vint32m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_i32m2(int *rs1, vuint8mf2_t rs2, vint32m2_t vs3, size_t vl);
vint32m2_t __riscv_vluxei16_v_i32m2(const int * rs1, vuint16m1_t rs2, size_t vl);
vint32m2_t __riscv_vloxei16_v_i32m2(const int * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_i32m2(int *rs1, vuint16m1_t rs2, vint32m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_i32m2(int *rs1, vuint16m1_t rs2, vint32m2_t vs3, size_t vl);
vint32m2_t __riscv_vluxei32_v_i32m2(const int * rs1, vuint32m2_t rs2, size_t vl);
vint32m2_t __riscv_vloxei32_v_i32m2(const int * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_i32m2(int *rs1, vuint32m2_t rs2, vint32m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_i32m2(int *rs1, vuint32m2_t rs2, vint32m2_t vs3, size_t vl);
vint32m2_t __riscv_vluxei64_v_i32m2(const int * rs1, vuint64m4_t rs2, size_t vl);
vint32m2_t __riscv_vloxei64_v_i32m2(const int * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_i32m2(int *rs1, vuint64m4_t rs2, vint32m2_t vs3, size_t vl);
void __riscv_vsoxei64_v_i32m2(int *rs1, vuint64m4_t rs2, vint32m2_t vs3, size_t vl);
vint32m2_t __riscv_vl2re32_v_i32m2(const int * rs1);
void __riscv_vs2r_v_i32m2(int *rs1, vint32m2_t vs3);
vfloat32m2_t __riscv_vle32ff_v_f32m2(const float * rs1, size_t *new_vl, size_t vl);
vfloat32m2_t __riscv_vluxei8_v_f32m2(const float * rs1, vuint8mf2_t rs2, size_t vl);
vfloat32m2_t __riscv_vloxei8_v_f32m2(const float * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_f32m2(float *rs1, vuint8mf2_t rs2, vfloat32m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_f32m2(float *rs1, vuint8mf2_t rs2, vfloat32m2_t vs3, size_t vl);
vfloat32m2_t __riscv_vluxei16_v_f32m2(const float * rs1, vuint16m1_t rs2, size_t vl);
vfloat32m2_t __riscv_vloxei16_v_f32m2(const float * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_f32m2(float *rs1, vuint16m1_t rs2, vfloat32m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_f32m2(float *rs1, vuint16m1_t rs2, vfloat32m2_t vs3, size_t vl);
vfloat32m2_t __riscv_vluxei32_v_f32m2(const float * rs1, vuint32m2_t rs2, size_t vl);
vfloat32m2_t __riscv_vloxei32_v_f32m2(const float * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_f32m2(float *rs1, vuint32m2_t rs2, vfloat32m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_f32m2(float *rs1, vuint32m2_t rs2, vfloat32m2_t vs3, size_t vl);
vfloat32m2_t __riscv_vluxei64_v_f32m2(const float * rs1, vuint64m4_t rs2, size_t vl);
vfloat32m2_t __riscv_vloxei64_v_f32m2(const float * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_f32m2(float *rs1, vuint64m4_t rs2, vfloat32m2_t vs3, size_t vl);
void __riscv_vsoxei64_v_f32m2(float *rs1, vuint64m4_t rs2, vfloat32m2_t vs3, size_t vl);
vfloat32m2_t __riscv_vl2re32_v_f32m2(const float * rs1);
void __riscv_vs2r_v_f32m2(float *rs1, vfloat32m2_t vs3);
vuint32m4_t __riscv_vle32ff_v_u32m4(const unsigned int * rs1, size_t *new_vl, size_t vl);
vuint32m4_t __riscv_vluxei8_v_u32m4(const unsigned int * rs1, vuint8m1_t rs2, size_t vl);
vuint32m4_t __riscv_vloxei8_v_u32m4(const unsigned int * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_u32m4(unsigned int *rs1, vuint8m1_t rs2, vuint32m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_u32m4(unsigned int *rs1, vuint8m1_t rs2, vuint32m4_t vs3, size_t vl);
vuint32m4_t __riscv_vluxei16_v_u32m4(const unsigned int * rs1, vuint16m2_t rs2, size_t vl);
vuint32m4_t __riscv_vloxei16_v_u32m4(const unsigned int * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_u32m4(unsigned int *rs1, vuint16m2_t rs2, vuint32m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_u32m4(unsigned int *rs1, vuint16m2_t rs2, vuint32m4_t vs3, size_t vl);
vuint32m4_t __riscv_vluxei32_v_u32m4(const unsigned int * rs1, vuint32m4_t rs2, size_t vl);
vuint32m4_t __riscv_vloxei32_v_u32m4(const unsigned int * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_u32m4(unsigned int *rs1, vuint32m4_t rs2, vuint32m4_t vs3, size_t vl);
void __riscv_vsoxei32_v_u32m4(unsigned int *rs1, vuint32m4_t rs2, vuint32m4_t vs3, size_t vl);
vuint32m4_t __riscv_vluxei64_v_u32m4(const unsigned int * rs1, vuint64m8_t rs2, size_t vl);
vuint32m4_t __riscv_vloxei64_v_u32m4(const unsigned int * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_u32m4(unsigned int *rs1, vuint64m8_t rs2, vuint32m4_t vs3, size_t vl);
void __riscv_vsoxei64_v_u32m4(unsigned int *rs1, vuint64m8_t rs2, vuint32m4_t vs3, size_t vl);
vuint32m4_t __riscv_vl4re32_v_u32m4(const unsigned int * rs1);
void __riscv_vs4r_v_u32m4(unsigned int *rs1, vuint32m4_t vs3);
vint32m4_t __riscv_vle32ff_v_i32m4(const int * rs1, size_t *new_vl, size_t vl);
vint32m4_t __riscv_vluxei8_v_i32m4(const int * rs1, vuint8m1_t rs2, size_t vl);
vint32m4_t __riscv_vloxei8_v_i32m4(const int * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_i32m4(int *rs1, vuint8m1_t rs2, vint32m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_i32m4(int *rs1, vuint8m1_t rs2, vint32m4_t vs3, size_t vl);
vint32m4_t __riscv_vluxei16_v_i32m4(const int * rs1, vuint16m2_t rs2, size_t vl);
vint32m4_t __riscv_vloxei16_v_i32m4(const int * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_i32m4(int *rs1, vuint16m2_t rs2, vint32m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_i32m4(int *rs1, vuint16m2_t rs2, vint32m4_t vs3, size_t vl);
vint32m4_t __riscv_vluxei32_v_i32m4(const int * rs1, vuint32m4_t rs2, size_t vl);
vint32m4_t __riscv_vloxei32_v_i32m4(const int * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_i32m4(int *rs1, vuint32m4_t rs2, vint32m4_t vs3, size_t vl);
void __riscv_vsoxei32_v_i32m4(int *rs1, vuint32m4_t rs2, vint32m4_t vs3, size_t vl);
vint32m4_t __riscv_vluxei64_v_i32m4(const int * rs1, vuint64m8_t rs2, size_t vl);
vint32m4_t __riscv_vloxei64_v_i32m4(const int * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_i32m4(int *rs1, vuint64m8_t rs2, vint32m4_t vs3, size_t vl);
void __riscv_vsoxei64_v_i32m4(int *rs1, vuint64m8_t rs2, vint32m4_t vs3, size_t vl);
vint32m4_t __riscv_vl4re32_v_i32m4(const int * rs1);
void __riscv_vs4r_v_i32m4(int *rs1, vint32m4_t vs3);
vfloat32m4_t __riscv_vle32ff_v_f32m4(const float * rs1, size_t *new_vl, size_t vl);
vfloat32m4_t __riscv_vluxei8_v_f32m4(const float * rs1, vuint8m1_t rs2, size_t vl);
vfloat32m4_t __riscv_vloxei8_v_f32m4(const float * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_f32m4(float *rs1, vuint8m1_t rs2, vfloat32m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_f32m4(float *rs1, vuint8m1_t rs2, vfloat32m4_t vs3, size_t vl);
vfloat32m4_t __riscv_vluxei16_v_f32m4(const float * rs1, vuint16m2_t rs2, size_t vl);
vfloat32m4_t __riscv_vloxei16_v_f32m4(const float * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_f32m4(float *rs1, vuint16m2_t rs2, vfloat32m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_f32m4(float *rs1, vuint16m2_t rs2, vfloat32m4_t vs3, size_t vl);
vfloat32m4_t __riscv_vluxei32_v_f32m4(const float * rs1, vuint32m4_t rs2, size_t vl);
vfloat32m4_t __riscv_vloxei32_v_f32m4(const float * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_f32m4(float *rs1, vuint32m4_t rs2, vfloat32m4_t vs3, size_t vl);
void __riscv_vsoxei32_v_f32m4(float *rs1, vuint32m4_t rs2, vfloat32m4_t vs3, size_t vl);
vfloat32m4_t __riscv_vluxei64_v_f32m4(const float * rs1, vuint64m8_t rs2, size_t vl);
vfloat32m4_t __riscv_vloxei64_v_f32m4(const float * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_f32m4(float *rs1, vuint64m8_t rs2, vfloat32m4_t vs3, size_t vl);
void __riscv_vsoxei64_v_f32m4(float *rs1, vuint64m8_t rs2, vfloat32m4_t vs3, size_t vl);
vfloat32m4_t __riscv_vl4re32_v_f32m4(const float * rs1);
void __riscv_vs4r_v_f32m4(float *rs1, vfloat32m4_t vs3);
vuint32m8_t __riscv_vle32ff_v_u32m8(const unsigned int * rs1, size_t *new_vl, size_t vl);
vuint32m8_t __riscv_vluxei8_v_u32m8(const unsigned int * rs1, vuint8m2_t rs2, size_t vl);
vuint32m8_t __riscv_vloxei8_v_u32m8(const unsigned int * rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxei8_v_u32m8(unsigned int *rs1, vuint8m2_t rs2, vuint32m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_u32m8(unsigned int *rs1, vuint8m2_t rs2, vuint32m8_t vs3, size_t vl);
vuint32m8_t __riscv_vluxei16_v_u32m8(const unsigned int * rs1, vuint16m4_t rs2, size_t vl);
vuint32m8_t __riscv_vloxei16_v_u32m8(const unsigned int * rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxei16_v_u32m8(unsigned int *rs1, vuint16m4_t rs2, vuint32m8_t vs3, size_t vl);
void __riscv_vsoxei16_v_u32m8(unsigned int *rs1, vuint16m4_t rs2, vuint32m8_t vs3, size_t vl);
vuint32m8_t __riscv_vluxei32_v_u32m8(const unsigned int * rs1, vuint32m8_t rs2, size_t vl);
vuint32m8_t __riscv_vloxei32_v_u32m8(const unsigned int * rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxei32_v_u32m8(unsigned int *rs1, vuint32m8_t rs2, vuint32m8_t vs3, size_t vl);
void __riscv_vsoxei32_v_u32m8(unsigned int *rs1, vuint32m8_t rs2, vuint32m8_t vs3, size_t vl);
vuint32m8_t __riscv_vl8re32_v_u32m8(const unsigned int * rs1);
void __riscv_vs8r_v_u32m8(unsigned int *rs1, vuint32m8_t vs3);
vint32m8_t __riscv_vle32ff_v_i32m8(const int * rs1, size_t *new_vl, size_t vl);
vint32m8_t __riscv_vluxei8_v_i32m8(const int * rs1, vuint8m2_t rs2, size_t vl);
vint32m8_t __riscv_vloxei8_v_i32m8(const int * rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxei8_v_i32m8(int *rs1, vuint8m2_t rs2, vint32m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_i32m8(int *rs1, vuint8m2_t rs2, vint32m8_t vs3, size_t vl);
vint32m8_t __riscv_vluxei16_v_i32m8(const int * rs1, vuint16m4_t rs2, size_t vl);
vint32m8_t __riscv_vloxei16_v_i32m8(const int * rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxei16_v_i32m8(int *rs1, vuint16m4_t rs2, vint32m8_t vs3, size_t vl);
void __riscv_vsoxei16_v_i32m8(int *rs1, vuint16m4_t rs2, vint32m8_t vs3, size_t vl);
vint32m8_t __riscv_vluxei32_v_i32m8(const int * rs1, vuint32m8_t rs2, size_t vl);
vint32m8_t __riscv_vloxei32_v_i32m8(const int * rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxei32_v_i32m8(int *rs1, vuint32m8_t rs2, vint32m8_t vs3, size_t vl);
void __riscv_vsoxei32_v_i32m8(int *rs1, vuint32m8_t rs2, vint32m8_t vs3, size_t vl);
vint32m8_t __riscv_vl8re32_v_i32m8(const int * rs1);
void __riscv_vs8r_v_i32m8(int *rs1, vint32m8_t vs3);
vfloat32m8_t __riscv_vle32ff_v_f32m8(const float * rs1, size_t *new_vl, size_t vl);
vfloat32m8_t __riscv_vluxei8_v_f32m8(const float * rs1, vuint8m2_t rs2, size_t vl);
vfloat32m8_t __riscv_vloxei8_v_f32m8(const float * rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxei8_v_f32m8(float *rs1, vuint8m2_t rs2, vfloat32m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_f32m8(float *rs1, vuint8m2_t rs2, vfloat32m8_t vs3, size_t vl);
vfloat32m8_t __riscv_vluxei16_v_f32m8(const float * rs1, vuint16m4_t rs2, size_t vl);
vfloat32m8_t __riscv_vloxei16_v_f32m8(const float * rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxei16_v_f32m8(float *rs1, vuint16m4_t rs2, vfloat32m8_t vs3, size_t vl);
void __riscv_vsoxei16_v_f32m8(float *rs1, vuint16m4_t rs2, vfloat32m8_t vs3, size_t vl);
vfloat32m8_t __riscv_vluxei32_v_f32m8(const float * rs1, vuint32m8_t rs2, size_t vl);
vfloat32m8_t __riscv_vloxei32_v_f32m8(const float * rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxei32_v_f32m8(float *rs1, vuint32m8_t rs2, vfloat32m8_t vs3, size_t vl);
void __riscv_vsoxei32_v_f32m8(float *rs1, vuint32m8_t rs2, vfloat32m8_t vs3, size_t vl);
vfloat32m8_t __riscv_vl8re32_v_f32m8(const float * rs1);
void __riscv_vs8r_v_f32m8(float *rs1, vfloat32m8_t vs3);
vuint64m1_t __riscv_vle64ff_v_u64m1(const uint64_t * rs1, size_t *new_vl, size_t vl);
vuint64m1_t __riscv_vluxei8_v_u64m1(const uint64_t * rs1, vuint8mf8_t rs2, size_t vl);
vuint64m1_t __riscv_vloxei8_v_u64m1(const uint64_t * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_u64m1(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_u64m1(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1_t vs3, size_t vl);
vuint64m1_t __riscv_vluxei16_v_u64m1(const uint64_t * rs1, vuint16mf4_t rs2, size_t vl);
vuint64m1_t __riscv_vloxei16_v_u64m1(const uint64_t * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_u64m1(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_u64m1(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1_t vs3, size_t vl);
vuint64m1_t __riscv_vluxei32_v_u64m1(const uint64_t * rs1, vuint32mf2_t rs2, size_t vl);
vuint64m1_t __riscv_vloxei32_v_u64m1(const uint64_t * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_u64m1(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_u64m1(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1_t vs3, size_t vl);
vuint64m1_t __riscv_vluxei64_v_u64m1(const uint64_t * rs1, vuint64m1_t rs2, size_t vl);
vuint64m1_t __riscv_vloxei64_v_u64m1(const uint64_t * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_u64m1(uint64_t *rs1, vuint64m1_t rs2, vuint64m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_u64m1(uint64_t *rs1, vuint64m1_t rs2, vuint64m1_t vs3, size_t vl);
vuint64m1_t __riscv_vl1re64_v_u64m1(const uint64_t * rs1);
void __riscv_vs1r_v_u64m1(uint64_t *rs1, vuint64m1_t vs3);
vint64m1_t __riscv_vle64ff_v_i64m1(const int64_t * rs1, size_t *new_vl, size_t vl);
vint64m1_t __riscv_vluxei8_v_i64m1(const int64_t * rs1, vuint8mf8_t rs2, size_t vl);
vint64m1_t __riscv_vloxei8_v_i64m1(const int64_t * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_i64m1(int64_t *rs1, vuint8mf8_t rs2, vint64m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_i64m1(int64_t *rs1, vuint8mf8_t rs2, vint64m1_t vs3, size_t vl);
vint64m1_t __riscv_vluxei16_v_i64m1(const int64_t * rs1, vuint16mf4_t rs2, size_t vl);
vint64m1_t __riscv_vloxei16_v_i64m1(const int64_t * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_i64m1(int64_t *rs1, vuint16mf4_t rs2, vint64m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_i64m1(int64_t *rs1, vuint16mf4_t rs2, vint64m1_t vs3, size_t vl);
vint64m1_t __riscv_vluxei32_v_i64m1(const int64_t * rs1, vuint32mf2_t rs2, size_t vl);
vint64m1_t __riscv_vloxei32_v_i64m1(const int64_t * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_i64m1(int64_t *rs1, vuint32mf2_t rs2, vint64m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_i64m1(int64_t *rs1, vuint32mf2_t rs2, vint64m1_t vs3, size_t vl);
vint64m1_t __riscv_vluxei64_v_i64m1(const int64_t * rs1, vuint64m1_t rs2, size_t vl);
vint64m1_t __riscv_vloxei64_v_i64m1(const int64_t * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_i64m1(int64_t *rs1, vuint64m1_t rs2, vint64m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_i64m1(int64_t *rs1, vuint64m1_t rs2, vint64m1_t vs3, size_t vl);
vint64m1_t __riscv_vl1re64_v_i64m1(const int64_t * rs1);
void __riscv_vs1r_v_i64m1(int64_t *rs1, vint64m1_t vs3);
vfloat64m1_t __riscv_vle64ff_v_f64m1(const double * rs1, size_t *new_vl, size_t vl);
vfloat64m1_t __riscv_vluxei8_v_f64m1(const double * rs1, vuint8mf8_t rs2, size_t vl);
vfloat64m1_t __riscv_vloxei8_v_f64m1(const double * rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxei8_v_f64m1(double *rs1, vuint8mf8_t rs2, vfloat64m1_t vs3, size_t vl);
void __riscv_vsoxei8_v_f64m1(double *rs1, vuint8mf8_t rs2, vfloat64m1_t vs3, size_t vl);
vfloat64m1_t __riscv_vluxei16_v_f64m1(const double * rs1, vuint16mf4_t rs2, size_t vl);
vfloat64m1_t __riscv_vloxei16_v_f64m1(const double * rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxei16_v_f64m1(double *rs1, vuint16mf4_t rs2, vfloat64m1_t vs3, size_t vl);
void __riscv_vsoxei16_v_f64m1(double *rs1, vuint16mf4_t rs2, vfloat64m1_t vs3, size_t vl);
vfloat64m1_t __riscv_vluxei32_v_f64m1(const double * rs1, vuint32mf2_t rs2, size_t vl);
vfloat64m1_t __riscv_vloxei32_v_f64m1(const double * rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxei32_v_f64m1(double *rs1, vuint32mf2_t rs2, vfloat64m1_t vs3, size_t vl);
void __riscv_vsoxei32_v_f64m1(double *rs1, vuint32mf2_t rs2, vfloat64m1_t vs3, size_t vl);
vfloat64m1_t __riscv_vluxei64_v_f64m1(const double * rs1, vuint64m1_t rs2, size_t vl);
vfloat64m1_t __riscv_vloxei64_v_f64m1(const double * rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxei64_v_f64m1(double *rs1, vuint64m1_t rs2, vfloat64m1_t vs3, size_t vl);
void __riscv_vsoxei64_v_f64m1(double *rs1, vuint64m1_t rs2, vfloat64m1_t vs3, size_t vl);
vfloat64m1_t __riscv_vl1re64_v_f64m1(const double * rs1);
void __riscv_vs1r_v_f64m1(double *rs1, vfloat64m1_t vs3);
vuint64m2_t __riscv_vle64ff_v_u64m2(const uint64_t * rs1, size_t *new_vl, size_t vl);
vuint64m2_t __riscv_vluxei8_v_u64m2(const uint64_t * rs1, vuint8mf4_t rs2, size_t vl);
vuint64m2_t __riscv_vloxei8_v_u64m2(const uint64_t * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_u64m2(uint64_t *rs1, vuint8mf4_t rs2, vuint64m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_u64m2(uint64_t *rs1, vuint8mf4_t rs2, vuint64m2_t vs3, size_t vl);
vuint64m2_t __riscv_vluxei16_v_u64m2(const uint64_t * rs1, vuint16mf2_t rs2, size_t vl);
vuint64m2_t __riscv_vloxei16_v_u64m2(const uint64_t * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_u64m2(uint64_t *rs1, vuint16mf2_t rs2, vuint64m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_u64m2(uint64_t *rs1, vuint16mf2_t rs2, vuint64m2_t vs3, size_t vl);
vuint64m2_t __riscv_vluxei32_v_u64m2(const uint64_t * rs1, vuint32m1_t rs2, size_t vl);
vuint64m2_t __riscv_vloxei32_v_u64m2(const uint64_t * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_u64m2(uint64_t *rs1, vuint32m1_t rs2, vuint64m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_u64m2(uint64_t *rs1, vuint32m1_t rs2, vuint64m2_t vs3, size_t vl);
vuint64m2_t __riscv_vluxei64_v_u64m2(const uint64_t * rs1, vuint64m2_t rs2, size_t vl);
vuint64m2_t __riscv_vloxei64_v_u64m2(const uint64_t * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_u64m2(uint64_t *rs1, vuint64m2_t rs2, vuint64m2_t vs3, size_t vl);
void __riscv_vsoxei64_v_u64m2(uint64_t *rs1, vuint64m2_t rs2, vuint64m2_t vs3, size_t vl);
vuint64m2_t __riscv_vl2re64_v_u64m2(const uint64_t * rs1);
void __riscv_vs2r_v_u64m2(uint64_t *rs1, vuint64m2_t vs3);
vint64m2_t __riscv_vle64ff_v_i64m2(const int64_t * rs1, size_t *new_vl, size_t vl);
vint64m2_t __riscv_vluxei8_v_i64m2(const int64_t * rs1, vuint8mf4_t rs2, size_t vl);
vint64m2_t __riscv_vloxei8_v_i64m2(const int64_t * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_i64m2(int64_t *rs1, vuint8mf4_t rs2, vint64m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_i64m2(int64_t *rs1, vuint8mf4_t rs2, vint64m2_t vs3, size_t vl);
vint64m2_t __riscv_vluxei16_v_i64m2(const int64_t * rs1, vuint16mf2_t rs2, size_t vl);
vint64m2_t __riscv_vloxei16_v_i64m2(const int64_t * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_i64m2(int64_t *rs1, vuint16mf2_t rs2, vint64m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_i64m2(int64_t *rs1, vuint16mf2_t rs2, vint64m2_t vs3, size_t vl);
vint64m2_t __riscv_vluxei32_v_i64m2(const int64_t * rs1, vuint32m1_t rs2, size_t vl);
vint64m2_t __riscv_vloxei32_v_i64m2(const int64_t * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_i64m2(int64_t *rs1, vuint32m1_t rs2, vint64m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_i64m2(int64_t *rs1, vuint32m1_t rs2, vint64m2_t vs3, size_t vl);
vint64m2_t __riscv_vluxei64_v_i64m2(const int64_t * rs1, vuint64m2_t rs2, size_t vl);
vint64m2_t __riscv_vloxei64_v_i64m2(const int64_t * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_i64m2(int64_t *rs1, vuint64m2_t rs2, vint64m2_t vs3, size_t vl);
void __riscv_vsoxei64_v_i64m2(int64_t *rs1, vuint64m2_t rs2, vint64m2_t vs3, size_t vl);
vint64m2_t __riscv_vl2re64_v_i64m2(const int64_t * rs1);
void __riscv_vs2r_v_i64m2(int64_t *rs1, vint64m2_t vs3);
vfloat64m2_t __riscv_vle64ff_v_f64m2(const double * rs1, size_t *new_vl, size_t vl);
vfloat64m2_t __riscv_vluxei8_v_f64m2(const double * rs1, vuint8mf4_t rs2, size_t vl);
vfloat64m2_t __riscv_vloxei8_v_f64m2(const double * rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxei8_v_f64m2(double *rs1, vuint8mf4_t rs2, vfloat64m2_t vs3, size_t vl);
void __riscv_vsoxei8_v_f64m2(double *rs1, vuint8mf4_t rs2, vfloat64m2_t vs3, size_t vl);
vfloat64m2_t __riscv_vluxei16_v_f64m2(const double * rs1, vuint16mf2_t rs2, size_t vl);
vfloat64m2_t __riscv_vloxei16_v_f64m2(const double * rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxei16_v_f64m2(double *rs1, vuint16mf2_t rs2, vfloat64m2_t vs3, size_t vl);
void __riscv_vsoxei16_v_f64m2(double *rs1, vuint16mf2_t rs2, vfloat64m2_t vs3, size_t vl);
vfloat64m2_t __riscv_vluxei32_v_f64m2(const double * rs1, vuint32m1_t rs2, size_t vl);
vfloat64m2_t __riscv_vloxei32_v_f64m2(const double * rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxei32_v_f64m2(double *rs1, vuint32m1_t rs2, vfloat64m2_t vs3, size_t vl);
void __riscv_vsoxei32_v_f64m2(double *rs1, vuint32m1_t rs2, vfloat64m2_t vs3, size_t vl);
vfloat64m2_t __riscv_vluxei64_v_f64m2(const double * rs1, vuint64m2_t rs2, size_t vl);
vfloat64m2_t __riscv_vloxei64_v_f64m2(const double * rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxei64_v_f64m2(double *rs1, vuint64m2_t rs2, vfloat64m2_t vs3, size_t vl);
void __riscv_vsoxei64_v_f64m2(double *rs1, vuint64m2_t rs2, vfloat64m2_t vs3, size_t vl);
vfloat64m2_t __riscv_vl2re64_v_f64m2(const double * rs1);
void __riscv_vs2r_v_f64m2(double *rs1, vfloat64m2_t vs3);
vuint64m4_t __riscv_vle64ff_v_u64m4(const uint64_t * rs1, size_t *new_vl, size_t vl);
vuint64m4_t __riscv_vluxei8_v_u64m4(const uint64_t * rs1, vuint8mf2_t rs2, size_t vl);
vuint64m4_t __riscv_vloxei8_v_u64m4(const uint64_t * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_u64m4(uint64_t *rs1, vuint8mf2_t rs2, vuint64m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_u64m4(uint64_t *rs1, vuint8mf2_t rs2, vuint64m4_t vs3, size_t vl);
vuint64m4_t __riscv_vluxei16_v_u64m4(const uint64_t * rs1, vuint16m1_t rs2, size_t vl);
vuint64m4_t __riscv_vloxei16_v_u64m4(const uint64_t * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_u64m4(uint64_t *rs1, vuint16m1_t rs2, vuint64m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_u64m4(uint64_t *rs1, vuint16m1_t rs2, vuint64m4_t vs3, size_t vl);
vuint64m4_t __riscv_vluxei32_v_u64m4(const uint64_t * rs1, vuint32m2_t rs2, size_t vl);
vuint64m4_t __riscv_vloxei32_v_u64m4(const uint64_t * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_u64m4(uint64_t *rs1, vuint32m2_t rs2, vuint64m4_t vs3, size_t vl);
void __riscv_vsoxei32_v_u64m4(uint64_t *rs1, vuint32m2_t rs2, vuint64m4_t vs3, size_t vl);
vuint64m4_t __riscv_vluxei64_v_u64m4(const uint64_t * rs1, vuint64m4_t rs2, size_t vl);
vuint64m4_t __riscv_vloxei64_v_u64m4(const uint64_t * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_u64m4(uint64_t *rs1, vuint64m4_t rs2, vuint64m4_t vs3, size_t vl);
void __riscv_vsoxei64_v_u64m4(uint64_t *rs1, vuint64m4_t rs2, vuint64m4_t vs3, size_t vl);
vuint64m4_t __riscv_vl4re64_v_u64m4(const uint64_t * rs1);
void __riscv_vs4r_v_u64m4(uint64_t *rs1, vuint64m4_t vs3);
vint64m4_t __riscv_vle64ff_v_i64m4(const int64_t * rs1, size_t *new_vl, size_t vl);
vint64m4_t __riscv_vluxei8_v_i64m4(const int64_t * rs1, vuint8mf2_t rs2, size_t vl);
vint64m4_t __riscv_vloxei8_v_i64m4(const int64_t * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_i64m4(int64_t *rs1, vuint8mf2_t rs2, vint64m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_i64m4(int64_t *rs1, vuint8mf2_t rs2, vint64m4_t vs3, size_t vl);
vint64m4_t __riscv_vluxei16_v_i64m4(const int64_t * rs1, vuint16m1_t rs2, size_t vl);
vint64m4_t __riscv_vloxei16_v_i64m4(const int64_t * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_i64m4(int64_t *rs1, vuint16m1_t rs2, vint64m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_i64m4(int64_t *rs1, vuint16m1_t rs2, vint64m4_t vs3, size_t vl);
vint64m4_t __riscv_vluxei32_v_i64m4(const int64_t * rs1, vuint32m2_t rs2, size_t vl);
vint64m4_t __riscv_vloxei32_v_i64m4(const int64_t * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_i64m4(int64_t *rs1, vuint32m2_t rs2, vint64m4_t vs3, size_t vl);
void __riscv_vsoxei32_v_i64m4(int64_t *rs1, vuint32m2_t rs2, vint64m4_t vs3, size_t vl);
vint64m4_t __riscv_vluxei64_v_i64m4(const int64_t * rs1, vuint64m4_t rs2, size_t vl);
vint64m4_t __riscv_vloxei64_v_i64m4(const int64_t * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_i64m4(int64_t *rs1, vuint64m4_t rs2, vint64m4_t vs3, size_t vl);
void __riscv_vsoxei64_v_i64m4(int64_t *rs1, vuint64m4_t rs2, vint64m4_t vs3, size_t vl);
vint64m4_t __riscv_vl4re64_v_i64m4(const int64_t * rs1);
void __riscv_vs4r_v_i64m4(int64_t *rs1, vint64m4_t vs3);
vfloat64m4_t __riscv_vle64ff_v_f64m4(const double * rs1, size_t *new_vl, size_t vl);
vfloat64m4_t __riscv_vluxei8_v_f64m4(const double * rs1, vuint8mf2_t rs2, size_t vl);
vfloat64m4_t __riscv_vloxei8_v_f64m4(const double * rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxei8_v_f64m4(double *rs1, vuint8mf2_t rs2, vfloat64m4_t vs3, size_t vl);
void __riscv_vsoxei8_v_f64m4(double *rs1, vuint8mf2_t rs2, vfloat64m4_t vs3, size_t vl);
vfloat64m4_t __riscv_vluxei16_v_f64m4(const double * rs1, vuint16m1_t rs2, size_t vl);
vfloat64m4_t __riscv_vloxei16_v_f64m4(const double * rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxei16_v_f64m4(double *rs1, vuint16m1_t rs2, vfloat64m4_t vs3, size_t vl);
void __riscv_vsoxei16_v_f64m4(double *rs1, vuint16m1_t rs2, vfloat64m4_t vs3, size_t vl);
vfloat64m4_t __riscv_vluxei32_v_f64m4(const double * rs1, vuint32m2_t rs2, size_t vl);
vfloat64m4_t __riscv_vloxei32_v_f64m4(const double * rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxei32_v_f64m4(double *rs1, vuint32m2_t rs2, vfloat64m4_t vs3, size_t vl);
void __riscv_vsoxei32_v_f64m4(double *rs1, vuint32m2_t rs2, vfloat64m4_t vs3, size_t vl);
vfloat64m4_t __riscv_vluxei64_v_f64m4(const double * rs1, vuint64m4_t rs2, size_t vl);
vfloat64m4_t __riscv_vloxei64_v_f64m4(const double * rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxei64_v_f64m4(double *rs1, vuint64m4_t rs2, vfloat64m4_t vs3, size_t vl);
void __riscv_vsoxei64_v_f64m4(double *rs1, vuint64m4_t rs2, vfloat64m4_t vs3, size_t vl);
vfloat64m4_t __riscv_vl4re64_v_f64m4(const double * rs1);
void __riscv_vs4r_v_f64m4(double *rs1, vfloat64m4_t vs3);
vuint64m8_t __riscv_vle64ff_v_u64m8(const uint64_t * rs1, size_t *new_vl, size_t vl);
vuint64m8_t __riscv_vluxei8_v_u64m8(const uint64_t * rs1, vuint8m1_t rs2, size_t vl);
vuint64m8_t __riscv_vloxei8_v_u64m8(const uint64_t * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_u64m8(uint64_t *rs1, vuint8m1_t rs2, vuint64m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_u64m8(uint64_t *rs1, vuint8m1_t rs2, vuint64m8_t vs3, size_t vl);
vuint64m8_t __riscv_vluxei16_v_u64m8(const uint64_t * rs1, vuint16m2_t rs2, size_t vl);
vuint64m8_t __riscv_vloxei16_v_u64m8(const uint64_t * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_u64m8(uint64_t *rs1, vuint16m2_t rs2, vuint64m8_t vs3, size_t vl);
void __riscv_vsoxei16_v_u64m8(uint64_t *rs1, vuint16m2_t rs2, vuint64m8_t vs3, size_t vl);
vuint64m8_t __riscv_vluxei32_v_u64m8(const uint64_t * rs1, vuint32m4_t rs2, size_t vl);
vuint64m8_t __riscv_vloxei32_v_u64m8(const uint64_t * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_u64m8(uint64_t *rs1, vuint32m4_t rs2, vuint64m8_t vs3, size_t vl);
void __riscv_vsoxei32_v_u64m8(uint64_t *rs1, vuint32m4_t rs2, vuint64m8_t vs3, size_t vl);
vuint64m8_t __riscv_vluxei64_v_u64m8(const uint64_t * rs1, vuint64m8_t rs2, size_t vl);
vuint64m8_t __riscv_vloxei64_v_u64m8(const uint64_t * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_u64m8(uint64_t *rs1, vuint64m8_t rs2, vuint64m8_t vs3, size_t vl);
void __riscv_vsoxei64_v_u64m8(uint64_t *rs1, vuint64m8_t rs2, vuint64m8_t vs3, size_t vl);
vuint64m8_t __riscv_vl8re64_v_u64m8(const uint64_t * rs1);
void __riscv_vs8r_v_u64m8(uint64_t *rs1, vuint64m8_t vs3);
vint64m8_t __riscv_vle64ff_v_i64m8(const int64_t * rs1, size_t *new_vl, size_t vl);
vint64m8_t __riscv_vluxei8_v_i64m8(const int64_t * rs1, vuint8m1_t rs2, size_t vl);
vint64m8_t __riscv_vloxei8_v_i64m8(const int64_t * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_i64m8(int64_t *rs1, vuint8m1_t rs2, vint64m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_i64m8(int64_t *rs1, vuint8m1_t rs2, vint64m8_t vs3, size_t vl);
vint64m8_t __riscv_vluxei16_v_i64m8(const int64_t * rs1, vuint16m2_t rs2, size_t vl);
vint64m8_t __riscv_vloxei16_v_i64m8(const int64_t * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_i64m8(int64_t *rs1, vuint16m2_t rs2, vint64m8_t vs3, size_t vl);
void __riscv_vsoxei16_v_i64m8(int64_t *rs1, vuint16m2_t rs2, vint64m8_t vs3, size_t vl);
vint64m8_t __riscv_vluxei32_v_i64m8(const int64_t * rs1, vuint32m4_t rs2, size_t vl);
vint64m8_t __riscv_vloxei32_v_i64m8(const int64_t * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_i64m8(int64_t *rs1, vuint32m4_t rs2, vint64m8_t vs3, size_t vl);
void __riscv_vsoxei32_v_i64m8(int64_t *rs1, vuint32m4_t rs2, vint64m8_t vs3, size_t vl);
vint64m8_t __riscv_vluxei64_v_i64m8(const int64_t * rs1, vuint64m8_t rs2, size_t vl);
vint64m8_t __riscv_vloxei64_v_i64m8(const int64_t * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_i64m8(int64_t *rs1, vuint64m8_t rs2, vint64m8_t vs3, size_t vl);
void __riscv_vsoxei64_v_i64m8(int64_t *rs1, vuint64m8_t rs2, vint64m8_t vs3, size_t vl);
vint64m8_t __riscv_vl8re64_v_i64m8(const int64_t * rs1);
void __riscv_vs8r_v_i64m8(int64_t *rs1, vint64m8_t vs3);
vfloat64m8_t __riscv_vle64ff_v_f64m8(const double * rs1, size_t *new_vl, size_t vl);
vfloat64m8_t __riscv_vluxei8_v_f64m8(const double * rs1, vuint8m1_t rs2, size_t vl);
vfloat64m8_t __riscv_vloxei8_v_f64m8(const double * rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxei8_v_f64m8(double *rs1, vuint8m1_t rs2, vfloat64m8_t vs3, size_t vl);
void __riscv_vsoxei8_v_f64m8(double *rs1, vuint8m1_t rs2, vfloat64m8_t vs3, size_t vl);
vfloat64m8_t __riscv_vluxei16_v_f64m8(const double * rs1, vuint16m2_t rs2, size_t vl);
vfloat64m8_t __riscv_vloxei16_v_f64m8(const double * rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxei16_v_f64m8(double *rs1, vuint16m2_t rs2, vfloat64m8_t vs3, size_t vl);
void __riscv_vsoxei16_v_f64m8(double *rs1, vuint16m2_t rs2, vfloat64m8_t vs3, size_t vl);
vfloat64m8_t __riscv_vluxei32_v_f64m8(const double * rs1, vuint32m4_t rs2, size_t vl);
vfloat64m8_t __riscv_vloxei32_v_f64m8(const double * rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxei32_v_f64m8(double *rs1, vuint32m4_t rs2, vfloat64m8_t vs3, size_t vl);
void __riscv_vsoxei32_v_f64m8(double *rs1, vuint32m4_t rs2, vfloat64m8_t vs3, size_t vl);
vfloat64m8_t __riscv_vluxei64_v_f64m8(const double * rs1, vuint64m8_t rs2, size_t vl);
vfloat64m8_t __riscv_vloxei64_v_f64m8(const double * rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxei64_v_f64m8(double *rs1, vuint64m8_t rs2, vfloat64m8_t vs3, size_t vl);
void __riscv_vsoxei64_v_f64m8(double *rs1, vuint64m8_t rs2, vfloat64m8_t vs3, size_t vl);
vfloat64m8_t __riscv_vl8re64_v_f64m8(const double * rs1);
void __riscv_vs8r_v_f64m8(double *rs1, vfloat64m8_t vs3);

/* The reshaping forms, which rename a value rather than compute one */
vuint8mf8_t __riscv_vundefined_u8mf8(void);
vint8mf8_t __riscv_vundefined_i8mf8(void);
vuint8mf4_t __riscv_vundefined_u8mf4(void);
vint8mf4_t __riscv_vundefined_i8mf4(void);
vuint8mf2_t __riscv_vundefined_u8mf2(void);
vint8mf2_t __riscv_vundefined_i8mf2(void);
vuint8m1_t __riscv_vundefined_u8m1(void);
vint8m1_t __riscv_vundefined_i8m1(void);
vuint8m2_t __riscv_vundefined_u8m2(void);
vint8m2_t __riscv_vundefined_i8m2(void);
vuint8m4_t __riscv_vundefined_u8m4(void);
vint8m4_t __riscv_vundefined_i8m4(void);
vuint8m8_t __riscv_vundefined_u8m8(void);
vint8m8_t __riscv_vundefined_i8m8(void);
vuint16mf4_t __riscv_vundefined_u16mf4(void);
vint16mf4_t __riscv_vundefined_i16mf4(void);
vuint16mf2_t __riscv_vundefined_u16mf2(void);
vint16mf2_t __riscv_vundefined_i16mf2(void);
vuint16m1_t __riscv_vundefined_u16m1(void);
vint16m1_t __riscv_vundefined_i16m1(void);
vuint16m2_t __riscv_vundefined_u16m2(void);
vint16m2_t __riscv_vundefined_i16m2(void);
vuint16m4_t __riscv_vundefined_u16m4(void);
vint16m4_t __riscv_vundefined_i16m4(void);
vuint16m8_t __riscv_vundefined_u16m8(void);
vint16m8_t __riscv_vundefined_i16m8(void);
vuint32mf2_t __riscv_vundefined_u32mf2(void);
vint32mf2_t __riscv_vundefined_i32mf2(void);
vfloat32mf2_t __riscv_vundefined_f32mf2(void);
vuint32m1_t __riscv_vundefined_u32m1(void);
vint32m1_t __riscv_vundefined_i32m1(void);
vfloat32m1_t __riscv_vundefined_f32m1(void);
vuint32m2_t __riscv_vundefined_u32m2(void);
vint32m2_t __riscv_vundefined_i32m2(void);
vfloat32m2_t __riscv_vundefined_f32m2(void);
vuint32m4_t __riscv_vundefined_u32m4(void);
vint32m4_t __riscv_vundefined_i32m4(void);
vfloat32m4_t __riscv_vundefined_f32m4(void);
vuint32m8_t __riscv_vundefined_u32m8(void);
vint32m8_t __riscv_vundefined_i32m8(void);
vfloat32m8_t __riscv_vundefined_f32m8(void);
vuint64m1_t __riscv_vundefined_u64m1(void);
vint64m1_t __riscv_vundefined_i64m1(void);
vfloat64m1_t __riscv_vundefined_f64m1(void);
vuint64m2_t __riscv_vundefined_u64m2(void);
vint64m2_t __riscv_vundefined_i64m2(void);
vfloat64m2_t __riscv_vundefined_f64m2(void);
vuint64m4_t __riscv_vundefined_u64m4(void);
vint64m4_t __riscv_vundefined_i64m4(void);
vfloat64m4_t __riscv_vundefined_f64m4(void);
vuint64m8_t __riscv_vundefined_u64m8(void);
vint64m8_t __riscv_vundefined_i64m8(void);
vfloat64m8_t __riscv_vundefined_f64m8(void);
vint8mf8_t __riscv_vreinterpret_v_u8mf8_i8mf8(vuint8mf8_t src);
vuint8mf8_t __riscv_vreinterpret_v_i8mf8_u8mf8(vint8mf8_t src);
vint8mf4_t __riscv_vreinterpret_v_u8mf4_i8mf4(vuint8mf4_t src);
vuint8mf4_t __riscv_vreinterpret_v_i8mf4_u8mf4(vint8mf4_t src);
vint8mf2_t __riscv_vreinterpret_v_u8mf2_i8mf2(vuint8mf2_t src);
vuint8mf2_t __riscv_vreinterpret_v_i8mf2_u8mf2(vint8mf2_t src);
vint8m1_t __riscv_vreinterpret_v_u8m1_i8m1(vuint8m1_t src);
vuint8m1_t __riscv_vreinterpret_v_i8m1_u8m1(vint8m1_t src);
vint8m2_t __riscv_vreinterpret_v_u8m2_i8m2(vuint8m2_t src);
vuint8m2_t __riscv_vreinterpret_v_i8m2_u8m2(vint8m2_t src);
vint8m4_t __riscv_vreinterpret_v_u8m4_i8m4(vuint8m4_t src);
vuint8m4_t __riscv_vreinterpret_v_i8m4_u8m4(vint8m4_t src);
vint8m8_t __riscv_vreinterpret_v_u8m8_i8m8(vuint8m8_t src);
vuint8m8_t __riscv_vreinterpret_v_i8m8_u8m8(vint8m8_t src);
vint16mf4_t __riscv_vreinterpret_v_u16mf4_i16mf4(vuint16mf4_t src);
vuint16mf4_t __riscv_vreinterpret_v_i16mf4_u16mf4(vint16mf4_t src);
vint16mf2_t __riscv_vreinterpret_v_u16mf2_i16mf2(vuint16mf2_t src);
vuint16mf2_t __riscv_vreinterpret_v_i16mf2_u16mf2(vint16mf2_t src);
vint16m1_t __riscv_vreinterpret_v_u16m1_i16m1(vuint16m1_t src);
vuint16m1_t __riscv_vreinterpret_v_i16m1_u16m1(vint16m1_t src);
vint16m2_t __riscv_vreinterpret_v_u16m2_i16m2(vuint16m2_t src);
vuint16m2_t __riscv_vreinterpret_v_i16m2_u16m2(vint16m2_t src);
vint16m4_t __riscv_vreinterpret_v_u16m4_i16m4(vuint16m4_t src);
vuint16m4_t __riscv_vreinterpret_v_i16m4_u16m4(vint16m4_t src);
vint16m8_t __riscv_vreinterpret_v_u16m8_i16m8(vuint16m8_t src);
vuint16m8_t __riscv_vreinterpret_v_i16m8_u16m8(vint16m8_t src);
vint32mf2_t __riscv_vreinterpret_v_u32mf2_i32mf2(vuint32mf2_t src);
vfloat32mf2_t __riscv_vreinterpret_v_u32mf2_f32mf2(vuint32mf2_t src);
vuint32mf2_t __riscv_vreinterpret_v_i32mf2_u32mf2(vint32mf2_t src);
vfloat32mf2_t __riscv_vreinterpret_v_i32mf2_f32mf2(vint32mf2_t src);
vuint32mf2_t __riscv_vreinterpret_v_f32mf2_u32mf2(vfloat32mf2_t src);
vint32mf2_t __riscv_vreinterpret_v_f32mf2_i32mf2(vfloat32mf2_t src);
vint32m1_t __riscv_vreinterpret_v_u32m1_i32m1(vuint32m1_t src);
vfloat32m1_t __riscv_vreinterpret_v_u32m1_f32m1(vuint32m1_t src);
vuint32m1_t __riscv_vreinterpret_v_i32m1_u32m1(vint32m1_t src);
vfloat32m1_t __riscv_vreinterpret_v_i32m1_f32m1(vint32m1_t src);
vuint32m1_t __riscv_vreinterpret_v_f32m1_u32m1(vfloat32m1_t src);
vint32m1_t __riscv_vreinterpret_v_f32m1_i32m1(vfloat32m1_t src);
vint32m2_t __riscv_vreinterpret_v_u32m2_i32m2(vuint32m2_t src);
vfloat32m2_t __riscv_vreinterpret_v_u32m2_f32m2(vuint32m2_t src);
vuint32m2_t __riscv_vreinterpret_v_i32m2_u32m2(vint32m2_t src);
vfloat32m2_t __riscv_vreinterpret_v_i32m2_f32m2(vint32m2_t src);
vuint32m2_t __riscv_vreinterpret_v_f32m2_u32m2(vfloat32m2_t src);
vint32m2_t __riscv_vreinterpret_v_f32m2_i32m2(vfloat32m2_t src);
vint32m4_t __riscv_vreinterpret_v_u32m4_i32m4(vuint32m4_t src);
vfloat32m4_t __riscv_vreinterpret_v_u32m4_f32m4(vuint32m4_t src);
vuint32m4_t __riscv_vreinterpret_v_i32m4_u32m4(vint32m4_t src);
vfloat32m4_t __riscv_vreinterpret_v_i32m4_f32m4(vint32m4_t src);
vuint32m4_t __riscv_vreinterpret_v_f32m4_u32m4(vfloat32m4_t src);
vint32m4_t __riscv_vreinterpret_v_f32m4_i32m4(vfloat32m4_t src);
vint32m8_t __riscv_vreinterpret_v_u32m8_i32m8(vuint32m8_t src);
vfloat32m8_t __riscv_vreinterpret_v_u32m8_f32m8(vuint32m8_t src);
vuint32m8_t __riscv_vreinterpret_v_i32m8_u32m8(vint32m8_t src);
vfloat32m8_t __riscv_vreinterpret_v_i32m8_f32m8(vint32m8_t src);
vuint32m8_t __riscv_vreinterpret_v_f32m8_u32m8(vfloat32m8_t src);
vint32m8_t __riscv_vreinterpret_v_f32m8_i32m8(vfloat32m8_t src);
vint64m1_t __riscv_vreinterpret_v_u64m1_i64m1(vuint64m1_t src);
vfloat64m1_t __riscv_vreinterpret_v_u64m1_f64m1(vuint64m1_t src);
vuint64m1_t __riscv_vreinterpret_v_i64m1_u64m1(vint64m1_t src);
vfloat64m1_t __riscv_vreinterpret_v_i64m1_f64m1(vint64m1_t src);
vuint64m1_t __riscv_vreinterpret_v_f64m1_u64m1(vfloat64m1_t src);
vint64m1_t __riscv_vreinterpret_v_f64m1_i64m1(vfloat64m1_t src);
vint64m2_t __riscv_vreinterpret_v_u64m2_i64m2(vuint64m2_t src);
vfloat64m2_t __riscv_vreinterpret_v_u64m2_f64m2(vuint64m2_t src);
vuint64m2_t __riscv_vreinterpret_v_i64m2_u64m2(vint64m2_t src);
vfloat64m2_t __riscv_vreinterpret_v_i64m2_f64m2(vint64m2_t src);
vuint64m2_t __riscv_vreinterpret_v_f64m2_u64m2(vfloat64m2_t src);
vint64m2_t __riscv_vreinterpret_v_f64m2_i64m2(vfloat64m2_t src);
vint64m4_t __riscv_vreinterpret_v_u64m4_i64m4(vuint64m4_t src);
vfloat64m4_t __riscv_vreinterpret_v_u64m4_f64m4(vuint64m4_t src);
vuint64m4_t __riscv_vreinterpret_v_i64m4_u64m4(vint64m4_t src);
vfloat64m4_t __riscv_vreinterpret_v_i64m4_f64m4(vint64m4_t src);
vuint64m4_t __riscv_vreinterpret_v_f64m4_u64m4(vfloat64m4_t src);
vint64m4_t __riscv_vreinterpret_v_f64m4_i64m4(vfloat64m4_t src);
vint64m8_t __riscv_vreinterpret_v_u64m8_i64m8(vuint64m8_t src);
vfloat64m8_t __riscv_vreinterpret_v_u64m8_f64m8(vuint64m8_t src);
vuint64m8_t __riscv_vreinterpret_v_i64m8_u64m8(vint64m8_t src);
vfloat64m8_t __riscv_vreinterpret_v_i64m8_f64m8(vint64m8_t src);
vuint64m8_t __riscv_vreinterpret_v_f64m8_u64m8(vfloat64m8_t src);
vint64m8_t __riscv_vreinterpret_v_f64m8_i64m8(vfloat64m8_t src);
vuint16mf4_t __riscv_vreinterpret_v_u8mf4_u16mf4(vuint8mf4_t src);
vint16mf4_t __riscv_vreinterpret_v_i8mf4_i16mf4(vint8mf4_t src);
vuint16mf2_t __riscv_vreinterpret_v_u8mf2_u16mf2(vuint8mf2_t src);
vuint32mf2_t __riscv_vreinterpret_v_u8mf2_u32mf2(vuint8mf2_t src);
vint16mf2_t __riscv_vreinterpret_v_i8mf2_i16mf2(vint8mf2_t src);
vint32mf2_t __riscv_vreinterpret_v_i8mf2_i32mf2(vint8mf2_t src);
vuint16m1_t __riscv_vreinterpret_v_u8m1_u16m1(vuint8m1_t src);
vuint32m1_t __riscv_vreinterpret_v_u8m1_u32m1(vuint8m1_t src);
vuint64m1_t __riscv_vreinterpret_v_u8m1_u64m1(vuint8m1_t src);
vint16m1_t __riscv_vreinterpret_v_i8m1_i16m1(vint8m1_t src);
vint32m1_t __riscv_vreinterpret_v_i8m1_i32m1(vint8m1_t src);
vint64m1_t __riscv_vreinterpret_v_i8m1_i64m1(vint8m1_t src);
vuint16m2_t __riscv_vreinterpret_v_u8m2_u16m2(vuint8m2_t src);
vuint32m2_t __riscv_vreinterpret_v_u8m2_u32m2(vuint8m2_t src);
vuint64m2_t __riscv_vreinterpret_v_u8m2_u64m2(vuint8m2_t src);
vint16m2_t __riscv_vreinterpret_v_i8m2_i16m2(vint8m2_t src);
vint32m2_t __riscv_vreinterpret_v_i8m2_i32m2(vint8m2_t src);
vint64m2_t __riscv_vreinterpret_v_i8m2_i64m2(vint8m2_t src);
vuint16m4_t __riscv_vreinterpret_v_u8m4_u16m4(vuint8m4_t src);
vuint32m4_t __riscv_vreinterpret_v_u8m4_u32m4(vuint8m4_t src);
vuint64m4_t __riscv_vreinterpret_v_u8m4_u64m4(vuint8m4_t src);
vint16m4_t __riscv_vreinterpret_v_i8m4_i16m4(vint8m4_t src);
vint32m4_t __riscv_vreinterpret_v_i8m4_i32m4(vint8m4_t src);
vint64m4_t __riscv_vreinterpret_v_i8m4_i64m4(vint8m4_t src);
vuint16m8_t __riscv_vreinterpret_v_u8m8_u16m8(vuint8m8_t src);
vuint32m8_t __riscv_vreinterpret_v_u8m8_u32m8(vuint8m8_t src);
vuint64m8_t __riscv_vreinterpret_v_u8m8_u64m8(vuint8m8_t src);
vint16m8_t __riscv_vreinterpret_v_i8m8_i16m8(vint8m8_t src);
vint32m8_t __riscv_vreinterpret_v_i8m8_i32m8(vint8m8_t src);
vint64m8_t __riscv_vreinterpret_v_i8m8_i64m8(vint8m8_t src);
vuint8mf4_t __riscv_vreinterpret_v_u16mf4_u8mf4(vuint16mf4_t src);
vint8mf4_t __riscv_vreinterpret_v_i16mf4_i8mf4(vint16mf4_t src);
vuint8mf2_t __riscv_vreinterpret_v_u16mf2_u8mf2(vuint16mf2_t src);
vuint32mf2_t __riscv_vreinterpret_v_u16mf2_u32mf2(vuint16mf2_t src);
vint8mf2_t __riscv_vreinterpret_v_i16mf2_i8mf2(vint16mf2_t src);
vint32mf2_t __riscv_vreinterpret_v_i16mf2_i32mf2(vint16mf2_t src);
vuint8m1_t __riscv_vreinterpret_v_u16m1_u8m1(vuint16m1_t src);
vuint32m1_t __riscv_vreinterpret_v_u16m1_u32m1(vuint16m1_t src);
vuint64m1_t __riscv_vreinterpret_v_u16m1_u64m1(vuint16m1_t src);
vint8m1_t __riscv_vreinterpret_v_i16m1_i8m1(vint16m1_t src);
vint32m1_t __riscv_vreinterpret_v_i16m1_i32m1(vint16m1_t src);
vint64m1_t __riscv_vreinterpret_v_i16m1_i64m1(vint16m1_t src);
vuint8m2_t __riscv_vreinterpret_v_u16m2_u8m2(vuint16m2_t src);
vuint32m2_t __riscv_vreinterpret_v_u16m2_u32m2(vuint16m2_t src);
vuint64m2_t __riscv_vreinterpret_v_u16m2_u64m2(vuint16m2_t src);
vint8m2_t __riscv_vreinterpret_v_i16m2_i8m2(vint16m2_t src);
vint32m2_t __riscv_vreinterpret_v_i16m2_i32m2(vint16m2_t src);
vint64m2_t __riscv_vreinterpret_v_i16m2_i64m2(vint16m2_t src);
vuint8m4_t __riscv_vreinterpret_v_u16m4_u8m4(vuint16m4_t src);
vuint32m4_t __riscv_vreinterpret_v_u16m4_u32m4(vuint16m4_t src);
vuint64m4_t __riscv_vreinterpret_v_u16m4_u64m4(vuint16m4_t src);
vint8m4_t __riscv_vreinterpret_v_i16m4_i8m4(vint16m4_t src);
vint32m4_t __riscv_vreinterpret_v_i16m4_i32m4(vint16m4_t src);
vint64m4_t __riscv_vreinterpret_v_i16m4_i64m4(vint16m4_t src);
vuint8m8_t __riscv_vreinterpret_v_u16m8_u8m8(vuint16m8_t src);
vuint32m8_t __riscv_vreinterpret_v_u16m8_u32m8(vuint16m8_t src);
vuint64m8_t __riscv_vreinterpret_v_u16m8_u64m8(vuint16m8_t src);
vint8m8_t __riscv_vreinterpret_v_i16m8_i8m8(vint16m8_t src);
vint32m8_t __riscv_vreinterpret_v_i16m8_i32m8(vint16m8_t src);
vint64m8_t __riscv_vreinterpret_v_i16m8_i64m8(vint16m8_t src);
vuint8mf2_t __riscv_vreinterpret_v_u32mf2_u8mf2(vuint32mf2_t src);
vuint16mf2_t __riscv_vreinterpret_v_u32mf2_u16mf2(vuint32mf2_t src);
vint8mf2_t __riscv_vreinterpret_v_i32mf2_i8mf2(vint32mf2_t src);
vint16mf2_t __riscv_vreinterpret_v_i32mf2_i16mf2(vint32mf2_t src);
vuint8m1_t __riscv_vreinterpret_v_u32m1_u8m1(vuint32m1_t src);
vuint16m1_t __riscv_vreinterpret_v_u32m1_u16m1(vuint32m1_t src);
vuint64m1_t __riscv_vreinterpret_v_u32m1_u64m1(vuint32m1_t src);
vint8m1_t __riscv_vreinterpret_v_i32m1_i8m1(vint32m1_t src);
vint16m1_t __riscv_vreinterpret_v_i32m1_i16m1(vint32m1_t src);
vint64m1_t __riscv_vreinterpret_v_i32m1_i64m1(vint32m1_t src);
vuint8m2_t __riscv_vreinterpret_v_u32m2_u8m2(vuint32m2_t src);
vuint16m2_t __riscv_vreinterpret_v_u32m2_u16m2(vuint32m2_t src);
vuint64m2_t __riscv_vreinterpret_v_u32m2_u64m2(vuint32m2_t src);
vint8m2_t __riscv_vreinterpret_v_i32m2_i8m2(vint32m2_t src);
vint16m2_t __riscv_vreinterpret_v_i32m2_i16m2(vint32m2_t src);
vint64m2_t __riscv_vreinterpret_v_i32m2_i64m2(vint32m2_t src);
vuint8m4_t __riscv_vreinterpret_v_u32m4_u8m4(vuint32m4_t src);
vuint16m4_t __riscv_vreinterpret_v_u32m4_u16m4(vuint32m4_t src);
vuint64m4_t __riscv_vreinterpret_v_u32m4_u64m4(vuint32m4_t src);
vint8m4_t __riscv_vreinterpret_v_i32m4_i8m4(vint32m4_t src);
vint16m4_t __riscv_vreinterpret_v_i32m4_i16m4(vint32m4_t src);
vint64m4_t __riscv_vreinterpret_v_i32m4_i64m4(vint32m4_t src);
vuint8m8_t __riscv_vreinterpret_v_u32m8_u8m8(vuint32m8_t src);
vuint16m8_t __riscv_vreinterpret_v_u32m8_u16m8(vuint32m8_t src);
vuint64m8_t __riscv_vreinterpret_v_u32m8_u64m8(vuint32m8_t src);
vint8m8_t __riscv_vreinterpret_v_i32m8_i8m8(vint32m8_t src);
vint16m8_t __riscv_vreinterpret_v_i32m8_i16m8(vint32m8_t src);
vint64m8_t __riscv_vreinterpret_v_i32m8_i64m8(vint32m8_t src);
vuint8m1_t __riscv_vreinterpret_v_u64m1_u8m1(vuint64m1_t src);
vuint16m1_t __riscv_vreinterpret_v_u64m1_u16m1(vuint64m1_t src);
vuint32m1_t __riscv_vreinterpret_v_u64m1_u32m1(vuint64m1_t src);
vint8m1_t __riscv_vreinterpret_v_i64m1_i8m1(vint64m1_t src);
vint16m1_t __riscv_vreinterpret_v_i64m1_i16m1(vint64m1_t src);
vint32m1_t __riscv_vreinterpret_v_i64m1_i32m1(vint64m1_t src);
vuint8m2_t __riscv_vreinterpret_v_u64m2_u8m2(vuint64m2_t src);
vuint16m2_t __riscv_vreinterpret_v_u64m2_u16m2(vuint64m2_t src);
vuint32m2_t __riscv_vreinterpret_v_u64m2_u32m2(vuint64m2_t src);
vint8m2_t __riscv_vreinterpret_v_i64m2_i8m2(vint64m2_t src);
vint16m2_t __riscv_vreinterpret_v_i64m2_i16m2(vint64m2_t src);
vint32m2_t __riscv_vreinterpret_v_i64m2_i32m2(vint64m2_t src);
vuint8m4_t __riscv_vreinterpret_v_u64m4_u8m4(vuint64m4_t src);
vuint16m4_t __riscv_vreinterpret_v_u64m4_u16m4(vuint64m4_t src);
vuint32m4_t __riscv_vreinterpret_v_u64m4_u32m4(vuint64m4_t src);
vint8m4_t __riscv_vreinterpret_v_i64m4_i8m4(vint64m4_t src);
vint16m4_t __riscv_vreinterpret_v_i64m4_i16m4(vint64m4_t src);
vint32m4_t __riscv_vreinterpret_v_i64m4_i32m4(vint64m4_t src);
vuint8m8_t __riscv_vreinterpret_v_u64m8_u8m8(vuint64m8_t src);
vuint16m8_t __riscv_vreinterpret_v_u64m8_u16m8(vuint64m8_t src);
vuint32m8_t __riscv_vreinterpret_v_u64m8_u32m8(vuint64m8_t src);
vint8m8_t __riscv_vreinterpret_v_i64m8_i8m8(vint64m8_t src);
vint16m8_t __riscv_vreinterpret_v_i64m8_i16m8(vint64m8_t src);
vint32m8_t __riscv_vreinterpret_v_i64m8_i32m8(vint64m8_t src);
vbool64_t __riscv_vreinterpret_v_u8mf8_b64(vuint8mf8_t src);
vuint8mf8_t __riscv_vreinterpret_v_b64_u8mf8(vbool64_t src);
vbool64_t __riscv_vreinterpret_v_i8mf8_b64(vint8mf8_t src);
vint8mf8_t __riscv_vreinterpret_v_b64_i8mf8(vbool64_t src);
vbool32_t __riscv_vreinterpret_v_u8mf4_b32(vuint8mf4_t src);
vuint8mf4_t __riscv_vreinterpret_v_b32_u8mf4(vbool32_t src);
vbool32_t __riscv_vreinterpret_v_i8mf4_b32(vint8mf4_t src);
vint8mf4_t __riscv_vreinterpret_v_b32_i8mf4(vbool32_t src);
vbool16_t __riscv_vreinterpret_v_u8mf2_b16(vuint8mf2_t src);
vuint8mf2_t __riscv_vreinterpret_v_b16_u8mf2(vbool16_t src);
vbool16_t __riscv_vreinterpret_v_i8mf2_b16(vint8mf2_t src);
vint8mf2_t __riscv_vreinterpret_v_b16_i8mf2(vbool16_t src);
vbool8_t __riscv_vreinterpret_v_u8m1_b8(vuint8m1_t src);
vuint8m1_t __riscv_vreinterpret_v_b8_u8m1(vbool8_t src);
vbool8_t __riscv_vreinterpret_v_i8m1_b8(vint8m1_t src);
vint8m1_t __riscv_vreinterpret_v_b8_i8m1(vbool8_t src);
vbool4_t __riscv_vreinterpret_v_u8m2_b4(vuint8m2_t src);
vuint8m2_t __riscv_vreinterpret_v_b4_u8m2(vbool4_t src);
vbool4_t __riscv_vreinterpret_v_i8m2_b4(vint8m2_t src);
vint8m2_t __riscv_vreinterpret_v_b4_i8m2(vbool4_t src);
vbool2_t __riscv_vreinterpret_v_u8m4_b2(vuint8m4_t src);
vuint8m4_t __riscv_vreinterpret_v_b2_u8m4(vbool2_t src);
vbool2_t __riscv_vreinterpret_v_i8m4_b2(vint8m4_t src);
vint8m4_t __riscv_vreinterpret_v_b2_i8m4(vbool2_t src);
vbool1_t __riscv_vreinterpret_v_u8m8_b1(vuint8m8_t src);
vuint8m8_t __riscv_vreinterpret_v_b1_u8m8(vbool1_t src);
vbool1_t __riscv_vreinterpret_v_i8m8_b1(vint8m8_t src);
vint8m8_t __riscv_vreinterpret_v_b1_i8m8(vbool1_t src);
vuint8mf4_t __riscv_vlmul_ext_v_u8mf8_u8mf4(vuint8mf8_t src);
vuint8mf2_t __riscv_vlmul_ext_v_u8mf8_u8mf2(vuint8mf8_t src);
vuint8m1_t __riscv_vlmul_ext_v_u8mf8_u8m1(vuint8mf8_t src);
vuint8m2_t __riscv_vlmul_ext_v_u8mf8_u8m2(vuint8mf8_t src);
vuint8m4_t __riscv_vlmul_ext_v_u8mf8_u8m4(vuint8mf8_t src);
vuint8m8_t __riscv_vlmul_ext_v_u8mf8_u8m8(vuint8mf8_t src);
vint8mf4_t __riscv_vlmul_ext_v_i8mf8_i8mf4(vint8mf8_t src);
vint8mf2_t __riscv_vlmul_ext_v_i8mf8_i8mf2(vint8mf8_t src);
vint8m1_t __riscv_vlmul_ext_v_i8mf8_i8m1(vint8mf8_t src);
vint8m2_t __riscv_vlmul_ext_v_i8mf8_i8m2(vint8mf8_t src);
vint8m4_t __riscv_vlmul_ext_v_i8mf8_i8m4(vint8mf8_t src);
vint8m8_t __riscv_vlmul_ext_v_i8mf8_i8m8(vint8mf8_t src);
vuint8mf8_t __riscv_vlmul_trunc_v_u8mf4_u8mf8(vuint8mf4_t src);
vuint8mf2_t __riscv_vlmul_ext_v_u8mf4_u8mf2(vuint8mf4_t src);
vuint8m1_t __riscv_vlmul_ext_v_u8mf4_u8m1(vuint8mf4_t src);
vuint8m2_t __riscv_vlmul_ext_v_u8mf4_u8m2(vuint8mf4_t src);
vuint8m4_t __riscv_vlmul_ext_v_u8mf4_u8m4(vuint8mf4_t src);
vuint8m8_t __riscv_vlmul_ext_v_u8mf4_u8m8(vuint8mf4_t src);
vint8mf8_t __riscv_vlmul_trunc_v_i8mf4_i8mf8(vint8mf4_t src);
vint8mf2_t __riscv_vlmul_ext_v_i8mf4_i8mf2(vint8mf4_t src);
vint8m1_t __riscv_vlmul_ext_v_i8mf4_i8m1(vint8mf4_t src);
vint8m2_t __riscv_vlmul_ext_v_i8mf4_i8m2(vint8mf4_t src);
vint8m4_t __riscv_vlmul_ext_v_i8mf4_i8m4(vint8mf4_t src);
vint8m8_t __riscv_vlmul_ext_v_i8mf4_i8m8(vint8mf4_t src);
vuint8mf8_t __riscv_vlmul_trunc_v_u8mf2_u8mf8(vuint8mf2_t src);
vuint8mf4_t __riscv_vlmul_trunc_v_u8mf2_u8mf4(vuint8mf2_t src);
vuint8m1_t __riscv_vlmul_ext_v_u8mf2_u8m1(vuint8mf2_t src);
vuint8m2_t __riscv_vlmul_ext_v_u8mf2_u8m2(vuint8mf2_t src);
vuint8m4_t __riscv_vlmul_ext_v_u8mf2_u8m4(vuint8mf2_t src);
vuint8m8_t __riscv_vlmul_ext_v_u8mf2_u8m8(vuint8mf2_t src);
vint8mf8_t __riscv_vlmul_trunc_v_i8mf2_i8mf8(vint8mf2_t src);
vint8mf4_t __riscv_vlmul_trunc_v_i8mf2_i8mf4(vint8mf2_t src);
vint8m1_t __riscv_vlmul_ext_v_i8mf2_i8m1(vint8mf2_t src);
vint8m2_t __riscv_vlmul_ext_v_i8mf2_i8m2(vint8mf2_t src);
vint8m4_t __riscv_vlmul_ext_v_i8mf2_i8m4(vint8mf2_t src);
vint8m8_t __riscv_vlmul_ext_v_i8mf2_i8m8(vint8mf2_t src);
vuint8mf8_t __riscv_vlmul_trunc_v_u8m1_u8mf8(vuint8m1_t src);
vuint8mf4_t __riscv_vlmul_trunc_v_u8m1_u8mf4(vuint8m1_t src);
vuint8mf2_t __riscv_vlmul_trunc_v_u8m1_u8mf2(vuint8m1_t src);
vuint8m2_t __riscv_vlmul_ext_v_u8m1_u8m2(vuint8m1_t src);
vuint8m4_t __riscv_vlmul_ext_v_u8m1_u8m4(vuint8m1_t src);
vuint8m8_t __riscv_vlmul_ext_v_u8m1_u8m8(vuint8m1_t src);
vint8mf8_t __riscv_vlmul_trunc_v_i8m1_i8mf8(vint8m1_t src);
vint8mf4_t __riscv_vlmul_trunc_v_i8m1_i8mf4(vint8m1_t src);
vint8mf2_t __riscv_vlmul_trunc_v_i8m1_i8mf2(vint8m1_t src);
vint8m2_t __riscv_vlmul_ext_v_i8m1_i8m2(vint8m1_t src);
vint8m4_t __riscv_vlmul_ext_v_i8m1_i8m4(vint8m1_t src);
vint8m8_t __riscv_vlmul_ext_v_i8m1_i8m8(vint8m1_t src);
vuint8mf8_t __riscv_vlmul_trunc_v_u8m2_u8mf8(vuint8m2_t src);
vuint8mf4_t __riscv_vlmul_trunc_v_u8m2_u8mf4(vuint8m2_t src);
vuint8mf2_t __riscv_vlmul_trunc_v_u8m2_u8mf2(vuint8m2_t src);
vuint8m1_t __riscv_vlmul_trunc_v_u8m2_u8m1(vuint8m2_t src);
vuint8m4_t __riscv_vlmul_ext_v_u8m2_u8m4(vuint8m2_t src);
vuint8m8_t __riscv_vlmul_ext_v_u8m2_u8m8(vuint8m2_t src);
vint8mf8_t __riscv_vlmul_trunc_v_i8m2_i8mf8(vint8m2_t src);
vint8mf4_t __riscv_vlmul_trunc_v_i8m2_i8mf4(vint8m2_t src);
vint8mf2_t __riscv_vlmul_trunc_v_i8m2_i8mf2(vint8m2_t src);
vint8m1_t __riscv_vlmul_trunc_v_i8m2_i8m1(vint8m2_t src);
vint8m4_t __riscv_vlmul_ext_v_i8m2_i8m4(vint8m2_t src);
vint8m8_t __riscv_vlmul_ext_v_i8m2_i8m8(vint8m2_t src);
vuint8mf8_t __riscv_vlmul_trunc_v_u8m4_u8mf8(vuint8m4_t src);
vuint8mf4_t __riscv_vlmul_trunc_v_u8m4_u8mf4(vuint8m4_t src);
vuint8mf2_t __riscv_vlmul_trunc_v_u8m4_u8mf2(vuint8m4_t src);
vuint8m1_t __riscv_vlmul_trunc_v_u8m4_u8m1(vuint8m4_t src);
vuint8m2_t __riscv_vlmul_trunc_v_u8m4_u8m2(vuint8m4_t src);
vuint8m8_t __riscv_vlmul_ext_v_u8m4_u8m8(vuint8m4_t src);
vint8mf8_t __riscv_vlmul_trunc_v_i8m4_i8mf8(vint8m4_t src);
vint8mf4_t __riscv_vlmul_trunc_v_i8m4_i8mf4(vint8m4_t src);
vint8mf2_t __riscv_vlmul_trunc_v_i8m4_i8mf2(vint8m4_t src);
vint8m1_t __riscv_vlmul_trunc_v_i8m4_i8m1(vint8m4_t src);
vint8m2_t __riscv_vlmul_trunc_v_i8m4_i8m2(vint8m4_t src);
vint8m8_t __riscv_vlmul_ext_v_i8m4_i8m8(vint8m4_t src);
vuint8mf8_t __riscv_vlmul_trunc_v_u8m8_u8mf8(vuint8m8_t src);
vuint8mf4_t __riscv_vlmul_trunc_v_u8m8_u8mf4(vuint8m8_t src);
vuint8mf2_t __riscv_vlmul_trunc_v_u8m8_u8mf2(vuint8m8_t src);
vuint8m1_t __riscv_vlmul_trunc_v_u8m8_u8m1(vuint8m8_t src);
vuint8m2_t __riscv_vlmul_trunc_v_u8m8_u8m2(vuint8m8_t src);
vuint8m4_t __riscv_vlmul_trunc_v_u8m8_u8m4(vuint8m8_t src);
vint8mf8_t __riscv_vlmul_trunc_v_i8m8_i8mf8(vint8m8_t src);
vint8mf4_t __riscv_vlmul_trunc_v_i8m8_i8mf4(vint8m8_t src);
vint8mf2_t __riscv_vlmul_trunc_v_i8m8_i8mf2(vint8m8_t src);
vint8m1_t __riscv_vlmul_trunc_v_i8m8_i8m1(vint8m8_t src);
vint8m2_t __riscv_vlmul_trunc_v_i8m8_i8m2(vint8m8_t src);
vint8m4_t __riscv_vlmul_trunc_v_i8m8_i8m4(vint8m8_t src);
vuint16mf2_t __riscv_vlmul_ext_v_u16mf4_u16mf2(vuint16mf4_t src);
vuint16m1_t __riscv_vlmul_ext_v_u16mf4_u16m1(vuint16mf4_t src);
vuint16m2_t __riscv_vlmul_ext_v_u16mf4_u16m2(vuint16mf4_t src);
vuint16m4_t __riscv_vlmul_ext_v_u16mf4_u16m4(vuint16mf4_t src);
vuint16m8_t __riscv_vlmul_ext_v_u16mf4_u16m8(vuint16mf4_t src);
vint16mf2_t __riscv_vlmul_ext_v_i16mf4_i16mf2(vint16mf4_t src);
vint16m1_t __riscv_vlmul_ext_v_i16mf4_i16m1(vint16mf4_t src);
vint16m2_t __riscv_vlmul_ext_v_i16mf4_i16m2(vint16mf4_t src);
vint16m4_t __riscv_vlmul_ext_v_i16mf4_i16m4(vint16mf4_t src);
vint16m8_t __riscv_vlmul_ext_v_i16mf4_i16m8(vint16mf4_t src);
vuint16mf4_t __riscv_vlmul_trunc_v_u16mf2_u16mf4(vuint16mf2_t src);
vuint16m1_t __riscv_vlmul_ext_v_u16mf2_u16m1(vuint16mf2_t src);
vuint16m2_t __riscv_vlmul_ext_v_u16mf2_u16m2(vuint16mf2_t src);
vuint16m4_t __riscv_vlmul_ext_v_u16mf2_u16m4(vuint16mf2_t src);
vuint16m8_t __riscv_vlmul_ext_v_u16mf2_u16m8(vuint16mf2_t src);
vint16mf4_t __riscv_vlmul_trunc_v_i16mf2_i16mf4(vint16mf2_t src);
vint16m1_t __riscv_vlmul_ext_v_i16mf2_i16m1(vint16mf2_t src);
vint16m2_t __riscv_vlmul_ext_v_i16mf2_i16m2(vint16mf2_t src);
vint16m4_t __riscv_vlmul_ext_v_i16mf2_i16m4(vint16mf2_t src);
vint16m8_t __riscv_vlmul_ext_v_i16mf2_i16m8(vint16mf2_t src);
vuint16mf4_t __riscv_vlmul_trunc_v_u16m1_u16mf4(vuint16m1_t src);
vuint16mf2_t __riscv_vlmul_trunc_v_u16m1_u16mf2(vuint16m1_t src);
vuint16m2_t __riscv_vlmul_ext_v_u16m1_u16m2(vuint16m1_t src);
vuint16m4_t __riscv_vlmul_ext_v_u16m1_u16m4(vuint16m1_t src);
vuint16m8_t __riscv_vlmul_ext_v_u16m1_u16m8(vuint16m1_t src);
vint16mf4_t __riscv_vlmul_trunc_v_i16m1_i16mf4(vint16m1_t src);
vint16mf2_t __riscv_vlmul_trunc_v_i16m1_i16mf2(vint16m1_t src);
vint16m2_t __riscv_vlmul_ext_v_i16m1_i16m2(vint16m1_t src);
vint16m4_t __riscv_vlmul_ext_v_i16m1_i16m4(vint16m1_t src);
vint16m8_t __riscv_vlmul_ext_v_i16m1_i16m8(vint16m1_t src);
vuint16mf4_t __riscv_vlmul_trunc_v_u16m2_u16mf4(vuint16m2_t src);
vuint16mf2_t __riscv_vlmul_trunc_v_u16m2_u16mf2(vuint16m2_t src);
vuint16m1_t __riscv_vlmul_trunc_v_u16m2_u16m1(vuint16m2_t src);
vuint16m4_t __riscv_vlmul_ext_v_u16m2_u16m4(vuint16m2_t src);
vuint16m8_t __riscv_vlmul_ext_v_u16m2_u16m8(vuint16m2_t src);
vint16mf4_t __riscv_vlmul_trunc_v_i16m2_i16mf4(vint16m2_t src);
vint16mf2_t __riscv_vlmul_trunc_v_i16m2_i16mf2(vint16m2_t src);
vint16m1_t __riscv_vlmul_trunc_v_i16m2_i16m1(vint16m2_t src);
vint16m4_t __riscv_vlmul_ext_v_i16m2_i16m4(vint16m2_t src);
vint16m8_t __riscv_vlmul_ext_v_i16m2_i16m8(vint16m2_t src);
vuint16mf4_t __riscv_vlmul_trunc_v_u16m4_u16mf4(vuint16m4_t src);
vuint16mf2_t __riscv_vlmul_trunc_v_u16m4_u16mf2(vuint16m4_t src);
vuint16m1_t __riscv_vlmul_trunc_v_u16m4_u16m1(vuint16m4_t src);
vuint16m2_t __riscv_vlmul_trunc_v_u16m4_u16m2(vuint16m4_t src);
vuint16m8_t __riscv_vlmul_ext_v_u16m4_u16m8(vuint16m4_t src);
vint16mf4_t __riscv_vlmul_trunc_v_i16m4_i16mf4(vint16m4_t src);
vint16mf2_t __riscv_vlmul_trunc_v_i16m4_i16mf2(vint16m4_t src);
vint16m1_t __riscv_vlmul_trunc_v_i16m4_i16m1(vint16m4_t src);
vint16m2_t __riscv_vlmul_trunc_v_i16m4_i16m2(vint16m4_t src);
vint16m8_t __riscv_vlmul_ext_v_i16m4_i16m8(vint16m4_t src);
vuint16mf4_t __riscv_vlmul_trunc_v_u16m8_u16mf4(vuint16m8_t src);
vuint16mf2_t __riscv_vlmul_trunc_v_u16m8_u16mf2(vuint16m8_t src);
vuint16m1_t __riscv_vlmul_trunc_v_u16m8_u16m1(vuint16m8_t src);
vuint16m2_t __riscv_vlmul_trunc_v_u16m8_u16m2(vuint16m8_t src);
vuint16m4_t __riscv_vlmul_trunc_v_u16m8_u16m4(vuint16m8_t src);
vint16mf4_t __riscv_vlmul_trunc_v_i16m8_i16mf4(vint16m8_t src);
vint16mf2_t __riscv_vlmul_trunc_v_i16m8_i16mf2(vint16m8_t src);
vint16m1_t __riscv_vlmul_trunc_v_i16m8_i16m1(vint16m8_t src);
vint16m2_t __riscv_vlmul_trunc_v_i16m8_i16m2(vint16m8_t src);
vint16m4_t __riscv_vlmul_trunc_v_i16m8_i16m4(vint16m8_t src);
vuint32m1_t __riscv_vlmul_ext_v_u32mf2_u32m1(vuint32mf2_t src);
vuint32m2_t __riscv_vlmul_ext_v_u32mf2_u32m2(vuint32mf2_t src);
vuint32m4_t __riscv_vlmul_ext_v_u32mf2_u32m4(vuint32mf2_t src);
vuint32m8_t __riscv_vlmul_ext_v_u32mf2_u32m8(vuint32mf2_t src);
vint32m1_t __riscv_vlmul_ext_v_i32mf2_i32m1(vint32mf2_t src);
vint32m2_t __riscv_vlmul_ext_v_i32mf2_i32m2(vint32mf2_t src);
vint32m4_t __riscv_vlmul_ext_v_i32mf2_i32m4(vint32mf2_t src);
vint32m8_t __riscv_vlmul_ext_v_i32mf2_i32m8(vint32mf2_t src);
vfloat32m1_t __riscv_vlmul_ext_v_f32mf2_f32m1(vfloat32mf2_t src);
vfloat32m2_t __riscv_vlmul_ext_v_f32mf2_f32m2(vfloat32mf2_t src);
vfloat32m4_t __riscv_vlmul_ext_v_f32mf2_f32m4(vfloat32mf2_t src);
vfloat32m8_t __riscv_vlmul_ext_v_f32mf2_f32m8(vfloat32mf2_t src);
vuint32mf2_t __riscv_vlmul_trunc_v_u32m1_u32mf2(vuint32m1_t src);
vuint32m2_t __riscv_vlmul_ext_v_u32m1_u32m2(vuint32m1_t src);
vuint32m4_t __riscv_vlmul_ext_v_u32m1_u32m4(vuint32m1_t src);
vuint32m8_t __riscv_vlmul_ext_v_u32m1_u32m8(vuint32m1_t src);
vint32mf2_t __riscv_vlmul_trunc_v_i32m1_i32mf2(vint32m1_t src);
vint32m2_t __riscv_vlmul_ext_v_i32m1_i32m2(vint32m1_t src);
vint32m4_t __riscv_vlmul_ext_v_i32m1_i32m4(vint32m1_t src);
vint32m8_t __riscv_vlmul_ext_v_i32m1_i32m8(vint32m1_t src);
vfloat32mf2_t __riscv_vlmul_trunc_v_f32m1_f32mf2(vfloat32m1_t src);
vfloat32m2_t __riscv_vlmul_ext_v_f32m1_f32m2(vfloat32m1_t src);
vfloat32m4_t __riscv_vlmul_ext_v_f32m1_f32m4(vfloat32m1_t src);
vfloat32m8_t __riscv_vlmul_ext_v_f32m1_f32m8(vfloat32m1_t src);
vuint32mf2_t __riscv_vlmul_trunc_v_u32m2_u32mf2(vuint32m2_t src);
vuint32m1_t __riscv_vlmul_trunc_v_u32m2_u32m1(vuint32m2_t src);
vuint32m4_t __riscv_vlmul_ext_v_u32m2_u32m4(vuint32m2_t src);
vuint32m8_t __riscv_vlmul_ext_v_u32m2_u32m8(vuint32m2_t src);
vint32mf2_t __riscv_vlmul_trunc_v_i32m2_i32mf2(vint32m2_t src);
vint32m1_t __riscv_vlmul_trunc_v_i32m2_i32m1(vint32m2_t src);
vint32m4_t __riscv_vlmul_ext_v_i32m2_i32m4(vint32m2_t src);
vint32m8_t __riscv_vlmul_ext_v_i32m2_i32m8(vint32m2_t src);
vfloat32mf2_t __riscv_vlmul_trunc_v_f32m2_f32mf2(vfloat32m2_t src);
vfloat32m1_t __riscv_vlmul_trunc_v_f32m2_f32m1(vfloat32m2_t src);
vfloat32m4_t __riscv_vlmul_ext_v_f32m2_f32m4(vfloat32m2_t src);
vfloat32m8_t __riscv_vlmul_ext_v_f32m2_f32m8(vfloat32m2_t src);
vuint32mf2_t __riscv_vlmul_trunc_v_u32m4_u32mf2(vuint32m4_t src);
vuint32m1_t __riscv_vlmul_trunc_v_u32m4_u32m1(vuint32m4_t src);
vuint32m2_t __riscv_vlmul_trunc_v_u32m4_u32m2(vuint32m4_t src);
vuint32m8_t __riscv_vlmul_ext_v_u32m4_u32m8(vuint32m4_t src);
vint32mf2_t __riscv_vlmul_trunc_v_i32m4_i32mf2(vint32m4_t src);
vint32m1_t __riscv_vlmul_trunc_v_i32m4_i32m1(vint32m4_t src);
vint32m2_t __riscv_vlmul_trunc_v_i32m4_i32m2(vint32m4_t src);
vint32m8_t __riscv_vlmul_ext_v_i32m4_i32m8(vint32m4_t src);
vfloat32mf2_t __riscv_vlmul_trunc_v_f32m4_f32mf2(vfloat32m4_t src);
vfloat32m1_t __riscv_vlmul_trunc_v_f32m4_f32m1(vfloat32m4_t src);
vfloat32m2_t __riscv_vlmul_trunc_v_f32m4_f32m2(vfloat32m4_t src);
vfloat32m8_t __riscv_vlmul_ext_v_f32m4_f32m8(vfloat32m4_t src);
vuint32mf2_t __riscv_vlmul_trunc_v_u32m8_u32mf2(vuint32m8_t src);
vuint32m1_t __riscv_vlmul_trunc_v_u32m8_u32m1(vuint32m8_t src);
vuint32m2_t __riscv_vlmul_trunc_v_u32m8_u32m2(vuint32m8_t src);
vuint32m4_t __riscv_vlmul_trunc_v_u32m8_u32m4(vuint32m8_t src);
vint32mf2_t __riscv_vlmul_trunc_v_i32m8_i32mf2(vint32m8_t src);
vint32m1_t __riscv_vlmul_trunc_v_i32m8_i32m1(vint32m8_t src);
vint32m2_t __riscv_vlmul_trunc_v_i32m8_i32m2(vint32m8_t src);
vint32m4_t __riscv_vlmul_trunc_v_i32m8_i32m4(vint32m8_t src);
vfloat32mf2_t __riscv_vlmul_trunc_v_f32m8_f32mf2(vfloat32m8_t src);
vfloat32m1_t __riscv_vlmul_trunc_v_f32m8_f32m1(vfloat32m8_t src);
vfloat32m2_t __riscv_vlmul_trunc_v_f32m8_f32m2(vfloat32m8_t src);
vfloat32m4_t __riscv_vlmul_trunc_v_f32m8_f32m4(vfloat32m8_t src);
vuint64m2_t __riscv_vlmul_ext_v_u64m1_u64m2(vuint64m1_t src);
vuint64m4_t __riscv_vlmul_ext_v_u64m1_u64m4(vuint64m1_t src);
vuint64m8_t __riscv_vlmul_ext_v_u64m1_u64m8(vuint64m1_t src);
vint64m2_t __riscv_vlmul_ext_v_i64m1_i64m2(vint64m1_t src);
vint64m4_t __riscv_vlmul_ext_v_i64m1_i64m4(vint64m1_t src);
vint64m8_t __riscv_vlmul_ext_v_i64m1_i64m8(vint64m1_t src);
vfloat64m2_t __riscv_vlmul_ext_v_f64m1_f64m2(vfloat64m1_t src);
vfloat64m4_t __riscv_vlmul_ext_v_f64m1_f64m4(vfloat64m1_t src);
vfloat64m8_t __riscv_vlmul_ext_v_f64m1_f64m8(vfloat64m1_t src);
vuint64m1_t __riscv_vlmul_trunc_v_u64m2_u64m1(vuint64m2_t src);
vuint64m4_t __riscv_vlmul_ext_v_u64m2_u64m4(vuint64m2_t src);
vuint64m8_t __riscv_vlmul_ext_v_u64m2_u64m8(vuint64m2_t src);
vint64m1_t __riscv_vlmul_trunc_v_i64m2_i64m1(vint64m2_t src);
vint64m4_t __riscv_vlmul_ext_v_i64m2_i64m4(vint64m2_t src);
vint64m8_t __riscv_vlmul_ext_v_i64m2_i64m8(vint64m2_t src);
vfloat64m1_t __riscv_vlmul_trunc_v_f64m2_f64m1(vfloat64m2_t src);
vfloat64m4_t __riscv_vlmul_ext_v_f64m2_f64m4(vfloat64m2_t src);
vfloat64m8_t __riscv_vlmul_ext_v_f64m2_f64m8(vfloat64m2_t src);
vuint64m1_t __riscv_vlmul_trunc_v_u64m4_u64m1(vuint64m4_t src);
vuint64m2_t __riscv_vlmul_trunc_v_u64m4_u64m2(vuint64m4_t src);
vuint64m8_t __riscv_vlmul_ext_v_u64m4_u64m8(vuint64m4_t src);
vint64m1_t __riscv_vlmul_trunc_v_i64m4_i64m1(vint64m4_t src);
vint64m2_t __riscv_vlmul_trunc_v_i64m4_i64m2(vint64m4_t src);
vint64m8_t __riscv_vlmul_ext_v_i64m4_i64m8(vint64m4_t src);
vfloat64m1_t __riscv_vlmul_trunc_v_f64m4_f64m1(vfloat64m4_t src);
vfloat64m2_t __riscv_vlmul_trunc_v_f64m4_f64m2(vfloat64m4_t src);
vfloat64m8_t __riscv_vlmul_ext_v_f64m4_f64m8(vfloat64m4_t src);
vuint64m1_t __riscv_vlmul_trunc_v_u64m8_u64m1(vuint64m8_t src);
vuint64m2_t __riscv_vlmul_trunc_v_u64m8_u64m2(vuint64m8_t src);
vuint64m4_t __riscv_vlmul_trunc_v_u64m8_u64m4(vuint64m8_t src);
vint64m1_t __riscv_vlmul_trunc_v_i64m8_i64m1(vint64m8_t src);
vint64m2_t __riscv_vlmul_trunc_v_i64m8_i64m2(vint64m8_t src);
vint64m4_t __riscv_vlmul_trunc_v_i64m8_i64m4(vint64m8_t src);
vfloat64m1_t __riscv_vlmul_trunc_v_f64m8_f64m1(vfloat64m8_t src);
vfloat64m2_t __riscv_vlmul_trunc_v_f64m8_f64m2(vfloat64m8_t src);
vfloat64m4_t __riscv_vlmul_trunc_v_f64m8_f64m4(vfloat64m8_t src);


/* The segment tuples, which hold one vector per field of the element */
vuint8mf8x2_t __riscv_vundefined_u8mf8x2(void);
vuint8mf8x2_t __riscv_vcreate_v_u8mf8_u8mf8x2(vuint8mf8_t v0, vuint8mf8_t v1);
vuint8mf8_t __riscv_vget_v_u8mf8x2_u8mf8(vuint8mf8x2_t src, size_t index);
vuint8mf8x2_t __riscv_vset_v_u8mf8x2_u8mf8(vuint8mf8x2_t dest, size_t index, vuint8mf8_t value);
vuint8mf8x2_t __riscv_vlseg2e8_v_u8mf8x2(const unsigned char *rs1, size_t vl);
void __riscv_vsseg2e8_v_u8mf8x2(unsigned char *rs1, vuint8mf8x2_t vs3, size_t vl);
vuint8mf8x2_t __riscv_vlseg2e8ff_v_u8mf8x2(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf8x2_t __riscv_vlsseg2e8_v_u8mf8x2(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_u8mf8x2(unsigned char *rs1, long rs2, vuint8mf8x2_t vs3, size_t vl);
vuint8mf8x2_t __riscv_vluxseg2ei8_v_u8mf8x2(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
vuint8mf8x2_t __riscv_vloxseg2ei8_v_u8mf8x2(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u8mf8x2(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u8mf8x2(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x2_t vs3, size_t vl);
vuint8mf8x2_t __riscv_vluxseg2ei16_v_u8mf8x2(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
vuint8mf8x2_t __riscv_vloxseg2ei16_v_u8mf8x2(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u8mf8x2(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u8mf8x2(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x2_t vs3, size_t vl);
vuint8mf8x2_t __riscv_vluxseg2ei32_v_u8mf8x2(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
vuint8mf8x2_t __riscv_vloxseg2ei32_v_u8mf8x2(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u8mf8x2(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u8mf8x2(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x2_t vs3, size_t vl);
vuint8mf8x2_t __riscv_vluxseg2ei64_v_u8mf8x2(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
vuint8mf8x2_t __riscv_vloxseg2ei64_v_u8mf8x2(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u8mf8x2(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u8mf8x2(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x2_t vs3, size_t vl);
vint8mf8x2_t __riscv_vundefined_i8mf8x2(void);
vint8mf8x2_t __riscv_vcreate_v_i8mf8_i8mf8x2(vint8mf8_t v0, vint8mf8_t v1);
vint8mf8_t __riscv_vget_v_i8mf8x2_i8mf8(vint8mf8x2_t src, size_t index);
vint8mf8x2_t __riscv_vset_v_i8mf8x2_i8mf8(vint8mf8x2_t dest, size_t index, vint8mf8_t value);
vint8mf8x2_t __riscv_vlseg2e8_v_i8mf8x2(const signed char *rs1, size_t vl);
void __riscv_vsseg2e8_v_i8mf8x2(signed char *rs1, vint8mf8x2_t vs3, size_t vl);
vint8mf8x2_t __riscv_vlseg2e8ff_v_i8mf8x2(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf8x2_t __riscv_vlsseg2e8_v_i8mf8x2(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_i8mf8x2(signed char *rs1, long rs2, vint8mf8x2_t vs3, size_t vl);
vint8mf8x2_t __riscv_vluxseg2ei8_v_i8mf8x2(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
vint8mf8x2_t __riscv_vloxseg2ei8_v_i8mf8x2(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i8mf8x2(signed char *rs1, vuint8mf8_t rs2, vint8mf8x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i8mf8x2(signed char *rs1, vuint8mf8_t rs2, vint8mf8x2_t vs3, size_t vl);
vint8mf8x2_t __riscv_vluxseg2ei16_v_i8mf8x2(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
vint8mf8x2_t __riscv_vloxseg2ei16_v_i8mf8x2(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i8mf8x2(signed char *rs1, vuint16mf4_t rs2, vint8mf8x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i8mf8x2(signed char *rs1, vuint16mf4_t rs2, vint8mf8x2_t vs3, size_t vl);
vint8mf8x2_t __riscv_vluxseg2ei32_v_i8mf8x2(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
vint8mf8x2_t __riscv_vloxseg2ei32_v_i8mf8x2(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i8mf8x2(signed char *rs1, vuint32mf2_t rs2, vint8mf8x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i8mf8x2(signed char *rs1, vuint32mf2_t rs2, vint8mf8x2_t vs3, size_t vl);
vint8mf8x2_t __riscv_vluxseg2ei64_v_i8mf8x2(const signed char *rs1, vuint64m1_t rs2, size_t vl);
vint8mf8x2_t __riscv_vloxseg2ei64_v_i8mf8x2(const signed char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i8mf8x2(signed char *rs1, vuint64m1_t rs2, vint8mf8x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i8mf8x2(signed char *rs1, vuint64m1_t rs2, vint8mf8x2_t vs3, size_t vl);
vuint8mf8x3_t __riscv_vundefined_u8mf8x3(void);
vuint8mf8x3_t __riscv_vcreate_v_u8mf8_u8mf8x3(vuint8mf8_t v0, vuint8mf8_t v1, vuint8mf8_t v2);
vuint8mf8_t __riscv_vget_v_u8mf8x3_u8mf8(vuint8mf8x3_t src, size_t index);
vuint8mf8x3_t __riscv_vset_v_u8mf8x3_u8mf8(vuint8mf8x3_t dest, size_t index, vuint8mf8_t value);
vuint8mf8x3_t __riscv_vlseg3e8_v_u8mf8x3(const unsigned char *rs1, size_t vl);
void __riscv_vsseg3e8_v_u8mf8x3(unsigned char *rs1, vuint8mf8x3_t vs3, size_t vl);
vuint8mf8x3_t __riscv_vlseg3e8ff_v_u8mf8x3(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf8x3_t __riscv_vlsseg3e8_v_u8mf8x3(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_u8mf8x3(unsigned char *rs1, long rs2, vuint8mf8x3_t vs3, size_t vl);
vuint8mf8x3_t __riscv_vluxseg3ei8_v_u8mf8x3(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
vuint8mf8x3_t __riscv_vloxseg3ei8_v_u8mf8x3(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u8mf8x3(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u8mf8x3(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x3_t vs3, size_t vl);
vuint8mf8x3_t __riscv_vluxseg3ei16_v_u8mf8x3(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
vuint8mf8x3_t __riscv_vloxseg3ei16_v_u8mf8x3(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u8mf8x3(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u8mf8x3(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x3_t vs3, size_t vl);
vuint8mf8x3_t __riscv_vluxseg3ei32_v_u8mf8x3(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
vuint8mf8x3_t __riscv_vloxseg3ei32_v_u8mf8x3(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u8mf8x3(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u8mf8x3(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x3_t vs3, size_t vl);
vuint8mf8x3_t __riscv_vluxseg3ei64_v_u8mf8x3(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
vuint8mf8x3_t __riscv_vloxseg3ei64_v_u8mf8x3(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u8mf8x3(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u8mf8x3(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x3_t vs3, size_t vl);
vint8mf8x3_t __riscv_vundefined_i8mf8x3(void);
vint8mf8x3_t __riscv_vcreate_v_i8mf8_i8mf8x3(vint8mf8_t v0, vint8mf8_t v1, vint8mf8_t v2);
vint8mf8_t __riscv_vget_v_i8mf8x3_i8mf8(vint8mf8x3_t src, size_t index);
vint8mf8x3_t __riscv_vset_v_i8mf8x3_i8mf8(vint8mf8x3_t dest, size_t index, vint8mf8_t value);
vint8mf8x3_t __riscv_vlseg3e8_v_i8mf8x3(const signed char *rs1, size_t vl);
void __riscv_vsseg3e8_v_i8mf8x3(signed char *rs1, vint8mf8x3_t vs3, size_t vl);
vint8mf8x3_t __riscv_vlseg3e8ff_v_i8mf8x3(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf8x3_t __riscv_vlsseg3e8_v_i8mf8x3(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_i8mf8x3(signed char *rs1, long rs2, vint8mf8x3_t vs3, size_t vl);
vint8mf8x3_t __riscv_vluxseg3ei8_v_i8mf8x3(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
vint8mf8x3_t __riscv_vloxseg3ei8_v_i8mf8x3(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i8mf8x3(signed char *rs1, vuint8mf8_t rs2, vint8mf8x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i8mf8x3(signed char *rs1, vuint8mf8_t rs2, vint8mf8x3_t vs3, size_t vl);
vint8mf8x3_t __riscv_vluxseg3ei16_v_i8mf8x3(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
vint8mf8x3_t __riscv_vloxseg3ei16_v_i8mf8x3(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i8mf8x3(signed char *rs1, vuint16mf4_t rs2, vint8mf8x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i8mf8x3(signed char *rs1, vuint16mf4_t rs2, vint8mf8x3_t vs3, size_t vl);
vint8mf8x3_t __riscv_vluxseg3ei32_v_i8mf8x3(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
vint8mf8x3_t __riscv_vloxseg3ei32_v_i8mf8x3(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i8mf8x3(signed char *rs1, vuint32mf2_t rs2, vint8mf8x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i8mf8x3(signed char *rs1, vuint32mf2_t rs2, vint8mf8x3_t vs3, size_t vl);
vint8mf8x3_t __riscv_vluxseg3ei64_v_i8mf8x3(const signed char *rs1, vuint64m1_t rs2, size_t vl);
vint8mf8x3_t __riscv_vloxseg3ei64_v_i8mf8x3(const signed char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i8mf8x3(signed char *rs1, vuint64m1_t rs2, vint8mf8x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i8mf8x3(signed char *rs1, vuint64m1_t rs2, vint8mf8x3_t vs3, size_t vl);
vuint8mf8x4_t __riscv_vundefined_u8mf8x4(void);
vuint8mf8x4_t __riscv_vcreate_v_u8mf8_u8mf8x4(vuint8mf8_t v0, vuint8mf8_t v1, vuint8mf8_t v2, vuint8mf8_t v3);
vuint8mf8_t __riscv_vget_v_u8mf8x4_u8mf8(vuint8mf8x4_t src, size_t index);
vuint8mf8x4_t __riscv_vset_v_u8mf8x4_u8mf8(vuint8mf8x4_t dest, size_t index, vuint8mf8_t value);
vuint8mf8x4_t __riscv_vlseg4e8_v_u8mf8x4(const unsigned char *rs1, size_t vl);
void __riscv_vsseg4e8_v_u8mf8x4(unsigned char *rs1, vuint8mf8x4_t vs3, size_t vl);
vuint8mf8x4_t __riscv_vlseg4e8ff_v_u8mf8x4(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf8x4_t __riscv_vlsseg4e8_v_u8mf8x4(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_u8mf8x4(unsigned char *rs1, long rs2, vuint8mf8x4_t vs3, size_t vl);
vuint8mf8x4_t __riscv_vluxseg4ei8_v_u8mf8x4(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
vuint8mf8x4_t __riscv_vloxseg4ei8_v_u8mf8x4(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u8mf8x4(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u8mf8x4(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x4_t vs3, size_t vl);
vuint8mf8x4_t __riscv_vluxseg4ei16_v_u8mf8x4(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
vuint8mf8x4_t __riscv_vloxseg4ei16_v_u8mf8x4(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u8mf8x4(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u8mf8x4(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x4_t vs3, size_t vl);
vuint8mf8x4_t __riscv_vluxseg4ei32_v_u8mf8x4(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
vuint8mf8x4_t __riscv_vloxseg4ei32_v_u8mf8x4(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u8mf8x4(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u8mf8x4(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x4_t vs3, size_t vl);
vuint8mf8x4_t __riscv_vluxseg4ei64_v_u8mf8x4(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
vuint8mf8x4_t __riscv_vloxseg4ei64_v_u8mf8x4(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u8mf8x4(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u8mf8x4(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x4_t vs3, size_t vl);
vint8mf8x4_t __riscv_vundefined_i8mf8x4(void);
vint8mf8x4_t __riscv_vcreate_v_i8mf8_i8mf8x4(vint8mf8_t v0, vint8mf8_t v1, vint8mf8_t v2, vint8mf8_t v3);
vint8mf8_t __riscv_vget_v_i8mf8x4_i8mf8(vint8mf8x4_t src, size_t index);
vint8mf8x4_t __riscv_vset_v_i8mf8x4_i8mf8(vint8mf8x4_t dest, size_t index, vint8mf8_t value);
vint8mf8x4_t __riscv_vlseg4e8_v_i8mf8x4(const signed char *rs1, size_t vl);
void __riscv_vsseg4e8_v_i8mf8x4(signed char *rs1, vint8mf8x4_t vs3, size_t vl);
vint8mf8x4_t __riscv_vlseg4e8ff_v_i8mf8x4(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf8x4_t __riscv_vlsseg4e8_v_i8mf8x4(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_i8mf8x4(signed char *rs1, long rs2, vint8mf8x4_t vs3, size_t vl);
vint8mf8x4_t __riscv_vluxseg4ei8_v_i8mf8x4(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
vint8mf8x4_t __riscv_vloxseg4ei8_v_i8mf8x4(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i8mf8x4(signed char *rs1, vuint8mf8_t rs2, vint8mf8x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i8mf8x4(signed char *rs1, vuint8mf8_t rs2, vint8mf8x4_t vs3, size_t vl);
vint8mf8x4_t __riscv_vluxseg4ei16_v_i8mf8x4(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
vint8mf8x4_t __riscv_vloxseg4ei16_v_i8mf8x4(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i8mf8x4(signed char *rs1, vuint16mf4_t rs2, vint8mf8x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i8mf8x4(signed char *rs1, vuint16mf4_t rs2, vint8mf8x4_t vs3, size_t vl);
vint8mf8x4_t __riscv_vluxseg4ei32_v_i8mf8x4(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
vint8mf8x4_t __riscv_vloxseg4ei32_v_i8mf8x4(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i8mf8x4(signed char *rs1, vuint32mf2_t rs2, vint8mf8x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i8mf8x4(signed char *rs1, vuint32mf2_t rs2, vint8mf8x4_t vs3, size_t vl);
vint8mf8x4_t __riscv_vluxseg4ei64_v_i8mf8x4(const signed char *rs1, vuint64m1_t rs2, size_t vl);
vint8mf8x4_t __riscv_vloxseg4ei64_v_i8mf8x4(const signed char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i8mf8x4(signed char *rs1, vuint64m1_t rs2, vint8mf8x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i8mf8x4(signed char *rs1, vuint64m1_t rs2, vint8mf8x4_t vs3, size_t vl);
vuint8mf8x5_t __riscv_vundefined_u8mf8x5(void);
vuint8mf8x5_t __riscv_vcreate_v_u8mf8_u8mf8x5(vuint8mf8_t v0, vuint8mf8_t v1, vuint8mf8_t v2, vuint8mf8_t v3, vuint8mf8_t v4);
vuint8mf8_t __riscv_vget_v_u8mf8x5_u8mf8(vuint8mf8x5_t src, size_t index);
vuint8mf8x5_t __riscv_vset_v_u8mf8x5_u8mf8(vuint8mf8x5_t dest, size_t index, vuint8mf8_t value);
vuint8mf8x5_t __riscv_vlseg5e8_v_u8mf8x5(const unsigned char *rs1, size_t vl);
void __riscv_vsseg5e8_v_u8mf8x5(unsigned char *rs1, vuint8mf8x5_t vs3, size_t vl);
vuint8mf8x5_t __riscv_vlseg5e8ff_v_u8mf8x5(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf8x5_t __riscv_vlsseg5e8_v_u8mf8x5(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg5e8_v_u8mf8x5(unsigned char *rs1, long rs2, vuint8mf8x5_t vs3, size_t vl);
vuint8mf8x5_t __riscv_vluxseg5ei8_v_u8mf8x5(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
vuint8mf8x5_t __riscv_vloxseg5ei8_v_u8mf8x5(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u8mf8x5(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u8mf8x5(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x5_t vs3, size_t vl);
vuint8mf8x5_t __riscv_vluxseg5ei16_v_u8mf8x5(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
vuint8mf8x5_t __riscv_vloxseg5ei16_v_u8mf8x5(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u8mf8x5(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u8mf8x5(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x5_t vs3, size_t vl);
vuint8mf8x5_t __riscv_vluxseg5ei32_v_u8mf8x5(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
vuint8mf8x5_t __riscv_vloxseg5ei32_v_u8mf8x5(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u8mf8x5(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u8mf8x5(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x5_t vs3, size_t vl);
vuint8mf8x5_t __riscv_vluxseg5ei64_v_u8mf8x5(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
vuint8mf8x5_t __riscv_vloxseg5ei64_v_u8mf8x5(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u8mf8x5(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u8mf8x5(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x5_t vs3, size_t vl);
vint8mf8x5_t __riscv_vundefined_i8mf8x5(void);
vint8mf8x5_t __riscv_vcreate_v_i8mf8_i8mf8x5(vint8mf8_t v0, vint8mf8_t v1, vint8mf8_t v2, vint8mf8_t v3, vint8mf8_t v4);
vint8mf8_t __riscv_vget_v_i8mf8x5_i8mf8(vint8mf8x5_t src, size_t index);
vint8mf8x5_t __riscv_vset_v_i8mf8x5_i8mf8(vint8mf8x5_t dest, size_t index, vint8mf8_t value);
vint8mf8x5_t __riscv_vlseg5e8_v_i8mf8x5(const signed char *rs1, size_t vl);
void __riscv_vsseg5e8_v_i8mf8x5(signed char *rs1, vint8mf8x5_t vs3, size_t vl);
vint8mf8x5_t __riscv_vlseg5e8ff_v_i8mf8x5(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf8x5_t __riscv_vlsseg5e8_v_i8mf8x5(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg5e8_v_i8mf8x5(signed char *rs1, long rs2, vint8mf8x5_t vs3, size_t vl);
vint8mf8x5_t __riscv_vluxseg5ei8_v_i8mf8x5(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
vint8mf8x5_t __riscv_vloxseg5ei8_v_i8mf8x5(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i8mf8x5(signed char *rs1, vuint8mf8_t rs2, vint8mf8x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i8mf8x5(signed char *rs1, vuint8mf8_t rs2, vint8mf8x5_t vs3, size_t vl);
vint8mf8x5_t __riscv_vluxseg5ei16_v_i8mf8x5(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
vint8mf8x5_t __riscv_vloxseg5ei16_v_i8mf8x5(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i8mf8x5(signed char *rs1, vuint16mf4_t rs2, vint8mf8x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i8mf8x5(signed char *rs1, vuint16mf4_t rs2, vint8mf8x5_t vs3, size_t vl);
vint8mf8x5_t __riscv_vluxseg5ei32_v_i8mf8x5(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
vint8mf8x5_t __riscv_vloxseg5ei32_v_i8mf8x5(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i8mf8x5(signed char *rs1, vuint32mf2_t rs2, vint8mf8x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i8mf8x5(signed char *rs1, vuint32mf2_t rs2, vint8mf8x5_t vs3, size_t vl);
vint8mf8x5_t __riscv_vluxseg5ei64_v_i8mf8x5(const signed char *rs1, vuint64m1_t rs2, size_t vl);
vint8mf8x5_t __riscv_vloxseg5ei64_v_i8mf8x5(const signed char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i8mf8x5(signed char *rs1, vuint64m1_t rs2, vint8mf8x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i8mf8x5(signed char *rs1, vuint64m1_t rs2, vint8mf8x5_t vs3, size_t vl);
vuint8mf8x6_t __riscv_vundefined_u8mf8x6(void);
vuint8mf8x6_t __riscv_vcreate_v_u8mf8_u8mf8x6(vuint8mf8_t v0, vuint8mf8_t v1, vuint8mf8_t v2, vuint8mf8_t v3, vuint8mf8_t v4, vuint8mf8_t v5);
vuint8mf8_t __riscv_vget_v_u8mf8x6_u8mf8(vuint8mf8x6_t src, size_t index);
vuint8mf8x6_t __riscv_vset_v_u8mf8x6_u8mf8(vuint8mf8x6_t dest, size_t index, vuint8mf8_t value);
vuint8mf8x6_t __riscv_vlseg6e8_v_u8mf8x6(const unsigned char *rs1, size_t vl);
void __riscv_vsseg6e8_v_u8mf8x6(unsigned char *rs1, vuint8mf8x6_t vs3, size_t vl);
vuint8mf8x6_t __riscv_vlseg6e8ff_v_u8mf8x6(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf8x6_t __riscv_vlsseg6e8_v_u8mf8x6(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg6e8_v_u8mf8x6(unsigned char *rs1, long rs2, vuint8mf8x6_t vs3, size_t vl);
vuint8mf8x6_t __riscv_vluxseg6ei8_v_u8mf8x6(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
vuint8mf8x6_t __riscv_vloxseg6ei8_v_u8mf8x6(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u8mf8x6(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u8mf8x6(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x6_t vs3, size_t vl);
vuint8mf8x6_t __riscv_vluxseg6ei16_v_u8mf8x6(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
vuint8mf8x6_t __riscv_vloxseg6ei16_v_u8mf8x6(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u8mf8x6(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u8mf8x6(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x6_t vs3, size_t vl);
vuint8mf8x6_t __riscv_vluxseg6ei32_v_u8mf8x6(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
vuint8mf8x6_t __riscv_vloxseg6ei32_v_u8mf8x6(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u8mf8x6(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u8mf8x6(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x6_t vs3, size_t vl);
vuint8mf8x6_t __riscv_vluxseg6ei64_v_u8mf8x6(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
vuint8mf8x6_t __riscv_vloxseg6ei64_v_u8mf8x6(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u8mf8x6(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u8mf8x6(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x6_t vs3, size_t vl);
vint8mf8x6_t __riscv_vundefined_i8mf8x6(void);
vint8mf8x6_t __riscv_vcreate_v_i8mf8_i8mf8x6(vint8mf8_t v0, vint8mf8_t v1, vint8mf8_t v2, vint8mf8_t v3, vint8mf8_t v4, vint8mf8_t v5);
vint8mf8_t __riscv_vget_v_i8mf8x6_i8mf8(vint8mf8x6_t src, size_t index);
vint8mf8x6_t __riscv_vset_v_i8mf8x6_i8mf8(vint8mf8x6_t dest, size_t index, vint8mf8_t value);
vint8mf8x6_t __riscv_vlseg6e8_v_i8mf8x6(const signed char *rs1, size_t vl);
void __riscv_vsseg6e8_v_i8mf8x6(signed char *rs1, vint8mf8x6_t vs3, size_t vl);
vint8mf8x6_t __riscv_vlseg6e8ff_v_i8mf8x6(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf8x6_t __riscv_vlsseg6e8_v_i8mf8x6(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg6e8_v_i8mf8x6(signed char *rs1, long rs2, vint8mf8x6_t vs3, size_t vl);
vint8mf8x6_t __riscv_vluxseg6ei8_v_i8mf8x6(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
vint8mf8x6_t __riscv_vloxseg6ei8_v_i8mf8x6(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i8mf8x6(signed char *rs1, vuint8mf8_t rs2, vint8mf8x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i8mf8x6(signed char *rs1, vuint8mf8_t rs2, vint8mf8x6_t vs3, size_t vl);
vint8mf8x6_t __riscv_vluxseg6ei16_v_i8mf8x6(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
vint8mf8x6_t __riscv_vloxseg6ei16_v_i8mf8x6(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i8mf8x6(signed char *rs1, vuint16mf4_t rs2, vint8mf8x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i8mf8x6(signed char *rs1, vuint16mf4_t rs2, vint8mf8x6_t vs3, size_t vl);
vint8mf8x6_t __riscv_vluxseg6ei32_v_i8mf8x6(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
vint8mf8x6_t __riscv_vloxseg6ei32_v_i8mf8x6(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i8mf8x6(signed char *rs1, vuint32mf2_t rs2, vint8mf8x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i8mf8x6(signed char *rs1, vuint32mf2_t rs2, vint8mf8x6_t vs3, size_t vl);
vint8mf8x6_t __riscv_vluxseg6ei64_v_i8mf8x6(const signed char *rs1, vuint64m1_t rs2, size_t vl);
vint8mf8x6_t __riscv_vloxseg6ei64_v_i8mf8x6(const signed char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i8mf8x6(signed char *rs1, vuint64m1_t rs2, vint8mf8x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i8mf8x6(signed char *rs1, vuint64m1_t rs2, vint8mf8x6_t vs3, size_t vl);
vuint8mf8x7_t __riscv_vundefined_u8mf8x7(void);
vuint8mf8x7_t __riscv_vcreate_v_u8mf8_u8mf8x7(vuint8mf8_t v0, vuint8mf8_t v1, vuint8mf8_t v2, vuint8mf8_t v3, vuint8mf8_t v4, vuint8mf8_t v5, vuint8mf8_t v6);
vuint8mf8_t __riscv_vget_v_u8mf8x7_u8mf8(vuint8mf8x7_t src, size_t index);
vuint8mf8x7_t __riscv_vset_v_u8mf8x7_u8mf8(vuint8mf8x7_t dest, size_t index, vuint8mf8_t value);
vuint8mf8x7_t __riscv_vlseg7e8_v_u8mf8x7(const unsigned char *rs1, size_t vl);
void __riscv_vsseg7e8_v_u8mf8x7(unsigned char *rs1, vuint8mf8x7_t vs3, size_t vl);
vuint8mf8x7_t __riscv_vlseg7e8ff_v_u8mf8x7(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf8x7_t __riscv_vlsseg7e8_v_u8mf8x7(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg7e8_v_u8mf8x7(unsigned char *rs1, long rs2, vuint8mf8x7_t vs3, size_t vl);
vuint8mf8x7_t __riscv_vluxseg7ei8_v_u8mf8x7(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
vuint8mf8x7_t __riscv_vloxseg7ei8_v_u8mf8x7(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u8mf8x7(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u8mf8x7(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x7_t vs3, size_t vl);
vuint8mf8x7_t __riscv_vluxseg7ei16_v_u8mf8x7(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
vuint8mf8x7_t __riscv_vloxseg7ei16_v_u8mf8x7(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u8mf8x7(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u8mf8x7(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x7_t vs3, size_t vl);
vuint8mf8x7_t __riscv_vluxseg7ei32_v_u8mf8x7(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
vuint8mf8x7_t __riscv_vloxseg7ei32_v_u8mf8x7(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u8mf8x7(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u8mf8x7(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x7_t vs3, size_t vl);
vuint8mf8x7_t __riscv_vluxseg7ei64_v_u8mf8x7(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
vuint8mf8x7_t __riscv_vloxseg7ei64_v_u8mf8x7(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u8mf8x7(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u8mf8x7(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x7_t vs3, size_t vl);
vint8mf8x7_t __riscv_vundefined_i8mf8x7(void);
vint8mf8x7_t __riscv_vcreate_v_i8mf8_i8mf8x7(vint8mf8_t v0, vint8mf8_t v1, vint8mf8_t v2, vint8mf8_t v3, vint8mf8_t v4, vint8mf8_t v5, vint8mf8_t v6);
vint8mf8_t __riscv_vget_v_i8mf8x7_i8mf8(vint8mf8x7_t src, size_t index);
vint8mf8x7_t __riscv_vset_v_i8mf8x7_i8mf8(vint8mf8x7_t dest, size_t index, vint8mf8_t value);
vint8mf8x7_t __riscv_vlseg7e8_v_i8mf8x7(const signed char *rs1, size_t vl);
void __riscv_vsseg7e8_v_i8mf8x7(signed char *rs1, vint8mf8x7_t vs3, size_t vl);
vint8mf8x7_t __riscv_vlseg7e8ff_v_i8mf8x7(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf8x7_t __riscv_vlsseg7e8_v_i8mf8x7(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg7e8_v_i8mf8x7(signed char *rs1, long rs2, vint8mf8x7_t vs3, size_t vl);
vint8mf8x7_t __riscv_vluxseg7ei8_v_i8mf8x7(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
vint8mf8x7_t __riscv_vloxseg7ei8_v_i8mf8x7(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i8mf8x7(signed char *rs1, vuint8mf8_t rs2, vint8mf8x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i8mf8x7(signed char *rs1, vuint8mf8_t rs2, vint8mf8x7_t vs3, size_t vl);
vint8mf8x7_t __riscv_vluxseg7ei16_v_i8mf8x7(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
vint8mf8x7_t __riscv_vloxseg7ei16_v_i8mf8x7(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i8mf8x7(signed char *rs1, vuint16mf4_t rs2, vint8mf8x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i8mf8x7(signed char *rs1, vuint16mf4_t rs2, vint8mf8x7_t vs3, size_t vl);
vint8mf8x7_t __riscv_vluxseg7ei32_v_i8mf8x7(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
vint8mf8x7_t __riscv_vloxseg7ei32_v_i8mf8x7(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i8mf8x7(signed char *rs1, vuint32mf2_t rs2, vint8mf8x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i8mf8x7(signed char *rs1, vuint32mf2_t rs2, vint8mf8x7_t vs3, size_t vl);
vint8mf8x7_t __riscv_vluxseg7ei64_v_i8mf8x7(const signed char *rs1, vuint64m1_t rs2, size_t vl);
vint8mf8x7_t __riscv_vloxseg7ei64_v_i8mf8x7(const signed char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i8mf8x7(signed char *rs1, vuint64m1_t rs2, vint8mf8x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i8mf8x7(signed char *rs1, vuint64m1_t rs2, vint8mf8x7_t vs3, size_t vl);
vuint8mf8x8_t __riscv_vundefined_u8mf8x8(void);
vuint8mf8x8_t __riscv_vcreate_v_u8mf8_u8mf8x8(vuint8mf8_t v0, vuint8mf8_t v1, vuint8mf8_t v2, vuint8mf8_t v3, vuint8mf8_t v4, vuint8mf8_t v5, vuint8mf8_t v6, vuint8mf8_t v7);
vuint8mf8_t __riscv_vget_v_u8mf8x8_u8mf8(vuint8mf8x8_t src, size_t index);
vuint8mf8x8_t __riscv_vset_v_u8mf8x8_u8mf8(vuint8mf8x8_t dest, size_t index, vuint8mf8_t value);
vuint8mf8x8_t __riscv_vlseg8e8_v_u8mf8x8(const unsigned char *rs1, size_t vl);
void __riscv_vsseg8e8_v_u8mf8x8(unsigned char *rs1, vuint8mf8x8_t vs3, size_t vl);
vuint8mf8x8_t __riscv_vlseg8e8ff_v_u8mf8x8(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf8x8_t __riscv_vlsseg8e8_v_u8mf8x8(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg8e8_v_u8mf8x8(unsigned char *rs1, long rs2, vuint8mf8x8_t vs3, size_t vl);
vuint8mf8x8_t __riscv_vluxseg8ei8_v_u8mf8x8(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
vuint8mf8x8_t __riscv_vloxseg8ei8_v_u8mf8x8(const unsigned char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u8mf8x8(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u8mf8x8(unsigned char *rs1, vuint8mf8_t rs2, vuint8mf8x8_t vs3, size_t vl);
vuint8mf8x8_t __riscv_vluxseg8ei16_v_u8mf8x8(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
vuint8mf8x8_t __riscv_vloxseg8ei16_v_u8mf8x8(const unsigned char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u8mf8x8(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u8mf8x8(unsigned char *rs1, vuint16mf4_t rs2, vuint8mf8x8_t vs3, size_t vl);
vuint8mf8x8_t __riscv_vluxseg8ei32_v_u8mf8x8(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
vuint8mf8x8_t __riscv_vloxseg8ei32_v_u8mf8x8(const unsigned char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u8mf8x8(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u8mf8x8(unsigned char *rs1, vuint32mf2_t rs2, vuint8mf8x8_t vs3, size_t vl);
vuint8mf8x8_t __riscv_vluxseg8ei64_v_u8mf8x8(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
vuint8mf8x8_t __riscv_vloxseg8ei64_v_u8mf8x8(const unsigned char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u8mf8x8(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u8mf8x8(unsigned char *rs1, vuint64m1_t rs2, vuint8mf8x8_t vs3, size_t vl);
vint8mf8x8_t __riscv_vundefined_i8mf8x8(void);
vint8mf8x8_t __riscv_vcreate_v_i8mf8_i8mf8x8(vint8mf8_t v0, vint8mf8_t v1, vint8mf8_t v2, vint8mf8_t v3, vint8mf8_t v4, vint8mf8_t v5, vint8mf8_t v6, vint8mf8_t v7);
vint8mf8_t __riscv_vget_v_i8mf8x8_i8mf8(vint8mf8x8_t src, size_t index);
vint8mf8x8_t __riscv_vset_v_i8mf8x8_i8mf8(vint8mf8x8_t dest, size_t index, vint8mf8_t value);
vint8mf8x8_t __riscv_vlseg8e8_v_i8mf8x8(const signed char *rs1, size_t vl);
void __riscv_vsseg8e8_v_i8mf8x8(signed char *rs1, vint8mf8x8_t vs3, size_t vl);
vint8mf8x8_t __riscv_vlseg8e8ff_v_i8mf8x8(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf8x8_t __riscv_vlsseg8e8_v_i8mf8x8(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg8e8_v_i8mf8x8(signed char *rs1, long rs2, vint8mf8x8_t vs3, size_t vl);
vint8mf8x8_t __riscv_vluxseg8ei8_v_i8mf8x8(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
vint8mf8x8_t __riscv_vloxseg8ei8_v_i8mf8x8(const signed char *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i8mf8x8(signed char *rs1, vuint8mf8_t rs2, vint8mf8x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i8mf8x8(signed char *rs1, vuint8mf8_t rs2, vint8mf8x8_t vs3, size_t vl);
vint8mf8x8_t __riscv_vluxseg8ei16_v_i8mf8x8(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
vint8mf8x8_t __riscv_vloxseg8ei16_v_i8mf8x8(const signed char *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i8mf8x8(signed char *rs1, vuint16mf4_t rs2, vint8mf8x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i8mf8x8(signed char *rs1, vuint16mf4_t rs2, vint8mf8x8_t vs3, size_t vl);
vint8mf8x8_t __riscv_vluxseg8ei32_v_i8mf8x8(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
vint8mf8x8_t __riscv_vloxseg8ei32_v_i8mf8x8(const signed char *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i8mf8x8(signed char *rs1, vuint32mf2_t rs2, vint8mf8x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i8mf8x8(signed char *rs1, vuint32mf2_t rs2, vint8mf8x8_t vs3, size_t vl);
vint8mf8x8_t __riscv_vluxseg8ei64_v_i8mf8x8(const signed char *rs1, vuint64m1_t rs2, size_t vl);
vint8mf8x8_t __riscv_vloxseg8ei64_v_i8mf8x8(const signed char *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i8mf8x8(signed char *rs1, vuint64m1_t rs2, vint8mf8x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i8mf8x8(signed char *rs1, vuint64m1_t rs2, vint8mf8x8_t vs3, size_t vl);
vuint8mf4x2_t __riscv_vundefined_u8mf4x2(void);
vuint8mf4x2_t __riscv_vcreate_v_u8mf4_u8mf4x2(vuint8mf4_t v0, vuint8mf4_t v1);
vuint8mf4_t __riscv_vget_v_u8mf4x2_u8mf4(vuint8mf4x2_t src, size_t index);
vuint8mf4x2_t __riscv_vset_v_u8mf4x2_u8mf4(vuint8mf4x2_t dest, size_t index, vuint8mf4_t value);
vuint8mf4x2_t __riscv_vlseg2e8_v_u8mf4x2(const unsigned char *rs1, size_t vl);
void __riscv_vsseg2e8_v_u8mf4x2(unsigned char *rs1, vuint8mf4x2_t vs3, size_t vl);
vuint8mf4x2_t __riscv_vlseg2e8ff_v_u8mf4x2(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf4x2_t __riscv_vlsseg2e8_v_u8mf4x2(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_u8mf4x2(unsigned char *rs1, long rs2, vuint8mf4x2_t vs3, size_t vl);
vuint8mf4x2_t __riscv_vluxseg2ei8_v_u8mf4x2(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
vuint8mf4x2_t __riscv_vloxseg2ei8_v_u8mf4x2(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u8mf4x2(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u8mf4x2(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x2_t vs3, size_t vl);
vuint8mf4x2_t __riscv_vluxseg2ei16_v_u8mf4x2(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
vuint8mf4x2_t __riscv_vloxseg2ei16_v_u8mf4x2(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u8mf4x2(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u8mf4x2(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x2_t vs3, size_t vl);
vuint8mf4x2_t __riscv_vluxseg2ei32_v_u8mf4x2(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
vuint8mf4x2_t __riscv_vloxseg2ei32_v_u8mf4x2(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u8mf4x2(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u8mf4x2(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x2_t vs3, size_t vl);
vuint8mf4x2_t __riscv_vluxseg2ei64_v_u8mf4x2(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
vuint8mf4x2_t __riscv_vloxseg2ei64_v_u8mf4x2(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u8mf4x2(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u8mf4x2(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x2_t vs3, size_t vl);
vint8mf4x2_t __riscv_vundefined_i8mf4x2(void);
vint8mf4x2_t __riscv_vcreate_v_i8mf4_i8mf4x2(vint8mf4_t v0, vint8mf4_t v1);
vint8mf4_t __riscv_vget_v_i8mf4x2_i8mf4(vint8mf4x2_t src, size_t index);
vint8mf4x2_t __riscv_vset_v_i8mf4x2_i8mf4(vint8mf4x2_t dest, size_t index, vint8mf4_t value);
vint8mf4x2_t __riscv_vlseg2e8_v_i8mf4x2(const signed char *rs1, size_t vl);
void __riscv_vsseg2e8_v_i8mf4x2(signed char *rs1, vint8mf4x2_t vs3, size_t vl);
vint8mf4x2_t __riscv_vlseg2e8ff_v_i8mf4x2(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf4x2_t __riscv_vlsseg2e8_v_i8mf4x2(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_i8mf4x2(signed char *rs1, long rs2, vint8mf4x2_t vs3, size_t vl);
vint8mf4x2_t __riscv_vluxseg2ei8_v_i8mf4x2(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
vint8mf4x2_t __riscv_vloxseg2ei8_v_i8mf4x2(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i8mf4x2(signed char *rs1, vuint8mf4_t rs2, vint8mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i8mf4x2(signed char *rs1, vuint8mf4_t rs2, vint8mf4x2_t vs3, size_t vl);
vint8mf4x2_t __riscv_vluxseg2ei16_v_i8mf4x2(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
vint8mf4x2_t __riscv_vloxseg2ei16_v_i8mf4x2(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i8mf4x2(signed char *rs1, vuint16mf2_t rs2, vint8mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i8mf4x2(signed char *rs1, vuint16mf2_t rs2, vint8mf4x2_t vs3, size_t vl);
vint8mf4x2_t __riscv_vluxseg2ei32_v_i8mf4x2(const signed char *rs1, vuint32m1_t rs2, size_t vl);
vint8mf4x2_t __riscv_vloxseg2ei32_v_i8mf4x2(const signed char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i8mf4x2(signed char *rs1, vuint32m1_t rs2, vint8mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i8mf4x2(signed char *rs1, vuint32m1_t rs2, vint8mf4x2_t vs3, size_t vl);
vint8mf4x2_t __riscv_vluxseg2ei64_v_i8mf4x2(const signed char *rs1, vuint64m2_t rs2, size_t vl);
vint8mf4x2_t __riscv_vloxseg2ei64_v_i8mf4x2(const signed char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i8mf4x2(signed char *rs1, vuint64m2_t rs2, vint8mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i8mf4x2(signed char *rs1, vuint64m2_t rs2, vint8mf4x2_t vs3, size_t vl);
vuint8mf4x3_t __riscv_vundefined_u8mf4x3(void);
vuint8mf4x3_t __riscv_vcreate_v_u8mf4_u8mf4x3(vuint8mf4_t v0, vuint8mf4_t v1, vuint8mf4_t v2);
vuint8mf4_t __riscv_vget_v_u8mf4x3_u8mf4(vuint8mf4x3_t src, size_t index);
vuint8mf4x3_t __riscv_vset_v_u8mf4x3_u8mf4(vuint8mf4x3_t dest, size_t index, vuint8mf4_t value);
vuint8mf4x3_t __riscv_vlseg3e8_v_u8mf4x3(const unsigned char *rs1, size_t vl);
void __riscv_vsseg3e8_v_u8mf4x3(unsigned char *rs1, vuint8mf4x3_t vs3, size_t vl);
vuint8mf4x3_t __riscv_vlseg3e8ff_v_u8mf4x3(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf4x3_t __riscv_vlsseg3e8_v_u8mf4x3(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_u8mf4x3(unsigned char *rs1, long rs2, vuint8mf4x3_t vs3, size_t vl);
vuint8mf4x3_t __riscv_vluxseg3ei8_v_u8mf4x3(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
vuint8mf4x3_t __riscv_vloxseg3ei8_v_u8mf4x3(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u8mf4x3(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u8mf4x3(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x3_t vs3, size_t vl);
vuint8mf4x3_t __riscv_vluxseg3ei16_v_u8mf4x3(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
vuint8mf4x3_t __riscv_vloxseg3ei16_v_u8mf4x3(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u8mf4x3(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u8mf4x3(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x3_t vs3, size_t vl);
vuint8mf4x3_t __riscv_vluxseg3ei32_v_u8mf4x3(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
vuint8mf4x3_t __riscv_vloxseg3ei32_v_u8mf4x3(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u8mf4x3(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u8mf4x3(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x3_t vs3, size_t vl);
vuint8mf4x3_t __riscv_vluxseg3ei64_v_u8mf4x3(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
vuint8mf4x3_t __riscv_vloxseg3ei64_v_u8mf4x3(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u8mf4x3(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u8mf4x3(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x3_t vs3, size_t vl);
vint8mf4x3_t __riscv_vundefined_i8mf4x3(void);
vint8mf4x3_t __riscv_vcreate_v_i8mf4_i8mf4x3(vint8mf4_t v0, vint8mf4_t v1, vint8mf4_t v2);
vint8mf4_t __riscv_vget_v_i8mf4x3_i8mf4(vint8mf4x3_t src, size_t index);
vint8mf4x3_t __riscv_vset_v_i8mf4x3_i8mf4(vint8mf4x3_t dest, size_t index, vint8mf4_t value);
vint8mf4x3_t __riscv_vlseg3e8_v_i8mf4x3(const signed char *rs1, size_t vl);
void __riscv_vsseg3e8_v_i8mf4x3(signed char *rs1, vint8mf4x3_t vs3, size_t vl);
vint8mf4x3_t __riscv_vlseg3e8ff_v_i8mf4x3(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf4x3_t __riscv_vlsseg3e8_v_i8mf4x3(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_i8mf4x3(signed char *rs1, long rs2, vint8mf4x3_t vs3, size_t vl);
vint8mf4x3_t __riscv_vluxseg3ei8_v_i8mf4x3(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
vint8mf4x3_t __riscv_vloxseg3ei8_v_i8mf4x3(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i8mf4x3(signed char *rs1, vuint8mf4_t rs2, vint8mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i8mf4x3(signed char *rs1, vuint8mf4_t rs2, vint8mf4x3_t vs3, size_t vl);
vint8mf4x3_t __riscv_vluxseg3ei16_v_i8mf4x3(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
vint8mf4x3_t __riscv_vloxseg3ei16_v_i8mf4x3(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i8mf4x3(signed char *rs1, vuint16mf2_t rs2, vint8mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i8mf4x3(signed char *rs1, vuint16mf2_t rs2, vint8mf4x3_t vs3, size_t vl);
vint8mf4x3_t __riscv_vluxseg3ei32_v_i8mf4x3(const signed char *rs1, vuint32m1_t rs2, size_t vl);
vint8mf4x3_t __riscv_vloxseg3ei32_v_i8mf4x3(const signed char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i8mf4x3(signed char *rs1, vuint32m1_t rs2, vint8mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i8mf4x3(signed char *rs1, vuint32m1_t rs2, vint8mf4x3_t vs3, size_t vl);
vint8mf4x3_t __riscv_vluxseg3ei64_v_i8mf4x3(const signed char *rs1, vuint64m2_t rs2, size_t vl);
vint8mf4x3_t __riscv_vloxseg3ei64_v_i8mf4x3(const signed char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i8mf4x3(signed char *rs1, vuint64m2_t rs2, vint8mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i8mf4x3(signed char *rs1, vuint64m2_t rs2, vint8mf4x3_t vs3, size_t vl);
vuint8mf4x4_t __riscv_vundefined_u8mf4x4(void);
vuint8mf4x4_t __riscv_vcreate_v_u8mf4_u8mf4x4(vuint8mf4_t v0, vuint8mf4_t v1, vuint8mf4_t v2, vuint8mf4_t v3);
vuint8mf4_t __riscv_vget_v_u8mf4x4_u8mf4(vuint8mf4x4_t src, size_t index);
vuint8mf4x4_t __riscv_vset_v_u8mf4x4_u8mf4(vuint8mf4x4_t dest, size_t index, vuint8mf4_t value);
vuint8mf4x4_t __riscv_vlseg4e8_v_u8mf4x4(const unsigned char *rs1, size_t vl);
void __riscv_vsseg4e8_v_u8mf4x4(unsigned char *rs1, vuint8mf4x4_t vs3, size_t vl);
vuint8mf4x4_t __riscv_vlseg4e8ff_v_u8mf4x4(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf4x4_t __riscv_vlsseg4e8_v_u8mf4x4(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_u8mf4x4(unsigned char *rs1, long rs2, vuint8mf4x4_t vs3, size_t vl);
vuint8mf4x4_t __riscv_vluxseg4ei8_v_u8mf4x4(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
vuint8mf4x4_t __riscv_vloxseg4ei8_v_u8mf4x4(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u8mf4x4(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u8mf4x4(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x4_t vs3, size_t vl);
vuint8mf4x4_t __riscv_vluxseg4ei16_v_u8mf4x4(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
vuint8mf4x4_t __riscv_vloxseg4ei16_v_u8mf4x4(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u8mf4x4(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u8mf4x4(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x4_t vs3, size_t vl);
vuint8mf4x4_t __riscv_vluxseg4ei32_v_u8mf4x4(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
vuint8mf4x4_t __riscv_vloxseg4ei32_v_u8mf4x4(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u8mf4x4(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u8mf4x4(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x4_t vs3, size_t vl);
vuint8mf4x4_t __riscv_vluxseg4ei64_v_u8mf4x4(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
vuint8mf4x4_t __riscv_vloxseg4ei64_v_u8mf4x4(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u8mf4x4(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u8mf4x4(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x4_t vs3, size_t vl);
vint8mf4x4_t __riscv_vundefined_i8mf4x4(void);
vint8mf4x4_t __riscv_vcreate_v_i8mf4_i8mf4x4(vint8mf4_t v0, vint8mf4_t v1, vint8mf4_t v2, vint8mf4_t v3);
vint8mf4_t __riscv_vget_v_i8mf4x4_i8mf4(vint8mf4x4_t src, size_t index);
vint8mf4x4_t __riscv_vset_v_i8mf4x4_i8mf4(vint8mf4x4_t dest, size_t index, vint8mf4_t value);
vint8mf4x4_t __riscv_vlseg4e8_v_i8mf4x4(const signed char *rs1, size_t vl);
void __riscv_vsseg4e8_v_i8mf4x4(signed char *rs1, vint8mf4x4_t vs3, size_t vl);
vint8mf4x4_t __riscv_vlseg4e8ff_v_i8mf4x4(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf4x4_t __riscv_vlsseg4e8_v_i8mf4x4(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_i8mf4x4(signed char *rs1, long rs2, vint8mf4x4_t vs3, size_t vl);
vint8mf4x4_t __riscv_vluxseg4ei8_v_i8mf4x4(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
vint8mf4x4_t __riscv_vloxseg4ei8_v_i8mf4x4(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i8mf4x4(signed char *rs1, vuint8mf4_t rs2, vint8mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i8mf4x4(signed char *rs1, vuint8mf4_t rs2, vint8mf4x4_t vs3, size_t vl);
vint8mf4x4_t __riscv_vluxseg4ei16_v_i8mf4x4(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
vint8mf4x4_t __riscv_vloxseg4ei16_v_i8mf4x4(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i8mf4x4(signed char *rs1, vuint16mf2_t rs2, vint8mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i8mf4x4(signed char *rs1, vuint16mf2_t rs2, vint8mf4x4_t vs3, size_t vl);
vint8mf4x4_t __riscv_vluxseg4ei32_v_i8mf4x4(const signed char *rs1, vuint32m1_t rs2, size_t vl);
vint8mf4x4_t __riscv_vloxseg4ei32_v_i8mf4x4(const signed char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i8mf4x4(signed char *rs1, vuint32m1_t rs2, vint8mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i8mf4x4(signed char *rs1, vuint32m1_t rs2, vint8mf4x4_t vs3, size_t vl);
vint8mf4x4_t __riscv_vluxseg4ei64_v_i8mf4x4(const signed char *rs1, vuint64m2_t rs2, size_t vl);
vint8mf4x4_t __riscv_vloxseg4ei64_v_i8mf4x4(const signed char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i8mf4x4(signed char *rs1, vuint64m2_t rs2, vint8mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i8mf4x4(signed char *rs1, vuint64m2_t rs2, vint8mf4x4_t vs3, size_t vl);
vuint8mf4x5_t __riscv_vundefined_u8mf4x5(void);
vuint8mf4x5_t __riscv_vcreate_v_u8mf4_u8mf4x5(vuint8mf4_t v0, vuint8mf4_t v1, vuint8mf4_t v2, vuint8mf4_t v3, vuint8mf4_t v4);
vuint8mf4_t __riscv_vget_v_u8mf4x5_u8mf4(vuint8mf4x5_t src, size_t index);
vuint8mf4x5_t __riscv_vset_v_u8mf4x5_u8mf4(vuint8mf4x5_t dest, size_t index, vuint8mf4_t value);
vuint8mf4x5_t __riscv_vlseg5e8_v_u8mf4x5(const unsigned char *rs1, size_t vl);
void __riscv_vsseg5e8_v_u8mf4x5(unsigned char *rs1, vuint8mf4x5_t vs3, size_t vl);
vuint8mf4x5_t __riscv_vlseg5e8ff_v_u8mf4x5(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf4x5_t __riscv_vlsseg5e8_v_u8mf4x5(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg5e8_v_u8mf4x5(unsigned char *rs1, long rs2, vuint8mf4x5_t vs3, size_t vl);
vuint8mf4x5_t __riscv_vluxseg5ei8_v_u8mf4x5(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
vuint8mf4x5_t __riscv_vloxseg5ei8_v_u8mf4x5(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u8mf4x5(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u8mf4x5(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x5_t vs3, size_t vl);
vuint8mf4x5_t __riscv_vluxseg5ei16_v_u8mf4x5(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
vuint8mf4x5_t __riscv_vloxseg5ei16_v_u8mf4x5(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u8mf4x5(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u8mf4x5(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x5_t vs3, size_t vl);
vuint8mf4x5_t __riscv_vluxseg5ei32_v_u8mf4x5(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
vuint8mf4x5_t __riscv_vloxseg5ei32_v_u8mf4x5(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u8mf4x5(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u8mf4x5(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x5_t vs3, size_t vl);
vuint8mf4x5_t __riscv_vluxseg5ei64_v_u8mf4x5(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
vuint8mf4x5_t __riscv_vloxseg5ei64_v_u8mf4x5(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u8mf4x5(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u8mf4x5(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x5_t vs3, size_t vl);
vint8mf4x5_t __riscv_vundefined_i8mf4x5(void);
vint8mf4x5_t __riscv_vcreate_v_i8mf4_i8mf4x5(vint8mf4_t v0, vint8mf4_t v1, vint8mf4_t v2, vint8mf4_t v3, vint8mf4_t v4);
vint8mf4_t __riscv_vget_v_i8mf4x5_i8mf4(vint8mf4x5_t src, size_t index);
vint8mf4x5_t __riscv_vset_v_i8mf4x5_i8mf4(vint8mf4x5_t dest, size_t index, vint8mf4_t value);
vint8mf4x5_t __riscv_vlseg5e8_v_i8mf4x5(const signed char *rs1, size_t vl);
void __riscv_vsseg5e8_v_i8mf4x5(signed char *rs1, vint8mf4x5_t vs3, size_t vl);
vint8mf4x5_t __riscv_vlseg5e8ff_v_i8mf4x5(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf4x5_t __riscv_vlsseg5e8_v_i8mf4x5(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg5e8_v_i8mf4x5(signed char *rs1, long rs2, vint8mf4x5_t vs3, size_t vl);
vint8mf4x5_t __riscv_vluxseg5ei8_v_i8mf4x5(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
vint8mf4x5_t __riscv_vloxseg5ei8_v_i8mf4x5(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i8mf4x5(signed char *rs1, vuint8mf4_t rs2, vint8mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i8mf4x5(signed char *rs1, vuint8mf4_t rs2, vint8mf4x5_t vs3, size_t vl);
vint8mf4x5_t __riscv_vluxseg5ei16_v_i8mf4x5(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
vint8mf4x5_t __riscv_vloxseg5ei16_v_i8mf4x5(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i8mf4x5(signed char *rs1, vuint16mf2_t rs2, vint8mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i8mf4x5(signed char *rs1, vuint16mf2_t rs2, vint8mf4x5_t vs3, size_t vl);
vint8mf4x5_t __riscv_vluxseg5ei32_v_i8mf4x5(const signed char *rs1, vuint32m1_t rs2, size_t vl);
vint8mf4x5_t __riscv_vloxseg5ei32_v_i8mf4x5(const signed char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i8mf4x5(signed char *rs1, vuint32m1_t rs2, vint8mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i8mf4x5(signed char *rs1, vuint32m1_t rs2, vint8mf4x5_t vs3, size_t vl);
vint8mf4x5_t __riscv_vluxseg5ei64_v_i8mf4x5(const signed char *rs1, vuint64m2_t rs2, size_t vl);
vint8mf4x5_t __riscv_vloxseg5ei64_v_i8mf4x5(const signed char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i8mf4x5(signed char *rs1, vuint64m2_t rs2, vint8mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i8mf4x5(signed char *rs1, vuint64m2_t rs2, vint8mf4x5_t vs3, size_t vl);
vuint8mf4x6_t __riscv_vundefined_u8mf4x6(void);
vuint8mf4x6_t __riscv_vcreate_v_u8mf4_u8mf4x6(vuint8mf4_t v0, vuint8mf4_t v1, vuint8mf4_t v2, vuint8mf4_t v3, vuint8mf4_t v4, vuint8mf4_t v5);
vuint8mf4_t __riscv_vget_v_u8mf4x6_u8mf4(vuint8mf4x6_t src, size_t index);
vuint8mf4x6_t __riscv_vset_v_u8mf4x6_u8mf4(vuint8mf4x6_t dest, size_t index, vuint8mf4_t value);
vuint8mf4x6_t __riscv_vlseg6e8_v_u8mf4x6(const unsigned char *rs1, size_t vl);
void __riscv_vsseg6e8_v_u8mf4x6(unsigned char *rs1, vuint8mf4x6_t vs3, size_t vl);
vuint8mf4x6_t __riscv_vlseg6e8ff_v_u8mf4x6(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf4x6_t __riscv_vlsseg6e8_v_u8mf4x6(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg6e8_v_u8mf4x6(unsigned char *rs1, long rs2, vuint8mf4x6_t vs3, size_t vl);
vuint8mf4x6_t __riscv_vluxseg6ei8_v_u8mf4x6(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
vuint8mf4x6_t __riscv_vloxseg6ei8_v_u8mf4x6(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u8mf4x6(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u8mf4x6(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x6_t vs3, size_t vl);
vuint8mf4x6_t __riscv_vluxseg6ei16_v_u8mf4x6(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
vuint8mf4x6_t __riscv_vloxseg6ei16_v_u8mf4x6(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u8mf4x6(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u8mf4x6(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x6_t vs3, size_t vl);
vuint8mf4x6_t __riscv_vluxseg6ei32_v_u8mf4x6(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
vuint8mf4x6_t __riscv_vloxseg6ei32_v_u8mf4x6(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u8mf4x6(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u8mf4x6(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x6_t vs3, size_t vl);
vuint8mf4x6_t __riscv_vluxseg6ei64_v_u8mf4x6(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
vuint8mf4x6_t __riscv_vloxseg6ei64_v_u8mf4x6(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u8mf4x6(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u8mf4x6(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x6_t vs3, size_t vl);
vint8mf4x6_t __riscv_vundefined_i8mf4x6(void);
vint8mf4x6_t __riscv_vcreate_v_i8mf4_i8mf4x6(vint8mf4_t v0, vint8mf4_t v1, vint8mf4_t v2, vint8mf4_t v3, vint8mf4_t v4, vint8mf4_t v5);
vint8mf4_t __riscv_vget_v_i8mf4x6_i8mf4(vint8mf4x6_t src, size_t index);
vint8mf4x6_t __riscv_vset_v_i8mf4x6_i8mf4(vint8mf4x6_t dest, size_t index, vint8mf4_t value);
vint8mf4x6_t __riscv_vlseg6e8_v_i8mf4x6(const signed char *rs1, size_t vl);
void __riscv_vsseg6e8_v_i8mf4x6(signed char *rs1, vint8mf4x6_t vs3, size_t vl);
vint8mf4x6_t __riscv_vlseg6e8ff_v_i8mf4x6(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf4x6_t __riscv_vlsseg6e8_v_i8mf4x6(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg6e8_v_i8mf4x6(signed char *rs1, long rs2, vint8mf4x6_t vs3, size_t vl);
vint8mf4x6_t __riscv_vluxseg6ei8_v_i8mf4x6(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
vint8mf4x6_t __riscv_vloxseg6ei8_v_i8mf4x6(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i8mf4x6(signed char *rs1, vuint8mf4_t rs2, vint8mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i8mf4x6(signed char *rs1, vuint8mf4_t rs2, vint8mf4x6_t vs3, size_t vl);
vint8mf4x6_t __riscv_vluxseg6ei16_v_i8mf4x6(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
vint8mf4x6_t __riscv_vloxseg6ei16_v_i8mf4x6(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i8mf4x6(signed char *rs1, vuint16mf2_t rs2, vint8mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i8mf4x6(signed char *rs1, vuint16mf2_t rs2, vint8mf4x6_t vs3, size_t vl);
vint8mf4x6_t __riscv_vluxseg6ei32_v_i8mf4x6(const signed char *rs1, vuint32m1_t rs2, size_t vl);
vint8mf4x6_t __riscv_vloxseg6ei32_v_i8mf4x6(const signed char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i8mf4x6(signed char *rs1, vuint32m1_t rs2, vint8mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i8mf4x6(signed char *rs1, vuint32m1_t rs2, vint8mf4x6_t vs3, size_t vl);
vint8mf4x6_t __riscv_vluxseg6ei64_v_i8mf4x6(const signed char *rs1, vuint64m2_t rs2, size_t vl);
vint8mf4x6_t __riscv_vloxseg6ei64_v_i8mf4x6(const signed char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i8mf4x6(signed char *rs1, vuint64m2_t rs2, vint8mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i8mf4x6(signed char *rs1, vuint64m2_t rs2, vint8mf4x6_t vs3, size_t vl);
vuint8mf4x7_t __riscv_vundefined_u8mf4x7(void);
vuint8mf4x7_t __riscv_vcreate_v_u8mf4_u8mf4x7(vuint8mf4_t v0, vuint8mf4_t v1, vuint8mf4_t v2, vuint8mf4_t v3, vuint8mf4_t v4, vuint8mf4_t v5, vuint8mf4_t v6);
vuint8mf4_t __riscv_vget_v_u8mf4x7_u8mf4(vuint8mf4x7_t src, size_t index);
vuint8mf4x7_t __riscv_vset_v_u8mf4x7_u8mf4(vuint8mf4x7_t dest, size_t index, vuint8mf4_t value);
vuint8mf4x7_t __riscv_vlseg7e8_v_u8mf4x7(const unsigned char *rs1, size_t vl);
void __riscv_vsseg7e8_v_u8mf4x7(unsigned char *rs1, vuint8mf4x7_t vs3, size_t vl);
vuint8mf4x7_t __riscv_vlseg7e8ff_v_u8mf4x7(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf4x7_t __riscv_vlsseg7e8_v_u8mf4x7(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg7e8_v_u8mf4x7(unsigned char *rs1, long rs2, vuint8mf4x7_t vs3, size_t vl);
vuint8mf4x7_t __riscv_vluxseg7ei8_v_u8mf4x7(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
vuint8mf4x7_t __riscv_vloxseg7ei8_v_u8mf4x7(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u8mf4x7(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u8mf4x7(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x7_t vs3, size_t vl);
vuint8mf4x7_t __riscv_vluxseg7ei16_v_u8mf4x7(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
vuint8mf4x7_t __riscv_vloxseg7ei16_v_u8mf4x7(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u8mf4x7(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u8mf4x7(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x7_t vs3, size_t vl);
vuint8mf4x7_t __riscv_vluxseg7ei32_v_u8mf4x7(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
vuint8mf4x7_t __riscv_vloxseg7ei32_v_u8mf4x7(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u8mf4x7(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u8mf4x7(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x7_t vs3, size_t vl);
vuint8mf4x7_t __riscv_vluxseg7ei64_v_u8mf4x7(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
vuint8mf4x7_t __riscv_vloxseg7ei64_v_u8mf4x7(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u8mf4x7(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u8mf4x7(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x7_t vs3, size_t vl);
vint8mf4x7_t __riscv_vundefined_i8mf4x7(void);
vint8mf4x7_t __riscv_vcreate_v_i8mf4_i8mf4x7(vint8mf4_t v0, vint8mf4_t v1, vint8mf4_t v2, vint8mf4_t v3, vint8mf4_t v4, vint8mf4_t v5, vint8mf4_t v6);
vint8mf4_t __riscv_vget_v_i8mf4x7_i8mf4(vint8mf4x7_t src, size_t index);
vint8mf4x7_t __riscv_vset_v_i8mf4x7_i8mf4(vint8mf4x7_t dest, size_t index, vint8mf4_t value);
vint8mf4x7_t __riscv_vlseg7e8_v_i8mf4x7(const signed char *rs1, size_t vl);
void __riscv_vsseg7e8_v_i8mf4x7(signed char *rs1, vint8mf4x7_t vs3, size_t vl);
vint8mf4x7_t __riscv_vlseg7e8ff_v_i8mf4x7(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf4x7_t __riscv_vlsseg7e8_v_i8mf4x7(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg7e8_v_i8mf4x7(signed char *rs1, long rs2, vint8mf4x7_t vs3, size_t vl);
vint8mf4x7_t __riscv_vluxseg7ei8_v_i8mf4x7(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
vint8mf4x7_t __riscv_vloxseg7ei8_v_i8mf4x7(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i8mf4x7(signed char *rs1, vuint8mf4_t rs2, vint8mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i8mf4x7(signed char *rs1, vuint8mf4_t rs2, vint8mf4x7_t vs3, size_t vl);
vint8mf4x7_t __riscv_vluxseg7ei16_v_i8mf4x7(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
vint8mf4x7_t __riscv_vloxseg7ei16_v_i8mf4x7(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i8mf4x7(signed char *rs1, vuint16mf2_t rs2, vint8mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i8mf4x7(signed char *rs1, vuint16mf2_t rs2, vint8mf4x7_t vs3, size_t vl);
vint8mf4x7_t __riscv_vluxseg7ei32_v_i8mf4x7(const signed char *rs1, vuint32m1_t rs2, size_t vl);
vint8mf4x7_t __riscv_vloxseg7ei32_v_i8mf4x7(const signed char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i8mf4x7(signed char *rs1, vuint32m1_t rs2, vint8mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i8mf4x7(signed char *rs1, vuint32m1_t rs2, vint8mf4x7_t vs3, size_t vl);
vint8mf4x7_t __riscv_vluxseg7ei64_v_i8mf4x7(const signed char *rs1, vuint64m2_t rs2, size_t vl);
vint8mf4x7_t __riscv_vloxseg7ei64_v_i8mf4x7(const signed char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i8mf4x7(signed char *rs1, vuint64m2_t rs2, vint8mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i8mf4x7(signed char *rs1, vuint64m2_t rs2, vint8mf4x7_t vs3, size_t vl);
vuint8mf4x8_t __riscv_vundefined_u8mf4x8(void);
vuint8mf4x8_t __riscv_vcreate_v_u8mf4_u8mf4x8(vuint8mf4_t v0, vuint8mf4_t v1, vuint8mf4_t v2, vuint8mf4_t v3, vuint8mf4_t v4, vuint8mf4_t v5, vuint8mf4_t v6, vuint8mf4_t v7);
vuint8mf4_t __riscv_vget_v_u8mf4x8_u8mf4(vuint8mf4x8_t src, size_t index);
vuint8mf4x8_t __riscv_vset_v_u8mf4x8_u8mf4(vuint8mf4x8_t dest, size_t index, vuint8mf4_t value);
vuint8mf4x8_t __riscv_vlseg8e8_v_u8mf4x8(const unsigned char *rs1, size_t vl);
void __riscv_vsseg8e8_v_u8mf4x8(unsigned char *rs1, vuint8mf4x8_t vs3, size_t vl);
vuint8mf4x8_t __riscv_vlseg8e8ff_v_u8mf4x8(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf4x8_t __riscv_vlsseg8e8_v_u8mf4x8(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg8e8_v_u8mf4x8(unsigned char *rs1, long rs2, vuint8mf4x8_t vs3, size_t vl);
vuint8mf4x8_t __riscv_vluxseg8ei8_v_u8mf4x8(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
vuint8mf4x8_t __riscv_vloxseg8ei8_v_u8mf4x8(const unsigned char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u8mf4x8(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u8mf4x8(unsigned char *rs1, vuint8mf4_t rs2, vuint8mf4x8_t vs3, size_t vl);
vuint8mf4x8_t __riscv_vluxseg8ei16_v_u8mf4x8(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
vuint8mf4x8_t __riscv_vloxseg8ei16_v_u8mf4x8(const unsigned char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u8mf4x8(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u8mf4x8(unsigned char *rs1, vuint16mf2_t rs2, vuint8mf4x8_t vs3, size_t vl);
vuint8mf4x8_t __riscv_vluxseg8ei32_v_u8mf4x8(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
vuint8mf4x8_t __riscv_vloxseg8ei32_v_u8mf4x8(const unsigned char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u8mf4x8(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u8mf4x8(unsigned char *rs1, vuint32m1_t rs2, vuint8mf4x8_t vs3, size_t vl);
vuint8mf4x8_t __riscv_vluxseg8ei64_v_u8mf4x8(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
vuint8mf4x8_t __riscv_vloxseg8ei64_v_u8mf4x8(const unsigned char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u8mf4x8(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u8mf4x8(unsigned char *rs1, vuint64m2_t rs2, vuint8mf4x8_t vs3, size_t vl);
vint8mf4x8_t __riscv_vundefined_i8mf4x8(void);
vint8mf4x8_t __riscv_vcreate_v_i8mf4_i8mf4x8(vint8mf4_t v0, vint8mf4_t v1, vint8mf4_t v2, vint8mf4_t v3, vint8mf4_t v4, vint8mf4_t v5, vint8mf4_t v6, vint8mf4_t v7);
vint8mf4_t __riscv_vget_v_i8mf4x8_i8mf4(vint8mf4x8_t src, size_t index);
vint8mf4x8_t __riscv_vset_v_i8mf4x8_i8mf4(vint8mf4x8_t dest, size_t index, vint8mf4_t value);
vint8mf4x8_t __riscv_vlseg8e8_v_i8mf4x8(const signed char *rs1, size_t vl);
void __riscv_vsseg8e8_v_i8mf4x8(signed char *rs1, vint8mf4x8_t vs3, size_t vl);
vint8mf4x8_t __riscv_vlseg8e8ff_v_i8mf4x8(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf4x8_t __riscv_vlsseg8e8_v_i8mf4x8(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg8e8_v_i8mf4x8(signed char *rs1, long rs2, vint8mf4x8_t vs3, size_t vl);
vint8mf4x8_t __riscv_vluxseg8ei8_v_i8mf4x8(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
vint8mf4x8_t __riscv_vloxseg8ei8_v_i8mf4x8(const signed char *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i8mf4x8(signed char *rs1, vuint8mf4_t rs2, vint8mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i8mf4x8(signed char *rs1, vuint8mf4_t rs2, vint8mf4x8_t vs3, size_t vl);
vint8mf4x8_t __riscv_vluxseg8ei16_v_i8mf4x8(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
vint8mf4x8_t __riscv_vloxseg8ei16_v_i8mf4x8(const signed char *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i8mf4x8(signed char *rs1, vuint16mf2_t rs2, vint8mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i8mf4x8(signed char *rs1, vuint16mf2_t rs2, vint8mf4x8_t vs3, size_t vl);
vint8mf4x8_t __riscv_vluxseg8ei32_v_i8mf4x8(const signed char *rs1, vuint32m1_t rs2, size_t vl);
vint8mf4x8_t __riscv_vloxseg8ei32_v_i8mf4x8(const signed char *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i8mf4x8(signed char *rs1, vuint32m1_t rs2, vint8mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i8mf4x8(signed char *rs1, vuint32m1_t rs2, vint8mf4x8_t vs3, size_t vl);
vint8mf4x8_t __riscv_vluxseg8ei64_v_i8mf4x8(const signed char *rs1, vuint64m2_t rs2, size_t vl);
vint8mf4x8_t __riscv_vloxseg8ei64_v_i8mf4x8(const signed char *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i8mf4x8(signed char *rs1, vuint64m2_t rs2, vint8mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i8mf4x8(signed char *rs1, vuint64m2_t rs2, vint8mf4x8_t vs3, size_t vl);
vuint8mf2x2_t __riscv_vundefined_u8mf2x2(void);
vuint8mf2x2_t __riscv_vcreate_v_u8mf2_u8mf2x2(vuint8mf2_t v0, vuint8mf2_t v1);
vuint8mf2_t __riscv_vget_v_u8mf2x2_u8mf2(vuint8mf2x2_t src, size_t index);
vuint8mf2x2_t __riscv_vset_v_u8mf2x2_u8mf2(vuint8mf2x2_t dest, size_t index, vuint8mf2_t value);
vuint8mf2x2_t __riscv_vlseg2e8_v_u8mf2x2(const unsigned char *rs1, size_t vl);
void __riscv_vsseg2e8_v_u8mf2x2(unsigned char *rs1, vuint8mf2x2_t vs3, size_t vl);
vuint8mf2x2_t __riscv_vlseg2e8ff_v_u8mf2x2(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf2x2_t __riscv_vlsseg2e8_v_u8mf2x2(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_u8mf2x2(unsigned char *rs1, long rs2, vuint8mf2x2_t vs3, size_t vl);
vuint8mf2x2_t __riscv_vluxseg2ei8_v_u8mf2x2(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
vuint8mf2x2_t __riscv_vloxseg2ei8_v_u8mf2x2(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u8mf2x2(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u8mf2x2(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x2_t vs3, size_t vl);
vuint8mf2x2_t __riscv_vluxseg2ei16_v_u8mf2x2(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
vuint8mf2x2_t __riscv_vloxseg2ei16_v_u8mf2x2(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u8mf2x2(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u8mf2x2(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x2_t vs3, size_t vl);
vuint8mf2x2_t __riscv_vluxseg2ei32_v_u8mf2x2(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
vuint8mf2x2_t __riscv_vloxseg2ei32_v_u8mf2x2(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u8mf2x2(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u8mf2x2(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x2_t vs3, size_t vl);
vuint8mf2x2_t __riscv_vluxseg2ei64_v_u8mf2x2(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
vuint8mf2x2_t __riscv_vloxseg2ei64_v_u8mf2x2(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u8mf2x2(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u8mf2x2(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x2_t vs3, size_t vl);
vint8mf2x2_t __riscv_vundefined_i8mf2x2(void);
vint8mf2x2_t __riscv_vcreate_v_i8mf2_i8mf2x2(vint8mf2_t v0, vint8mf2_t v1);
vint8mf2_t __riscv_vget_v_i8mf2x2_i8mf2(vint8mf2x2_t src, size_t index);
vint8mf2x2_t __riscv_vset_v_i8mf2x2_i8mf2(vint8mf2x2_t dest, size_t index, vint8mf2_t value);
vint8mf2x2_t __riscv_vlseg2e8_v_i8mf2x2(const signed char *rs1, size_t vl);
void __riscv_vsseg2e8_v_i8mf2x2(signed char *rs1, vint8mf2x2_t vs3, size_t vl);
vint8mf2x2_t __riscv_vlseg2e8ff_v_i8mf2x2(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf2x2_t __riscv_vlsseg2e8_v_i8mf2x2(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_i8mf2x2(signed char *rs1, long rs2, vint8mf2x2_t vs3, size_t vl);
vint8mf2x2_t __riscv_vluxseg2ei8_v_i8mf2x2(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
vint8mf2x2_t __riscv_vloxseg2ei8_v_i8mf2x2(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i8mf2x2(signed char *rs1, vuint8mf2_t rs2, vint8mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i8mf2x2(signed char *rs1, vuint8mf2_t rs2, vint8mf2x2_t vs3, size_t vl);
vint8mf2x2_t __riscv_vluxseg2ei16_v_i8mf2x2(const signed char *rs1, vuint16m1_t rs2, size_t vl);
vint8mf2x2_t __riscv_vloxseg2ei16_v_i8mf2x2(const signed char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i8mf2x2(signed char *rs1, vuint16m1_t rs2, vint8mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i8mf2x2(signed char *rs1, vuint16m1_t rs2, vint8mf2x2_t vs3, size_t vl);
vint8mf2x2_t __riscv_vluxseg2ei32_v_i8mf2x2(const signed char *rs1, vuint32m2_t rs2, size_t vl);
vint8mf2x2_t __riscv_vloxseg2ei32_v_i8mf2x2(const signed char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i8mf2x2(signed char *rs1, vuint32m2_t rs2, vint8mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i8mf2x2(signed char *rs1, vuint32m2_t rs2, vint8mf2x2_t vs3, size_t vl);
vint8mf2x2_t __riscv_vluxseg2ei64_v_i8mf2x2(const signed char *rs1, vuint64m4_t rs2, size_t vl);
vint8mf2x2_t __riscv_vloxseg2ei64_v_i8mf2x2(const signed char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i8mf2x2(signed char *rs1, vuint64m4_t rs2, vint8mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i8mf2x2(signed char *rs1, vuint64m4_t rs2, vint8mf2x2_t vs3, size_t vl);
vuint8mf2x3_t __riscv_vundefined_u8mf2x3(void);
vuint8mf2x3_t __riscv_vcreate_v_u8mf2_u8mf2x3(vuint8mf2_t v0, vuint8mf2_t v1, vuint8mf2_t v2);
vuint8mf2_t __riscv_vget_v_u8mf2x3_u8mf2(vuint8mf2x3_t src, size_t index);
vuint8mf2x3_t __riscv_vset_v_u8mf2x3_u8mf2(vuint8mf2x3_t dest, size_t index, vuint8mf2_t value);
vuint8mf2x3_t __riscv_vlseg3e8_v_u8mf2x3(const unsigned char *rs1, size_t vl);
void __riscv_vsseg3e8_v_u8mf2x3(unsigned char *rs1, vuint8mf2x3_t vs3, size_t vl);
vuint8mf2x3_t __riscv_vlseg3e8ff_v_u8mf2x3(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf2x3_t __riscv_vlsseg3e8_v_u8mf2x3(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_u8mf2x3(unsigned char *rs1, long rs2, vuint8mf2x3_t vs3, size_t vl);
vuint8mf2x3_t __riscv_vluxseg3ei8_v_u8mf2x3(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
vuint8mf2x3_t __riscv_vloxseg3ei8_v_u8mf2x3(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u8mf2x3(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u8mf2x3(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x3_t vs3, size_t vl);
vuint8mf2x3_t __riscv_vluxseg3ei16_v_u8mf2x3(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
vuint8mf2x3_t __riscv_vloxseg3ei16_v_u8mf2x3(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u8mf2x3(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u8mf2x3(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x3_t vs3, size_t vl);
vuint8mf2x3_t __riscv_vluxseg3ei32_v_u8mf2x3(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
vuint8mf2x3_t __riscv_vloxseg3ei32_v_u8mf2x3(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u8mf2x3(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u8mf2x3(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x3_t vs3, size_t vl);
vuint8mf2x3_t __riscv_vluxseg3ei64_v_u8mf2x3(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
vuint8mf2x3_t __riscv_vloxseg3ei64_v_u8mf2x3(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u8mf2x3(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u8mf2x3(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x3_t vs3, size_t vl);
vint8mf2x3_t __riscv_vundefined_i8mf2x3(void);
vint8mf2x3_t __riscv_vcreate_v_i8mf2_i8mf2x3(vint8mf2_t v0, vint8mf2_t v1, vint8mf2_t v2);
vint8mf2_t __riscv_vget_v_i8mf2x3_i8mf2(vint8mf2x3_t src, size_t index);
vint8mf2x3_t __riscv_vset_v_i8mf2x3_i8mf2(vint8mf2x3_t dest, size_t index, vint8mf2_t value);
vint8mf2x3_t __riscv_vlseg3e8_v_i8mf2x3(const signed char *rs1, size_t vl);
void __riscv_vsseg3e8_v_i8mf2x3(signed char *rs1, vint8mf2x3_t vs3, size_t vl);
vint8mf2x3_t __riscv_vlseg3e8ff_v_i8mf2x3(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf2x3_t __riscv_vlsseg3e8_v_i8mf2x3(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_i8mf2x3(signed char *rs1, long rs2, vint8mf2x3_t vs3, size_t vl);
vint8mf2x3_t __riscv_vluxseg3ei8_v_i8mf2x3(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
vint8mf2x3_t __riscv_vloxseg3ei8_v_i8mf2x3(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i8mf2x3(signed char *rs1, vuint8mf2_t rs2, vint8mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i8mf2x3(signed char *rs1, vuint8mf2_t rs2, vint8mf2x3_t vs3, size_t vl);
vint8mf2x3_t __riscv_vluxseg3ei16_v_i8mf2x3(const signed char *rs1, vuint16m1_t rs2, size_t vl);
vint8mf2x3_t __riscv_vloxseg3ei16_v_i8mf2x3(const signed char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i8mf2x3(signed char *rs1, vuint16m1_t rs2, vint8mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i8mf2x3(signed char *rs1, vuint16m1_t rs2, vint8mf2x3_t vs3, size_t vl);
vint8mf2x3_t __riscv_vluxseg3ei32_v_i8mf2x3(const signed char *rs1, vuint32m2_t rs2, size_t vl);
vint8mf2x3_t __riscv_vloxseg3ei32_v_i8mf2x3(const signed char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i8mf2x3(signed char *rs1, vuint32m2_t rs2, vint8mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i8mf2x3(signed char *rs1, vuint32m2_t rs2, vint8mf2x3_t vs3, size_t vl);
vint8mf2x3_t __riscv_vluxseg3ei64_v_i8mf2x3(const signed char *rs1, vuint64m4_t rs2, size_t vl);
vint8mf2x3_t __riscv_vloxseg3ei64_v_i8mf2x3(const signed char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i8mf2x3(signed char *rs1, vuint64m4_t rs2, vint8mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i8mf2x3(signed char *rs1, vuint64m4_t rs2, vint8mf2x3_t vs3, size_t vl);
vuint8mf2x4_t __riscv_vundefined_u8mf2x4(void);
vuint8mf2x4_t __riscv_vcreate_v_u8mf2_u8mf2x4(vuint8mf2_t v0, vuint8mf2_t v1, vuint8mf2_t v2, vuint8mf2_t v3);
vuint8mf2_t __riscv_vget_v_u8mf2x4_u8mf2(vuint8mf2x4_t src, size_t index);
vuint8mf2x4_t __riscv_vset_v_u8mf2x4_u8mf2(vuint8mf2x4_t dest, size_t index, vuint8mf2_t value);
vuint8mf2x4_t __riscv_vlseg4e8_v_u8mf2x4(const unsigned char *rs1, size_t vl);
void __riscv_vsseg4e8_v_u8mf2x4(unsigned char *rs1, vuint8mf2x4_t vs3, size_t vl);
vuint8mf2x4_t __riscv_vlseg4e8ff_v_u8mf2x4(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf2x4_t __riscv_vlsseg4e8_v_u8mf2x4(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_u8mf2x4(unsigned char *rs1, long rs2, vuint8mf2x4_t vs3, size_t vl);
vuint8mf2x4_t __riscv_vluxseg4ei8_v_u8mf2x4(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
vuint8mf2x4_t __riscv_vloxseg4ei8_v_u8mf2x4(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u8mf2x4(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u8mf2x4(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x4_t vs3, size_t vl);
vuint8mf2x4_t __riscv_vluxseg4ei16_v_u8mf2x4(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
vuint8mf2x4_t __riscv_vloxseg4ei16_v_u8mf2x4(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u8mf2x4(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u8mf2x4(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x4_t vs3, size_t vl);
vuint8mf2x4_t __riscv_vluxseg4ei32_v_u8mf2x4(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
vuint8mf2x4_t __riscv_vloxseg4ei32_v_u8mf2x4(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u8mf2x4(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u8mf2x4(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x4_t vs3, size_t vl);
vuint8mf2x4_t __riscv_vluxseg4ei64_v_u8mf2x4(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
vuint8mf2x4_t __riscv_vloxseg4ei64_v_u8mf2x4(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u8mf2x4(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u8mf2x4(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x4_t vs3, size_t vl);
vint8mf2x4_t __riscv_vundefined_i8mf2x4(void);
vint8mf2x4_t __riscv_vcreate_v_i8mf2_i8mf2x4(vint8mf2_t v0, vint8mf2_t v1, vint8mf2_t v2, vint8mf2_t v3);
vint8mf2_t __riscv_vget_v_i8mf2x4_i8mf2(vint8mf2x4_t src, size_t index);
vint8mf2x4_t __riscv_vset_v_i8mf2x4_i8mf2(vint8mf2x4_t dest, size_t index, vint8mf2_t value);
vint8mf2x4_t __riscv_vlseg4e8_v_i8mf2x4(const signed char *rs1, size_t vl);
void __riscv_vsseg4e8_v_i8mf2x4(signed char *rs1, vint8mf2x4_t vs3, size_t vl);
vint8mf2x4_t __riscv_vlseg4e8ff_v_i8mf2x4(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf2x4_t __riscv_vlsseg4e8_v_i8mf2x4(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_i8mf2x4(signed char *rs1, long rs2, vint8mf2x4_t vs3, size_t vl);
vint8mf2x4_t __riscv_vluxseg4ei8_v_i8mf2x4(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
vint8mf2x4_t __riscv_vloxseg4ei8_v_i8mf2x4(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i8mf2x4(signed char *rs1, vuint8mf2_t rs2, vint8mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i8mf2x4(signed char *rs1, vuint8mf2_t rs2, vint8mf2x4_t vs3, size_t vl);
vint8mf2x4_t __riscv_vluxseg4ei16_v_i8mf2x4(const signed char *rs1, vuint16m1_t rs2, size_t vl);
vint8mf2x4_t __riscv_vloxseg4ei16_v_i8mf2x4(const signed char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i8mf2x4(signed char *rs1, vuint16m1_t rs2, vint8mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i8mf2x4(signed char *rs1, vuint16m1_t rs2, vint8mf2x4_t vs3, size_t vl);
vint8mf2x4_t __riscv_vluxseg4ei32_v_i8mf2x4(const signed char *rs1, vuint32m2_t rs2, size_t vl);
vint8mf2x4_t __riscv_vloxseg4ei32_v_i8mf2x4(const signed char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i8mf2x4(signed char *rs1, vuint32m2_t rs2, vint8mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i8mf2x4(signed char *rs1, vuint32m2_t rs2, vint8mf2x4_t vs3, size_t vl);
vint8mf2x4_t __riscv_vluxseg4ei64_v_i8mf2x4(const signed char *rs1, vuint64m4_t rs2, size_t vl);
vint8mf2x4_t __riscv_vloxseg4ei64_v_i8mf2x4(const signed char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i8mf2x4(signed char *rs1, vuint64m4_t rs2, vint8mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i8mf2x4(signed char *rs1, vuint64m4_t rs2, vint8mf2x4_t vs3, size_t vl);
vuint8mf2x5_t __riscv_vundefined_u8mf2x5(void);
vuint8mf2x5_t __riscv_vcreate_v_u8mf2_u8mf2x5(vuint8mf2_t v0, vuint8mf2_t v1, vuint8mf2_t v2, vuint8mf2_t v3, vuint8mf2_t v4);
vuint8mf2_t __riscv_vget_v_u8mf2x5_u8mf2(vuint8mf2x5_t src, size_t index);
vuint8mf2x5_t __riscv_vset_v_u8mf2x5_u8mf2(vuint8mf2x5_t dest, size_t index, vuint8mf2_t value);
vuint8mf2x5_t __riscv_vlseg5e8_v_u8mf2x5(const unsigned char *rs1, size_t vl);
void __riscv_vsseg5e8_v_u8mf2x5(unsigned char *rs1, vuint8mf2x5_t vs3, size_t vl);
vuint8mf2x5_t __riscv_vlseg5e8ff_v_u8mf2x5(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf2x5_t __riscv_vlsseg5e8_v_u8mf2x5(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg5e8_v_u8mf2x5(unsigned char *rs1, long rs2, vuint8mf2x5_t vs3, size_t vl);
vuint8mf2x5_t __riscv_vluxseg5ei8_v_u8mf2x5(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
vuint8mf2x5_t __riscv_vloxseg5ei8_v_u8mf2x5(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u8mf2x5(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u8mf2x5(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x5_t vs3, size_t vl);
vuint8mf2x5_t __riscv_vluxseg5ei16_v_u8mf2x5(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
vuint8mf2x5_t __riscv_vloxseg5ei16_v_u8mf2x5(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u8mf2x5(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u8mf2x5(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x5_t vs3, size_t vl);
vuint8mf2x5_t __riscv_vluxseg5ei32_v_u8mf2x5(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
vuint8mf2x5_t __riscv_vloxseg5ei32_v_u8mf2x5(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u8mf2x5(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u8mf2x5(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x5_t vs3, size_t vl);
vuint8mf2x5_t __riscv_vluxseg5ei64_v_u8mf2x5(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
vuint8mf2x5_t __riscv_vloxseg5ei64_v_u8mf2x5(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u8mf2x5(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u8mf2x5(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x5_t vs3, size_t vl);
vint8mf2x5_t __riscv_vundefined_i8mf2x5(void);
vint8mf2x5_t __riscv_vcreate_v_i8mf2_i8mf2x5(vint8mf2_t v0, vint8mf2_t v1, vint8mf2_t v2, vint8mf2_t v3, vint8mf2_t v4);
vint8mf2_t __riscv_vget_v_i8mf2x5_i8mf2(vint8mf2x5_t src, size_t index);
vint8mf2x5_t __riscv_vset_v_i8mf2x5_i8mf2(vint8mf2x5_t dest, size_t index, vint8mf2_t value);
vint8mf2x5_t __riscv_vlseg5e8_v_i8mf2x5(const signed char *rs1, size_t vl);
void __riscv_vsseg5e8_v_i8mf2x5(signed char *rs1, vint8mf2x5_t vs3, size_t vl);
vint8mf2x5_t __riscv_vlseg5e8ff_v_i8mf2x5(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf2x5_t __riscv_vlsseg5e8_v_i8mf2x5(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg5e8_v_i8mf2x5(signed char *rs1, long rs2, vint8mf2x5_t vs3, size_t vl);
vint8mf2x5_t __riscv_vluxseg5ei8_v_i8mf2x5(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
vint8mf2x5_t __riscv_vloxseg5ei8_v_i8mf2x5(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i8mf2x5(signed char *rs1, vuint8mf2_t rs2, vint8mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i8mf2x5(signed char *rs1, vuint8mf2_t rs2, vint8mf2x5_t vs3, size_t vl);
vint8mf2x5_t __riscv_vluxseg5ei16_v_i8mf2x5(const signed char *rs1, vuint16m1_t rs2, size_t vl);
vint8mf2x5_t __riscv_vloxseg5ei16_v_i8mf2x5(const signed char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i8mf2x5(signed char *rs1, vuint16m1_t rs2, vint8mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i8mf2x5(signed char *rs1, vuint16m1_t rs2, vint8mf2x5_t vs3, size_t vl);
vint8mf2x5_t __riscv_vluxseg5ei32_v_i8mf2x5(const signed char *rs1, vuint32m2_t rs2, size_t vl);
vint8mf2x5_t __riscv_vloxseg5ei32_v_i8mf2x5(const signed char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i8mf2x5(signed char *rs1, vuint32m2_t rs2, vint8mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i8mf2x5(signed char *rs1, vuint32m2_t rs2, vint8mf2x5_t vs3, size_t vl);
vint8mf2x5_t __riscv_vluxseg5ei64_v_i8mf2x5(const signed char *rs1, vuint64m4_t rs2, size_t vl);
vint8mf2x5_t __riscv_vloxseg5ei64_v_i8mf2x5(const signed char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i8mf2x5(signed char *rs1, vuint64m4_t rs2, vint8mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i8mf2x5(signed char *rs1, vuint64m4_t rs2, vint8mf2x5_t vs3, size_t vl);
vuint8mf2x6_t __riscv_vundefined_u8mf2x6(void);
vuint8mf2x6_t __riscv_vcreate_v_u8mf2_u8mf2x6(vuint8mf2_t v0, vuint8mf2_t v1, vuint8mf2_t v2, vuint8mf2_t v3, vuint8mf2_t v4, vuint8mf2_t v5);
vuint8mf2_t __riscv_vget_v_u8mf2x6_u8mf2(vuint8mf2x6_t src, size_t index);
vuint8mf2x6_t __riscv_vset_v_u8mf2x6_u8mf2(vuint8mf2x6_t dest, size_t index, vuint8mf2_t value);
vuint8mf2x6_t __riscv_vlseg6e8_v_u8mf2x6(const unsigned char *rs1, size_t vl);
void __riscv_vsseg6e8_v_u8mf2x6(unsigned char *rs1, vuint8mf2x6_t vs3, size_t vl);
vuint8mf2x6_t __riscv_vlseg6e8ff_v_u8mf2x6(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf2x6_t __riscv_vlsseg6e8_v_u8mf2x6(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg6e8_v_u8mf2x6(unsigned char *rs1, long rs2, vuint8mf2x6_t vs3, size_t vl);
vuint8mf2x6_t __riscv_vluxseg6ei8_v_u8mf2x6(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
vuint8mf2x6_t __riscv_vloxseg6ei8_v_u8mf2x6(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u8mf2x6(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u8mf2x6(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x6_t vs3, size_t vl);
vuint8mf2x6_t __riscv_vluxseg6ei16_v_u8mf2x6(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
vuint8mf2x6_t __riscv_vloxseg6ei16_v_u8mf2x6(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u8mf2x6(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u8mf2x6(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x6_t vs3, size_t vl);
vuint8mf2x6_t __riscv_vluxseg6ei32_v_u8mf2x6(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
vuint8mf2x6_t __riscv_vloxseg6ei32_v_u8mf2x6(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u8mf2x6(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u8mf2x6(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x6_t vs3, size_t vl);
vuint8mf2x6_t __riscv_vluxseg6ei64_v_u8mf2x6(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
vuint8mf2x6_t __riscv_vloxseg6ei64_v_u8mf2x6(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u8mf2x6(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u8mf2x6(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x6_t vs3, size_t vl);
vint8mf2x6_t __riscv_vundefined_i8mf2x6(void);
vint8mf2x6_t __riscv_vcreate_v_i8mf2_i8mf2x6(vint8mf2_t v0, vint8mf2_t v1, vint8mf2_t v2, vint8mf2_t v3, vint8mf2_t v4, vint8mf2_t v5);
vint8mf2_t __riscv_vget_v_i8mf2x6_i8mf2(vint8mf2x6_t src, size_t index);
vint8mf2x6_t __riscv_vset_v_i8mf2x6_i8mf2(vint8mf2x6_t dest, size_t index, vint8mf2_t value);
vint8mf2x6_t __riscv_vlseg6e8_v_i8mf2x6(const signed char *rs1, size_t vl);
void __riscv_vsseg6e8_v_i8mf2x6(signed char *rs1, vint8mf2x6_t vs3, size_t vl);
vint8mf2x6_t __riscv_vlseg6e8ff_v_i8mf2x6(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf2x6_t __riscv_vlsseg6e8_v_i8mf2x6(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg6e8_v_i8mf2x6(signed char *rs1, long rs2, vint8mf2x6_t vs3, size_t vl);
vint8mf2x6_t __riscv_vluxseg6ei8_v_i8mf2x6(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
vint8mf2x6_t __riscv_vloxseg6ei8_v_i8mf2x6(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i8mf2x6(signed char *rs1, vuint8mf2_t rs2, vint8mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i8mf2x6(signed char *rs1, vuint8mf2_t rs2, vint8mf2x6_t vs3, size_t vl);
vint8mf2x6_t __riscv_vluxseg6ei16_v_i8mf2x6(const signed char *rs1, vuint16m1_t rs2, size_t vl);
vint8mf2x6_t __riscv_vloxseg6ei16_v_i8mf2x6(const signed char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i8mf2x6(signed char *rs1, vuint16m1_t rs2, vint8mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i8mf2x6(signed char *rs1, vuint16m1_t rs2, vint8mf2x6_t vs3, size_t vl);
vint8mf2x6_t __riscv_vluxseg6ei32_v_i8mf2x6(const signed char *rs1, vuint32m2_t rs2, size_t vl);
vint8mf2x6_t __riscv_vloxseg6ei32_v_i8mf2x6(const signed char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i8mf2x6(signed char *rs1, vuint32m2_t rs2, vint8mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i8mf2x6(signed char *rs1, vuint32m2_t rs2, vint8mf2x6_t vs3, size_t vl);
vint8mf2x6_t __riscv_vluxseg6ei64_v_i8mf2x6(const signed char *rs1, vuint64m4_t rs2, size_t vl);
vint8mf2x6_t __riscv_vloxseg6ei64_v_i8mf2x6(const signed char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i8mf2x6(signed char *rs1, vuint64m4_t rs2, vint8mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i8mf2x6(signed char *rs1, vuint64m4_t rs2, vint8mf2x6_t vs3, size_t vl);
vuint8mf2x7_t __riscv_vundefined_u8mf2x7(void);
vuint8mf2x7_t __riscv_vcreate_v_u8mf2_u8mf2x7(vuint8mf2_t v0, vuint8mf2_t v1, vuint8mf2_t v2, vuint8mf2_t v3, vuint8mf2_t v4, vuint8mf2_t v5, vuint8mf2_t v6);
vuint8mf2_t __riscv_vget_v_u8mf2x7_u8mf2(vuint8mf2x7_t src, size_t index);
vuint8mf2x7_t __riscv_vset_v_u8mf2x7_u8mf2(vuint8mf2x7_t dest, size_t index, vuint8mf2_t value);
vuint8mf2x7_t __riscv_vlseg7e8_v_u8mf2x7(const unsigned char *rs1, size_t vl);
void __riscv_vsseg7e8_v_u8mf2x7(unsigned char *rs1, vuint8mf2x7_t vs3, size_t vl);
vuint8mf2x7_t __riscv_vlseg7e8ff_v_u8mf2x7(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf2x7_t __riscv_vlsseg7e8_v_u8mf2x7(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg7e8_v_u8mf2x7(unsigned char *rs1, long rs2, vuint8mf2x7_t vs3, size_t vl);
vuint8mf2x7_t __riscv_vluxseg7ei8_v_u8mf2x7(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
vuint8mf2x7_t __riscv_vloxseg7ei8_v_u8mf2x7(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u8mf2x7(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u8mf2x7(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x7_t vs3, size_t vl);
vuint8mf2x7_t __riscv_vluxseg7ei16_v_u8mf2x7(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
vuint8mf2x7_t __riscv_vloxseg7ei16_v_u8mf2x7(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u8mf2x7(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u8mf2x7(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x7_t vs3, size_t vl);
vuint8mf2x7_t __riscv_vluxseg7ei32_v_u8mf2x7(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
vuint8mf2x7_t __riscv_vloxseg7ei32_v_u8mf2x7(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u8mf2x7(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u8mf2x7(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x7_t vs3, size_t vl);
vuint8mf2x7_t __riscv_vluxseg7ei64_v_u8mf2x7(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
vuint8mf2x7_t __riscv_vloxseg7ei64_v_u8mf2x7(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u8mf2x7(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u8mf2x7(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x7_t vs3, size_t vl);
vint8mf2x7_t __riscv_vundefined_i8mf2x7(void);
vint8mf2x7_t __riscv_vcreate_v_i8mf2_i8mf2x7(vint8mf2_t v0, vint8mf2_t v1, vint8mf2_t v2, vint8mf2_t v3, vint8mf2_t v4, vint8mf2_t v5, vint8mf2_t v6);
vint8mf2_t __riscv_vget_v_i8mf2x7_i8mf2(vint8mf2x7_t src, size_t index);
vint8mf2x7_t __riscv_vset_v_i8mf2x7_i8mf2(vint8mf2x7_t dest, size_t index, vint8mf2_t value);
vint8mf2x7_t __riscv_vlseg7e8_v_i8mf2x7(const signed char *rs1, size_t vl);
void __riscv_vsseg7e8_v_i8mf2x7(signed char *rs1, vint8mf2x7_t vs3, size_t vl);
vint8mf2x7_t __riscv_vlseg7e8ff_v_i8mf2x7(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf2x7_t __riscv_vlsseg7e8_v_i8mf2x7(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg7e8_v_i8mf2x7(signed char *rs1, long rs2, vint8mf2x7_t vs3, size_t vl);
vint8mf2x7_t __riscv_vluxseg7ei8_v_i8mf2x7(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
vint8mf2x7_t __riscv_vloxseg7ei8_v_i8mf2x7(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i8mf2x7(signed char *rs1, vuint8mf2_t rs2, vint8mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i8mf2x7(signed char *rs1, vuint8mf2_t rs2, vint8mf2x7_t vs3, size_t vl);
vint8mf2x7_t __riscv_vluxseg7ei16_v_i8mf2x7(const signed char *rs1, vuint16m1_t rs2, size_t vl);
vint8mf2x7_t __riscv_vloxseg7ei16_v_i8mf2x7(const signed char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i8mf2x7(signed char *rs1, vuint16m1_t rs2, vint8mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i8mf2x7(signed char *rs1, vuint16m1_t rs2, vint8mf2x7_t vs3, size_t vl);
vint8mf2x7_t __riscv_vluxseg7ei32_v_i8mf2x7(const signed char *rs1, vuint32m2_t rs2, size_t vl);
vint8mf2x7_t __riscv_vloxseg7ei32_v_i8mf2x7(const signed char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i8mf2x7(signed char *rs1, vuint32m2_t rs2, vint8mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i8mf2x7(signed char *rs1, vuint32m2_t rs2, vint8mf2x7_t vs3, size_t vl);
vint8mf2x7_t __riscv_vluxseg7ei64_v_i8mf2x7(const signed char *rs1, vuint64m4_t rs2, size_t vl);
vint8mf2x7_t __riscv_vloxseg7ei64_v_i8mf2x7(const signed char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i8mf2x7(signed char *rs1, vuint64m4_t rs2, vint8mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i8mf2x7(signed char *rs1, vuint64m4_t rs2, vint8mf2x7_t vs3, size_t vl);
vuint8mf2x8_t __riscv_vundefined_u8mf2x8(void);
vuint8mf2x8_t __riscv_vcreate_v_u8mf2_u8mf2x8(vuint8mf2_t v0, vuint8mf2_t v1, vuint8mf2_t v2, vuint8mf2_t v3, vuint8mf2_t v4, vuint8mf2_t v5, vuint8mf2_t v6, vuint8mf2_t v7);
vuint8mf2_t __riscv_vget_v_u8mf2x8_u8mf2(vuint8mf2x8_t src, size_t index);
vuint8mf2x8_t __riscv_vset_v_u8mf2x8_u8mf2(vuint8mf2x8_t dest, size_t index, vuint8mf2_t value);
vuint8mf2x8_t __riscv_vlseg8e8_v_u8mf2x8(const unsigned char *rs1, size_t vl);
void __riscv_vsseg8e8_v_u8mf2x8(unsigned char *rs1, vuint8mf2x8_t vs3, size_t vl);
vuint8mf2x8_t __riscv_vlseg8e8ff_v_u8mf2x8(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8mf2x8_t __riscv_vlsseg8e8_v_u8mf2x8(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg8e8_v_u8mf2x8(unsigned char *rs1, long rs2, vuint8mf2x8_t vs3, size_t vl);
vuint8mf2x8_t __riscv_vluxseg8ei8_v_u8mf2x8(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
vuint8mf2x8_t __riscv_vloxseg8ei8_v_u8mf2x8(const unsigned char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u8mf2x8(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u8mf2x8(unsigned char *rs1, vuint8mf2_t rs2, vuint8mf2x8_t vs3, size_t vl);
vuint8mf2x8_t __riscv_vluxseg8ei16_v_u8mf2x8(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
vuint8mf2x8_t __riscv_vloxseg8ei16_v_u8mf2x8(const unsigned char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u8mf2x8(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u8mf2x8(unsigned char *rs1, vuint16m1_t rs2, vuint8mf2x8_t vs3, size_t vl);
vuint8mf2x8_t __riscv_vluxseg8ei32_v_u8mf2x8(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
vuint8mf2x8_t __riscv_vloxseg8ei32_v_u8mf2x8(const unsigned char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u8mf2x8(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u8mf2x8(unsigned char *rs1, vuint32m2_t rs2, vuint8mf2x8_t vs3, size_t vl);
vuint8mf2x8_t __riscv_vluxseg8ei64_v_u8mf2x8(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
vuint8mf2x8_t __riscv_vloxseg8ei64_v_u8mf2x8(const unsigned char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u8mf2x8(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u8mf2x8(unsigned char *rs1, vuint64m4_t rs2, vuint8mf2x8_t vs3, size_t vl);
vint8mf2x8_t __riscv_vundefined_i8mf2x8(void);
vint8mf2x8_t __riscv_vcreate_v_i8mf2_i8mf2x8(vint8mf2_t v0, vint8mf2_t v1, vint8mf2_t v2, vint8mf2_t v3, vint8mf2_t v4, vint8mf2_t v5, vint8mf2_t v6, vint8mf2_t v7);
vint8mf2_t __riscv_vget_v_i8mf2x8_i8mf2(vint8mf2x8_t src, size_t index);
vint8mf2x8_t __riscv_vset_v_i8mf2x8_i8mf2(vint8mf2x8_t dest, size_t index, vint8mf2_t value);
vint8mf2x8_t __riscv_vlseg8e8_v_i8mf2x8(const signed char *rs1, size_t vl);
void __riscv_vsseg8e8_v_i8mf2x8(signed char *rs1, vint8mf2x8_t vs3, size_t vl);
vint8mf2x8_t __riscv_vlseg8e8ff_v_i8mf2x8(const signed char *rs1, size_t *new_vl, size_t vl);
vint8mf2x8_t __riscv_vlsseg8e8_v_i8mf2x8(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg8e8_v_i8mf2x8(signed char *rs1, long rs2, vint8mf2x8_t vs3, size_t vl);
vint8mf2x8_t __riscv_vluxseg8ei8_v_i8mf2x8(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
vint8mf2x8_t __riscv_vloxseg8ei8_v_i8mf2x8(const signed char *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i8mf2x8(signed char *rs1, vuint8mf2_t rs2, vint8mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i8mf2x8(signed char *rs1, vuint8mf2_t rs2, vint8mf2x8_t vs3, size_t vl);
vint8mf2x8_t __riscv_vluxseg8ei16_v_i8mf2x8(const signed char *rs1, vuint16m1_t rs2, size_t vl);
vint8mf2x8_t __riscv_vloxseg8ei16_v_i8mf2x8(const signed char *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i8mf2x8(signed char *rs1, vuint16m1_t rs2, vint8mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i8mf2x8(signed char *rs1, vuint16m1_t rs2, vint8mf2x8_t vs3, size_t vl);
vint8mf2x8_t __riscv_vluxseg8ei32_v_i8mf2x8(const signed char *rs1, vuint32m2_t rs2, size_t vl);
vint8mf2x8_t __riscv_vloxseg8ei32_v_i8mf2x8(const signed char *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i8mf2x8(signed char *rs1, vuint32m2_t rs2, vint8mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i8mf2x8(signed char *rs1, vuint32m2_t rs2, vint8mf2x8_t vs3, size_t vl);
vint8mf2x8_t __riscv_vluxseg8ei64_v_i8mf2x8(const signed char *rs1, vuint64m4_t rs2, size_t vl);
vint8mf2x8_t __riscv_vloxseg8ei64_v_i8mf2x8(const signed char *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i8mf2x8(signed char *rs1, vuint64m4_t rs2, vint8mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i8mf2x8(signed char *rs1, vuint64m4_t rs2, vint8mf2x8_t vs3, size_t vl);
vuint8m1x2_t __riscv_vundefined_u8m1x2(void);
vuint8m1x2_t __riscv_vcreate_v_u8m1_u8m1x2(vuint8m1_t v0, vuint8m1_t v1);
vuint8m1_t __riscv_vget_v_u8m1x2_u8m1(vuint8m1x2_t src, size_t index);
vuint8m1x2_t __riscv_vset_v_u8m1x2_u8m1(vuint8m1x2_t dest, size_t index, vuint8m1_t value);
vuint8m1x2_t __riscv_vlseg2e8_v_u8m1x2(const unsigned char *rs1, size_t vl);
void __riscv_vsseg2e8_v_u8m1x2(unsigned char *rs1, vuint8m1x2_t vs3, size_t vl);
vuint8m1x2_t __riscv_vlseg2e8ff_v_u8m1x2(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m1x2_t __riscv_vlsseg2e8_v_u8m1x2(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_u8m1x2(unsigned char *rs1, long rs2, vuint8m1x2_t vs3, size_t vl);
vuint8m1x2_t __riscv_vluxseg2ei8_v_u8m1x2(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
vuint8m1x2_t __riscv_vloxseg2ei8_v_u8m1x2(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u8m1x2(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u8m1x2(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x2_t vs3, size_t vl);
vuint8m1x2_t __riscv_vluxseg2ei16_v_u8m1x2(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
vuint8m1x2_t __riscv_vloxseg2ei16_v_u8m1x2(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u8m1x2(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u8m1x2(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x2_t vs3, size_t vl);
vuint8m1x2_t __riscv_vluxseg2ei32_v_u8m1x2(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
vuint8m1x2_t __riscv_vloxseg2ei32_v_u8m1x2(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u8m1x2(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u8m1x2(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x2_t vs3, size_t vl);
vuint8m1x2_t __riscv_vluxseg2ei64_v_u8m1x2(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
vuint8m1x2_t __riscv_vloxseg2ei64_v_u8m1x2(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u8m1x2(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u8m1x2(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x2_t vs3, size_t vl);
vint8m1x2_t __riscv_vundefined_i8m1x2(void);
vint8m1x2_t __riscv_vcreate_v_i8m1_i8m1x2(vint8m1_t v0, vint8m1_t v1);
vint8m1_t __riscv_vget_v_i8m1x2_i8m1(vint8m1x2_t src, size_t index);
vint8m1x2_t __riscv_vset_v_i8m1x2_i8m1(vint8m1x2_t dest, size_t index, vint8m1_t value);
vint8m1x2_t __riscv_vlseg2e8_v_i8m1x2(const signed char *rs1, size_t vl);
void __riscv_vsseg2e8_v_i8m1x2(signed char *rs1, vint8m1x2_t vs3, size_t vl);
vint8m1x2_t __riscv_vlseg2e8ff_v_i8m1x2(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m1x2_t __riscv_vlsseg2e8_v_i8m1x2(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_i8m1x2(signed char *rs1, long rs2, vint8m1x2_t vs3, size_t vl);
vint8m1x2_t __riscv_vluxseg2ei8_v_i8m1x2(const signed char *rs1, vuint8m1_t rs2, size_t vl);
vint8m1x2_t __riscv_vloxseg2ei8_v_i8m1x2(const signed char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i8m1x2(signed char *rs1, vuint8m1_t rs2, vint8m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i8m1x2(signed char *rs1, vuint8m1_t rs2, vint8m1x2_t vs3, size_t vl);
vint8m1x2_t __riscv_vluxseg2ei16_v_i8m1x2(const signed char *rs1, vuint16m2_t rs2, size_t vl);
vint8m1x2_t __riscv_vloxseg2ei16_v_i8m1x2(const signed char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i8m1x2(signed char *rs1, vuint16m2_t rs2, vint8m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i8m1x2(signed char *rs1, vuint16m2_t rs2, vint8m1x2_t vs3, size_t vl);
vint8m1x2_t __riscv_vluxseg2ei32_v_i8m1x2(const signed char *rs1, vuint32m4_t rs2, size_t vl);
vint8m1x2_t __riscv_vloxseg2ei32_v_i8m1x2(const signed char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i8m1x2(signed char *rs1, vuint32m4_t rs2, vint8m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i8m1x2(signed char *rs1, vuint32m4_t rs2, vint8m1x2_t vs3, size_t vl);
vint8m1x2_t __riscv_vluxseg2ei64_v_i8m1x2(const signed char *rs1, vuint64m8_t rs2, size_t vl);
vint8m1x2_t __riscv_vloxseg2ei64_v_i8m1x2(const signed char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i8m1x2(signed char *rs1, vuint64m8_t rs2, vint8m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i8m1x2(signed char *rs1, vuint64m8_t rs2, vint8m1x2_t vs3, size_t vl);
vuint8m1x3_t __riscv_vundefined_u8m1x3(void);
vuint8m1x3_t __riscv_vcreate_v_u8m1_u8m1x3(vuint8m1_t v0, vuint8m1_t v1, vuint8m1_t v2);
vuint8m1_t __riscv_vget_v_u8m1x3_u8m1(vuint8m1x3_t src, size_t index);
vuint8m1x3_t __riscv_vset_v_u8m1x3_u8m1(vuint8m1x3_t dest, size_t index, vuint8m1_t value);
vuint8m1x3_t __riscv_vlseg3e8_v_u8m1x3(const unsigned char *rs1, size_t vl);
void __riscv_vsseg3e8_v_u8m1x3(unsigned char *rs1, vuint8m1x3_t vs3, size_t vl);
vuint8m1x3_t __riscv_vlseg3e8ff_v_u8m1x3(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m1x3_t __riscv_vlsseg3e8_v_u8m1x3(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_u8m1x3(unsigned char *rs1, long rs2, vuint8m1x3_t vs3, size_t vl);
vuint8m1x3_t __riscv_vluxseg3ei8_v_u8m1x3(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
vuint8m1x3_t __riscv_vloxseg3ei8_v_u8m1x3(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u8m1x3(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u8m1x3(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x3_t vs3, size_t vl);
vuint8m1x3_t __riscv_vluxseg3ei16_v_u8m1x3(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
vuint8m1x3_t __riscv_vloxseg3ei16_v_u8m1x3(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u8m1x3(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u8m1x3(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x3_t vs3, size_t vl);
vuint8m1x3_t __riscv_vluxseg3ei32_v_u8m1x3(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
vuint8m1x3_t __riscv_vloxseg3ei32_v_u8m1x3(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u8m1x3(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u8m1x3(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x3_t vs3, size_t vl);
vuint8m1x3_t __riscv_vluxseg3ei64_v_u8m1x3(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
vuint8m1x3_t __riscv_vloxseg3ei64_v_u8m1x3(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u8m1x3(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u8m1x3(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x3_t vs3, size_t vl);
vint8m1x3_t __riscv_vundefined_i8m1x3(void);
vint8m1x3_t __riscv_vcreate_v_i8m1_i8m1x3(vint8m1_t v0, vint8m1_t v1, vint8m1_t v2);
vint8m1_t __riscv_vget_v_i8m1x3_i8m1(vint8m1x3_t src, size_t index);
vint8m1x3_t __riscv_vset_v_i8m1x3_i8m1(vint8m1x3_t dest, size_t index, vint8m1_t value);
vint8m1x3_t __riscv_vlseg3e8_v_i8m1x3(const signed char *rs1, size_t vl);
void __riscv_vsseg3e8_v_i8m1x3(signed char *rs1, vint8m1x3_t vs3, size_t vl);
vint8m1x3_t __riscv_vlseg3e8ff_v_i8m1x3(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m1x3_t __riscv_vlsseg3e8_v_i8m1x3(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_i8m1x3(signed char *rs1, long rs2, vint8m1x3_t vs3, size_t vl);
vint8m1x3_t __riscv_vluxseg3ei8_v_i8m1x3(const signed char *rs1, vuint8m1_t rs2, size_t vl);
vint8m1x3_t __riscv_vloxseg3ei8_v_i8m1x3(const signed char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i8m1x3(signed char *rs1, vuint8m1_t rs2, vint8m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i8m1x3(signed char *rs1, vuint8m1_t rs2, vint8m1x3_t vs3, size_t vl);
vint8m1x3_t __riscv_vluxseg3ei16_v_i8m1x3(const signed char *rs1, vuint16m2_t rs2, size_t vl);
vint8m1x3_t __riscv_vloxseg3ei16_v_i8m1x3(const signed char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i8m1x3(signed char *rs1, vuint16m2_t rs2, vint8m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i8m1x3(signed char *rs1, vuint16m2_t rs2, vint8m1x3_t vs3, size_t vl);
vint8m1x3_t __riscv_vluxseg3ei32_v_i8m1x3(const signed char *rs1, vuint32m4_t rs2, size_t vl);
vint8m1x3_t __riscv_vloxseg3ei32_v_i8m1x3(const signed char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i8m1x3(signed char *rs1, vuint32m4_t rs2, vint8m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i8m1x3(signed char *rs1, vuint32m4_t rs2, vint8m1x3_t vs3, size_t vl);
vint8m1x3_t __riscv_vluxseg3ei64_v_i8m1x3(const signed char *rs1, vuint64m8_t rs2, size_t vl);
vint8m1x3_t __riscv_vloxseg3ei64_v_i8m1x3(const signed char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i8m1x3(signed char *rs1, vuint64m8_t rs2, vint8m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i8m1x3(signed char *rs1, vuint64m8_t rs2, vint8m1x3_t vs3, size_t vl);
vuint8m1x4_t __riscv_vundefined_u8m1x4(void);
vuint8m1x4_t __riscv_vcreate_v_u8m1_u8m1x4(vuint8m1_t v0, vuint8m1_t v1, vuint8m1_t v2, vuint8m1_t v3);
vuint8m1_t __riscv_vget_v_u8m1x4_u8m1(vuint8m1x4_t src, size_t index);
vuint8m1x4_t __riscv_vset_v_u8m1x4_u8m1(vuint8m1x4_t dest, size_t index, vuint8m1_t value);
vuint8m1x4_t __riscv_vlseg4e8_v_u8m1x4(const unsigned char *rs1, size_t vl);
void __riscv_vsseg4e8_v_u8m1x4(unsigned char *rs1, vuint8m1x4_t vs3, size_t vl);
vuint8m1x4_t __riscv_vlseg4e8ff_v_u8m1x4(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m1x4_t __riscv_vlsseg4e8_v_u8m1x4(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_u8m1x4(unsigned char *rs1, long rs2, vuint8m1x4_t vs3, size_t vl);
vuint8m1x4_t __riscv_vluxseg4ei8_v_u8m1x4(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
vuint8m1x4_t __riscv_vloxseg4ei8_v_u8m1x4(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u8m1x4(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u8m1x4(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x4_t vs3, size_t vl);
vuint8m1x4_t __riscv_vluxseg4ei16_v_u8m1x4(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
vuint8m1x4_t __riscv_vloxseg4ei16_v_u8m1x4(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u8m1x4(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u8m1x4(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x4_t vs3, size_t vl);
vuint8m1x4_t __riscv_vluxseg4ei32_v_u8m1x4(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
vuint8m1x4_t __riscv_vloxseg4ei32_v_u8m1x4(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u8m1x4(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u8m1x4(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x4_t vs3, size_t vl);
vuint8m1x4_t __riscv_vluxseg4ei64_v_u8m1x4(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
vuint8m1x4_t __riscv_vloxseg4ei64_v_u8m1x4(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u8m1x4(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u8m1x4(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x4_t vs3, size_t vl);
vint8m1x4_t __riscv_vundefined_i8m1x4(void);
vint8m1x4_t __riscv_vcreate_v_i8m1_i8m1x4(vint8m1_t v0, vint8m1_t v1, vint8m1_t v2, vint8m1_t v3);
vint8m1_t __riscv_vget_v_i8m1x4_i8m1(vint8m1x4_t src, size_t index);
vint8m1x4_t __riscv_vset_v_i8m1x4_i8m1(vint8m1x4_t dest, size_t index, vint8m1_t value);
vint8m1x4_t __riscv_vlseg4e8_v_i8m1x4(const signed char *rs1, size_t vl);
void __riscv_vsseg4e8_v_i8m1x4(signed char *rs1, vint8m1x4_t vs3, size_t vl);
vint8m1x4_t __riscv_vlseg4e8ff_v_i8m1x4(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m1x4_t __riscv_vlsseg4e8_v_i8m1x4(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_i8m1x4(signed char *rs1, long rs2, vint8m1x4_t vs3, size_t vl);
vint8m1x4_t __riscv_vluxseg4ei8_v_i8m1x4(const signed char *rs1, vuint8m1_t rs2, size_t vl);
vint8m1x4_t __riscv_vloxseg4ei8_v_i8m1x4(const signed char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i8m1x4(signed char *rs1, vuint8m1_t rs2, vint8m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i8m1x4(signed char *rs1, vuint8m1_t rs2, vint8m1x4_t vs3, size_t vl);
vint8m1x4_t __riscv_vluxseg4ei16_v_i8m1x4(const signed char *rs1, vuint16m2_t rs2, size_t vl);
vint8m1x4_t __riscv_vloxseg4ei16_v_i8m1x4(const signed char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i8m1x4(signed char *rs1, vuint16m2_t rs2, vint8m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i8m1x4(signed char *rs1, vuint16m2_t rs2, vint8m1x4_t vs3, size_t vl);
vint8m1x4_t __riscv_vluxseg4ei32_v_i8m1x4(const signed char *rs1, vuint32m4_t rs2, size_t vl);
vint8m1x4_t __riscv_vloxseg4ei32_v_i8m1x4(const signed char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i8m1x4(signed char *rs1, vuint32m4_t rs2, vint8m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i8m1x4(signed char *rs1, vuint32m4_t rs2, vint8m1x4_t vs3, size_t vl);
vint8m1x4_t __riscv_vluxseg4ei64_v_i8m1x4(const signed char *rs1, vuint64m8_t rs2, size_t vl);
vint8m1x4_t __riscv_vloxseg4ei64_v_i8m1x4(const signed char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i8m1x4(signed char *rs1, vuint64m8_t rs2, vint8m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i8m1x4(signed char *rs1, vuint64m8_t rs2, vint8m1x4_t vs3, size_t vl);
vuint8m1x5_t __riscv_vundefined_u8m1x5(void);
vuint8m1x5_t __riscv_vcreate_v_u8m1_u8m1x5(vuint8m1_t v0, vuint8m1_t v1, vuint8m1_t v2, vuint8m1_t v3, vuint8m1_t v4);
vuint8m1_t __riscv_vget_v_u8m1x5_u8m1(vuint8m1x5_t src, size_t index);
vuint8m1x5_t __riscv_vset_v_u8m1x5_u8m1(vuint8m1x5_t dest, size_t index, vuint8m1_t value);
vuint8m1x5_t __riscv_vlseg5e8_v_u8m1x5(const unsigned char *rs1, size_t vl);
void __riscv_vsseg5e8_v_u8m1x5(unsigned char *rs1, vuint8m1x5_t vs3, size_t vl);
vuint8m1x5_t __riscv_vlseg5e8ff_v_u8m1x5(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m1x5_t __riscv_vlsseg5e8_v_u8m1x5(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg5e8_v_u8m1x5(unsigned char *rs1, long rs2, vuint8m1x5_t vs3, size_t vl);
vuint8m1x5_t __riscv_vluxseg5ei8_v_u8m1x5(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
vuint8m1x5_t __riscv_vloxseg5ei8_v_u8m1x5(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u8m1x5(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u8m1x5(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x5_t vs3, size_t vl);
vuint8m1x5_t __riscv_vluxseg5ei16_v_u8m1x5(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
vuint8m1x5_t __riscv_vloxseg5ei16_v_u8m1x5(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u8m1x5(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u8m1x5(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x5_t vs3, size_t vl);
vuint8m1x5_t __riscv_vluxseg5ei32_v_u8m1x5(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
vuint8m1x5_t __riscv_vloxseg5ei32_v_u8m1x5(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u8m1x5(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u8m1x5(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x5_t vs3, size_t vl);
vuint8m1x5_t __riscv_vluxseg5ei64_v_u8m1x5(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
vuint8m1x5_t __riscv_vloxseg5ei64_v_u8m1x5(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u8m1x5(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u8m1x5(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x5_t vs3, size_t vl);
vint8m1x5_t __riscv_vundefined_i8m1x5(void);
vint8m1x5_t __riscv_vcreate_v_i8m1_i8m1x5(vint8m1_t v0, vint8m1_t v1, vint8m1_t v2, vint8m1_t v3, vint8m1_t v4);
vint8m1_t __riscv_vget_v_i8m1x5_i8m1(vint8m1x5_t src, size_t index);
vint8m1x5_t __riscv_vset_v_i8m1x5_i8m1(vint8m1x5_t dest, size_t index, vint8m1_t value);
vint8m1x5_t __riscv_vlseg5e8_v_i8m1x5(const signed char *rs1, size_t vl);
void __riscv_vsseg5e8_v_i8m1x5(signed char *rs1, vint8m1x5_t vs3, size_t vl);
vint8m1x5_t __riscv_vlseg5e8ff_v_i8m1x5(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m1x5_t __riscv_vlsseg5e8_v_i8m1x5(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg5e8_v_i8m1x5(signed char *rs1, long rs2, vint8m1x5_t vs3, size_t vl);
vint8m1x5_t __riscv_vluxseg5ei8_v_i8m1x5(const signed char *rs1, vuint8m1_t rs2, size_t vl);
vint8m1x5_t __riscv_vloxseg5ei8_v_i8m1x5(const signed char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i8m1x5(signed char *rs1, vuint8m1_t rs2, vint8m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i8m1x5(signed char *rs1, vuint8m1_t rs2, vint8m1x5_t vs3, size_t vl);
vint8m1x5_t __riscv_vluxseg5ei16_v_i8m1x5(const signed char *rs1, vuint16m2_t rs2, size_t vl);
vint8m1x5_t __riscv_vloxseg5ei16_v_i8m1x5(const signed char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i8m1x5(signed char *rs1, vuint16m2_t rs2, vint8m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i8m1x5(signed char *rs1, vuint16m2_t rs2, vint8m1x5_t vs3, size_t vl);
vint8m1x5_t __riscv_vluxseg5ei32_v_i8m1x5(const signed char *rs1, vuint32m4_t rs2, size_t vl);
vint8m1x5_t __riscv_vloxseg5ei32_v_i8m1x5(const signed char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i8m1x5(signed char *rs1, vuint32m4_t rs2, vint8m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i8m1x5(signed char *rs1, vuint32m4_t rs2, vint8m1x5_t vs3, size_t vl);
vint8m1x5_t __riscv_vluxseg5ei64_v_i8m1x5(const signed char *rs1, vuint64m8_t rs2, size_t vl);
vint8m1x5_t __riscv_vloxseg5ei64_v_i8m1x5(const signed char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i8m1x5(signed char *rs1, vuint64m8_t rs2, vint8m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i8m1x5(signed char *rs1, vuint64m8_t rs2, vint8m1x5_t vs3, size_t vl);
vuint8m1x6_t __riscv_vundefined_u8m1x6(void);
vuint8m1x6_t __riscv_vcreate_v_u8m1_u8m1x6(vuint8m1_t v0, vuint8m1_t v1, vuint8m1_t v2, vuint8m1_t v3, vuint8m1_t v4, vuint8m1_t v5);
vuint8m1_t __riscv_vget_v_u8m1x6_u8m1(vuint8m1x6_t src, size_t index);
vuint8m1x6_t __riscv_vset_v_u8m1x6_u8m1(vuint8m1x6_t dest, size_t index, vuint8m1_t value);
vuint8m1x6_t __riscv_vlseg6e8_v_u8m1x6(const unsigned char *rs1, size_t vl);
void __riscv_vsseg6e8_v_u8m1x6(unsigned char *rs1, vuint8m1x6_t vs3, size_t vl);
vuint8m1x6_t __riscv_vlseg6e8ff_v_u8m1x6(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m1x6_t __riscv_vlsseg6e8_v_u8m1x6(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg6e8_v_u8m1x6(unsigned char *rs1, long rs2, vuint8m1x6_t vs3, size_t vl);
vuint8m1x6_t __riscv_vluxseg6ei8_v_u8m1x6(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
vuint8m1x6_t __riscv_vloxseg6ei8_v_u8m1x6(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u8m1x6(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u8m1x6(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x6_t vs3, size_t vl);
vuint8m1x6_t __riscv_vluxseg6ei16_v_u8m1x6(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
vuint8m1x6_t __riscv_vloxseg6ei16_v_u8m1x6(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u8m1x6(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u8m1x6(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x6_t vs3, size_t vl);
vuint8m1x6_t __riscv_vluxseg6ei32_v_u8m1x6(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
vuint8m1x6_t __riscv_vloxseg6ei32_v_u8m1x6(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u8m1x6(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u8m1x6(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x6_t vs3, size_t vl);
vuint8m1x6_t __riscv_vluxseg6ei64_v_u8m1x6(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
vuint8m1x6_t __riscv_vloxseg6ei64_v_u8m1x6(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u8m1x6(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u8m1x6(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x6_t vs3, size_t vl);
vint8m1x6_t __riscv_vundefined_i8m1x6(void);
vint8m1x6_t __riscv_vcreate_v_i8m1_i8m1x6(vint8m1_t v0, vint8m1_t v1, vint8m1_t v2, vint8m1_t v3, vint8m1_t v4, vint8m1_t v5);
vint8m1_t __riscv_vget_v_i8m1x6_i8m1(vint8m1x6_t src, size_t index);
vint8m1x6_t __riscv_vset_v_i8m1x6_i8m1(vint8m1x6_t dest, size_t index, vint8m1_t value);
vint8m1x6_t __riscv_vlseg6e8_v_i8m1x6(const signed char *rs1, size_t vl);
void __riscv_vsseg6e8_v_i8m1x6(signed char *rs1, vint8m1x6_t vs3, size_t vl);
vint8m1x6_t __riscv_vlseg6e8ff_v_i8m1x6(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m1x6_t __riscv_vlsseg6e8_v_i8m1x6(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg6e8_v_i8m1x6(signed char *rs1, long rs2, vint8m1x6_t vs3, size_t vl);
vint8m1x6_t __riscv_vluxseg6ei8_v_i8m1x6(const signed char *rs1, vuint8m1_t rs2, size_t vl);
vint8m1x6_t __riscv_vloxseg6ei8_v_i8m1x6(const signed char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i8m1x6(signed char *rs1, vuint8m1_t rs2, vint8m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i8m1x6(signed char *rs1, vuint8m1_t rs2, vint8m1x6_t vs3, size_t vl);
vint8m1x6_t __riscv_vluxseg6ei16_v_i8m1x6(const signed char *rs1, vuint16m2_t rs2, size_t vl);
vint8m1x6_t __riscv_vloxseg6ei16_v_i8m1x6(const signed char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i8m1x6(signed char *rs1, vuint16m2_t rs2, vint8m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i8m1x6(signed char *rs1, vuint16m2_t rs2, vint8m1x6_t vs3, size_t vl);
vint8m1x6_t __riscv_vluxseg6ei32_v_i8m1x6(const signed char *rs1, vuint32m4_t rs2, size_t vl);
vint8m1x6_t __riscv_vloxseg6ei32_v_i8m1x6(const signed char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i8m1x6(signed char *rs1, vuint32m4_t rs2, vint8m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i8m1x6(signed char *rs1, vuint32m4_t rs2, vint8m1x6_t vs3, size_t vl);
vint8m1x6_t __riscv_vluxseg6ei64_v_i8m1x6(const signed char *rs1, vuint64m8_t rs2, size_t vl);
vint8m1x6_t __riscv_vloxseg6ei64_v_i8m1x6(const signed char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i8m1x6(signed char *rs1, vuint64m8_t rs2, vint8m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i8m1x6(signed char *rs1, vuint64m8_t rs2, vint8m1x6_t vs3, size_t vl);
vuint8m1x7_t __riscv_vundefined_u8m1x7(void);
vuint8m1x7_t __riscv_vcreate_v_u8m1_u8m1x7(vuint8m1_t v0, vuint8m1_t v1, vuint8m1_t v2, vuint8m1_t v3, vuint8m1_t v4, vuint8m1_t v5, vuint8m1_t v6);
vuint8m1_t __riscv_vget_v_u8m1x7_u8m1(vuint8m1x7_t src, size_t index);
vuint8m1x7_t __riscv_vset_v_u8m1x7_u8m1(vuint8m1x7_t dest, size_t index, vuint8m1_t value);
vuint8m1x7_t __riscv_vlseg7e8_v_u8m1x7(const unsigned char *rs1, size_t vl);
void __riscv_vsseg7e8_v_u8m1x7(unsigned char *rs1, vuint8m1x7_t vs3, size_t vl);
vuint8m1x7_t __riscv_vlseg7e8ff_v_u8m1x7(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m1x7_t __riscv_vlsseg7e8_v_u8m1x7(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg7e8_v_u8m1x7(unsigned char *rs1, long rs2, vuint8m1x7_t vs3, size_t vl);
vuint8m1x7_t __riscv_vluxseg7ei8_v_u8m1x7(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
vuint8m1x7_t __riscv_vloxseg7ei8_v_u8m1x7(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u8m1x7(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u8m1x7(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x7_t vs3, size_t vl);
vuint8m1x7_t __riscv_vluxseg7ei16_v_u8m1x7(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
vuint8m1x7_t __riscv_vloxseg7ei16_v_u8m1x7(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u8m1x7(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u8m1x7(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x7_t vs3, size_t vl);
vuint8m1x7_t __riscv_vluxseg7ei32_v_u8m1x7(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
vuint8m1x7_t __riscv_vloxseg7ei32_v_u8m1x7(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u8m1x7(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u8m1x7(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x7_t vs3, size_t vl);
vuint8m1x7_t __riscv_vluxseg7ei64_v_u8m1x7(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
vuint8m1x7_t __riscv_vloxseg7ei64_v_u8m1x7(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u8m1x7(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u8m1x7(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x7_t vs3, size_t vl);
vint8m1x7_t __riscv_vundefined_i8m1x7(void);
vint8m1x7_t __riscv_vcreate_v_i8m1_i8m1x7(vint8m1_t v0, vint8m1_t v1, vint8m1_t v2, vint8m1_t v3, vint8m1_t v4, vint8m1_t v5, vint8m1_t v6);
vint8m1_t __riscv_vget_v_i8m1x7_i8m1(vint8m1x7_t src, size_t index);
vint8m1x7_t __riscv_vset_v_i8m1x7_i8m1(vint8m1x7_t dest, size_t index, vint8m1_t value);
vint8m1x7_t __riscv_vlseg7e8_v_i8m1x7(const signed char *rs1, size_t vl);
void __riscv_vsseg7e8_v_i8m1x7(signed char *rs1, vint8m1x7_t vs3, size_t vl);
vint8m1x7_t __riscv_vlseg7e8ff_v_i8m1x7(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m1x7_t __riscv_vlsseg7e8_v_i8m1x7(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg7e8_v_i8m1x7(signed char *rs1, long rs2, vint8m1x7_t vs3, size_t vl);
vint8m1x7_t __riscv_vluxseg7ei8_v_i8m1x7(const signed char *rs1, vuint8m1_t rs2, size_t vl);
vint8m1x7_t __riscv_vloxseg7ei8_v_i8m1x7(const signed char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i8m1x7(signed char *rs1, vuint8m1_t rs2, vint8m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i8m1x7(signed char *rs1, vuint8m1_t rs2, vint8m1x7_t vs3, size_t vl);
vint8m1x7_t __riscv_vluxseg7ei16_v_i8m1x7(const signed char *rs1, vuint16m2_t rs2, size_t vl);
vint8m1x7_t __riscv_vloxseg7ei16_v_i8m1x7(const signed char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i8m1x7(signed char *rs1, vuint16m2_t rs2, vint8m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i8m1x7(signed char *rs1, vuint16m2_t rs2, vint8m1x7_t vs3, size_t vl);
vint8m1x7_t __riscv_vluxseg7ei32_v_i8m1x7(const signed char *rs1, vuint32m4_t rs2, size_t vl);
vint8m1x7_t __riscv_vloxseg7ei32_v_i8m1x7(const signed char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i8m1x7(signed char *rs1, vuint32m4_t rs2, vint8m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i8m1x7(signed char *rs1, vuint32m4_t rs2, vint8m1x7_t vs3, size_t vl);
vint8m1x7_t __riscv_vluxseg7ei64_v_i8m1x7(const signed char *rs1, vuint64m8_t rs2, size_t vl);
vint8m1x7_t __riscv_vloxseg7ei64_v_i8m1x7(const signed char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i8m1x7(signed char *rs1, vuint64m8_t rs2, vint8m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i8m1x7(signed char *rs1, vuint64m8_t rs2, vint8m1x7_t vs3, size_t vl);
vuint8m1x8_t __riscv_vundefined_u8m1x8(void);
vuint8m1x8_t __riscv_vcreate_v_u8m1_u8m1x8(vuint8m1_t v0, vuint8m1_t v1, vuint8m1_t v2, vuint8m1_t v3, vuint8m1_t v4, vuint8m1_t v5, vuint8m1_t v6, vuint8m1_t v7);
vuint8m1_t __riscv_vget_v_u8m1x8_u8m1(vuint8m1x8_t src, size_t index);
vuint8m1x8_t __riscv_vset_v_u8m1x8_u8m1(vuint8m1x8_t dest, size_t index, vuint8m1_t value);
vuint8m1x8_t __riscv_vlseg8e8_v_u8m1x8(const unsigned char *rs1, size_t vl);
void __riscv_vsseg8e8_v_u8m1x8(unsigned char *rs1, vuint8m1x8_t vs3, size_t vl);
vuint8m1x8_t __riscv_vlseg8e8ff_v_u8m1x8(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m1x8_t __riscv_vlsseg8e8_v_u8m1x8(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg8e8_v_u8m1x8(unsigned char *rs1, long rs2, vuint8m1x8_t vs3, size_t vl);
vuint8m1x8_t __riscv_vluxseg8ei8_v_u8m1x8(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
vuint8m1x8_t __riscv_vloxseg8ei8_v_u8m1x8(const unsigned char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u8m1x8(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u8m1x8(unsigned char *rs1, vuint8m1_t rs2, vuint8m1x8_t vs3, size_t vl);
vuint8m1x8_t __riscv_vluxseg8ei16_v_u8m1x8(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
vuint8m1x8_t __riscv_vloxseg8ei16_v_u8m1x8(const unsigned char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u8m1x8(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u8m1x8(unsigned char *rs1, vuint16m2_t rs2, vuint8m1x8_t vs3, size_t vl);
vuint8m1x8_t __riscv_vluxseg8ei32_v_u8m1x8(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
vuint8m1x8_t __riscv_vloxseg8ei32_v_u8m1x8(const unsigned char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u8m1x8(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u8m1x8(unsigned char *rs1, vuint32m4_t rs2, vuint8m1x8_t vs3, size_t vl);
vuint8m1x8_t __riscv_vluxseg8ei64_v_u8m1x8(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
vuint8m1x8_t __riscv_vloxseg8ei64_v_u8m1x8(const unsigned char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u8m1x8(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u8m1x8(unsigned char *rs1, vuint64m8_t rs2, vuint8m1x8_t vs3, size_t vl);
vint8m1x8_t __riscv_vundefined_i8m1x8(void);
vint8m1x8_t __riscv_vcreate_v_i8m1_i8m1x8(vint8m1_t v0, vint8m1_t v1, vint8m1_t v2, vint8m1_t v3, vint8m1_t v4, vint8m1_t v5, vint8m1_t v6, vint8m1_t v7);
vint8m1_t __riscv_vget_v_i8m1x8_i8m1(vint8m1x8_t src, size_t index);
vint8m1x8_t __riscv_vset_v_i8m1x8_i8m1(vint8m1x8_t dest, size_t index, vint8m1_t value);
vint8m1x8_t __riscv_vlseg8e8_v_i8m1x8(const signed char *rs1, size_t vl);
void __riscv_vsseg8e8_v_i8m1x8(signed char *rs1, vint8m1x8_t vs3, size_t vl);
vint8m1x8_t __riscv_vlseg8e8ff_v_i8m1x8(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m1x8_t __riscv_vlsseg8e8_v_i8m1x8(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg8e8_v_i8m1x8(signed char *rs1, long rs2, vint8m1x8_t vs3, size_t vl);
vint8m1x8_t __riscv_vluxseg8ei8_v_i8m1x8(const signed char *rs1, vuint8m1_t rs2, size_t vl);
vint8m1x8_t __riscv_vloxseg8ei8_v_i8m1x8(const signed char *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i8m1x8(signed char *rs1, vuint8m1_t rs2, vint8m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i8m1x8(signed char *rs1, vuint8m1_t rs2, vint8m1x8_t vs3, size_t vl);
vint8m1x8_t __riscv_vluxseg8ei16_v_i8m1x8(const signed char *rs1, vuint16m2_t rs2, size_t vl);
vint8m1x8_t __riscv_vloxseg8ei16_v_i8m1x8(const signed char *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i8m1x8(signed char *rs1, vuint16m2_t rs2, vint8m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i8m1x8(signed char *rs1, vuint16m2_t rs2, vint8m1x8_t vs3, size_t vl);
vint8m1x8_t __riscv_vluxseg8ei32_v_i8m1x8(const signed char *rs1, vuint32m4_t rs2, size_t vl);
vint8m1x8_t __riscv_vloxseg8ei32_v_i8m1x8(const signed char *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i8m1x8(signed char *rs1, vuint32m4_t rs2, vint8m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i8m1x8(signed char *rs1, vuint32m4_t rs2, vint8m1x8_t vs3, size_t vl);
vint8m1x8_t __riscv_vluxseg8ei64_v_i8m1x8(const signed char *rs1, vuint64m8_t rs2, size_t vl);
vint8m1x8_t __riscv_vloxseg8ei64_v_i8m1x8(const signed char *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i8m1x8(signed char *rs1, vuint64m8_t rs2, vint8m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i8m1x8(signed char *rs1, vuint64m8_t rs2, vint8m1x8_t vs3, size_t vl);
vuint8m2x2_t __riscv_vundefined_u8m2x2(void);
vuint8m2x2_t __riscv_vcreate_v_u8m2_u8m2x2(vuint8m2_t v0, vuint8m2_t v1);
vuint8m2_t __riscv_vget_v_u8m2x2_u8m2(vuint8m2x2_t src, size_t index);
vuint8m2x2_t __riscv_vset_v_u8m2x2_u8m2(vuint8m2x2_t dest, size_t index, vuint8m2_t value);
vuint8m2x2_t __riscv_vlseg2e8_v_u8m2x2(const unsigned char *rs1, size_t vl);
void __riscv_vsseg2e8_v_u8m2x2(unsigned char *rs1, vuint8m2x2_t vs3, size_t vl);
vuint8m2x2_t __riscv_vlseg2e8ff_v_u8m2x2(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m2x2_t __riscv_vlsseg2e8_v_u8m2x2(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_u8m2x2(unsigned char *rs1, long rs2, vuint8m2x2_t vs3, size_t vl);
vuint8m2x2_t __riscv_vluxseg2ei8_v_u8m2x2(const unsigned char *rs1, vuint8m2_t rs2, size_t vl);
vuint8m2x2_t __riscv_vloxseg2ei8_v_u8m2x2(const unsigned char *rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u8m2x2(unsigned char *rs1, vuint8m2_t rs2, vuint8m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u8m2x2(unsigned char *rs1, vuint8m2_t rs2, vuint8m2x2_t vs3, size_t vl);
vuint8m2x2_t __riscv_vluxseg2ei16_v_u8m2x2(const unsigned char *rs1, vuint16m4_t rs2, size_t vl);
vuint8m2x2_t __riscv_vloxseg2ei16_v_u8m2x2(const unsigned char *rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u8m2x2(unsigned char *rs1, vuint16m4_t rs2, vuint8m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u8m2x2(unsigned char *rs1, vuint16m4_t rs2, vuint8m2x2_t vs3, size_t vl);
vuint8m2x2_t __riscv_vluxseg2ei32_v_u8m2x2(const unsigned char *rs1, vuint32m8_t rs2, size_t vl);
vuint8m2x2_t __riscv_vloxseg2ei32_v_u8m2x2(const unsigned char *rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u8m2x2(unsigned char *rs1, vuint32m8_t rs2, vuint8m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u8m2x2(unsigned char *rs1, vuint32m8_t rs2, vuint8m2x2_t vs3, size_t vl);
vint8m2x2_t __riscv_vundefined_i8m2x2(void);
vint8m2x2_t __riscv_vcreate_v_i8m2_i8m2x2(vint8m2_t v0, vint8m2_t v1);
vint8m2_t __riscv_vget_v_i8m2x2_i8m2(vint8m2x2_t src, size_t index);
vint8m2x2_t __riscv_vset_v_i8m2x2_i8m2(vint8m2x2_t dest, size_t index, vint8m2_t value);
vint8m2x2_t __riscv_vlseg2e8_v_i8m2x2(const signed char *rs1, size_t vl);
void __riscv_vsseg2e8_v_i8m2x2(signed char *rs1, vint8m2x2_t vs3, size_t vl);
vint8m2x2_t __riscv_vlseg2e8ff_v_i8m2x2(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m2x2_t __riscv_vlsseg2e8_v_i8m2x2(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_i8m2x2(signed char *rs1, long rs2, vint8m2x2_t vs3, size_t vl);
vint8m2x2_t __riscv_vluxseg2ei8_v_i8m2x2(const signed char *rs1, vuint8m2_t rs2, size_t vl);
vint8m2x2_t __riscv_vloxseg2ei8_v_i8m2x2(const signed char *rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i8m2x2(signed char *rs1, vuint8m2_t rs2, vint8m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i8m2x2(signed char *rs1, vuint8m2_t rs2, vint8m2x2_t vs3, size_t vl);
vint8m2x2_t __riscv_vluxseg2ei16_v_i8m2x2(const signed char *rs1, vuint16m4_t rs2, size_t vl);
vint8m2x2_t __riscv_vloxseg2ei16_v_i8m2x2(const signed char *rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i8m2x2(signed char *rs1, vuint16m4_t rs2, vint8m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i8m2x2(signed char *rs1, vuint16m4_t rs2, vint8m2x2_t vs3, size_t vl);
vint8m2x2_t __riscv_vluxseg2ei32_v_i8m2x2(const signed char *rs1, vuint32m8_t rs2, size_t vl);
vint8m2x2_t __riscv_vloxseg2ei32_v_i8m2x2(const signed char *rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i8m2x2(signed char *rs1, vuint32m8_t rs2, vint8m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i8m2x2(signed char *rs1, vuint32m8_t rs2, vint8m2x2_t vs3, size_t vl);
vuint8m2x3_t __riscv_vundefined_u8m2x3(void);
vuint8m2x3_t __riscv_vcreate_v_u8m2_u8m2x3(vuint8m2_t v0, vuint8m2_t v1, vuint8m2_t v2);
vuint8m2_t __riscv_vget_v_u8m2x3_u8m2(vuint8m2x3_t src, size_t index);
vuint8m2x3_t __riscv_vset_v_u8m2x3_u8m2(vuint8m2x3_t dest, size_t index, vuint8m2_t value);
vuint8m2x3_t __riscv_vlseg3e8_v_u8m2x3(const unsigned char *rs1, size_t vl);
void __riscv_vsseg3e8_v_u8m2x3(unsigned char *rs1, vuint8m2x3_t vs3, size_t vl);
vuint8m2x3_t __riscv_vlseg3e8ff_v_u8m2x3(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m2x3_t __riscv_vlsseg3e8_v_u8m2x3(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_u8m2x3(unsigned char *rs1, long rs2, vuint8m2x3_t vs3, size_t vl);
vuint8m2x3_t __riscv_vluxseg3ei8_v_u8m2x3(const unsigned char *rs1, vuint8m2_t rs2, size_t vl);
vuint8m2x3_t __riscv_vloxseg3ei8_v_u8m2x3(const unsigned char *rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u8m2x3(unsigned char *rs1, vuint8m2_t rs2, vuint8m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u8m2x3(unsigned char *rs1, vuint8m2_t rs2, vuint8m2x3_t vs3, size_t vl);
vuint8m2x3_t __riscv_vluxseg3ei16_v_u8m2x3(const unsigned char *rs1, vuint16m4_t rs2, size_t vl);
vuint8m2x3_t __riscv_vloxseg3ei16_v_u8m2x3(const unsigned char *rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u8m2x3(unsigned char *rs1, vuint16m4_t rs2, vuint8m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u8m2x3(unsigned char *rs1, vuint16m4_t rs2, vuint8m2x3_t vs3, size_t vl);
vuint8m2x3_t __riscv_vluxseg3ei32_v_u8m2x3(const unsigned char *rs1, vuint32m8_t rs2, size_t vl);
vuint8m2x3_t __riscv_vloxseg3ei32_v_u8m2x3(const unsigned char *rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u8m2x3(unsigned char *rs1, vuint32m8_t rs2, vuint8m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u8m2x3(unsigned char *rs1, vuint32m8_t rs2, vuint8m2x3_t vs3, size_t vl);
vint8m2x3_t __riscv_vundefined_i8m2x3(void);
vint8m2x3_t __riscv_vcreate_v_i8m2_i8m2x3(vint8m2_t v0, vint8m2_t v1, vint8m2_t v2);
vint8m2_t __riscv_vget_v_i8m2x3_i8m2(vint8m2x3_t src, size_t index);
vint8m2x3_t __riscv_vset_v_i8m2x3_i8m2(vint8m2x3_t dest, size_t index, vint8m2_t value);
vint8m2x3_t __riscv_vlseg3e8_v_i8m2x3(const signed char *rs1, size_t vl);
void __riscv_vsseg3e8_v_i8m2x3(signed char *rs1, vint8m2x3_t vs3, size_t vl);
vint8m2x3_t __riscv_vlseg3e8ff_v_i8m2x3(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m2x3_t __riscv_vlsseg3e8_v_i8m2x3(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg3e8_v_i8m2x3(signed char *rs1, long rs2, vint8m2x3_t vs3, size_t vl);
vint8m2x3_t __riscv_vluxseg3ei8_v_i8m2x3(const signed char *rs1, vuint8m2_t rs2, size_t vl);
vint8m2x3_t __riscv_vloxseg3ei8_v_i8m2x3(const signed char *rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i8m2x3(signed char *rs1, vuint8m2_t rs2, vint8m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i8m2x3(signed char *rs1, vuint8m2_t rs2, vint8m2x3_t vs3, size_t vl);
vint8m2x3_t __riscv_vluxseg3ei16_v_i8m2x3(const signed char *rs1, vuint16m4_t rs2, size_t vl);
vint8m2x3_t __riscv_vloxseg3ei16_v_i8m2x3(const signed char *rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i8m2x3(signed char *rs1, vuint16m4_t rs2, vint8m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i8m2x3(signed char *rs1, vuint16m4_t rs2, vint8m2x3_t vs3, size_t vl);
vint8m2x3_t __riscv_vluxseg3ei32_v_i8m2x3(const signed char *rs1, vuint32m8_t rs2, size_t vl);
vint8m2x3_t __riscv_vloxseg3ei32_v_i8m2x3(const signed char *rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i8m2x3(signed char *rs1, vuint32m8_t rs2, vint8m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i8m2x3(signed char *rs1, vuint32m8_t rs2, vint8m2x3_t vs3, size_t vl);
vuint8m2x4_t __riscv_vundefined_u8m2x4(void);
vuint8m2x4_t __riscv_vcreate_v_u8m2_u8m2x4(vuint8m2_t v0, vuint8m2_t v1, vuint8m2_t v2, vuint8m2_t v3);
vuint8m2_t __riscv_vget_v_u8m2x4_u8m2(vuint8m2x4_t src, size_t index);
vuint8m2x4_t __riscv_vset_v_u8m2x4_u8m2(vuint8m2x4_t dest, size_t index, vuint8m2_t value);
vuint8m2x4_t __riscv_vlseg4e8_v_u8m2x4(const unsigned char *rs1, size_t vl);
void __riscv_vsseg4e8_v_u8m2x4(unsigned char *rs1, vuint8m2x4_t vs3, size_t vl);
vuint8m2x4_t __riscv_vlseg4e8ff_v_u8m2x4(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m2x4_t __riscv_vlsseg4e8_v_u8m2x4(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_u8m2x4(unsigned char *rs1, long rs2, vuint8m2x4_t vs3, size_t vl);
vuint8m2x4_t __riscv_vluxseg4ei8_v_u8m2x4(const unsigned char *rs1, vuint8m2_t rs2, size_t vl);
vuint8m2x4_t __riscv_vloxseg4ei8_v_u8m2x4(const unsigned char *rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u8m2x4(unsigned char *rs1, vuint8m2_t rs2, vuint8m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u8m2x4(unsigned char *rs1, vuint8m2_t rs2, vuint8m2x4_t vs3, size_t vl);
vuint8m2x4_t __riscv_vluxseg4ei16_v_u8m2x4(const unsigned char *rs1, vuint16m4_t rs2, size_t vl);
vuint8m2x4_t __riscv_vloxseg4ei16_v_u8m2x4(const unsigned char *rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u8m2x4(unsigned char *rs1, vuint16m4_t rs2, vuint8m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u8m2x4(unsigned char *rs1, vuint16m4_t rs2, vuint8m2x4_t vs3, size_t vl);
vuint8m2x4_t __riscv_vluxseg4ei32_v_u8m2x4(const unsigned char *rs1, vuint32m8_t rs2, size_t vl);
vuint8m2x4_t __riscv_vloxseg4ei32_v_u8m2x4(const unsigned char *rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u8m2x4(unsigned char *rs1, vuint32m8_t rs2, vuint8m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u8m2x4(unsigned char *rs1, vuint32m8_t rs2, vuint8m2x4_t vs3, size_t vl);
vint8m2x4_t __riscv_vundefined_i8m2x4(void);
vint8m2x4_t __riscv_vcreate_v_i8m2_i8m2x4(vint8m2_t v0, vint8m2_t v1, vint8m2_t v2, vint8m2_t v3);
vint8m2_t __riscv_vget_v_i8m2x4_i8m2(vint8m2x4_t src, size_t index);
vint8m2x4_t __riscv_vset_v_i8m2x4_i8m2(vint8m2x4_t dest, size_t index, vint8m2_t value);
vint8m2x4_t __riscv_vlseg4e8_v_i8m2x4(const signed char *rs1, size_t vl);
void __riscv_vsseg4e8_v_i8m2x4(signed char *rs1, vint8m2x4_t vs3, size_t vl);
vint8m2x4_t __riscv_vlseg4e8ff_v_i8m2x4(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m2x4_t __riscv_vlsseg4e8_v_i8m2x4(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg4e8_v_i8m2x4(signed char *rs1, long rs2, vint8m2x4_t vs3, size_t vl);
vint8m2x4_t __riscv_vluxseg4ei8_v_i8m2x4(const signed char *rs1, vuint8m2_t rs2, size_t vl);
vint8m2x4_t __riscv_vloxseg4ei8_v_i8m2x4(const signed char *rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i8m2x4(signed char *rs1, vuint8m2_t rs2, vint8m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i8m2x4(signed char *rs1, vuint8m2_t rs2, vint8m2x4_t vs3, size_t vl);
vint8m2x4_t __riscv_vluxseg4ei16_v_i8m2x4(const signed char *rs1, vuint16m4_t rs2, size_t vl);
vint8m2x4_t __riscv_vloxseg4ei16_v_i8m2x4(const signed char *rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i8m2x4(signed char *rs1, vuint16m4_t rs2, vint8m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i8m2x4(signed char *rs1, vuint16m4_t rs2, vint8m2x4_t vs3, size_t vl);
vint8m2x4_t __riscv_vluxseg4ei32_v_i8m2x4(const signed char *rs1, vuint32m8_t rs2, size_t vl);
vint8m2x4_t __riscv_vloxseg4ei32_v_i8m2x4(const signed char *rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i8m2x4(signed char *rs1, vuint32m8_t rs2, vint8m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i8m2x4(signed char *rs1, vuint32m8_t rs2, vint8m2x4_t vs3, size_t vl);
vuint8m4x2_t __riscv_vundefined_u8m4x2(void);
vuint8m4x2_t __riscv_vcreate_v_u8m4_u8m4x2(vuint8m4_t v0, vuint8m4_t v1);
vuint8m4_t __riscv_vget_v_u8m4x2_u8m4(vuint8m4x2_t src, size_t index);
vuint8m4x2_t __riscv_vset_v_u8m4x2_u8m4(vuint8m4x2_t dest, size_t index, vuint8m4_t value);
vuint8m4x2_t __riscv_vlseg2e8_v_u8m4x2(const unsigned char *rs1, size_t vl);
void __riscv_vsseg2e8_v_u8m4x2(unsigned char *rs1, vuint8m4x2_t vs3, size_t vl);
vuint8m4x2_t __riscv_vlseg2e8ff_v_u8m4x2(const unsigned char *rs1, size_t *new_vl, size_t vl);
vuint8m4x2_t __riscv_vlsseg2e8_v_u8m4x2(const unsigned char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_u8m4x2(unsigned char *rs1, long rs2, vuint8m4x2_t vs3, size_t vl);
vuint8m4x2_t __riscv_vluxseg2ei8_v_u8m4x2(const unsigned char *rs1, vuint8m4_t rs2, size_t vl);
vuint8m4x2_t __riscv_vloxseg2ei8_v_u8m4x2(const unsigned char *rs1, vuint8m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u8m4x2(unsigned char *rs1, vuint8m4_t rs2, vuint8m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u8m4x2(unsigned char *rs1, vuint8m4_t rs2, vuint8m4x2_t vs3, size_t vl);
vuint8m4x2_t __riscv_vluxseg2ei16_v_u8m4x2(const unsigned char *rs1, vuint16m8_t rs2, size_t vl);
vuint8m4x2_t __riscv_vloxseg2ei16_v_u8m4x2(const unsigned char *rs1, vuint16m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u8m4x2(unsigned char *rs1, vuint16m8_t rs2, vuint8m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u8m4x2(unsigned char *rs1, vuint16m8_t rs2, vuint8m4x2_t vs3, size_t vl);
vint8m4x2_t __riscv_vundefined_i8m4x2(void);
vint8m4x2_t __riscv_vcreate_v_i8m4_i8m4x2(vint8m4_t v0, vint8m4_t v1);
vint8m4_t __riscv_vget_v_i8m4x2_i8m4(vint8m4x2_t src, size_t index);
vint8m4x2_t __riscv_vset_v_i8m4x2_i8m4(vint8m4x2_t dest, size_t index, vint8m4_t value);
vint8m4x2_t __riscv_vlseg2e8_v_i8m4x2(const signed char *rs1, size_t vl);
void __riscv_vsseg2e8_v_i8m4x2(signed char *rs1, vint8m4x2_t vs3, size_t vl);
vint8m4x2_t __riscv_vlseg2e8ff_v_i8m4x2(const signed char *rs1, size_t *new_vl, size_t vl);
vint8m4x2_t __riscv_vlsseg2e8_v_i8m4x2(const signed char *rs1, long rs2, size_t vl);
void __riscv_vssseg2e8_v_i8m4x2(signed char *rs1, long rs2, vint8m4x2_t vs3, size_t vl);
vint8m4x2_t __riscv_vluxseg2ei8_v_i8m4x2(const signed char *rs1, vuint8m4_t rs2, size_t vl);
vint8m4x2_t __riscv_vloxseg2ei8_v_i8m4x2(const signed char *rs1, vuint8m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i8m4x2(signed char *rs1, vuint8m4_t rs2, vint8m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i8m4x2(signed char *rs1, vuint8m4_t rs2, vint8m4x2_t vs3, size_t vl);
vint8m4x2_t __riscv_vluxseg2ei16_v_i8m4x2(const signed char *rs1, vuint16m8_t rs2, size_t vl);
vint8m4x2_t __riscv_vloxseg2ei16_v_i8m4x2(const signed char *rs1, vuint16m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i8m4x2(signed char *rs1, vuint16m8_t rs2, vint8m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i8m4x2(signed char *rs1, vuint16m8_t rs2, vint8m4x2_t vs3, size_t vl);
vuint16mf4x2_t __riscv_vundefined_u16mf4x2(void);
vuint16mf4x2_t __riscv_vcreate_v_u16mf4_u16mf4x2(vuint16mf4_t v0, vuint16mf4_t v1);
vuint16mf4_t __riscv_vget_v_u16mf4x2_u16mf4(vuint16mf4x2_t src, size_t index);
vuint16mf4x2_t __riscv_vset_v_u16mf4x2_u16mf4(vuint16mf4x2_t dest, size_t index, vuint16mf4_t value);
vuint16mf4x2_t __riscv_vlseg2e16_v_u16mf4x2(const unsigned short *rs1, size_t vl);
void __riscv_vsseg2e16_v_u16mf4x2(unsigned short *rs1, vuint16mf4x2_t vs3, size_t vl);
vuint16mf4x2_t __riscv_vlseg2e16ff_v_u16mf4x2(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf4x2_t __riscv_vlsseg2e16_v_u16mf4x2(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_u16mf4x2(unsigned short *rs1, long rs2, vuint16mf4x2_t vs3, size_t vl);
vuint16mf4x2_t __riscv_vluxseg2ei8_v_u16mf4x2(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
vuint16mf4x2_t __riscv_vloxseg2ei8_v_u16mf4x2(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u16mf4x2(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u16mf4x2(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x2_t vs3, size_t vl);
vuint16mf4x2_t __riscv_vluxseg2ei16_v_u16mf4x2(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
vuint16mf4x2_t __riscv_vloxseg2ei16_v_u16mf4x2(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u16mf4x2(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u16mf4x2(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x2_t vs3, size_t vl);
vuint16mf4x2_t __riscv_vluxseg2ei32_v_u16mf4x2(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
vuint16mf4x2_t __riscv_vloxseg2ei32_v_u16mf4x2(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u16mf4x2(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u16mf4x2(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x2_t vs3, size_t vl);
vuint16mf4x2_t __riscv_vluxseg2ei64_v_u16mf4x2(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
vuint16mf4x2_t __riscv_vloxseg2ei64_v_u16mf4x2(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u16mf4x2(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u16mf4x2(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x2_t vs3, size_t vl);
vint16mf4x2_t __riscv_vundefined_i16mf4x2(void);
vint16mf4x2_t __riscv_vcreate_v_i16mf4_i16mf4x2(vint16mf4_t v0, vint16mf4_t v1);
vint16mf4_t __riscv_vget_v_i16mf4x2_i16mf4(vint16mf4x2_t src, size_t index);
vint16mf4x2_t __riscv_vset_v_i16mf4x2_i16mf4(vint16mf4x2_t dest, size_t index, vint16mf4_t value);
vint16mf4x2_t __riscv_vlseg2e16_v_i16mf4x2(const short *rs1, size_t vl);
void __riscv_vsseg2e16_v_i16mf4x2(short *rs1, vint16mf4x2_t vs3, size_t vl);
vint16mf4x2_t __riscv_vlseg2e16ff_v_i16mf4x2(const short *rs1, size_t *new_vl, size_t vl);
vint16mf4x2_t __riscv_vlsseg2e16_v_i16mf4x2(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_i16mf4x2(short *rs1, long rs2, vint16mf4x2_t vs3, size_t vl);
vint16mf4x2_t __riscv_vluxseg2ei8_v_i16mf4x2(const short *rs1, vuint8mf8_t rs2, size_t vl);
vint16mf4x2_t __riscv_vloxseg2ei8_v_i16mf4x2(const short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i16mf4x2(short *rs1, vuint8mf8_t rs2, vint16mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i16mf4x2(short *rs1, vuint8mf8_t rs2, vint16mf4x2_t vs3, size_t vl);
vint16mf4x2_t __riscv_vluxseg2ei16_v_i16mf4x2(const short *rs1, vuint16mf4_t rs2, size_t vl);
vint16mf4x2_t __riscv_vloxseg2ei16_v_i16mf4x2(const short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i16mf4x2(short *rs1, vuint16mf4_t rs2, vint16mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i16mf4x2(short *rs1, vuint16mf4_t rs2, vint16mf4x2_t vs3, size_t vl);
vint16mf4x2_t __riscv_vluxseg2ei32_v_i16mf4x2(const short *rs1, vuint32mf2_t rs2, size_t vl);
vint16mf4x2_t __riscv_vloxseg2ei32_v_i16mf4x2(const short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i16mf4x2(short *rs1, vuint32mf2_t rs2, vint16mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i16mf4x2(short *rs1, vuint32mf2_t rs2, vint16mf4x2_t vs3, size_t vl);
vint16mf4x2_t __riscv_vluxseg2ei64_v_i16mf4x2(const short *rs1, vuint64m1_t rs2, size_t vl);
vint16mf4x2_t __riscv_vloxseg2ei64_v_i16mf4x2(const short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i16mf4x2(short *rs1, vuint64m1_t rs2, vint16mf4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i16mf4x2(short *rs1, vuint64m1_t rs2, vint16mf4x2_t vs3, size_t vl);
vuint16mf4x3_t __riscv_vundefined_u16mf4x3(void);
vuint16mf4x3_t __riscv_vcreate_v_u16mf4_u16mf4x3(vuint16mf4_t v0, vuint16mf4_t v1, vuint16mf4_t v2);
vuint16mf4_t __riscv_vget_v_u16mf4x3_u16mf4(vuint16mf4x3_t src, size_t index);
vuint16mf4x3_t __riscv_vset_v_u16mf4x3_u16mf4(vuint16mf4x3_t dest, size_t index, vuint16mf4_t value);
vuint16mf4x3_t __riscv_vlseg3e16_v_u16mf4x3(const unsigned short *rs1, size_t vl);
void __riscv_vsseg3e16_v_u16mf4x3(unsigned short *rs1, vuint16mf4x3_t vs3, size_t vl);
vuint16mf4x3_t __riscv_vlseg3e16ff_v_u16mf4x3(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf4x3_t __riscv_vlsseg3e16_v_u16mf4x3(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg3e16_v_u16mf4x3(unsigned short *rs1, long rs2, vuint16mf4x3_t vs3, size_t vl);
vuint16mf4x3_t __riscv_vluxseg3ei8_v_u16mf4x3(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
vuint16mf4x3_t __riscv_vloxseg3ei8_v_u16mf4x3(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u16mf4x3(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u16mf4x3(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x3_t vs3, size_t vl);
vuint16mf4x3_t __riscv_vluxseg3ei16_v_u16mf4x3(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
vuint16mf4x3_t __riscv_vloxseg3ei16_v_u16mf4x3(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u16mf4x3(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u16mf4x3(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x3_t vs3, size_t vl);
vuint16mf4x3_t __riscv_vluxseg3ei32_v_u16mf4x3(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
vuint16mf4x3_t __riscv_vloxseg3ei32_v_u16mf4x3(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u16mf4x3(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u16mf4x3(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x3_t vs3, size_t vl);
vuint16mf4x3_t __riscv_vluxseg3ei64_v_u16mf4x3(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
vuint16mf4x3_t __riscv_vloxseg3ei64_v_u16mf4x3(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u16mf4x3(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u16mf4x3(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x3_t vs3, size_t vl);
vint16mf4x3_t __riscv_vundefined_i16mf4x3(void);
vint16mf4x3_t __riscv_vcreate_v_i16mf4_i16mf4x3(vint16mf4_t v0, vint16mf4_t v1, vint16mf4_t v2);
vint16mf4_t __riscv_vget_v_i16mf4x3_i16mf4(vint16mf4x3_t src, size_t index);
vint16mf4x3_t __riscv_vset_v_i16mf4x3_i16mf4(vint16mf4x3_t dest, size_t index, vint16mf4_t value);
vint16mf4x3_t __riscv_vlseg3e16_v_i16mf4x3(const short *rs1, size_t vl);
void __riscv_vsseg3e16_v_i16mf4x3(short *rs1, vint16mf4x3_t vs3, size_t vl);
vint16mf4x3_t __riscv_vlseg3e16ff_v_i16mf4x3(const short *rs1, size_t *new_vl, size_t vl);
vint16mf4x3_t __riscv_vlsseg3e16_v_i16mf4x3(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg3e16_v_i16mf4x3(short *rs1, long rs2, vint16mf4x3_t vs3, size_t vl);
vint16mf4x3_t __riscv_vluxseg3ei8_v_i16mf4x3(const short *rs1, vuint8mf8_t rs2, size_t vl);
vint16mf4x3_t __riscv_vloxseg3ei8_v_i16mf4x3(const short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i16mf4x3(short *rs1, vuint8mf8_t rs2, vint16mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i16mf4x3(short *rs1, vuint8mf8_t rs2, vint16mf4x3_t vs3, size_t vl);
vint16mf4x3_t __riscv_vluxseg3ei16_v_i16mf4x3(const short *rs1, vuint16mf4_t rs2, size_t vl);
vint16mf4x3_t __riscv_vloxseg3ei16_v_i16mf4x3(const short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i16mf4x3(short *rs1, vuint16mf4_t rs2, vint16mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i16mf4x3(short *rs1, vuint16mf4_t rs2, vint16mf4x3_t vs3, size_t vl);
vint16mf4x3_t __riscv_vluxseg3ei32_v_i16mf4x3(const short *rs1, vuint32mf2_t rs2, size_t vl);
vint16mf4x3_t __riscv_vloxseg3ei32_v_i16mf4x3(const short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i16mf4x3(short *rs1, vuint32mf2_t rs2, vint16mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i16mf4x3(short *rs1, vuint32mf2_t rs2, vint16mf4x3_t vs3, size_t vl);
vint16mf4x3_t __riscv_vluxseg3ei64_v_i16mf4x3(const short *rs1, vuint64m1_t rs2, size_t vl);
vint16mf4x3_t __riscv_vloxseg3ei64_v_i16mf4x3(const short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i16mf4x3(short *rs1, vuint64m1_t rs2, vint16mf4x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i16mf4x3(short *rs1, vuint64m1_t rs2, vint16mf4x3_t vs3, size_t vl);
vuint16mf4x4_t __riscv_vundefined_u16mf4x4(void);
vuint16mf4x4_t __riscv_vcreate_v_u16mf4_u16mf4x4(vuint16mf4_t v0, vuint16mf4_t v1, vuint16mf4_t v2, vuint16mf4_t v3);
vuint16mf4_t __riscv_vget_v_u16mf4x4_u16mf4(vuint16mf4x4_t src, size_t index);
vuint16mf4x4_t __riscv_vset_v_u16mf4x4_u16mf4(vuint16mf4x4_t dest, size_t index, vuint16mf4_t value);
vuint16mf4x4_t __riscv_vlseg4e16_v_u16mf4x4(const unsigned short *rs1, size_t vl);
void __riscv_vsseg4e16_v_u16mf4x4(unsigned short *rs1, vuint16mf4x4_t vs3, size_t vl);
vuint16mf4x4_t __riscv_vlseg4e16ff_v_u16mf4x4(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf4x4_t __riscv_vlsseg4e16_v_u16mf4x4(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg4e16_v_u16mf4x4(unsigned short *rs1, long rs2, vuint16mf4x4_t vs3, size_t vl);
vuint16mf4x4_t __riscv_vluxseg4ei8_v_u16mf4x4(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
vuint16mf4x4_t __riscv_vloxseg4ei8_v_u16mf4x4(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u16mf4x4(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u16mf4x4(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x4_t vs3, size_t vl);
vuint16mf4x4_t __riscv_vluxseg4ei16_v_u16mf4x4(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
vuint16mf4x4_t __riscv_vloxseg4ei16_v_u16mf4x4(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u16mf4x4(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u16mf4x4(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x4_t vs3, size_t vl);
vuint16mf4x4_t __riscv_vluxseg4ei32_v_u16mf4x4(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
vuint16mf4x4_t __riscv_vloxseg4ei32_v_u16mf4x4(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u16mf4x4(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u16mf4x4(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x4_t vs3, size_t vl);
vuint16mf4x4_t __riscv_vluxseg4ei64_v_u16mf4x4(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
vuint16mf4x4_t __riscv_vloxseg4ei64_v_u16mf4x4(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u16mf4x4(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u16mf4x4(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x4_t vs3, size_t vl);
vint16mf4x4_t __riscv_vundefined_i16mf4x4(void);
vint16mf4x4_t __riscv_vcreate_v_i16mf4_i16mf4x4(vint16mf4_t v0, vint16mf4_t v1, vint16mf4_t v2, vint16mf4_t v3);
vint16mf4_t __riscv_vget_v_i16mf4x4_i16mf4(vint16mf4x4_t src, size_t index);
vint16mf4x4_t __riscv_vset_v_i16mf4x4_i16mf4(vint16mf4x4_t dest, size_t index, vint16mf4_t value);
vint16mf4x4_t __riscv_vlseg4e16_v_i16mf4x4(const short *rs1, size_t vl);
void __riscv_vsseg4e16_v_i16mf4x4(short *rs1, vint16mf4x4_t vs3, size_t vl);
vint16mf4x4_t __riscv_vlseg4e16ff_v_i16mf4x4(const short *rs1, size_t *new_vl, size_t vl);
vint16mf4x4_t __riscv_vlsseg4e16_v_i16mf4x4(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg4e16_v_i16mf4x4(short *rs1, long rs2, vint16mf4x4_t vs3, size_t vl);
vint16mf4x4_t __riscv_vluxseg4ei8_v_i16mf4x4(const short *rs1, vuint8mf8_t rs2, size_t vl);
vint16mf4x4_t __riscv_vloxseg4ei8_v_i16mf4x4(const short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i16mf4x4(short *rs1, vuint8mf8_t rs2, vint16mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i16mf4x4(short *rs1, vuint8mf8_t rs2, vint16mf4x4_t vs3, size_t vl);
vint16mf4x4_t __riscv_vluxseg4ei16_v_i16mf4x4(const short *rs1, vuint16mf4_t rs2, size_t vl);
vint16mf4x4_t __riscv_vloxseg4ei16_v_i16mf4x4(const short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i16mf4x4(short *rs1, vuint16mf4_t rs2, vint16mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i16mf4x4(short *rs1, vuint16mf4_t rs2, vint16mf4x4_t vs3, size_t vl);
vint16mf4x4_t __riscv_vluxseg4ei32_v_i16mf4x4(const short *rs1, vuint32mf2_t rs2, size_t vl);
vint16mf4x4_t __riscv_vloxseg4ei32_v_i16mf4x4(const short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i16mf4x4(short *rs1, vuint32mf2_t rs2, vint16mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i16mf4x4(short *rs1, vuint32mf2_t rs2, vint16mf4x4_t vs3, size_t vl);
vint16mf4x4_t __riscv_vluxseg4ei64_v_i16mf4x4(const short *rs1, vuint64m1_t rs2, size_t vl);
vint16mf4x4_t __riscv_vloxseg4ei64_v_i16mf4x4(const short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i16mf4x4(short *rs1, vuint64m1_t rs2, vint16mf4x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i16mf4x4(short *rs1, vuint64m1_t rs2, vint16mf4x4_t vs3, size_t vl);
vuint16mf4x5_t __riscv_vundefined_u16mf4x5(void);
vuint16mf4x5_t __riscv_vcreate_v_u16mf4_u16mf4x5(vuint16mf4_t v0, vuint16mf4_t v1, vuint16mf4_t v2, vuint16mf4_t v3, vuint16mf4_t v4);
vuint16mf4_t __riscv_vget_v_u16mf4x5_u16mf4(vuint16mf4x5_t src, size_t index);
vuint16mf4x5_t __riscv_vset_v_u16mf4x5_u16mf4(vuint16mf4x5_t dest, size_t index, vuint16mf4_t value);
vuint16mf4x5_t __riscv_vlseg5e16_v_u16mf4x5(const unsigned short *rs1, size_t vl);
void __riscv_vsseg5e16_v_u16mf4x5(unsigned short *rs1, vuint16mf4x5_t vs3, size_t vl);
vuint16mf4x5_t __riscv_vlseg5e16ff_v_u16mf4x5(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf4x5_t __riscv_vlsseg5e16_v_u16mf4x5(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg5e16_v_u16mf4x5(unsigned short *rs1, long rs2, vuint16mf4x5_t vs3, size_t vl);
vuint16mf4x5_t __riscv_vluxseg5ei8_v_u16mf4x5(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
vuint16mf4x5_t __riscv_vloxseg5ei8_v_u16mf4x5(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u16mf4x5(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u16mf4x5(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x5_t vs3, size_t vl);
vuint16mf4x5_t __riscv_vluxseg5ei16_v_u16mf4x5(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
vuint16mf4x5_t __riscv_vloxseg5ei16_v_u16mf4x5(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u16mf4x5(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u16mf4x5(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x5_t vs3, size_t vl);
vuint16mf4x5_t __riscv_vluxseg5ei32_v_u16mf4x5(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
vuint16mf4x5_t __riscv_vloxseg5ei32_v_u16mf4x5(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u16mf4x5(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u16mf4x5(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x5_t vs3, size_t vl);
vuint16mf4x5_t __riscv_vluxseg5ei64_v_u16mf4x5(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
vuint16mf4x5_t __riscv_vloxseg5ei64_v_u16mf4x5(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u16mf4x5(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u16mf4x5(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x5_t vs3, size_t vl);
vint16mf4x5_t __riscv_vundefined_i16mf4x5(void);
vint16mf4x5_t __riscv_vcreate_v_i16mf4_i16mf4x5(vint16mf4_t v0, vint16mf4_t v1, vint16mf4_t v2, vint16mf4_t v3, vint16mf4_t v4);
vint16mf4_t __riscv_vget_v_i16mf4x5_i16mf4(vint16mf4x5_t src, size_t index);
vint16mf4x5_t __riscv_vset_v_i16mf4x5_i16mf4(vint16mf4x5_t dest, size_t index, vint16mf4_t value);
vint16mf4x5_t __riscv_vlseg5e16_v_i16mf4x5(const short *rs1, size_t vl);
void __riscv_vsseg5e16_v_i16mf4x5(short *rs1, vint16mf4x5_t vs3, size_t vl);
vint16mf4x5_t __riscv_vlseg5e16ff_v_i16mf4x5(const short *rs1, size_t *new_vl, size_t vl);
vint16mf4x5_t __riscv_vlsseg5e16_v_i16mf4x5(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg5e16_v_i16mf4x5(short *rs1, long rs2, vint16mf4x5_t vs3, size_t vl);
vint16mf4x5_t __riscv_vluxseg5ei8_v_i16mf4x5(const short *rs1, vuint8mf8_t rs2, size_t vl);
vint16mf4x5_t __riscv_vloxseg5ei8_v_i16mf4x5(const short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i16mf4x5(short *rs1, vuint8mf8_t rs2, vint16mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i16mf4x5(short *rs1, vuint8mf8_t rs2, vint16mf4x5_t vs3, size_t vl);
vint16mf4x5_t __riscv_vluxseg5ei16_v_i16mf4x5(const short *rs1, vuint16mf4_t rs2, size_t vl);
vint16mf4x5_t __riscv_vloxseg5ei16_v_i16mf4x5(const short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i16mf4x5(short *rs1, vuint16mf4_t rs2, vint16mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i16mf4x5(short *rs1, vuint16mf4_t rs2, vint16mf4x5_t vs3, size_t vl);
vint16mf4x5_t __riscv_vluxseg5ei32_v_i16mf4x5(const short *rs1, vuint32mf2_t rs2, size_t vl);
vint16mf4x5_t __riscv_vloxseg5ei32_v_i16mf4x5(const short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i16mf4x5(short *rs1, vuint32mf2_t rs2, vint16mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i16mf4x5(short *rs1, vuint32mf2_t rs2, vint16mf4x5_t vs3, size_t vl);
vint16mf4x5_t __riscv_vluxseg5ei64_v_i16mf4x5(const short *rs1, vuint64m1_t rs2, size_t vl);
vint16mf4x5_t __riscv_vloxseg5ei64_v_i16mf4x5(const short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i16mf4x5(short *rs1, vuint64m1_t rs2, vint16mf4x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i16mf4x5(short *rs1, vuint64m1_t rs2, vint16mf4x5_t vs3, size_t vl);
vuint16mf4x6_t __riscv_vundefined_u16mf4x6(void);
vuint16mf4x6_t __riscv_vcreate_v_u16mf4_u16mf4x6(vuint16mf4_t v0, vuint16mf4_t v1, vuint16mf4_t v2, vuint16mf4_t v3, vuint16mf4_t v4, vuint16mf4_t v5);
vuint16mf4_t __riscv_vget_v_u16mf4x6_u16mf4(vuint16mf4x6_t src, size_t index);
vuint16mf4x6_t __riscv_vset_v_u16mf4x6_u16mf4(vuint16mf4x6_t dest, size_t index, vuint16mf4_t value);
vuint16mf4x6_t __riscv_vlseg6e16_v_u16mf4x6(const unsigned short *rs1, size_t vl);
void __riscv_vsseg6e16_v_u16mf4x6(unsigned short *rs1, vuint16mf4x6_t vs3, size_t vl);
vuint16mf4x6_t __riscv_vlseg6e16ff_v_u16mf4x6(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf4x6_t __riscv_vlsseg6e16_v_u16mf4x6(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg6e16_v_u16mf4x6(unsigned short *rs1, long rs2, vuint16mf4x6_t vs3, size_t vl);
vuint16mf4x6_t __riscv_vluxseg6ei8_v_u16mf4x6(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
vuint16mf4x6_t __riscv_vloxseg6ei8_v_u16mf4x6(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u16mf4x6(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u16mf4x6(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x6_t vs3, size_t vl);
vuint16mf4x6_t __riscv_vluxseg6ei16_v_u16mf4x6(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
vuint16mf4x6_t __riscv_vloxseg6ei16_v_u16mf4x6(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u16mf4x6(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u16mf4x6(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x6_t vs3, size_t vl);
vuint16mf4x6_t __riscv_vluxseg6ei32_v_u16mf4x6(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
vuint16mf4x6_t __riscv_vloxseg6ei32_v_u16mf4x6(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u16mf4x6(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u16mf4x6(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x6_t vs3, size_t vl);
vuint16mf4x6_t __riscv_vluxseg6ei64_v_u16mf4x6(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
vuint16mf4x6_t __riscv_vloxseg6ei64_v_u16mf4x6(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u16mf4x6(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u16mf4x6(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x6_t vs3, size_t vl);
vint16mf4x6_t __riscv_vundefined_i16mf4x6(void);
vint16mf4x6_t __riscv_vcreate_v_i16mf4_i16mf4x6(vint16mf4_t v0, vint16mf4_t v1, vint16mf4_t v2, vint16mf4_t v3, vint16mf4_t v4, vint16mf4_t v5);
vint16mf4_t __riscv_vget_v_i16mf4x6_i16mf4(vint16mf4x6_t src, size_t index);
vint16mf4x6_t __riscv_vset_v_i16mf4x6_i16mf4(vint16mf4x6_t dest, size_t index, vint16mf4_t value);
vint16mf4x6_t __riscv_vlseg6e16_v_i16mf4x6(const short *rs1, size_t vl);
void __riscv_vsseg6e16_v_i16mf4x6(short *rs1, vint16mf4x6_t vs3, size_t vl);
vint16mf4x6_t __riscv_vlseg6e16ff_v_i16mf4x6(const short *rs1, size_t *new_vl, size_t vl);
vint16mf4x6_t __riscv_vlsseg6e16_v_i16mf4x6(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg6e16_v_i16mf4x6(short *rs1, long rs2, vint16mf4x6_t vs3, size_t vl);
vint16mf4x6_t __riscv_vluxseg6ei8_v_i16mf4x6(const short *rs1, vuint8mf8_t rs2, size_t vl);
vint16mf4x6_t __riscv_vloxseg6ei8_v_i16mf4x6(const short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i16mf4x6(short *rs1, vuint8mf8_t rs2, vint16mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i16mf4x6(short *rs1, vuint8mf8_t rs2, vint16mf4x6_t vs3, size_t vl);
vint16mf4x6_t __riscv_vluxseg6ei16_v_i16mf4x6(const short *rs1, vuint16mf4_t rs2, size_t vl);
vint16mf4x6_t __riscv_vloxseg6ei16_v_i16mf4x6(const short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i16mf4x6(short *rs1, vuint16mf4_t rs2, vint16mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i16mf4x6(short *rs1, vuint16mf4_t rs2, vint16mf4x6_t vs3, size_t vl);
vint16mf4x6_t __riscv_vluxseg6ei32_v_i16mf4x6(const short *rs1, vuint32mf2_t rs2, size_t vl);
vint16mf4x6_t __riscv_vloxseg6ei32_v_i16mf4x6(const short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i16mf4x6(short *rs1, vuint32mf2_t rs2, vint16mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i16mf4x6(short *rs1, vuint32mf2_t rs2, vint16mf4x6_t vs3, size_t vl);
vint16mf4x6_t __riscv_vluxseg6ei64_v_i16mf4x6(const short *rs1, vuint64m1_t rs2, size_t vl);
vint16mf4x6_t __riscv_vloxseg6ei64_v_i16mf4x6(const short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i16mf4x6(short *rs1, vuint64m1_t rs2, vint16mf4x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i16mf4x6(short *rs1, vuint64m1_t rs2, vint16mf4x6_t vs3, size_t vl);
vuint16mf4x7_t __riscv_vundefined_u16mf4x7(void);
vuint16mf4x7_t __riscv_vcreate_v_u16mf4_u16mf4x7(vuint16mf4_t v0, vuint16mf4_t v1, vuint16mf4_t v2, vuint16mf4_t v3, vuint16mf4_t v4, vuint16mf4_t v5, vuint16mf4_t v6);
vuint16mf4_t __riscv_vget_v_u16mf4x7_u16mf4(vuint16mf4x7_t src, size_t index);
vuint16mf4x7_t __riscv_vset_v_u16mf4x7_u16mf4(vuint16mf4x7_t dest, size_t index, vuint16mf4_t value);
vuint16mf4x7_t __riscv_vlseg7e16_v_u16mf4x7(const unsigned short *rs1, size_t vl);
void __riscv_vsseg7e16_v_u16mf4x7(unsigned short *rs1, vuint16mf4x7_t vs3, size_t vl);
vuint16mf4x7_t __riscv_vlseg7e16ff_v_u16mf4x7(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf4x7_t __riscv_vlsseg7e16_v_u16mf4x7(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg7e16_v_u16mf4x7(unsigned short *rs1, long rs2, vuint16mf4x7_t vs3, size_t vl);
vuint16mf4x7_t __riscv_vluxseg7ei8_v_u16mf4x7(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
vuint16mf4x7_t __riscv_vloxseg7ei8_v_u16mf4x7(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u16mf4x7(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u16mf4x7(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x7_t vs3, size_t vl);
vuint16mf4x7_t __riscv_vluxseg7ei16_v_u16mf4x7(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
vuint16mf4x7_t __riscv_vloxseg7ei16_v_u16mf4x7(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u16mf4x7(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u16mf4x7(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x7_t vs3, size_t vl);
vuint16mf4x7_t __riscv_vluxseg7ei32_v_u16mf4x7(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
vuint16mf4x7_t __riscv_vloxseg7ei32_v_u16mf4x7(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u16mf4x7(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u16mf4x7(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x7_t vs3, size_t vl);
vuint16mf4x7_t __riscv_vluxseg7ei64_v_u16mf4x7(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
vuint16mf4x7_t __riscv_vloxseg7ei64_v_u16mf4x7(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u16mf4x7(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u16mf4x7(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x7_t vs3, size_t vl);
vint16mf4x7_t __riscv_vundefined_i16mf4x7(void);
vint16mf4x7_t __riscv_vcreate_v_i16mf4_i16mf4x7(vint16mf4_t v0, vint16mf4_t v1, vint16mf4_t v2, vint16mf4_t v3, vint16mf4_t v4, vint16mf4_t v5, vint16mf4_t v6);
vint16mf4_t __riscv_vget_v_i16mf4x7_i16mf4(vint16mf4x7_t src, size_t index);
vint16mf4x7_t __riscv_vset_v_i16mf4x7_i16mf4(vint16mf4x7_t dest, size_t index, vint16mf4_t value);
vint16mf4x7_t __riscv_vlseg7e16_v_i16mf4x7(const short *rs1, size_t vl);
void __riscv_vsseg7e16_v_i16mf4x7(short *rs1, vint16mf4x7_t vs3, size_t vl);
vint16mf4x7_t __riscv_vlseg7e16ff_v_i16mf4x7(const short *rs1, size_t *new_vl, size_t vl);
vint16mf4x7_t __riscv_vlsseg7e16_v_i16mf4x7(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg7e16_v_i16mf4x7(short *rs1, long rs2, vint16mf4x7_t vs3, size_t vl);
vint16mf4x7_t __riscv_vluxseg7ei8_v_i16mf4x7(const short *rs1, vuint8mf8_t rs2, size_t vl);
vint16mf4x7_t __riscv_vloxseg7ei8_v_i16mf4x7(const short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i16mf4x7(short *rs1, vuint8mf8_t rs2, vint16mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i16mf4x7(short *rs1, vuint8mf8_t rs2, vint16mf4x7_t vs3, size_t vl);
vint16mf4x7_t __riscv_vluxseg7ei16_v_i16mf4x7(const short *rs1, vuint16mf4_t rs2, size_t vl);
vint16mf4x7_t __riscv_vloxseg7ei16_v_i16mf4x7(const short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i16mf4x7(short *rs1, vuint16mf4_t rs2, vint16mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i16mf4x7(short *rs1, vuint16mf4_t rs2, vint16mf4x7_t vs3, size_t vl);
vint16mf4x7_t __riscv_vluxseg7ei32_v_i16mf4x7(const short *rs1, vuint32mf2_t rs2, size_t vl);
vint16mf4x7_t __riscv_vloxseg7ei32_v_i16mf4x7(const short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i16mf4x7(short *rs1, vuint32mf2_t rs2, vint16mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i16mf4x7(short *rs1, vuint32mf2_t rs2, vint16mf4x7_t vs3, size_t vl);
vint16mf4x7_t __riscv_vluxseg7ei64_v_i16mf4x7(const short *rs1, vuint64m1_t rs2, size_t vl);
vint16mf4x7_t __riscv_vloxseg7ei64_v_i16mf4x7(const short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i16mf4x7(short *rs1, vuint64m1_t rs2, vint16mf4x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i16mf4x7(short *rs1, vuint64m1_t rs2, vint16mf4x7_t vs3, size_t vl);
vuint16mf4x8_t __riscv_vundefined_u16mf4x8(void);
vuint16mf4x8_t __riscv_vcreate_v_u16mf4_u16mf4x8(vuint16mf4_t v0, vuint16mf4_t v1, vuint16mf4_t v2, vuint16mf4_t v3, vuint16mf4_t v4, vuint16mf4_t v5, vuint16mf4_t v6, vuint16mf4_t v7);
vuint16mf4_t __riscv_vget_v_u16mf4x8_u16mf4(vuint16mf4x8_t src, size_t index);
vuint16mf4x8_t __riscv_vset_v_u16mf4x8_u16mf4(vuint16mf4x8_t dest, size_t index, vuint16mf4_t value);
vuint16mf4x8_t __riscv_vlseg8e16_v_u16mf4x8(const unsigned short *rs1, size_t vl);
void __riscv_vsseg8e16_v_u16mf4x8(unsigned short *rs1, vuint16mf4x8_t vs3, size_t vl);
vuint16mf4x8_t __riscv_vlseg8e16ff_v_u16mf4x8(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf4x8_t __riscv_vlsseg8e16_v_u16mf4x8(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg8e16_v_u16mf4x8(unsigned short *rs1, long rs2, vuint16mf4x8_t vs3, size_t vl);
vuint16mf4x8_t __riscv_vluxseg8ei8_v_u16mf4x8(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
vuint16mf4x8_t __riscv_vloxseg8ei8_v_u16mf4x8(const unsigned short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u16mf4x8(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u16mf4x8(unsigned short *rs1, vuint8mf8_t rs2, vuint16mf4x8_t vs3, size_t vl);
vuint16mf4x8_t __riscv_vluxseg8ei16_v_u16mf4x8(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
vuint16mf4x8_t __riscv_vloxseg8ei16_v_u16mf4x8(const unsigned short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u16mf4x8(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u16mf4x8(unsigned short *rs1, vuint16mf4_t rs2, vuint16mf4x8_t vs3, size_t vl);
vuint16mf4x8_t __riscv_vluxseg8ei32_v_u16mf4x8(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
vuint16mf4x8_t __riscv_vloxseg8ei32_v_u16mf4x8(const unsigned short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u16mf4x8(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u16mf4x8(unsigned short *rs1, vuint32mf2_t rs2, vuint16mf4x8_t vs3, size_t vl);
vuint16mf4x8_t __riscv_vluxseg8ei64_v_u16mf4x8(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
vuint16mf4x8_t __riscv_vloxseg8ei64_v_u16mf4x8(const unsigned short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u16mf4x8(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u16mf4x8(unsigned short *rs1, vuint64m1_t rs2, vuint16mf4x8_t vs3, size_t vl);
vint16mf4x8_t __riscv_vundefined_i16mf4x8(void);
vint16mf4x8_t __riscv_vcreate_v_i16mf4_i16mf4x8(vint16mf4_t v0, vint16mf4_t v1, vint16mf4_t v2, vint16mf4_t v3, vint16mf4_t v4, vint16mf4_t v5, vint16mf4_t v6, vint16mf4_t v7);
vint16mf4_t __riscv_vget_v_i16mf4x8_i16mf4(vint16mf4x8_t src, size_t index);
vint16mf4x8_t __riscv_vset_v_i16mf4x8_i16mf4(vint16mf4x8_t dest, size_t index, vint16mf4_t value);
vint16mf4x8_t __riscv_vlseg8e16_v_i16mf4x8(const short *rs1, size_t vl);
void __riscv_vsseg8e16_v_i16mf4x8(short *rs1, vint16mf4x8_t vs3, size_t vl);
vint16mf4x8_t __riscv_vlseg8e16ff_v_i16mf4x8(const short *rs1, size_t *new_vl, size_t vl);
vint16mf4x8_t __riscv_vlsseg8e16_v_i16mf4x8(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg8e16_v_i16mf4x8(short *rs1, long rs2, vint16mf4x8_t vs3, size_t vl);
vint16mf4x8_t __riscv_vluxseg8ei8_v_i16mf4x8(const short *rs1, vuint8mf8_t rs2, size_t vl);
vint16mf4x8_t __riscv_vloxseg8ei8_v_i16mf4x8(const short *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i16mf4x8(short *rs1, vuint8mf8_t rs2, vint16mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i16mf4x8(short *rs1, vuint8mf8_t rs2, vint16mf4x8_t vs3, size_t vl);
vint16mf4x8_t __riscv_vluxseg8ei16_v_i16mf4x8(const short *rs1, vuint16mf4_t rs2, size_t vl);
vint16mf4x8_t __riscv_vloxseg8ei16_v_i16mf4x8(const short *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i16mf4x8(short *rs1, vuint16mf4_t rs2, vint16mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i16mf4x8(short *rs1, vuint16mf4_t rs2, vint16mf4x8_t vs3, size_t vl);
vint16mf4x8_t __riscv_vluxseg8ei32_v_i16mf4x8(const short *rs1, vuint32mf2_t rs2, size_t vl);
vint16mf4x8_t __riscv_vloxseg8ei32_v_i16mf4x8(const short *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i16mf4x8(short *rs1, vuint32mf2_t rs2, vint16mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i16mf4x8(short *rs1, vuint32mf2_t rs2, vint16mf4x8_t vs3, size_t vl);
vint16mf4x8_t __riscv_vluxseg8ei64_v_i16mf4x8(const short *rs1, vuint64m1_t rs2, size_t vl);
vint16mf4x8_t __riscv_vloxseg8ei64_v_i16mf4x8(const short *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i16mf4x8(short *rs1, vuint64m1_t rs2, vint16mf4x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i16mf4x8(short *rs1, vuint64m1_t rs2, vint16mf4x8_t vs3, size_t vl);
vuint16mf2x2_t __riscv_vundefined_u16mf2x2(void);
vuint16mf2x2_t __riscv_vcreate_v_u16mf2_u16mf2x2(vuint16mf2_t v0, vuint16mf2_t v1);
vuint16mf2_t __riscv_vget_v_u16mf2x2_u16mf2(vuint16mf2x2_t src, size_t index);
vuint16mf2x2_t __riscv_vset_v_u16mf2x2_u16mf2(vuint16mf2x2_t dest, size_t index, vuint16mf2_t value);
vuint16mf2x2_t __riscv_vlseg2e16_v_u16mf2x2(const unsigned short *rs1, size_t vl);
void __riscv_vsseg2e16_v_u16mf2x2(unsigned short *rs1, vuint16mf2x2_t vs3, size_t vl);
vuint16mf2x2_t __riscv_vlseg2e16ff_v_u16mf2x2(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf2x2_t __riscv_vlsseg2e16_v_u16mf2x2(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_u16mf2x2(unsigned short *rs1, long rs2, vuint16mf2x2_t vs3, size_t vl);
vuint16mf2x2_t __riscv_vluxseg2ei8_v_u16mf2x2(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
vuint16mf2x2_t __riscv_vloxseg2ei8_v_u16mf2x2(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u16mf2x2(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u16mf2x2(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x2_t vs3, size_t vl);
vuint16mf2x2_t __riscv_vluxseg2ei16_v_u16mf2x2(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
vuint16mf2x2_t __riscv_vloxseg2ei16_v_u16mf2x2(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u16mf2x2(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u16mf2x2(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x2_t vs3, size_t vl);
vuint16mf2x2_t __riscv_vluxseg2ei32_v_u16mf2x2(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
vuint16mf2x2_t __riscv_vloxseg2ei32_v_u16mf2x2(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u16mf2x2(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u16mf2x2(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x2_t vs3, size_t vl);
vuint16mf2x2_t __riscv_vluxseg2ei64_v_u16mf2x2(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
vuint16mf2x2_t __riscv_vloxseg2ei64_v_u16mf2x2(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u16mf2x2(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u16mf2x2(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x2_t vs3, size_t vl);
vint16mf2x2_t __riscv_vundefined_i16mf2x2(void);
vint16mf2x2_t __riscv_vcreate_v_i16mf2_i16mf2x2(vint16mf2_t v0, vint16mf2_t v1);
vint16mf2_t __riscv_vget_v_i16mf2x2_i16mf2(vint16mf2x2_t src, size_t index);
vint16mf2x2_t __riscv_vset_v_i16mf2x2_i16mf2(vint16mf2x2_t dest, size_t index, vint16mf2_t value);
vint16mf2x2_t __riscv_vlseg2e16_v_i16mf2x2(const short *rs1, size_t vl);
void __riscv_vsseg2e16_v_i16mf2x2(short *rs1, vint16mf2x2_t vs3, size_t vl);
vint16mf2x2_t __riscv_vlseg2e16ff_v_i16mf2x2(const short *rs1, size_t *new_vl, size_t vl);
vint16mf2x2_t __riscv_vlsseg2e16_v_i16mf2x2(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_i16mf2x2(short *rs1, long rs2, vint16mf2x2_t vs3, size_t vl);
vint16mf2x2_t __riscv_vluxseg2ei8_v_i16mf2x2(const short *rs1, vuint8mf4_t rs2, size_t vl);
vint16mf2x2_t __riscv_vloxseg2ei8_v_i16mf2x2(const short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i16mf2x2(short *rs1, vuint8mf4_t rs2, vint16mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i16mf2x2(short *rs1, vuint8mf4_t rs2, vint16mf2x2_t vs3, size_t vl);
vint16mf2x2_t __riscv_vluxseg2ei16_v_i16mf2x2(const short *rs1, vuint16mf2_t rs2, size_t vl);
vint16mf2x2_t __riscv_vloxseg2ei16_v_i16mf2x2(const short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i16mf2x2(short *rs1, vuint16mf2_t rs2, vint16mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i16mf2x2(short *rs1, vuint16mf2_t rs2, vint16mf2x2_t vs3, size_t vl);
vint16mf2x2_t __riscv_vluxseg2ei32_v_i16mf2x2(const short *rs1, vuint32m1_t rs2, size_t vl);
vint16mf2x2_t __riscv_vloxseg2ei32_v_i16mf2x2(const short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i16mf2x2(short *rs1, vuint32m1_t rs2, vint16mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i16mf2x2(short *rs1, vuint32m1_t rs2, vint16mf2x2_t vs3, size_t vl);
vint16mf2x2_t __riscv_vluxseg2ei64_v_i16mf2x2(const short *rs1, vuint64m2_t rs2, size_t vl);
vint16mf2x2_t __riscv_vloxseg2ei64_v_i16mf2x2(const short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i16mf2x2(short *rs1, vuint64m2_t rs2, vint16mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i16mf2x2(short *rs1, vuint64m2_t rs2, vint16mf2x2_t vs3, size_t vl);
vuint16mf2x3_t __riscv_vundefined_u16mf2x3(void);
vuint16mf2x3_t __riscv_vcreate_v_u16mf2_u16mf2x3(vuint16mf2_t v0, vuint16mf2_t v1, vuint16mf2_t v2);
vuint16mf2_t __riscv_vget_v_u16mf2x3_u16mf2(vuint16mf2x3_t src, size_t index);
vuint16mf2x3_t __riscv_vset_v_u16mf2x3_u16mf2(vuint16mf2x3_t dest, size_t index, vuint16mf2_t value);
vuint16mf2x3_t __riscv_vlseg3e16_v_u16mf2x3(const unsigned short *rs1, size_t vl);
void __riscv_vsseg3e16_v_u16mf2x3(unsigned short *rs1, vuint16mf2x3_t vs3, size_t vl);
vuint16mf2x3_t __riscv_vlseg3e16ff_v_u16mf2x3(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf2x3_t __riscv_vlsseg3e16_v_u16mf2x3(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg3e16_v_u16mf2x3(unsigned short *rs1, long rs2, vuint16mf2x3_t vs3, size_t vl);
vuint16mf2x3_t __riscv_vluxseg3ei8_v_u16mf2x3(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
vuint16mf2x3_t __riscv_vloxseg3ei8_v_u16mf2x3(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u16mf2x3(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u16mf2x3(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x3_t vs3, size_t vl);
vuint16mf2x3_t __riscv_vluxseg3ei16_v_u16mf2x3(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
vuint16mf2x3_t __riscv_vloxseg3ei16_v_u16mf2x3(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u16mf2x3(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u16mf2x3(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x3_t vs3, size_t vl);
vuint16mf2x3_t __riscv_vluxseg3ei32_v_u16mf2x3(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
vuint16mf2x3_t __riscv_vloxseg3ei32_v_u16mf2x3(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u16mf2x3(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u16mf2x3(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x3_t vs3, size_t vl);
vuint16mf2x3_t __riscv_vluxseg3ei64_v_u16mf2x3(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
vuint16mf2x3_t __riscv_vloxseg3ei64_v_u16mf2x3(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u16mf2x3(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u16mf2x3(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x3_t vs3, size_t vl);
vint16mf2x3_t __riscv_vundefined_i16mf2x3(void);
vint16mf2x3_t __riscv_vcreate_v_i16mf2_i16mf2x3(vint16mf2_t v0, vint16mf2_t v1, vint16mf2_t v2);
vint16mf2_t __riscv_vget_v_i16mf2x3_i16mf2(vint16mf2x3_t src, size_t index);
vint16mf2x3_t __riscv_vset_v_i16mf2x3_i16mf2(vint16mf2x3_t dest, size_t index, vint16mf2_t value);
vint16mf2x3_t __riscv_vlseg3e16_v_i16mf2x3(const short *rs1, size_t vl);
void __riscv_vsseg3e16_v_i16mf2x3(short *rs1, vint16mf2x3_t vs3, size_t vl);
vint16mf2x3_t __riscv_vlseg3e16ff_v_i16mf2x3(const short *rs1, size_t *new_vl, size_t vl);
vint16mf2x3_t __riscv_vlsseg3e16_v_i16mf2x3(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg3e16_v_i16mf2x3(short *rs1, long rs2, vint16mf2x3_t vs3, size_t vl);
vint16mf2x3_t __riscv_vluxseg3ei8_v_i16mf2x3(const short *rs1, vuint8mf4_t rs2, size_t vl);
vint16mf2x3_t __riscv_vloxseg3ei8_v_i16mf2x3(const short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i16mf2x3(short *rs1, vuint8mf4_t rs2, vint16mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i16mf2x3(short *rs1, vuint8mf4_t rs2, vint16mf2x3_t vs3, size_t vl);
vint16mf2x3_t __riscv_vluxseg3ei16_v_i16mf2x3(const short *rs1, vuint16mf2_t rs2, size_t vl);
vint16mf2x3_t __riscv_vloxseg3ei16_v_i16mf2x3(const short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i16mf2x3(short *rs1, vuint16mf2_t rs2, vint16mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i16mf2x3(short *rs1, vuint16mf2_t rs2, vint16mf2x3_t vs3, size_t vl);
vint16mf2x3_t __riscv_vluxseg3ei32_v_i16mf2x3(const short *rs1, vuint32m1_t rs2, size_t vl);
vint16mf2x3_t __riscv_vloxseg3ei32_v_i16mf2x3(const short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i16mf2x3(short *rs1, vuint32m1_t rs2, vint16mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i16mf2x3(short *rs1, vuint32m1_t rs2, vint16mf2x3_t vs3, size_t vl);
vint16mf2x3_t __riscv_vluxseg3ei64_v_i16mf2x3(const short *rs1, vuint64m2_t rs2, size_t vl);
vint16mf2x3_t __riscv_vloxseg3ei64_v_i16mf2x3(const short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i16mf2x3(short *rs1, vuint64m2_t rs2, vint16mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i16mf2x3(short *rs1, vuint64m2_t rs2, vint16mf2x3_t vs3, size_t vl);
vuint16mf2x4_t __riscv_vundefined_u16mf2x4(void);
vuint16mf2x4_t __riscv_vcreate_v_u16mf2_u16mf2x4(vuint16mf2_t v0, vuint16mf2_t v1, vuint16mf2_t v2, vuint16mf2_t v3);
vuint16mf2_t __riscv_vget_v_u16mf2x4_u16mf2(vuint16mf2x4_t src, size_t index);
vuint16mf2x4_t __riscv_vset_v_u16mf2x4_u16mf2(vuint16mf2x4_t dest, size_t index, vuint16mf2_t value);
vuint16mf2x4_t __riscv_vlseg4e16_v_u16mf2x4(const unsigned short *rs1, size_t vl);
void __riscv_vsseg4e16_v_u16mf2x4(unsigned short *rs1, vuint16mf2x4_t vs3, size_t vl);
vuint16mf2x4_t __riscv_vlseg4e16ff_v_u16mf2x4(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf2x4_t __riscv_vlsseg4e16_v_u16mf2x4(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg4e16_v_u16mf2x4(unsigned short *rs1, long rs2, vuint16mf2x4_t vs3, size_t vl);
vuint16mf2x4_t __riscv_vluxseg4ei8_v_u16mf2x4(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
vuint16mf2x4_t __riscv_vloxseg4ei8_v_u16mf2x4(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u16mf2x4(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u16mf2x4(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x4_t vs3, size_t vl);
vuint16mf2x4_t __riscv_vluxseg4ei16_v_u16mf2x4(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
vuint16mf2x4_t __riscv_vloxseg4ei16_v_u16mf2x4(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u16mf2x4(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u16mf2x4(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x4_t vs3, size_t vl);
vuint16mf2x4_t __riscv_vluxseg4ei32_v_u16mf2x4(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
vuint16mf2x4_t __riscv_vloxseg4ei32_v_u16mf2x4(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u16mf2x4(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u16mf2x4(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x4_t vs3, size_t vl);
vuint16mf2x4_t __riscv_vluxseg4ei64_v_u16mf2x4(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
vuint16mf2x4_t __riscv_vloxseg4ei64_v_u16mf2x4(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u16mf2x4(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u16mf2x4(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x4_t vs3, size_t vl);
vint16mf2x4_t __riscv_vundefined_i16mf2x4(void);
vint16mf2x4_t __riscv_vcreate_v_i16mf2_i16mf2x4(vint16mf2_t v0, vint16mf2_t v1, vint16mf2_t v2, vint16mf2_t v3);
vint16mf2_t __riscv_vget_v_i16mf2x4_i16mf2(vint16mf2x4_t src, size_t index);
vint16mf2x4_t __riscv_vset_v_i16mf2x4_i16mf2(vint16mf2x4_t dest, size_t index, vint16mf2_t value);
vint16mf2x4_t __riscv_vlseg4e16_v_i16mf2x4(const short *rs1, size_t vl);
void __riscv_vsseg4e16_v_i16mf2x4(short *rs1, vint16mf2x4_t vs3, size_t vl);
vint16mf2x4_t __riscv_vlseg4e16ff_v_i16mf2x4(const short *rs1, size_t *new_vl, size_t vl);
vint16mf2x4_t __riscv_vlsseg4e16_v_i16mf2x4(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg4e16_v_i16mf2x4(short *rs1, long rs2, vint16mf2x4_t vs3, size_t vl);
vint16mf2x4_t __riscv_vluxseg4ei8_v_i16mf2x4(const short *rs1, vuint8mf4_t rs2, size_t vl);
vint16mf2x4_t __riscv_vloxseg4ei8_v_i16mf2x4(const short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i16mf2x4(short *rs1, vuint8mf4_t rs2, vint16mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i16mf2x4(short *rs1, vuint8mf4_t rs2, vint16mf2x4_t vs3, size_t vl);
vint16mf2x4_t __riscv_vluxseg4ei16_v_i16mf2x4(const short *rs1, vuint16mf2_t rs2, size_t vl);
vint16mf2x4_t __riscv_vloxseg4ei16_v_i16mf2x4(const short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i16mf2x4(short *rs1, vuint16mf2_t rs2, vint16mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i16mf2x4(short *rs1, vuint16mf2_t rs2, vint16mf2x4_t vs3, size_t vl);
vint16mf2x4_t __riscv_vluxseg4ei32_v_i16mf2x4(const short *rs1, vuint32m1_t rs2, size_t vl);
vint16mf2x4_t __riscv_vloxseg4ei32_v_i16mf2x4(const short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i16mf2x4(short *rs1, vuint32m1_t rs2, vint16mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i16mf2x4(short *rs1, vuint32m1_t rs2, vint16mf2x4_t vs3, size_t vl);
vint16mf2x4_t __riscv_vluxseg4ei64_v_i16mf2x4(const short *rs1, vuint64m2_t rs2, size_t vl);
vint16mf2x4_t __riscv_vloxseg4ei64_v_i16mf2x4(const short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i16mf2x4(short *rs1, vuint64m2_t rs2, vint16mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i16mf2x4(short *rs1, vuint64m2_t rs2, vint16mf2x4_t vs3, size_t vl);
vuint16mf2x5_t __riscv_vundefined_u16mf2x5(void);
vuint16mf2x5_t __riscv_vcreate_v_u16mf2_u16mf2x5(vuint16mf2_t v0, vuint16mf2_t v1, vuint16mf2_t v2, vuint16mf2_t v3, vuint16mf2_t v4);
vuint16mf2_t __riscv_vget_v_u16mf2x5_u16mf2(vuint16mf2x5_t src, size_t index);
vuint16mf2x5_t __riscv_vset_v_u16mf2x5_u16mf2(vuint16mf2x5_t dest, size_t index, vuint16mf2_t value);
vuint16mf2x5_t __riscv_vlseg5e16_v_u16mf2x5(const unsigned short *rs1, size_t vl);
void __riscv_vsseg5e16_v_u16mf2x5(unsigned short *rs1, vuint16mf2x5_t vs3, size_t vl);
vuint16mf2x5_t __riscv_vlseg5e16ff_v_u16mf2x5(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf2x5_t __riscv_vlsseg5e16_v_u16mf2x5(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg5e16_v_u16mf2x5(unsigned short *rs1, long rs2, vuint16mf2x5_t vs3, size_t vl);
vuint16mf2x5_t __riscv_vluxseg5ei8_v_u16mf2x5(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
vuint16mf2x5_t __riscv_vloxseg5ei8_v_u16mf2x5(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u16mf2x5(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u16mf2x5(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x5_t vs3, size_t vl);
vuint16mf2x5_t __riscv_vluxseg5ei16_v_u16mf2x5(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
vuint16mf2x5_t __riscv_vloxseg5ei16_v_u16mf2x5(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u16mf2x5(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u16mf2x5(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x5_t vs3, size_t vl);
vuint16mf2x5_t __riscv_vluxseg5ei32_v_u16mf2x5(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
vuint16mf2x5_t __riscv_vloxseg5ei32_v_u16mf2x5(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u16mf2x5(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u16mf2x5(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x5_t vs3, size_t vl);
vuint16mf2x5_t __riscv_vluxseg5ei64_v_u16mf2x5(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
vuint16mf2x5_t __riscv_vloxseg5ei64_v_u16mf2x5(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u16mf2x5(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u16mf2x5(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x5_t vs3, size_t vl);
vint16mf2x5_t __riscv_vundefined_i16mf2x5(void);
vint16mf2x5_t __riscv_vcreate_v_i16mf2_i16mf2x5(vint16mf2_t v0, vint16mf2_t v1, vint16mf2_t v2, vint16mf2_t v3, vint16mf2_t v4);
vint16mf2_t __riscv_vget_v_i16mf2x5_i16mf2(vint16mf2x5_t src, size_t index);
vint16mf2x5_t __riscv_vset_v_i16mf2x5_i16mf2(vint16mf2x5_t dest, size_t index, vint16mf2_t value);
vint16mf2x5_t __riscv_vlseg5e16_v_i16mf2x5(const short *rs1, size_t vl);
void __riscv_vsseg5e16_v_i16mf2x5(short *rs1, vint16mf2x5_t vs3, size_t vl);
vint16mf2x5_t __riscv_vlseg5e16ff_v_i16mf2x5(const short *rs1, size_t *new_vl, size_t vl);
vint16mf2x5_t __riscv_vlsseg5e16_v_i16mf2x5(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg5e16_v_i16mf2x5(short *rs1, long rs2, vint16mf2x5_t vs3, size_t vl);
vint16mf2x5_t __riscv_vluxseg5ei8_v_i16mf2x5(const short *rs1, vuint8mf4_t rs2, size_t vl);
vint16mf2x5_t __riscv_vloxseg5ei8_v_i16mf2x5(const short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i16mf2x5(short *rs1, vuint8mf4_t rs2, vint16mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i16mf2x5(short *rs1, vuint8mf4_t rs2, vint16mf2x5_t vs3, size_t vl);
vint16mf2x5_t __riscv_vluxseg5ei16_v_i16mf2x5(const short *rs1, vuint16mf2_t rs2, size_t vl);
vint16mf2x5_t __riscv_vloxseg5ei16_v_i16mf2x5(const short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i16mf2x5(short *rs1, vuint16mf2_t rs2, vint16mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i16mf2x5(short *rs1, vuint16mf2_t rs2, vint16mf2x5_t vs3, size_t vl);
vint16mf2x5_t __riscv_vluxseg5ei32_v_i16mf2x5(const short *rs1, vuint32m1_t rs2, size_t vl);
vint16mf2x5_t __riscv_vloxseg5ei32_v_i16mf2x5(const short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i16mf2x5(short *rs1, vuint32m1_t rs2, vint16mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i16mf2x5(short *rs1, vuint32m1_t rs2, vint16mf2x5_t vs3, size_t vl);
vint16mf2x5_t __riscv_vluxseg5ei64_v_i16mf2x5(const short *rs1, vuint64m2_t rs2, size_t vl);
vint16mf2x5_t __riscv_vloxseg5ei64_v_i16mf2x5(const short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i16mf2x5(short *rs1, vuint64m2_t rs2, vint16mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i16mf2x5(short *rs1, vuint64m2_t rs2, vint16mf2x5_t vs3, size_t vl);
vuint16mf2x6_t __riscv_vundefined_u16mf2x6(void);
vuint16mf2x6_t __riscv_vcreate_v_u16mf2_u16mf2x6(vuint16mf2_t v0, vuint16mf2_t v1, vuint16mf2_t v2, vuint16mf2_t v3, vuint16mf2_t v4, vuint16mf2_t v5);
vuint16mf2_t __riscv_vget_v_u16mf2x6_u16mf2(vuint16mf2x6_t src, size_t index);
vuint16mf2x6_t __riscv_vset_v_u16mf2x6_u16mf2(vuint16mf2x6_t dest, size_t index, vuint16mf2_t value);
vuint16mf2x6_t __riscv_vlseg6e16_v_u16mf2x6(const unsigned short *rs1, size_t vl);
void __riscv_vsseg6e16_v_u16mf2x6(unsigned short *rs1, vuint16mf2x6_t vs3, size_t vl);
vuint16mf2x6_t __riscv_vlseg6e16ff_v_u16mf2x6(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf2x6_t __riscv_vlsseg6e16_v_u16mf2x6(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg6e16_v_u16mf2x6(unsigned short *rs1, long rs2, vuint16mf2x6_t vs3, size_t vl);
vuint16mf2x6_t __riscv_vluxseg6ei8_v_u16mf2x6(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
vuint16mf2x6_t __riscv_vloxseg6ei8_v_u16mf2x6(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u16mf2x6(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u16mf2x6(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x6_t vs3, size_t vl);
vuint16mf2x6_t __riscv_vluxseg6ei16_v_u16mf2x6(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
vuint16mf2x6_t __riscv_vloxseg6ei16_v_u16mf2x6(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u16mf2x6(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u16mf2x6(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x6_t vs3, size_t vl);
vuint16mf2x6_t __riscv_vluxseg6ei32_v_u16mf2x6(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
vuint16mf2x6_t __riscv_vloxseg6ei32_v_u16mf2x6(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u16mf2x6(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u16mf2x6(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x6_t vs3, size_t vl);
vuint16mf2x6_t __riscv_vluxseg6ei64_v_u16mf2x6(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
vuint16mf2x6_t __riscv_vloxseg6ei64_v_u16mf2x6(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u16mf2x6(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u16mf2x6(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x6_t vs3, size_t vl);
vint16mf2x6_t __riscv_vundefined_i16mf2x6(void);
vint16mf2x6_t __riscv_vcreate_v_i16mf2_i16mf2x6(vint16mf2_t v0, vint16mf2_t v1, vint16mf2_t v2, vint16mf2_t v3, vint16mf2_t v4, vint16mf2_t v5);
vint16mf2_t __riscv_vget_v_i16mf2x6_i16mf2(vint16mf2x6_t src, size_t index);
vint16mf2x6_t __riscv_vset_v_i16mf2x6_i16mf2(vint16mf2x6_t dest, size_t index, vint16mf2_t value);
vint16mf2x6_t __riscv_vlseg6e16_v_i16mf2x6(const short *rs1, size_t vl);
void __riscv_vsseg6e16_v_i16mf2x6(short *rs1, vint16mf2x6_t vs3, size_t vl);
vint16mf2x6_t __riscv_vlseg6e16ff_v_i16mf2x6(const short *rs1, size_t *new_vl, size_t vl);
vint16mf2x6_t __riscv_vlsseg6e16_v_i16mf2x6(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg6e16_v_i16mf2x6(short *rs1, long rs2, vint16mf2x6_t vs3, size_t vl);
vint16mf2x6_t __riscv_vluxseg6ei8_v_i16mf2x6(const short *rs1, vuint8mf4_t rs2, size_t vl);
vint16mf2x6_t __riscv_vloxseg6ei8_v_i16mf2x6(const short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i16mf2x6(short *rs1, vuint8mf4_t rs2, vint16mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i16mf2x6(short *rs1, vuint8mf4_t rs2, vint16mf2x6_t vs3, size_t vl);
vint16mf2x6_t __riscv_vluxseg6ei16_v_i16mf2x6(const short *rs1, vuint16mf2_t rs2, size_t vl);
vint16mf2x6_t __riscv_vloxseg6ei16_v_i16mf2x6(const short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i16mf2x6(short *rs1, vuint16mf2_t rs2, vint16mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i16mf2x6(short *rs1, vuint16mf2_t rs2, vint16mf2x6_t vs3, size_t vl);
vint16mf2x6_t __riscv_vluxseg6ei32_v_i16mf2x6(const short *rs1, vuint32m1_t rs2, size_t vl);
vint16mf2x6_t __riscv_vloxseg6ei32_v_i16mf2x6(const short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i16mf2x6(short *rs1, vuint32m1_t rs2, vint16mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i16mf2x6(short *rs1, vuint32m1_t rs2, vint16mf2x6_t vs3, size_t vl);
vint16mf2x6_t __riscv_vluxseg6ei64_v_i16mf2x6(const short *rs1, vuint64m2_t rs2, size_t vl);
vint16mf2x6_t __riscv_vloxseg6ei64_v_i16mf2x6(const short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i16mf2x6(short *rs1, vuint64m2_t rs2, vint16mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i16mf2x6(short *rs1, vuint64m2_t rs2, vint16mf2x6_t vs3, size_t vl);
vuint16mf2x7_t __riscv_vundefined_u16mf2x7(void);
vuint16mf2x7_t __riscv_vcreate_v_u16mf2_u16mf2x7(vuint16mf2_t v0, vuint16mf2_t v1, vuint16mf2_t v2, vuint16mf2_t v3, vuint16mf2_t v4, vuint16mf2_t v5, vuint16mf2_t v6);
vuint16mf2_t __riscv_vget_v_u16mf2x7_u16mf2(vuint16mf2x7_t src, size_t index);
vuint16mf2x7_t __riscv_vset_v_u16mf2x7_u16mf2(vuint16mf2x7_t dest, size_t index, vuint16mf2_t value);
vuint16mf2x7_t __riscv_vlseg7e16_v_u16mf2x7(const unsigned short *rs1, size_t vl);
void __riscv_vsseg7e16_v_u16mf2x7(unsigned short *rs1, vuint16mf2x7_t vs3, size_t vl);
vuint16mf2x7_t __riscv_vlseg7e16ff_v_u16mf2x7(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf2x7_t __riscv_vlsseg7e16_v_u16mf2x7(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg7e16_v_u16mf2x7(unsigned short *rs1, long rs2, vuint16mf2x7_t vs3, size_t vl);
vuint16mf2x7_t __riscv_vluxseg7ei8_v_u16mf2x7(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
vuint16mf2x7_t __riscv_vloxseg7ei8_v_u16mf2x7(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u16mf2x7(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u16mf2x7(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x7_t vs3, size_t vl);
vuint16mf2x7_t __riscv_vluxseg7ei16_v_u16mf2x7(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
vuint16mf2x7_t __riscv_vloxseg7ei16_v_u16mf2x7(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u16mf2x7(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u16mf2x7(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x7_t vs3, size_t vl);
vuint16mf2x7_t __riscv_vluxseg7ei32_v_u16mf2x7(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
vuint16mf2x7_t __riscv_vloxseg7ei32_v_u16mf2x7(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u16mf2x7(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u16mf2x7(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x7_t vs3, size_t vl);
vuint16mf2x7_t __riscv_vluxseg7ei64_v_u16mf2x7(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
vuint16mf2x7_t __riscv_vloxseg7ei64_v_u16mf2x7(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u16mf2x7(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u16mf2x7(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x7_t vs3, size_t vl);
vint16mf2x7_t __riscv_vundefined_i16mf2x7(void);
vint16mf2x7_t __riscv_vcreate_v_i16mf2_i16mf2x7(vint16mf2_t v0, vint16mf2_t v1, vint16mf2_t v2, vint16mf2_t v3, vint16mf2_t v4, vint16mf2_t v5, vint16mf2_t v6);
vint16mf2_t __riscv_vget_v_i16mf2x7_i16mf2(vint16mf2x7_t src, size_t index);
vint16mf2x7_t __riscv_vset_v_i16mf2x7_i16mf2(vint16mf2x7_t dest, size_t index, vint16mf2_t value);
vint16mf2x7_t __riscv_vlseg7e16_v_i16mf2x7(const short *rs1, size_t vl);
void __riscv_vsseg7e16_v_i16mf2x7(short *rs1, vint16mf2x7_t vs3, size_t vl);
vint16mf2x7_t __riscv_vlseg7e16ff_v_i16mf2x7(const short *rs1, size_t *new_vl, size_t vl);
vint16mf2x7_t __riscv_vlsseg7e16_v_i16mf2x7(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg7e16_v_i16mf2x7(short *rs1, long rs2, vint16mf2x7_t vs3, size_t vl);
vint16mf2x7_t __riscv_vluxseg7ei8_v_i16mf2x7(const short *rs1, vuint8mf4_t rs2, size_t vl);
vint16mf2x7_t __riscv_vloxseg7ei8_v_i16mf2x7(const short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i16mf2x7(short *rs1, vuint8mf4_t rs2, vint16mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i16mf2x7(short *rs1, vuint8mf4_t rs2, vint16mf2x7_t vs3, size_t vl);
vint16mf2x7_t __riscv_vluxseg7ei16_v_i16mf2x7(const short *rs1, vuint16mf2_t rs2, size_t vl);
vint16mf2x7_t __riscv_vloxseg7ei16_v_i16mf2x7(const short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i16mf2x7(short *rs1, vuint16mf2_t rs2, vint16mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i16mf2x7(short *rs1, vuint16mf2_t rs2, vint16mf2x7_t vs3, size_t vl);
vint16mf2x7_t __riscv_vluxseg7ei32_v_i16mf2x7(const short *rs1, vuint32m1_t rs2, size_t vl);
vint16mf2x7_t __riscv_vloxseg7ei32_v_i16mf2x7(const short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i16mf2x7(short *rs1, vuint32m1_t rs2, vint16mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i16mf2x7(short *rs1, vuint32m1_t rs2, vint16mf2x7_t vs3, size_t vl);
vint16mf2x7_t __riscv_vluxseg7ei64_v_i16mf2x7(const short *rs1, vuint64m2_t rs2, size_t vl);
vint16mf2x7_t __riscv_vloxseg7ei64_v_i16mf2x7(const short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i16mf2x7(short *rs1, vuint64m2_t rs2, vint16mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i16mf2x7(short *rs1, vuint64m2_t rs2, vint16mf2x7_t vs3, size_t vl);
vuint16mf2x8_t __riscv_vundefined_u16mf2x8(void);
vuint16mf2x8_t __riscv_vcreate_v_u16mf2_u16mf2x8(vuint16mf2_t v0, vuint16mf2_t v1, vuint16mf2_t v2, vuint16mf2_t v3, vuint16mf2_t v4, vuint16mf2_t v5, vuint16mf2_t v6, vuint16mf2_t v7);
vuint16mf2_t __riscv_vget_v_u16mf2x8_u16mf2(vuint16mf2x8_t src, size_t index);
vuint16mf2x8_t __riscv_vset_v_u16mf2x8_u16mf2(vuint16mf2x8_t dest, size_t index, vuint16mf2_t value);
vuint16mf2x8_t __riscv_vlseg8e16_v_u16mf2x8(const unsigned short *rs1, size_t vl);
void __riscv_vsseg8e16_v_u16mf2x8(unsigned short *rs1, vuint16mf2x8_t vs3, size_t vl);
vuint16mf2x8_t __riscv_vlseg8e16ff_v_u16mf2x8(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16mf2x8_t __riscv_vlsseg8e16_v_u16mf2x8(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg8e16_v_u16mf2x8(unsigned short *rs1, long rs2, vuint16mf2x8_t vs3, size_t vl);
vuint16mf2x8_t __riscv_vluxseg8ei8_v_u16mf2x8(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
vuint16mf2x8_t __riscv_vloxseg8ei8_v_u16mf2x8(const unsigned short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u16mf2x8(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u16mf2x8(unsigned short *rs1, vuint8mf4_t rs2, vuint16mf2x8_t vs3, size_t vl);
vuint16mf2x8_t __riscv_vluxseg8ei16_v_u16mf2x8(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
vuint16mf2x8_t __riscv_vloxseg8ei16_v_u16mf2x8(const unsigned short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u16mf2x8(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u16mf2x8(unsigned short *rs1, vuint16mf2_t rs2, vuint16mf2x8_t vs3, size_t vl);
vuint16mf2x8_t __riscv_vluxseg8ei32_v_u16mf2x8(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
vuint16mf2x8_t __riscv_vloxseg8ei32_v_u16mf2x8(const unsigned short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u16mf2x8(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u16mf2x8(unsigned short *rs1, vuint32m1_t rs2, vuint16mf2x8_t vs3, size_t vl);
vuint16mf2x8_t __riscv_vluxseg8ei64_v_u16mf2x8(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
vuint16mf2x8_t __riscv_vloxseg8ei64_v_u16mf2x8(const unsigned short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u16mf2x8(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u16mf2x8(unsigned short *rs1, vuint64m2_t rs2, vuint16mf2x8_t vs3, size_t vl);
vint16mf2x8_t __riscv_vundefined_i16mf2x8(void);
vint16mf2x8_t __riscv_vcreate_v_i16mf2_i16mf2x8(vint16mf2_t v0, vint16mf2_t v1, vint16mf2_t v2, vint16mf2_t v3, vint16mf2_t v4, vint16mf2_t v5, vint16mf2_t v6, vint16mf2_t v7);
vint16mf2_t __riscv_vget_v_i16mf2x8_i16mf2(vint16mf2x8_t src, size_t index);
vint16mf2x8_t __riscv_vset_v_i16mf2x8_i16mf2(vint16mf2x8_t dest, size_t index, vint16mf2_t value);
vint16mf2x8_t __riscv_vlseg8e16_v_i16mf2x8(const short *rs1, size_t vl);
void __riscv_vsseg8e16_v_i16mf2x8(short *rs1, vint16mf2x8_t vs3, size_t vl);
vint16mf2x8_t __riscv_vlseg8e16ff_v_i16mf2x8(const short *rs1, size_t *new_vl, size_t vl);
vint16mf2x8_t __riscv_vlsseg8e16_v_i16mf2x8(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg8e16_v_i16mf2x8(short *rs1, long rs2, vint16mf2x8_t vs3, size_t vl);
vint16mf2x8_t __riscv_vluxseg8ei8_v_i16mf2x8(const short *rs1, vuint8mf4_t rs2, size_t vl);
vint16mf2x8_t __riscv_vloxseg8ei8_v_i16mf2x8(const short *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i16mf2x8(short *rs1, vuint8mf4_t rs2, vint16mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i16mf2x8(short *rs1, vuint8mf4_t rs2, vint16mf2x8_t vs3, size_t vl);
vint16mf2x8_t __riscv_vluxseg8ei16_v_i16mf2x8(const short *rs1, vuint16mf2_t rs2, size_t vl);
vint16mf2x8_t __riscv_vloxseg8ei16_v_i16mf2x8(const short *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i16mf2x8(short *rs1, vuint16mf2_t rs2, vint16mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i16mf2x8(short *rs1, vuint16mf2_t rs2, vint16mf2x8_t vs3, size_t vl);
vint16mf2x8_t __riscv_vluxseg8ei32_v_i16mf2x8(const short *rs1, vuint32m1_t rs2, size_t vl);
vint16mf2x8_t __riscv_vloxseg8ei32_v_i16mf2x8(const short *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i16mf2x8(short *rs1, vuint32m1_t rs2, vint16mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i16mf2x8(short *rs1, vuint32m1_t rs2, vint16mf2x8_t vs3, size_t vl);
vint16mf2x8_t __riscv_vluxseg8ei64_v_i16mf2x8(const short *rs1, vuint64m2_t rs2, size_t vl);
vint16mf2x8_t __riscv_vloxseg8ei64_v_i16mf2x8(const short *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i16mf2x8(short *rs1, vuint64m2_t rs2, vint16mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i16mf2x8(short *rs1, vuint64m2_t rs2, vint16mf2x8_t vs3, size_t vl);
vuint16m1x2_t __riscv_vundefined_u16m1x2(void);
vuint16m1x2_t __riscv_vcreate_v_u16m1_u16m1x2(vuint16m1_t v0, vuint16m1_t v1);
vuint16m1_t __riscv_vget_v_u16m1x2_u16m1(vuint16m1x2_t src, size_t index);
vuint16m1x2_t __riscv_vset_v_u16m1x2_u16m1(vuint16m1x2_t dest, size_t index, vuint16m1_t value);
vuint16m1x2_t __riscv_vlseg2e16_v_u16m1x2(const unsigned short *rs1, size_t vl);
void __riscv_vsseg2e16_v_u16m1x2(unsigned short *rs1, vuint16m1x2_t vs3, size_t vl);
vuint16m1x2_t __riscv_vlseg2e16ff_v_u16m1x2(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m1x2_t __riscv_vlsseg2e16_v_u16m1x2(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_u16m1x2(unsigned short *rs1, long rs2, vuint16m1x2_t vs3, size_t vl);
vuint16m1x2_t __riscv_vluxseg2ei8_v_u16m1x2(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
vuint16m1x2_t __riscv_vloxseg2ei8_v_u16m1x2(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u16m1x2(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u16m1x2(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x2_t vs3, size_t vl);
vuint16m1x2_t __riscv_vluxseg2ei16_v_u16m1x2(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
vuint16m1x2_t __riscv_vloxseg2ei16_v_u16m1x2(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u16m1x2(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u16m1x2(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x2_t vs3, size_t vl);
vuint16m1x2_t __riscv_vluxseg2ei32_v_u16m1x2(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
vuint16m1x2_t __riscv_vloxseg2ei32_v_u16m1x2(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u16m1x2(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u16m1x2(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x2_t vs3, size_t vl);
vuint16m1x2_t __riscv_vluxseg2ei64_v_u16m1x2(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
vuint16m1x2_t __riscv_vloxseg2ei64_v_u16m1x2(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u16m1x2(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u16m1x2(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x2_t vs3, size_t vl);
vint16m1x2_t __riscv_vundefined_i16m1x2(void);
vint16m1x2_t __riscv_vcreate_v_i16m1_i16m1x2(vint16m1_t v0, vint16m1_t v1);
vint16m1_t __riscv_vget_v_i16m1x2_i16m1(vint16m1x2_t src, size_t index);
vint16m1x2_t __riscv_vset_v_i16m1x2_i16m1(vint16m1x2_t dest, size_t index, vint16m1_t value);
vint16m1x2_t __riscv_vlseg2e16_v_i16m1x2(const short *rs1, size_t vl);
void __riscv_vsseg2e16_v_i16m1x2(short *rs1, vint16m1x2_t vs3, size_t vl);
vint16m1x2_t __riscv_vlseg2e16ff_v_i16m1x2(const short *rs1, size_t *new_vl, size_t vl);
vint16m1x2_t __riscv_vlsseg2e16_v_i16m1x2(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_i16m1x2(short *rs1, long rs2, vint16m1x2_t vs3, size_t vl);
vint16m1x2_t __riscv_vluxseg2ei8_v_i16m1x2(const short *rs1, vuint8mf2_t rs2, size_t vl);
vint16m1x2_t __riscv_vloxseg2ei8_v_i16m1x2(const short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i16m1x2(short *rs1, vuint8mf2_t rs2, vint16m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i16m1x2(short *rs1, vuint8mf2_t rs2, vint16m1x2_t vs3, size_t vl);
vint16m1x2_t __riscv_vluxseg2ei16_v_i16m1x2(const short *rs1, vuint16m1_t rs2, size_t vl);
vint16m1x2_t __riscv_vloxseg2ei16_v_i16m1x2(const short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i16m1x2(short *rs1, vuint16m1_t rs2, vint16m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i16m1x2(short *rs1, vuint16m1_t rs2, vint16m1x2_t vs3, size_t vl);
vint16m1x2_t __riscv_vluxseg2ei32_v_i16m1x2(const short *rs1, vuint32m2_t rs2, size_t vl);
vint16m1x2_t __riscv_vloxseg2ei32_v_i16m1x2(const short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i16m1x2(short *rs1, vuint32m2_t rs2, vint16m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i16m1x2(short *rs1, vuint32m2_t rs2, vint16m1x2_t vs3, size_t vl);
vint16m1x2_t __riscv_vluxseg2ei64_v_i16m1x2(const short *rs1, vuint64m4_t rs2, size_t vl);
vint16m1x2_t __riscv_vloxseg2ei64_v_i16m1x2(const short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i16m1x2(short *rs1, vuint64m4_t rs2, vint16m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i16m1x2(short *rs1, vuint64m4_t rs2, vint16m1x2_t vs3, size_t vl);
vuint16m1x3_t __riscv_vundefined_u16m1x3(void);
vuint16m1x3_t __riscv_vcreate_v_u16m1_u16m1x3(vuint16m1_t v0, vuint16m1_t v1, vuint16m1_t v2);
vuint16m1_t __riscv_vget_v_u16m1x3_u16m1(vuint16m1x3_t src, size_t index);
vuint16m1x3_t __riscv_vset_v_u16m1x3_u16m1(vuint16m1x3_t dest, size_t index, vuint16m1_t value);
vuint16m1x3_t __riscv_vlseg3e16_v_u16m1x3(const unsigned short *rs1, size_t vl);
void __riscv_vsseg3e16_v_u16m1x3(unsigned short *rs1, vuint16m1x3_t vs3, size_t vl);
vuint16m1x3_t __riscv_vlseg3e16ff_v_u16m1x3(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m1x3_t __riscv_vlsseg3e16_v_u16m1x3(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg3e16_v_u16m1x3(unsigned short *rs1, long rs2, vuint16m1x3_t vs3, size_t vl);
vuint16m1x3_t __riscv_vluxseg3ei8_v_u16m1x3(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
vuint16m1x3_t __riscv_vloxseg3ei8_v_u16m1x3(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u16m1x3(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u16m1x3(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x3_t vs3, size_t vl);
vuint16m1x3_t __riscv_vluxseg3ei16_v_u16m1x3(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
vuint16m1x3_t __riscv_vloxseg3ei16_v_u16m1x3(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u16m1x3(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u16m1x3(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x3_t vs3, size_t vl);
vuint16m1x3_t __riscv_vluxseg3ei32_v_u16m1x3(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
vuint16m1x3_t __riscv_vloxseg3ei32_v_u16m1x3(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u16m1x3(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u16m1x3(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x3_t vs3, size_t vl);
vuint16m1x3_t __riscv_vluxseg3ei64_v_u16m1x3(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
vuint16m1x3_t __riscv_vloxseg3ei64_v_u16m1x3(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u16m1x3(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u16m1x3(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x3_t vs3, size_t vl);
vint16m1x3_t __riscv_vundefined_i16m1x3(void);
vint16m1x3_t __riscv_vcreate_v_i16m1_i16m1x3(vint16m1_t v0, vint16m1_t v1, vint16m1_t v2);
vint16m1_t __riscv_vget_v_i16m1x3_i16m1(vint16m1x3_t src, size_t index);
vint16m1x3_t __riscv_vset_v_i16m1x3_i16m1(vint16m1x3_t dest, size_t index, vint16m1_t value);
vint16m1x3_t __riscv_vlseg3e16_v_i16m1x3(const short *rs1, size_t vl);
void __riscv_vsseg3e16_v_i16m1x3(short *rs1, vint16m1x3_t vs3, size_t vl);
vint16m1x3_t __riscv_vlseg3e16ff_v_i16m1x3(const short *rs1, size_t *new_vl, size_t vl);
vint16m1x3_t __riscv_vlsseg3e16_v_i16m1x3(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg3e16_v_i16m1x3(short *rs1, long rs2, vint16m1x3_t vs3, size_t vl);
vint16m1x3_t __riscv_vluxseg3ei8_v_i16m1x3(const short *rs1, vuint8mf2_t rs2, size_t vl);
vint16m1x3_t __riscv_vloxseg3ei8_v_i16m1x3(const short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i16m1x3(short *rs1, vuint8mf2_t rs2, vint16m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i16m1x3(short *rs1, vuint8mf2_t rs2, vint16m1x3_t vs3, size_t vl);
vint16m1x3_t __riscv_vluxseg3ei16_v_i16m1x3(const short *rs1, vuint16m1_t rs2, size_t vl);
vint16m1x3_t __riscv_vloxseg3ei16_v_i16m1x3(const short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i16m1x3(short *rs1, vuint16m1_t rs2, vint16m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i16m1x3(short *rs1, vuint16m1_t rs2, vint16m1x3_t vs3, size_t vl);
vint16m1x3_t __riscv_vluxseg3ei32_v_i16m1x3(const short *rs1, vuint32m2_t rs2, size_t vl);
vint16m1x3_t __riscv_vloxseg3ei32_v_i16m1x3(const short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i16m1x3(short *rs1, vuint32m2_t rs2, vint16m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i16m1x3(short *rs1, vuint32m2_t rs2, vint16m1x3_t vs3, size_t vl);
vint16m1x3_t __riscv_vluxseg3ei64_v_i16m1x3(const short *rs1, vuint64m4_t rs2, size_t vl);
vint16m1x3_t __riscv_vloxseg3ei64_v_i16m1x3(const short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i16m1x3(short *rs1, vuint64m4_t rs2, vint16m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i16m1x3(short *rs1, vuint64m4_t rs2, vint16m1x3_t vs3, size_t vl);
vuint16m1x4_t __riscv_vundefined_u16m1x4(void);
vuint16m1x4_t __riscv_vcreate_v_u16m1_u16m1x4(vuint16m1_t v0, vuint16m1_t v1, vuint16m1_t v2, vuint16m1_t v3);
vuint16m1_t __riscv_vget_v_u16m1x4_u16m1(vuint16m1x4_t src, size_t index);
vuint16m1x4_t __riscv_vset_v_u16m1x4_u16m1(vuint16m1x4_t dest, size_t index, vuint16m1_t value);
vuint16m1x4_t __riscv_vlseg4e16_v_u16m1x4(const unsigned short *rs1, size_t vl);
void __riscv_vsseg4e16_v_u16m1x4(unsigned short *rs1, vuint16m1x4_t vs3, size_t vl);
vuint16m1x4_t __riscv_vlseg4e16ff_v_u16m1x4(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m1x4_t __riscv_vlsseg4e16_v_u16m1x4(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg4e16_v_u16m1x4(unsigned short *rs1, long rs2, vuint16m1x4_t vs3, size_t vl);
vuint16m1x4_t __riscv_vluxseg4ei8_v_u16m1x4(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
vuint16m1x4_t __riscv_vloxseg4ei8_v_u16m1x4(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u16m1x4(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u16m1x4(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x4_t vs3, size_t vl);
vuint16m1x4_t __riscv_vluxseg4ei16_v_u16m1x4(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
vuint16m1x4_t __riscv_vloxseg4ei16_v_u16m1x4(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u16m1x4(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u16m1x4(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x4_t vs3, size_t vl);
vuint16m1x4_t __riscv_vluxseg4ei32_v_u16m1x4(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
vuint16m1x4_t __riscv_vloxseg4ei32_v_u16m1x4(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u16m1x4(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u16m1x4(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x4_t vs3, size_t vl);
vuint16m1x4_t __riscv_vluxseg4ei64_v_u16m1x4(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
vuint16m1x4_t __riscv_vloxseg4ei64_v_u16m1x4(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u16m1x4(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u16m1x4(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x4_t vs3, size_t vl);
vint16m1x4_t __riscv_vundefined_i16m1x4(void);
vint16m1x4_t __riscv_vcreate_v_i16m1_i16m1x4(vint16m1_t v0, vint16m1_t v1, vint16m1_t v2, vint16m1_t v3);
vint16m1_t __riscv_vget_v_i16m1x4_i16m1(vint16m1x4_t src, size_t index);
vint16m1x4_t __riscv_vset_v_i16m1x4_i16m1(vint16m1x4_t dest, size_t index, vint16m1_t value);
vint16m1x4_t __riscv_vlseg4e16_v_i16m1x4(const short *rs1, size_t vl);
void __riscv_vsseg4e16_v_i16m1x4(short *rs1, vint16m1x4_t vs3, size_t vl);
vint16m1x4_t __riscv_vlseg4e16ff_v_i16m1x4(const short *rs1, size_t *new_vl, size_t vl);
vint16m1x4_t __riscv_vlsseg4e16_v_i16m1x4(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg4e16_v_i16m1x4(short *rs1, long rs2, vint16m1x4_t vs3, size_t vl);
vint16m1x4_t __riscv_vluxseg4ei8_v_i16m1x4(const short *rs1, vuint8mf2_t rs2, size_t vl);
vint16m1x4_t __riscv_vloxseg4ei8_v_i16m1x4(const short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i16m1x4(short *rs1, vuint8mf2_t rs2, vint16m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i16m1x4(short *rs1, vuint8mf2_t rs2, vint16m1x4_t vs3, size_t vl);
vint16m1x4_t __riscv_vluxseg4ei16_v_i16m1x4(const short *rs1, vuint16m1_t rs2, size_t vl);
vint16m1x4_t __riscv_vloxseg4ei16_v_i16m1x4(const short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i16m1x4(short *rs1, vuint16m1_t rs2, vint16m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i16m1x4(short *rs1, vuint16m1_t rs2, vint16m1x4_t vs3, size_t vl);
vint16m1x4_t __riscv_vluxseg4ei32_v_i16m1x4(const short *rs1, vuint32m2_t rs2, size_t vl);
vint16m1x4_t __riscv_vloxseg4ei32_v_i16m1x4(const short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i16m1x4(short *rs1, vuint32m2_t rs2, vint16m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i16m1x4(short *rs1, vuint32m2_t rs2, vint16m1x4_t vs3, size_t vl);
vint16m1x4_t __riscv_vluxseg4ei64_v_i16m1x4(const short *rs1, vuint64m4_t rs2, size_t vl);
vint16m1x4_t __riscv_vloxseg4ei64_v_i16m1x4(const short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i16m1x4(short *rs1, vuint64m4_t rs2, vint16m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i16m1x4(short *rs1, vuint64m4_t rs2, vint16m1x4_t vs3, size_t vl);
vuint16m1x5_t __riscv_vundefined_u16m1x5(void);
vuint16m1x5_t __riscv_vcreate_v_u16m1_u16m1x5(vuint16m1_t v0, vuint16m1_t v1, vuint16m1_t v2, vuint16m1_t v3, vuint16m1_t v4);
vuint16m1_t __riscv_vget_v_u16m1x5_u16m1(vuint16m1x5_t src, size_t index);
vuint16m1x5_t __riscv_vset_v_u16m1x5_u16m1(vuint16m1x5_t dest, size_t index, vuint16m1_t value);
vuint16m1x5_t __riscv_vlseg5e16_v_u16m1x5(const unsigned short *rs1, size_t vl);
void __riscv_vsseg5e16_v_u16m1x5(unsigned short *rs1, vuint16m1x5_t vs3, size_t vl);
vuint16m1x5_t __riscv_vlseg5e16ff_v_u16m1x5(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m1x5_t __riscv_vlsseg5e16_v_u16m1x5(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg5e16_v_u16m1x5(unsigned short *rs1, long rs2, vuint16m1x5_t vs3, size_t vl);
vuint16m1x5_t __riscv_vluxseg5ei8_v_u16m1x5(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
vuint16m1x5_t __riscv_vloxseg5ei8_v_u16m1x5(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u16m1x5(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u16m1x5(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x5_t vs3, size_t vl);
vuint16m1x5_t __riscv_vluxseg5ei16_v_u16m1x5(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
vuint16m1x5_t __riscv_vloxseg5ei16_v_u16m1x5(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u16m1x5(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u16m1x5(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x5_t vs3, size_t vl);
vuint16m1x5_t __riscv_vluxseg5ei32_v_u16m1x5(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
vuint16m1x5_t __riscv_vloxseg5ei32_v_u16m1x5(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u16m1x5(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u16m1x5(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x5_t vs3, size_t vl);
vuint16m1x5_t __riscv_vluxseg5ei64_v_u16m1x5(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
vuint16m1x5_t __riscv_vloxseg5ei64_v_u16m1x5(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u16m1x5(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u16m1x5(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x5_t vs3, size_t vl);
vint16m1x5_t __riscv_vundefined_i16m1x5(void);
vint16m1x5_t __riscv_vcreate_v_i16m1_i16m1x5(vint16m1_t v0, vint16m1_t v1, vint16m1_t v2, vint16m1_t v3, vint16m1_t v4);
vint16m1_t __riscv_vget_v_i16m1x5_i16m1(vint16m1x5_t src, size_t index);
vint16m1x5_t __riscv_vset_v_i16m1x5_i16m1(vint16m1x5_t dest, size_t index, vint16m1_t value);
vint16m1x5_t __riscv_vlseg5e16_v_i16m1x5(const short *rs1, size_t vl);
void __riscv_vsseg5e16_v_i16m1x5(short *rs1, vint16m1x5_t vs3, size_t vl);
vint16m1x5_t __riscv_vlseg5e16ff_v_i16m1x5(const short *rs1, size_t *new_vl, size_t vl);
vint16m1x5_t __riscv_vlsseg5e16_v_i16m1x5(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg5e16_v_i16m1x5(short *rs1, long rs2, vint16m1x5_t vs3, size_t vl);
vint16m1x5_t __riscv_vluxseg5ei8_v_i16m1x5(const short *rs1, vuint8mf2_t rs2, size_t vl);
vint16m1x5_t __riscv_vloxseg5ei8_v_i16m1x5(const short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i16m1x5(short *rs1, vuint8mf2_t rs2, vint16m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i16m1x5(short *rs1, vuint8mf2_t rs2, vint16m1x5_t vs3, size_t vl);
vint16m1x5_t __riscv_vluxseg5ei16_v_i16m1x5(const short *rs1, vuint16m1_t rs2, size_t vl);
vint16m1x5_t __riscv_vloxseg5ei16_v_i16m1x5(const short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i16m1x5(short *rs1, vuint16m1_t rs2, vint16m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i16m1x5(short *rs1, vuint16m1_t rs2, vint16m1x5_t vs3, size_t vl);
vint16m1x5_t __riscv_vluxseg5ei32_v_i16m1x5(const short *rs1, vuint32m2_t rs2, size_t vl);
vint16m1x5_t __riscv_vloxseg5ei32_v_i16m1x5(const short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i16m1x5(short *rs1, vuint32m2_t rs2, vint16m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i16m1x5(short *rs1, vuint32m2_t rs2, vint16m1x5_t vs3, size_t vl);
vint16m1x5_t __riscv_vluxseg5ei64_v_i16m1x5(const short *rs1, vuint64m4_t rs2, size_t vl);
vint16m1x5_t __riscv_vloxseg5ei64_v_i16m1x5(const short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i16m1x5(short *rs1, vuint64m4_t rs2, vint16m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i16m1x5(short *rs1, vuint64m4_t rs2, vint16m1x5_t vs3, size_t vl);
vuint16m1x6_t __riscv_vundefined_u16m1x6(void);
vuint16m1x6_t __riscv_vcreate_v_u16m1_u16m1x6(vuint16m1_t v0, vuint16m1_t v1, vuint16m1_t v2, vuint16m1_t v3, vuint16m1_t v4, vuint16m1_t v5);
vuint16m1_t __riscv_vget_v_u16m1x6_u16m1(vuint16m1x6_t src, size_t index);
vuint16m1x6_t __riscv_vset_v_u16m1x6_u16m1(vuint16m1x6_t dest, size_t index, vuint16m1_t value);
vuint16m1x6_t __riscv_vlseg6e16_v_u16m1x6(const unsigned short *rs1, size_t vl);
void __riscv_vsseg6e16_v_u16m1x6(unsigned short *rs1, vuint16m1x6_t vs3, size_t vl);
vuint16m1x6_t __riscv_vlseg6e16ff_v_u16m1x6(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m1x6_t __riscv_vlsseg6e16_v_u16m1x6(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg6e16_v_u16m1x6(unsigned short *rs1, long rs2, vuint16m1x6_t vs3, size_t vl);
vuint16m1x6_t __riscv_vluxseg6ei8_v_u16m1x6(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
vuint16m1x6_t __riscv_vloxseg6ei8_v_u16m1x6(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u16m1x6(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u16m1x6(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x6_t vs3, size_t vl);
vuint16m1x6_t __riscv_vluxseg6ei16_v_u16m1x6(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
vuint16m1x6_t __riscv_vloxseg6ei16_v_u16m1x6(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u16m1x6(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u16m1x6(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x6_t vs3, size_t vl);
vuint16m1x6_t __riscv_vluxseg6ei32_v_u16m1x6(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
vuint16m1x6_t __riscv_vloxseg6ei32_v_u16m1x6(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u16m1x6(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u16m1x6(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x6_t vs3, size_t vl);
vuint16m1x6_t __riscv_vluxseg6ei64_v_u16m1x6(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
vuint16m1x6_t __riscv_vloxseg6ei64_v_u16m1x6(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u16m1x6(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u16m1x6(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x6_t vs3, size_t vl);
vint16m1x6_t __riscv_vundefined_i16m1x6(void);
vint16m1x6_t __riscv_vcreate_v_i16m1_i16m1x6(vint16m1_t v0, vint16m1_t v1, vint16m1_t v2, vint16m1_t v3, vint16m1_t v4, vint16m1_t v5);
vint16m1_t __riscv_vget_v_i16m1x6_i16m1(vint16m1x6_t src, size_t index);
vint16m1x6_t __riscv_vset_v_i16m1x6_i16m1(vint16m1x6_t dest, size_t index, vint16m1_t value);
vint16m1x6_t __riscv_vlseg6e16_v_i16m1x6(const short *rs1, size_t vl);
void __riscv_vsseg6e16_v_i16m1x6(short *rs1, vint16m1x6_t vs3, size_t vl);
vint16m1x6_t __riscv_vlseg6e16ff_v_i16m1x6(const short *rs1, size_t *new_vl, size_t vl);
vint16m1x6_t __riscv_vlsseg6e16_v_i16m1x6(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg6e16_v_i16m1x6(short *rs1, long rs2, vint16m1x6_t vs3, size_t vl);
vint16m1x6_t __riscv_vluxseg6ei8_v_i16m1x6(const short *rs1, vuint8mf2_t rs2, size_t vl);
vint16m1x6_t __riscv_vloxseg6ei8_v_i16m1x6(const short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i16m1x6(short *rs1, vuint8mf2_t rs2, vint16m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i16m1x6(short *rs1, vuint8mf2_t rs2, vint16m1x6_t vs3, size_t vl);
vint16m1x6_t __riscv_vluxseg6ei16_v_i16m1x6(const short *rs1, vuint16m1_t rs2, size_t vl);
vint16m1x6_t __riscv_vloxseg6ei16_v_i16m1x6(const short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i16m1x6(short *rs1, vuint16m1_t rs2, vint16m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i16m1x6(short *rs1, vuint16m1_t rs2, vint16m1x6_t vs3, size_t vl);
vint16m1x6_t __riscv_vluxseg6ei32_v_i16m1x6(const short *rs1, vuint32m2_t rs2, size_t vl);
vint16m1x6_t __riscv_vloxseg6ei32_v_i16m1x6(const short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i16m1x6(short *rs1, vuint32m2_t rs2, vint16m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i16m1x6(short *rs1, vuint32m2_t rs2, vint16m1x6_t vs3, size_t vl);
vint16m1x6_t __riscv_vluxseg6ei64_v_i16m1x6(const short *rs1, vuint64m4_t rs2, size_t vl);
vint16m1x6_t __riscv_vloxseg6ei64_v_i16m1x6(const short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i16m1x6(short *rs1, vuint64m4_t rs2, vint16m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i16m1x6(short *rs1, vuint64m4_t rs2, vint16m1x6_t vs3, size_t vl);
vuint16m1x7_t __riscv_vundefined_u16m1x7(void);
vuint16m1x7_t __riscv_vcreate_v_u16m1_u16m1x7(vuint16m1_t v0, vuint16m1_t v1, vuint16m1_t v2, vuint16m1_t v3, vuint16m1_t v4, vuint16m1_t v5, vuint16m1_t v6);
vuint16m1_t __riscv_vget_v_u16m1x7_u16m1(vuint16m1x7_t src, size_t index);
vuint16m1x7_t __riscv_vset_v_u16m1x7_u16m1(vuint16m1x7_t dest, size_t index, vuint16m1_t value);
vuint16m1x7_t __riscv_vlseg7e16_v_u16m1x7(const unsigned short *rs1, size_t vl);
void __riscv_vsseg7e16_v_u16m1x7(unsigned short *rs1, vuint16m1x7_t vs3, size_t vl);
vuint16m1x7_t __riscv_vlseg7e16ff_v_u16m1x7(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m1x7_t __riscv_vlsseg7e16_v_u16m1x7(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg7e16_v_u16m1x7(unsigned short *rs1, long rs2, vuint16m1x7_t vs3, size_t vl);
vuint16m1x7_t __riscv_vluxseg7ei8_v_u16m1x7(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
vuint16m1x7_t __riscv_vloxseg7ei8_v_u16m1x7(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u16m1x7(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u16m1x7(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x7_t vs3, size_t vl);
vuint16m1x7_t __riscv_vluxseg7ei16_v_u16m1x7(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
vuint16m1x7_t __riscv_vloxseg7ei16_v_u16m1x7(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u16m1x7(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u16m1x7(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x7_t vs3, size_t vl);
vuint16m1x7_t __riscv_vluxseg7ei32_v_u16m1x7(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
vuint16m1x7_t __riscv_vloxseg7ei32_v_u16m1x7(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u16m1x7(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u16m1x7(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x7_t vs3, size_t vl);
vuint16m1x7_t __riscv_vluxseg7ei64_v_u16m1x7(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
vuint16m1x7_t __riscv_vloxseg7ei64_v_u16m1x7(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u16m1x7(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u16m1x7(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x7_t vs3, size_t vl);
vint16m1x7_t __riscv_vundefined_i16m1x7(void);
vint16m1x7_t __riscv_vcreate_v_i16m1_i16m1x7(vint16m1_t v0, vint16m1_t v1, vint16m1_t v2, vint16m1_t v3, vint16m1_t v4, vint16m1_t v5, vint16m1_t v6);
vint16m1_t __riscv_vget_v_i16m1x7_i16m1(vint16m1x7_t src, size_t index);
vint16m1x7_t __riscv_vset_v_i16m1x7_i16m1(vint16m1x7_t dest, size_t index, vint16m1_t value);
vint16m1x7_t __riscv_vlseg7e16_v_i16m1x7(const short *rs1, size_t vl);
void __riscv_vsseg7e16_v_i16m1x7(short *rs1, vint16m1x7_t vs3, size_t vl);
vint16m1x7_t __riscv_vlseg7e16ff_v_i16m1x7(const short *rs1, size_t *new_vl, size_t vl);
vint16m1x7_t __riscv_vlsseg7e16_v_i16m1x7(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg7e16_v_i16m1x7(short *rs1, long rs2, vint16m1x7_t vs3, size_t vl);
vint16m1x7_t __riscv_vluxseg7ei8_v_i16m1x7(const short *rs1, vuint8mf2_t rs2, size_t vl);
vint16m1x7_t __riscv_vloxseg7ei8_v_i16m1x7(const short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i16m1x7(short *rs1, vuint8mf2_t rs2, vint16m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i16m1x7(short *rs1, vuint8mf2_t rs2, vint16m1x7_t vs3, size_t vl);
vint16m1x7_t __riscv_vluxseg7ei16_v_i16m1x7(const short *rs1, vuint16m1_t rs2, size_t vl);
vint16m1x7_t __riscv_vloxseg7ei16_v_i16m1x7(const short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i16m1x7(short *rs1, vuint16m1_t rs2, vint16m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i16m1x7(short *rs1, vuint16m1_t rs2, vint16m1x7_t vs3, size_t vl);
vint16m1x7_t __riscv_vluxseg7ei32_v_i16m1x7(const short *rs1, vuint32m2_t rs2, size_t vl);
vint16m1x7_t __riscv_vloxseg7ei32_v_i16m1x7(const short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i16m1x7(short *rs1, vuint32m2_t rs2, vint16m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i16m1x7(short *rs1, vuint32m2_t rs2, vint16m1x7_t vs3, size_t vl);
vint16m1x7_t __riscv_vluxseg7ei64_v_i16m1x7(const short *rs1, vuint64m4_t rs2, size_t vl);
vint16m1x7_t __riscv_vloxseg7ei64_v_i16m1x7(const short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i16m1x7(short *rs1, vuint64m4_t rs2, vint16m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i16m1x7(short *rs1, vuint64m4_t rs2, vint16m1x7_t vs3, size_t vl);
vuint16m1x8_t __riscv_vundefined_u16m1x8(void);
vuint16m1x8_t __riscv_vcreate_v_u16m1_u16m1x8(vuint16m1_t v0, vuint16m1_t v1, vuint16m1_t v2, vuint16m1_t v3, vuint16m1_t v4, vuint16m1_t v5, vuint16m1_t v6, vuint16m1_t v7);
vuint16m1_t __riscv_vget_v_u16m1x8_u16m1(vuint16m1x8_t src, size_t index);
vuint16m1x8_t __riscv_vset_v_u16m1x8_u16m1(vuint16m1x8_t dest, size_t index, vuint16m1_t value);
vuint16m1x8_t __riscv_vlseg8e16_v_u16m1x8(const unsigned short *rs1, size_t vl);
void __riscv_vsseg8e16_v_u16m1x8(unsigned short *rs1, vuint16m1x8_t vs3, size_t vl);
vuint16m1x8_t __riscv_vlseg8e16ff_v_u16m1x8(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m1x8_t __riscv_vlsseg8e16_v_u16m1x8(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg8e16_v_u16m1x8(unsigned short *rs1, long rs2, vuint16m1x8_t vs3, size_t vl);
vuint16m1x8_t __riscv_vluxseg8ei8_v_u16m1x8(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
vuint16m1x8_t __riscv_vloxseg8ei8_v_u16m1x8(const unsigned short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u16m1x8(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u16m1x8(unsigned short *rs1, vuint8mf2_t rs2, vuint16m1x8_t vs3, size_t vl);
vuint16m1x8_t __riscv_vluxseg8ei16_v_u16m1x8(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
vuint16m1x8_t __riscv_vloxseg8ei16_v_u16m1x8(const unsigned short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u16m1x8(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u16m1x8(unsigned short *rs1, vuint16m1_t rs2, vuint16m1x8_t vs3, size_t vl);
vuint16m1x8_t __riscv_vluxseg8ei32_v_u16m1x8(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
vuint16m1x8_t __riscv_vloxseg8ei32_v_u16m1x8(const unsigned short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u16m1x8(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u16m1x8(unsigned short *rs1, vuint32m2_t rs2, vuint16m1x8_t vs3, size_t vl);
vuint16m1x8_t __riscv_vluxseg8ei64_v_u16m1x8(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
vuint16m1x8_t __riscv_vloxseg8ei64_v_u16m1x8(const unsigned short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u16m1x8(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u16m1x8(unsigned short *rs1, vuint64m4_t rs2, vuint16m1x8_t vs3, size_t vl);
vint16m1x8_t __riscv_vundefined_i16m1x8(void);
vint16m1x8_t __riscv_vcreate_v_i16m1_i16m1x8(vint16m1_t v0, vint16m1_t v1, vint16m1_t v2, vint16m1_t v3, vint16m1_t v4, vint16m1_t v5, vint16m1_t v6, vint16m1_t v7);
vint16m1_t __riscv_vget_v_i16m1x8_i16m1(vint16m1x8_t src, size_t index);
vint16m1x8_t __riscv_vset_v_i16m1x8_i16m1(vint16m1x8_t dest, size_t index, vint16m1_t value);
vint16m1x8_t __riscv_vlseg8e16_v_i16m1x8(const short *rs1, size_t vl);
void __riscv_vsseg8e16_v_i16m1x8(short *rs1, vint16m1x8_t vs3, size_t vl);
vint16m1x8_t __riscv_vlseg8e16ff_v_i16m1x8(const short *rs1, size_t *new_vl, size_t vl);
vint16m1x8_t __riscv_vlsseg8e16_v_i16m1x8(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg8e16_v_i16m1x8(short *rs1, long rs2, vint16m1x8_t vs3, size_t vl);
vint16m1x8_t __riscv_vluxseg8ei8_v_i16m1x8(const short *rs1, vuint8mf2_t rs2, size_t vl);
vint16m1x8_t __riscv_vloxseg8ei8_v_i16m1x8(const short *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i16m1x8(short *rs1, vuint8mf2_t rs2, vint16m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i16m1x8(short *rs1, vuint8mf2_t rs2, vint16m1x8_t vs3, size_t vl);
vint16m1x8_t __riscv_vluxseg8ei16_v_i16m1x8(const short *rs1, vuint16m1_t rs2, size_t vl);
vint16m1x8_t __riscv_vloxseg8ei16_v_i16m1x8(const short *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i16m1x8(short *rs1, vuint16m1_t rs2, vint16m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i16m1x8(short *rs1, vuint16m1_t rs2, vint16m1x8_t vs3, size_t vl);
vint16m1x8_t __riscv_vluxseg8ei32_v_i16m1x8(const short *rs1, vuint32m2_t rs2, size_t vl);
vint16m1x8_t __riscv_vloxseg8ei32_v_i16m1x8(const short *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i16m1x8(short *rs1, vuint32m2_t rs2, vint16m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i16m1x8(short *rs1, vuint32m2_t rs2, vint16m1x8_t vs3, size_t vl);
vint16m1x8_t __riscv_vluxseg8ei64_v_i16m1x8(const short *rs1, vuint64m4_t rs2, size_t vl);
vint16m1x8_t __riscv_vloxseg8ei64_v_i16m1x8(const short *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i16m1x8(short *rs1, vuint64m4_t rs2, vint16m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i16m1x8(short *rs1, vuint64m4_t rs2, vint16m1x8_t vs3, size_t vl);
vuint16m2x2_t __riscv_vundefined_u16m2x2(void);
vuint16m2x2_t __riscv_vcreate_v_u16m2_u16m2x2(vuint16m2_t v0, vuint16m2_t v1);
vuint16m2_t __riscv_vget_v_u16m2x2_u16m2(vuint16m2x2_t src, size_t index);
vuint16m2x2_t __riscv_vset_v_u16m2x2_u16m2(vuint16m2x2_t dest, size_t index, vuint16m2_t value);
vuint16m2x2_t __riscv_vlseg2e16_v_u16m2x2(const unsigned short *rs1, size_t vl);
void __riscv_vsseg2e16_v_u16m2x2(unsigned short *rs1, vuint16m2x2_t vs3, size_t vl);
vuint16m2x2_t __riscv_vlseg2e16ff_v_u16m2x2(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m2x2_t __riscv_vlsseg2e16_v_u16m2x2(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_u16m2x2(unsigned short *rs1, long rs2, vuint16m2x2_t vs3, size_t vl);
vuint16m2x2_t __riscv_vluxseg2ei8_v_u16m2x2(const unsigned short *rs1, vuint8m1_t rs2, size_t vl);
vuint16m2x2_t __riscv_vloxseg2ei8_v_u16m2x2(const unsigned short *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u16m2x2(unsigned short *rs1, vuint8m1_t rs2, vuint16m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u16m2x2(unsigned short *rs1, vuint8m1_t rs2, vuint16m2x2_t vs3, size_t vl);
vuint16m2x2_t __riscv_vluxseg2ei16_v_u16m2x2(const unsigned short *rs1, vuint16m2_t rs2, size_t vl);
vuint16m2x2_t __riscv_vloxseg2ei16_v_u16m2x2(const unsigned short *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u16m2x2(unsigned short *rs1, vuint16m2_t rs2, vuint16m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u16m2x2(unsigned short *rs1, vuint16m2_t rs2, vuint16m2x2_t vs3, size_t vl);
vuint16m2x2_t __riscv_vluxseg2ei32_v_u16m2x2(const unsigned short *rs1, vuint32m4_t rs2, size_t vl);
vuint16m2x2_t __riscv_vloxseg2ei32_v_u16m2x2(const unsigned short *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u16m2x2(unsigned short *rs1, vuint32m4_t rs2, vuint16m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u16m2x2(unsigned short *rs1, vuint32m4_t rs2, vuint16m2x2_t vs3, size_t vl);
vuint16m2x2_t __riscv_vluxseg2ei64_v_u16m2x2(const unsigned short *rs1, vuint64m8_t rs2, size_t vl);
vuint16m2x2_t __riscv_vloxseg2ei64_v_u16m2x2(const unsigned short *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u16m2x2(unsigned short *rs1, vuint64m8_t rs2, vuint16m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u16m2x2(unsigned short *rs1, vuint64m8_t rs2, vuint16m2x2_t vs3, size_t vl);
vint16m2x2_t __riscv_vundefined_i16m2x2(void);
vint16m2x2_t __riscv_vcreate_v_i16m2_i16m2x2(vint16m2_t v0, vint16m2_t v1);
vint16m2_t __riscv_vget_v_i16m2x2_i16m2(vint16m2x2_t src, size_t index);
vint16m2x2_t __riscv_vset_v_i16m2x2_i16m2(vint16m2x2_t dest, size_t index, vint16m2_t value);
vint16m2x2_t __riscv_vlseg2e16_v_i16m2x2(const short *rs1, size_t vl);
void __riscv_vsseg2e16_v_i16m2x2(short *rs1, vint16m2x2_t vs3, size_t vl);
vint16m2x2_t __riscv_vlseg2e16ff_v_i16m2x2(const short *rs1, size_t *new_vl, size_t vl);
vint16m2x2_t __riscv_vlsseg2e16_v_i16m2x2(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_i16m2x2(short *rs1, long rs2, vint16m2x2_t vs3, size_t vl);
vint16m2x2_t __riscv_vluxseg2ei8_v_i16m2x2(const short *rs1, vuint8m1_t rs2, size_t vl);
vint16m2x2_t __riscv_vloxseg2ei8_v_i16m2x2(const short *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i16m2x2(short *rs1, vuint8m1_t rs2, vint16m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i16m2x2(short *rs1, vuint8m1_t rs2, vint16m2x2_t vs3, size_t vl);
vint16m2x2_t __riscv_vluxseg2ei16_v_i16m2x2(const short *rs1, vuint16m2_t rs2, size_t vl);
vint16m2x2_t __riscv_vloxseg2ei16_v_i16m2x2(const short *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i16m2x2(short *rs1, vuint16m2_t rs2, vint16m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i16m2x2(short *rs1, vuint16m2_t rs2, vint16m2x2_t vs3, size_t vl);
vint16m2x2_t __riscv_vluxseg2ei32_v_i16m2x2(const short *rs1, vuint32m4_t rs2, size_t vl);
vint16m2x2_t __riscv_vloxseg2ei32_v_i16m2x2(const short *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i16m2x2(short *rs1, vuint32m4_t rs2, vint16m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i16m2x2(short *rs1, vuint32m4_t rs2, vint16m2x2_t vs3, size_t vl);
vint16m2x2_t __riscv_vluxseg2ei64_v_i16m2x2(const short *rs1, vuint64m8_t rs2, size_t vl);
vint16m2x2_t __riscv_vloxseg2ei64_v_i16m2x2(const short *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i16m2x2(short *rs1, vuint64m8_t rs2, vint16m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i16m2x2(short *rs1, vuint64m8_t rs2, vint16m2x2_t vs3, size_t vl);
vuint16m2x3_t __riscv_vundefined_u16m2x3(void);
vuint16m2x3_t __riscv_vcreate_v_u16m2_u16m2x3(vuint16m2_t v0, vuint16m2_t v1, vuint16m2_t v2);
vuint16m2_t __riscv_vget_v_u16m2x3_u16m2(vuint16m2x3_t src, size_t index);
vuint16m2x3_t __riscv_vset_v_u16m2x3_u16m2(vuint16m2x3_t dest, size_t index, vuint16m2_t value);
vuint16m2x3_t __riscv_vlseg3e16_v_u16m2x3(const unsigned short *rs1, size_t vl);
void __riscv_vsseg3e16_v_u16m2x3(unsigned short *rs1, vuint16m2x3_t vs3, size_t vl);
vuint16m2x3_t __riscv_vlseg3e16ff_v_u16m2x3(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m2x3_t __riscv_vlsseg3e16_v_u16m2x3(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg3e16_v_u16m2x3(unsigned short *rs1, long rs2, vuint16m2x3_t vs3, size_t vl);
vuint16m2x3_t __riscv_vluxseg3ei8_v_u16m2x3(const unsigned short *rs1, vuint8m1_t rs2, size_t vl);
vuint16m2x3_t __riscv_vloxseg3ei8_v_u16m2x3(const unsigned short *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u16m2x3(unsigned short *rs1, vuint8m1_t rs2, vuint16m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u16m2x3(unsigned short *rs1, vuint8m1_t rs2, vuint16m2x3_t vs3, size_t vl);
vuint16m2x3_t __riscv_vluxseg3ei16_v_u16m2x3(const unsigned short *rs1, vuint16m2_t rs2, size_t vl);
vuint16m2x3_t __riscv_vloxseg3ei16_v_u16m2x3(const unsigned short *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u16m2x3(unsigned short *rs1, vuint16m2_t rs2, vuint16m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u16m2x3(unsigned short *rs1, vuint16m2_t rs2, vuint16m2x3_t vs3, size_t vl);
vuint16m2x3_t __riscv_vluxseg3ei32_v_u16m2x3(const unsigned short *rs1, vuint32m4_t rs2, size_t vl);
vuint16m2x3_t __riscv_vloxseg3ei32_v_u16m2x3(const unsigned short *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u16m2x3(unsigned short *rs1, vuint32m4_t rs2, vuint16m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u16m2x3(unsigned short *rs1, vuint32m4_t rs2, vuint16m2x3_t vs3, size_t vl);
vuint16m2x3_t __riscv_vluxseg3ei64_v_u16m2x3(const unsigned short *rs1, vuint64m8_t rs2, size_t vl);
vuint16m2x3_t __riscv_vloxseg3ei64_v_u16m2x3(const unsigned short *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u16m2x3(unsigned short *rs1, vuint64m8_t rs2, vuint16m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u16m2x3(unsigned short *rs1, vuint64m8_t rs2, vuint16m2x3_t vs3, size_t vl);
vint16m2x3_t __riscv_vundefined_i16m2x3(void);
vint16m2x3_t __riscv_vcreate_v_i16m2_i16m2x3(vint16m2_t v0, vint16m2_t v1, vint16m2_t v2);
vint16m2_t __riscv_vget_v_i16m2x3_i16m2(vint16m2x3_t src, size_t index);
vint16m2x3_t __riscv_vset_v_i16m2x3_i16m2(vint16m2x3_t dest, size_t index, vint16m2_t value);
vint16m2x3_t __riscv_vlseg3e16_v_i16m2x3(const short *rs1, size_t vl);
void __riscv_vsseg3e16_v_i16m2x3(short *rs1, vint16m2x3_t vs3, size_t vl);
vint16m2x3_t __riscv_vlseg3e16ff_v_i16m2x3(const short *rs1, size_t *new_vl, size_t vl);
vint16m2x3_t __riscv_vlsseg3e16_v_i16m2x3(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg3e16_v_i16m2x3(short *rs1, long rs2, vint16m2x3_t vs3, size_t vl);
vint16m2x3_t __riscv_vluxseg3ei8_v_i16m2x3(const short *rs1, vuint8m1_t rs2, size_t vl);
vint16m2x3_t __riscv_vloxseg3ei8_v_i16m2x3(const short *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i16m2x3(short *rs1, vuint8m1_t rs2, vint16m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i16m2x3(short *rs1, vuint8m1_t rs2, vint16m2x3_t vs3, size_t vl);
vint16m2x3_t __riscv_vluxseg3ei16_v_i16m2x3(const short *rs1, vuint16m2_t rs2, size_t vl);
vint16m2x3_t __riscv_vloxseg3ei16_v_i16m2x3(const short *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i16m2x3(short *rs1, vuint16m2_t rs2, vint16m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i16m2x3(short *rs1, vuint16m2_t rs2, vint16m2x3_t vs3, size_t vl);
vint16m2x3_t __riscv_vluxseg3ei32_v_i16m2x3(const short *rs1, vuint32m4_t rs2, size_t vl);
vint16m2x3_t __riscv_vloxseg3ei32_v_i16m2x3(const short *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i16m2x3(short *rs1, vuint32m4_t rs2, vint16m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i16m2x3(short *rs1, vuint32m4_t rs2, vint16m2x3_t vs3, size_t vl);
vint16m2x3_t __riscv_vluxseg3ei64_v_i16m2x3(const short *rs1, vuint64m8_t rs2, size_t vl);
vint16m2x3_t __riscv_vloxseg3ei64_v_i16m2x3(const short *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i16m2x3(short *rs1, vuint64m8_t rs2, vint16m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i16m2x3(short *rs1, vuint64m8_t rs2, vint16m2x3_t vs3, size_t vl);
vuint16m2x4_t __riscv_vundefined_u16m2x4(void);
vuint16m2x4_t __riscv_vcreate_v_u16m2_u16m2x4(vuint16m2_t v0, vuint16m2_t v1, vuint16m2_t v2, vuint16m2_t v3);
vuint16m2_t __riscv_vget_v_u16m2x4_u16m2(vuint16m2x4_t src, size_t index);
vuint16m2x4_t __riscv_vset_v_u16m2x4_u16m2(vuint16m2x4_t dest, size_t index, vuint16m2_t value);
vuint16m2x4_t __riscv_vlseg4e16_v_u16m2x4(const unsigned short *rs1, size_t vl);
void __riscv_vsseg4e16_v_u16m2x4(unsigned short *rs1, vuint16m2x4_t vs3, size_t vl);
vuint16m2x4_t __riscv_vlseg4e16ff_v_u16m2x4(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m2x4_t __riscv_vlsseg4e16_v_u16m2x4(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg4e16_v_u16m2x4(unsigned short *rs1, long rs2, vuint16m2x4_t vs3, size_t vl);
vuint16m2x4_t __riscv_vluxseg4ei8_v_u16m2x4(const unsigned short *rs1, vuint8m1_t rs2, size_t vl);
vuint16m2x4_t __riscv_vloxseg4ei8_v_u16m2x4(const unsigned short *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u16m2x4(unsigned short *rs1, vuint8m1_t rs2, vuint16m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u16m2x4(unsigned short *rs1, vuint8m1_t rs2, vuint16m2x4_t vs3, size_t vl);
vuint16m2x4_t __riscv_vluxseg4ei16_v_u16m2x4(const unsigned short *rs1, vuint16m2_t rs2, size_t vl);
vuint16m2x4_t __riscv_vloxseg4ei16_v_u16m2x4(const unsigned short *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u16m2x4(unsigned short *rs1, vuint16m2_t rs2, vuint16m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u16m2x4(unsigned short *rs1, vuint16m2_t rs2, vuint16m2x4_t vs3, size_t vl);
vuint16m2x4_t __riscv_vluxseg4ei32_v_u16m2x4(const unsigned short *rs1, vuint32m4_t rs2, size_t vl);
vuint16m2x4_t __riscv_vloxseg4ei32_v_u16m2x4(const unsigned short *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u16m2x4(unsigned short *rs1, vuint32m4_t rs2, vuint16m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u16m2x4(unsigned short *rs1, vuint32m4_t rs2, vuint16m2x4_t vs3, size_t vl);
vuint16m2x4_t __riscv_vluxseg4ei64_v_u16m2x4(const unsigned short *rs1, vuint64m8_t rs2, size_t vl);
vuint16m2x4_t __riscv_vloxseg4ei64_v_u16m2x4(const unsigned short *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u16m2x4(unsigned short *rs1, vuint64m8_t rs2, vuint16m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u16m2x4(unsigned short *rs1, vuint64m8_t rs2, vuint16m2x4_t vs3, size_t vl);
vint16m2x4_t __riscv_vundefined_i16m2x4(void);
vint16m2x4_t __riscv_vcreate_v_i16m2_i16m2x4(vint16m2_t v0, vint16m2_t v1, vint16m2_t v2, vint16m2_t v3);
vint16m2_t __riscv_vget_v_i16m2x4_i16m2(vint16m2x4_t src, size_t index);
vint16m2x4_t __riscv_vset_v_i16m2x4_i16m2(vint16m2x4_t dest, size_t index, vint16m2_t value);
vint16m2x4_t __riscv_vlseg4e16_v_i16m2x4(const short *rs1, size_t vl);
void __riscv_vsseg4e16_v_i16m2x4(short *rs1, vint16m2x4_t vs3, size_t vl);
vint16m2x4_t __riscv_vlseg4e16ff_v_i16m2x4(const short *rs1, size_t *new_vl, size_t vl);
vint16m2x4_t __riscv_vlsseg4e16_v_i16m2x4(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg4e16_v_i16m2x4(short *rs1, long rs2, vint16m2x4_t vs3, size_t vl);
vint16m2x4_t __riscv_vluxseg4ei8_v_i16m2x4(const short *rs1, vuint8m1_t rs2, size_t vl);
vint16m2x4_t __riscv_vloxseg4ei8_v_i16m2x4(const short *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i16m2x4(short *rs1, vuint8m1_t rs2, vint16m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i16m2x4(short *rs1, vuint8m1_t rs2, vint16m2x4_t vs3, size_t vl);
vint16m2x4_t __riscv_vluxseg4ei16_v_i16m2x4(const short *rs1, vuint16m2_t rs2, size_t vl);
vint16m2x4_t __riscv_vloxseg4ei16_v_i16m2x4(const short *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i16m2x4(short *rs1, vuint16m2_t rs2, vint16m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i16m2x4(short *rs1, vuint16m2_t rs2, vint16m2x4_t vs3, size_t vl);
vint16m2x4_t __riscv_vluxseg4ei32_v_i16m2x4(const short *rs1, vuint32m4_t rs2, size_t vl);
vint16m2x4_t __riscv_vloxseg4ei32_v_i16m2x4(const short *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i16m2x4(short *rs1, vuint32m4_t rs2, vint16m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i16m2x4(short *rs1, vuint32m4_t rs2, vint16m2x4_t vs3, size_t vl);
vint16m2x4_t __riscv_vluxseg4ei64_v_i16m2x4(const short *rs1, vuint64m8_t rs2, size_t vl);
vint16m2x4_t __riscv_vloxseg4ei64_v_i16m2x4(const short *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i16m2x4(short *rs1, vuint64m8_t rs2, vint16m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i16m2x4(short *rs1, vuint64m8_t rs2, vint16m2x4_t vs3, size_t vl);
vuint16m4x2_t __riscv_vundefined_u16m4x2(void);
vuint16m4x2_t __riscv_vcreate_v_u16m4_u16m4x2(vuint16m4_t v0, vuint16m4_t v1);
vuint16m4_t __riscv_vget_v_u16m4x2_u16m4(vuint16m4x2_t src, size_t index);
vuint16m4x2_t __riscv_vset_v_u16m4x2_u16m4(vuint16m4x2_t dest, size_t index, vuint16m4_t value);
vuint16m4x2_t __riscv_vlseg2e16_v_u16m4x2(const unsigned short *rs1, size_t vl);
void __riscv_vsseg2e16_v_u16m4x2(unsigned short *rs1, vuint16m4x2_t vs3, size_t vl);
vuint16m4x2_t __riscv_vlseg2e16ff_v_u16m4x2(const unsigned short *rs1, size_t *new_vl, size_t vl);
vuint16m4x2_t __riscv_vlsseg2e16_v_u16m4x2(const unsigned short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_u16m4x2(unsigned short *rs1, long rs2, vuint16m4x2_t vs3, size_t vl);
vuint16m4x2_t __riscv_vluxseg2ei8_v_u16m4x2(const unsigned short *rs1, vuint8m2_t rs2, size_t vl);
vuint16m4x2_t __riscv_vloxseg2ei8_v_u16m4x2(const unsigned short *rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u16m4x2(unsigned short *rs1, vuint8m2_t rs2, vuint16m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u16m4x2(unsigned short *rs1, vuint8m2_t rs2, vuint16m4x2_t vs3, size_t vl);
vuint16m4x2_t __riscv_vluxseg2ei16_v_u16m4x2(const unsigned short *rs1, vuint16m4_t rs2, size_t vl);
vuint16m4x2_t __riscv_vloxseg2ei16_v_u16m4x2(const unsigned short *rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u16m4x2(unsigned short *rs1, vuint16m4_t rs2, vuint16m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u16m4x2(unsigned short *rs1, vuint16m4_t rs2, vuint16m4x2_t vs3, size_t vl);
vuint16m4x2_t __riscv_vluxseg2ei32_v_u16m4x2(const unsigned short *rs1, vuint32m8_t rs2, size_t vl);
vuint16m4x2_t __riscv_vloxseg2ei32_v_u16m4x2(const unsigned short *rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u16m4x2(unsigned short *rs1, vuint32m8_t rs2, vuint16m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u16m4x2(unsigned short *rs1, vuint32m8_t rs2, vuint16m4x2_t vs3, size_t vl);
vint16m4x2_t __riscv_vundefined_i16m4x2(void);
vint16m4x2_t __riscv_vcreate_v_i16m4_i16m4x2(vint16m4_t v0, vint16m4_t v1);
vint16m4_t __riscv_vget_v_i16m4x2_i16m4(vint16m4x2_t src, size_t index);
vint16m4x2_t __riscv_vset_v_i16m4x2_i16m4(vint16m4x2_t dest, size_t index, vint16m4_t value);
vint16m4x2_t __riscv_vlseg2e16_v_i16m4x2(const short *rs1, size_t vl);
void __riscv_vsseg2e16_v_i16m4x2(short *rs1, vint16m4x2_t vs3, size_t vl);
vint16m4x2_t __riscv_vlseg2e16ff_v_i16m4x2(const short *rs1, size_t *new_vl, size_t vl);
vint16m4x2_t __riscv_vlsseg2e16_v_i16m4x2(const short *rs1, long rs2, size_t vl);
void __riscv_vssseg2e16_v_i16m4x2(short *rs1, long rs2, vint16m4x2_t vs3, size_t vl);
vint16m4x2_t __riscv_vluxseg2ei8_v_i16m4x2(const short *rs1, vuint8m2_t rs2, size_t vl);
vint16m4x2_t __riscv_vloxseg2ei8_v_i16m4x2(const short *rs1, vuint8m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i16m4x2(short *rs1, vuint8m2_t rs2, vint16m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i16m4x2(short *rs1, vuint8m2_t rs2, vint16m4x2_t vs3, size_t vl);
vint16m4x2_t __riscv_vluxseg2ei16_v_i16m4x2(const short *rs1, vuint16m4_t rs2, size_t vl);
vint16m4x2_t __riscv_vloxseg2ei16_v_i16m4x2(const short *rs1, vuint16m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i16m4x2(short *rs1, vuint16m4_t rs2, vint16m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i16m4x2(short *rs1, vuint16m4_t rs2, vint16m4x2_t vs3, size_t vl);
vint16m4x2_t __riscv_vluxseg2ei32_v_i16m4x2(const short *rs1, vuint32m8_t rs2, size_t vl);
vint16m4x2_t __riscv_vloxseg2ei32_v_i16m4x2(const short *rs1, vuint32m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i16m4x2(short *rs1, vuint32m8_t rs2, vint16m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i16m4x2(short *rs1, vuint32m8_t rs2, vint16m4x2_t vs3, size_t vl);
vuint32mf2x2_t __riscv_vundefined_u32mf2x2(void);
vuint32mf2x2_t __riscv_vcreate_v_u32mf2_u32mf2x2(vuint32mf2_t v0, vuint32mf2_t v1);
vuint32mf2_t __riscv_vget_v_u32mf2x2_u32mf2(vuint32mf2x2_t src, size_t index);
vuint32mf2x2_t __riscv_vset_v_u32mf2x2_u32mf2(vuint32mf2x2_t dest, size_t index, vuint32mf2_t value);
vuint32mf2x2_t __riscv_vlseg2e32_v_u32mf2x2(const unsigned int *rs1, size_t vl);
void __riscv_vsseg2e32_v_u32mf2x2(unsigned int *rs1, vuint32mf2x2_t vs3, size_t vl);
vuint32mf2x2_t __riscv_vlseg2e32ff_v_u32mf2x2(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32mf2x2_t __riscv_vlsseg2e32_v_u32mf2x2(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_u32mf2x2(unsigned int *rs1, long rs2, vuint32mf2x2_t vs3, size_t vl);
vuint32mf2x2_t __riscv_vluxseg2ei8_v_u32mf2x2(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
vuint32mf2x2_t __riscv_vloxseg2ei8_v_u32mf2x2(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u32mf2x2(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u32mf2x2(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x2_t vs3, size_t vl);
vuint32mf2x2_t __riscv_vluxseg2ei16_v_u32mf2x2(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
vuint32mf2x2_t __riscv_vloxseg2ei16_v_u32mf2x2(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u32mf2x2(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u32mf2x2(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x2_t vs3, size_t vl);
vuint32mf2x2_t __riscv_vluxseg2ei32_v_u32mf2x2(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
vuint32mf2x2_t __riscv_vloxseg2ei32_v_u32mf2x2(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u32mf2x2(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u32mf2x2(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x2_t vs3, size_t vl);
vuint32mf2x2_t __riscv_vluxseg2ei64_v_u32mf2x2(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
vuint32mf2x2_t __riscv_vloxseg2ei64_v_u32mf2x2(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u32mf2x2(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u32mf2x2(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x2_t vs3, size_t vl);
vint32mf2x2_t __riscv_vundefined_i32mf2x2(void);
vint32mf2x2_t __riscv_vcreate_v_i32mf2_i32mf2x2(vint32mf2_t v0, vint32mf2_t v1);
vint32mf2_t __riscv_vget_v_i32mf2x2_i32mf2(vint32mf2x2_t src, size_t index);
vint32mf2x2_t __riscv_vset_v_i32mf2x2_i32mf2(vint32mf2x2_t dest, size_t index, vint32mf2_t value);
vint32mf2x2_t __riscv_vlseg2e32_v_i32mf2x2(const int *rs1, size_t vl);
void __riscv_vsseg2e32_v_i32mf2x2(int *rs1, vint32mf2x2_t vs3, size_t vl);
vint32mf2x2_t __riscv_vlseg2e32ff_v_i32mf2x2(const int *rs1, size_t *new_vl, size_t vl);
vint32mf2x2_t __riscv_vlsseg2e32_v_i32mf2x2(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_i32mf2x2(int *rs1, long rs2, vint32mf2x2_t vs3, size_t vl);
vint32mf2x2_t __riscv_vluxseg2ei8_v_i32mf2x2(const int *rs1, vuint8mf8_t rs2, size_t vl);
vint32mf2x2_t __riscv_vloxseg2ei8_v_i32mf2x2(const int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i32mf2x2(int *rs1, vuint8mf8_t rs2, vint32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i32mf2x2(int *rs1, vuint8mf8_t rs2, vint32mf2x2_t vs3, size_t vl);
vint32mf2x2_t __riscv_vluxseg2ei16_v_i32mf2x2(const int *rs1, vuint16mf4_t rs2, size_t vl);
vint32mf2x2_t __riscv_vloxseg2ei16_v_i32mf2x2(const int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i32mf2x2(int *rs1, vuint16mf4_t rs2, vint32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i32mf2x2(int *rs1, vuint16mf4_t rs2, vint32mf2x2_t vs3, size_t vl);
vint32mf2x2_t __riscv_vluxseg2ei32_v_i32mf2x2(const int *rs1, vuint32mf2_t rs2, size_t vl);
vint32mf2x2_t __riscv_vloxseg2ei32_v_i32mf2x2(const int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i32mf2x2(int *rs1, vuint32mf2_t rs2, vint32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i32mf2x2(int *rs1, vuint32mf2_t rs2, vint32mf2x2_t vs3, size_t vl);
vint32mf2x2_t __riscv_vluxseg2ei64_v_i32mf2x2(const int *rs1, vuint64m1_t rs2, size_t vl);
vint32mf2x2_t __riscv_vloxseg2ei64_v_i32mf2x2(const int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i32mf2x2(int *rs1, vuint64m1_t rs2, vint32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i32mf2x2(int *rs1, vuint64m1_t rs2, vint32mf2x2_t vs3, size_t vl);
vfloat32mf2x2_t __riscv_vundefined_f32mf2x2(void);
vfloat32mf2x2_t __riscv_vcreate_v_f32mf2_f32mf2x2(vfloat32mf2_t v0, vfloat32mf2_t v1);
vfloat32mf2_t __riscv_vget_v_f32mf2x2_f32mf2(vfloat32mf2x2_t src, size_t index);
vfloat32mf2x2_t __riscv_vset_v_f32mf2x2_f32mf2(vfloat32mf2x2_t dest, size_t index, vfloat32mf2_t value);
vfloat32mf2x2_t __riscv_vlseg2e32_v_f32mf2x2(const float *rs1, size_t vl);
void __riscv_vsseg2e32_v_f32mf2x2(float *rs1, vfloat32mf2x2_t vs3, size_t vl);
vfloat32mf2x2_t __riscv_vlseg2e32ff_v_f32mf2x2(const float *rs1, size_t *new_vl, size_t vl);
vfloat32mf2x2_t __riscv_vlsseg2e32_v_f32mf2x2(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_f32mf2x2(float *rs1, long rs2, vfloat32mf2x2_t vs3, size_t vl);
vfloat32mf2x2_t __riscv_vluxseg2ei8_v_f32mf2x2(const float *rs1, vuint8mf8_t rs2, size_t vl);
vfloat32mf2x2_t __riscv_vloxseg2ei8_v_f32mf2x2(const float *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_f32mf2x2(float *rs1, vuint8mf8_t rs2, vfloat32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_f32mf2x2(float *rs1, vuint8mf8_t rs2, vfloat32mf2x2_t vs3, size_t vl);
vfloat32mf2x2_t __riscv_vluxseg2ei16_v_f32mf2x2(const float *rs1, vuint16mf4_t rs2, size_t vl);
vfloat32mf2x2_t __riscv_vloxseg2ei16_v_f32mf2x2(const float *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_f32mf2x2(float *rs1, vuint16mf4_t rs2, vfloat32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_f32mf2x2(float *rs1, vuint16mf4_t rs2, vfloat32mf2x2_t vs3, size_t vl);
vfloat32mf2x2_t __riscv_vluxseg2ei32_v_f32mf2x2(const float *rs1, vuint32mf2_t rs2, size_t vl);
vfloat32mf2x2_t __riscv_vloxseg2ei32_v_f32mf2x2(const float *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_f32mf2x2(float *rs1, vuint32mf2_t rs2, vfloat32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_f32mf2x2(float *rs1, vuint32mf2_t rs2, vfloat32mf2x2_t vs3, size_t vl);
vfloat32mf2x2_t __riscv_vluxseg2ei64_v_f32mf2x2(const float *rs1, vuint64m1_t rs2, size_t vl);
vfloat32mf2x2_t __riscv_vloxseg2ei64_v_f32mf2x2(const float *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_f32mf2x2(float *rs1, vuint64m1_t rs2, vfloat32mf2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_f32mf2x2(float *rs1, vuint64m1_t rs2, vfloat32mf2x2_t vs3, size_t vl);
vuint32mf2x3_t __riscv_vundefined_u32mf2x3(void);
vuint32mf2x3_t __riscv_vcreate_v_u32mf2_u32mf2x3(vuint32mf2_t v0, vuint32mf2_t v1, vuint32mf2_t v2);
vuint32mf2_t __riscv_vget_v_u32mf2x3_u32mf2(vuint32mf2x3_t src, size_t index);
vuint32mf2x3_t __riscv_vset_v_u32mf2x3_u32mf2(vuint32mf2x3_t dest, size_t index, vuint32mf2_t value);
vuint32mf2x3_t __riscv_vlseg3e32_v_u32mf2x3(const unsigned int *rs1, size_t vl);
void __riscv_vsseg3e32_v_u32mf2x3(unsigned int *rs1, vuint32mf2x3_t vs3, size_t vl);
vuint32mf2x3_t __riscv_vlseg3e32ff_v_u32mf2x3(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32mf2x3_t __riscv_vlsseg3e32_v_u32mf2x3(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_u32mf2x3(unsigned int *rs1, long rs2, vuint32mf2x3_t vs3, size_t vl);
vuint32mf2x3_t __riscv_vluxseg3ei8_v_u32mf2x3(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
vuint32mf2x3_t __riscv_vloxseg3ei8_v_u32mf2x3(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u32mf2x3(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u32mf2x3(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x3_t vs3, size_t vl);
vuint32mf2x3_t __riscv_vluxseg3ei16_v_u32mf2x3(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
vuint32mf2x3_t __riscv_vloxseg3ei16_v_u32mf2x3(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u32mf2x3(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u32mf2x3(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x3_t vs3, size_t vl);
vuint32mf2x3_t __riscv_vluxseg3ei32_v_u32mf2x3(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
vuint32mf2x3_t __riscv_vloxseg3ei32_v_u32mf2x3(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u32mf2x3(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u32mf2x3(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x3_t vs3, size_t vl);
vuint32mf2x3_t __riscv_vluxseg3ei64_v_u32mf2x3(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
vuint32mf2x3_t __riscv_vloxseg3ei64_v_u32mf2x3(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u32mf2x3(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u32mf2x3(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x3_t vs3, size_t vl);
vint32mf2x3_t __riscv_vundefined_i32mf2x3(void);
vint32mf2x3_t __riscv_vcreate_v_i32mf2_i32mf2x3(vint32mf2_t v0, vint32mf2_t v1, vint32mf2_t v2);
vint32mf2_t __riscv_vget_v_i32mf2x3_i32mf2(vint32mf2x3_t src, size_t index);
vint32mf2x3_t __riscv_vset_v_i32mf2x3_i32mf2(vint32mf2x3_t dest, size_t index, vint32mf2_t value);
vint32mf2x3_t __riscv_vlseg3e32_v_i32mf2x3(const int *rs1, size_t vl);
void __riscv_vsseg3e32_v_i32mf2x3(int *rs1, vint32mf2x3_t vs3, size_t vl);
vint32mf2x3_t __riscv_vlseg3e32ff_v_i32mf2x3(const int *rs1, size_t *new_vl, size_t vl);
vint32mf2x3_t __riscv_vlsseg3e32_v_i32mf2x3(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_i32mf2x3(int *rs1, long rs2, vint32mf2x3_t vs3, size_t vl);
vint32mf2x3_t __riscv_vluxseg3ei8_v_i32mf2x3(const int *rs1, vuint8mf8_t rs2, size_t vl);
vint32mf2x3_t __riscv_vloxseg3ei8_v_i32mf2x3(const int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i32mf2x3(int *rs1, vuint8mf8_t rs2, vint32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i32mf2x3(int *rs1, vuint8mf8_t rs2, vint32mf2x3_t vs3, size_t vl);
vint32mf2x3_t __riscv_vluxseg3ei16_v_i32mf2x3(const int *rs1, vuint16mf4_t rs2, size_t vl);
vint32mf2x3_t __riscv_vloxseg3ei16_v_i32mf2x3(const int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i32mf2x3(int *rs1, vuint16mf4_t rs2, vint32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i32mf2x3(int *rs1, vuint16mf4_t rs2, vint32mf2x3_t vs3, size_t vl);
vint32mf2x3_t __riscv_vluxseg3ei32_v_i32mf2x3(const int *rs1, vuint32mf2_t rs2, size_t vl);
vint32mf2x3_t __riscv_vloxseg3ei32_v_i32mf2x3(const int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i32mf2x3(int *rs1, vuint32mf2_t rs2, vint32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i32mf2x3(int *rs1, vuint32mf2_t rs2, vint32mf2x3_t vs3, size_t vl);
vint32mf2x3_t __riscv_vluxseg3ei64_v_i32mf2x3(const int *rs1, vuint64m1_t rs2, size_t vl);
vint32mf2x3_t __riscv_vloxseg3ei64_v_i32mf2x3(const int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i32mf2x3(int *rs1, vuint64m1_t rs2, vint32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i32mf2x3(int *rs1, vuint64m1_t rs2, vint32mf2x3_t vs3, size_t vl);
vfloat32mf2x3_t __riscv_vundefined_f32mf2x3(void);
vfloat32mf2x3_t __riscv_vcreate_v_f32mf2_f32mf2x3(vfloat32mf2_t v0, vfloat32mf2_t v1, vfloat32mf2_t v2);
vfloat32mf2_t __riscv_vget_v_f32mf2x3_f32mf2(vfloat32mf2x3_t src, size_t index);
vfloat32mf2x3_t __riscv_vset_v_f32mf2x3_f32mf2(vfloat32mf2x3_t dest, size_t index, vfloat32mf2_t value);
vfloat32mf2x3_t __riscv_vlseg3e32_v_f32mf2x3(const float *rs1, size_t vl);
void __riscv_vsseg3e32_v_f32mf2x3(float *rs1, vfloat32mf2x3_t vs3, size_t vl);
vfloat32mf2x3_t __riscv_vlseg3e32ff_v_f32mf2x3(const float *rs1, size_t *new_vl, size_t vl);
vfloat32mf2x3_t __riscv_vlsseg3e32_v_f32mf2x3(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_f32mf2x3(float *rs1, long rs2, vfloat32mf2x3_t vs3, size_t vl);
vfloat32mf2x3_t __riscv_vluxseg3ei8_v_f32mf2x3(const float *rs1, vuint8mf8_t rs2, size_t vl);
vfloat32mf2x3_t __riscv_vloxseg3ei8_v_f32mf2x3(const float *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_f32mf2x3(float *rs1, vuint8mf8_t rs2, vfloat32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_f32mf2x3(float *rs1, vuint8mf8_t rs2, vfloat32mf2x3_t vs3, size_t vl);
vfloat32mf2x3_t __riscv_vluxseg3ei16_v_f32mf2x3(const float *rs1, vuint16mf4_t rs2, size_t vl);
vfloat32mf2x3_t __riscv_vloxseg3ei16_v_f32mf2x3(const float *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_f32mf2x3(float *rs1, vuint16mf4_t rs2, vfloat32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_f32mf2x3(float *rs1, vuint16mf4_t rs2, vfloat32mf2x3_t vs3, size_t vl);
vfloat32mf2x3_t __riscv_vluxseg3ei32_v_f32mf2x3(const float *rs1, vuint32mf2_t rs2, size_t vl);
vfloat32mf2x3_t __riscv_vloxseg3ei32_v_f32mf2x3(const float *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_f32mf2x3(float *rs1, vuint32mf2_t rs2, vfloat32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_f32mf2x3(float *rs1, vuint32mf2_t rs2, vfloat32mf2x3_t vs3, size_t vl);
vfloat32mf2x3_t __riscv_vluxseg3ei64_v_f32mf2x3(const float *rs1, vuint64m1_t rs2, size_t vl);
vfloat32mf2x3_t __riscv_vloxseg3ei64_v_f32mf2x3(const float *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_f32mf2x3(float *rs1, vuint64m1_t rs2, vfloat32mf2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_f32mf2x3(float *rs1, vuint64m1_t rs2, vfloat32mf2x3_t vs3, size_t vl);
vuint32mf2x4_t __riscv_vundefined_u32mf2x4(void);
vuint32mf2x4_t __riscv_vcreate_v_u32mf2_u32mf2x4(vuint32mf2_t v0, vuint32mf2_t v1, vuint32mf2_t v2, vuint32mf2_t v3);
vuint32mf2_t __riscv_vget_v_u32mf2x4_u32mf2(vuint32mf2x4_t src, size_t index);
vuint32mf2x4_t __riscv_vset_v_u32mf2x4_u32mf2(vuint32mf2x4_t dest, size_t index, vuint32mf2_t value);
vuint32mf2x4_t __riscv_vlseg4e32_v_u32mf2x4(const unsigned int *rs1, size_t vl);
void __riscv_vsseg4e32_v_u32mf2x4(unsigned int *rs1, vuint32mf2x4_t vs3, size_t vl);
vuint32mf2x4_t __riscv_vlseg4e32ff_v_u32mf2x4(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32mf2x4_t __riscv_vlsseg4e32_v_u32mf2x4(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_u32mf2x4(unsigned int *rs1, long rs2, vuint32mf2x4_t vs3, size_t vl);
vuint32mf2x4_t __riscv_vluxseg4ei8_v_u32mf2x4(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
vuint32mf2x4_t __riscv_vloxseg4ei8_v_u32mf2x4(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u32mf2x4(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u32mf2x4(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x4_t vs3, size_t vl);
vuint32mf2x4_t __riscv_vluxseg4ei16_v_u32mf2x4(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
vuint32mf2x4_t __riscv_vloxseg4ei16_v_u32mf2x4(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u32mf2x4(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u32mf2x4(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x4_t vs3, size_t vl);
vuint32mf2x4_t __riscv_vluxseg4ei32_v_u32mf2x4(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
vuint32mf2x4_t __riscv_vloxseg4ei32_v_u32mf2x4(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u32mf2x4(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u32mf2x4(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x4_t vs3, size_t vl);
vuint32mf2x4_t __riscv_vluxseg4ei64_v_u32mf2x4(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
vuint32mf2x4_t __riscv_vloxseg4ei64_v_u32mf2x4(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u32mf2x4(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u32mf2x4(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x4_t vs3, size_t vl);
vint32mf2x4_t __riscv_vundefined_i32mf2x4(void);
vint32mf2x4_t __riscv_vcreate_v_i32mf2_i32mf2x4(vint32mf2_t v0, vint32mf2_t v1, vint32mf2_t v2, vint32mf2_t v3);
vint32mf2_t __riscv_vget_v_i32mf2x4_i32mf2(vint32mf2x4_t src, size_t index);
vint32mf2x4_t __riscv_vset_v_i32mf2x4_i32mf2(vint32mf2x4_t dest, size_t index, vint32mf2_t value);
vint32mf2x4_t __riscv_vlseg4e32_v_i32mf2x4(const int *rs1, size_t vl);
void __riscv_vsseg4e32_v_i32mf2x4(int *rs1, vint32mf2x4_t vs3, size_t vl);
vint32mf2x4_t __riscv_vlseg4e32ff_v_i32mf2x4(const int *rs1, size_t *new_vl, size_t vl);
vint32mf2x4_t __riscv_vlsseg4e32_v_i32mf2x4(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_i32mf2x4(int *rs1, long rs2, vint32mf2x4_t vs3, size_t vl);
vint32mf2x4_t __riscv_vluxseg4ei8_v_i32mf2x4(const int *rs1, vuint8mf8_t rs2, size_t vl);
vint32mf2x4_t __riscv_vloxseg4ei8_v_i32mf2x4(const int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i32mf2x4(int *rs1, vuint8mf8_t rs2, vint32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i32mf2x4(int *rs1, vuint8mf8_t rs2, vint32mf2x4_t vs3, size_t vl);
vint32mf2x4_t __riscv_vluxseg4ei16_v_i32mf2x4(const int *rs1, vuint16mf4_t rs2, size_t vl);
vint32mf2x4_t __riscv_vloxseg4ei16_v_i32mf2x4(const int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i32mf2x4(int *rs1, vuint16mf4_t rs2, vint32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i32mf2x4(int *rs1, vuint16mf4_t rs2, vint32mf2x4_t vs3, size_t vl);
vint32mf2x4_t __riscv_vluxseg4ei32_v_i32mf2x4(const int *rs1, vuint32mf2_t rs2, size_t vl);
vint32mf2x4_t __riscv_vloxseg4ei32_v_i32mf2x4(const int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i32mf2x4(int *rs1, vuint32mf2_t rs2, vint32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i32mf2x4(int *rs1, vuint32mf2_t rs2, vint32mf2x4_t vs3, size_t vl);
vint32mf2x4_t __riscv_vluxseg4ei64_v_i32mf2x4(const int *rs1, vuint64m1_t rs2, size_t vl);
vint32mf2x4_t __riscv_vloxseg4ei64_v_i32mf2x4(const int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i32mf2x4(int *rs1, vuint64m1_t rs2, vint32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i32mf2x4(int *rs1, vuint64m1_t rs2, vint32mf2x4_t vs3, size_t vl);
vfloat32mf2x4_t __riscv_vundefined_f32mf2x4(void);
vfloat32mf2x4_t __riscv_vcreate_v_f32mf2_f32mf2x4(vfloat32mf2_t v0, vfloat32mf2_t v1, vfloat32mf2_t v2, vfloat32mf2_t v3);
vfloat32mf2_t __riscv_vget_v_f32mf2x4_f32mf2(vfloat32mf2x4_t src, size_t index);
vfloat32mf2x4_t __riscv_vset_v_f32mf2x4_f32mf2(vfloat32mf2x4_t dest, size_t index, vfloat32mf2_t value);
vfloat32mf2x4_t __riscv_vlseg4e32_v_f32mf2x4(const float *rs1, size_t vl);
void __riscv_vsseg4e32_v_f32mf2x4(float *rs1, vfloat32mf2x4_t vs3, size_t vl);
vfloat32mf2x4_t __riscv_vlseg4e32ff_v_f32mf2x4(const float *rs1, size_t *new_vl, size_t vl);
vfloat32mf2x4_t __riscv_vlsseg4e32_v_f32mf2x4(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_f32mf2x4(float *rs1, long rs2, vfloat32mf2x4_t vs3, size_t vl);
vfloat32mf2x4_t __riscv_vluxseg4ei8_v_f32mf2x4(const float *rs1, vuint8mf8_t rs2, size_t vl);
vfloat32mf2x4_t __riscv_vloxseg4ei8_v_f32mf2x4(const float *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_f32mf2x4(float *rs1, vuint8mf8_t rs2, vfloat32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_f32mf2x4(float *rs1, vuint8mf8_t rs2, vfloat32mf2x4_t vs3, size_t vl);
vfloat32mf2x4_t __riscv_vluxseg4ei16_v_f32mf2x4(const float *rs1, vuint16mf4_t rs2, size_t vl);
vfloat32mf2x4_t __riscv_vloxseg4ei16_v_f32mf2x4(const float *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_f32mf2x4(float *rs1, vuint16mf4_t rs2, vfloat32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_f32mf2x4(float *rs1, vuint16mf4_t rs2, vfloat32mf2x4_t vs3, size_t vl);
vfloat32mf2x4_t __riscv_vluxseg4ei32_v_f32mf2x4(const float *rs1, vuint32mf2_t rs2, size_t vl);
vfloat32mf2x4_t __riscv_vloxseg4ei32_v_f32mf2x4(const float *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_f32mf2x4(float *rs1, vuint32mf2_t rs2, vfloat32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_f32mf2x4(float *rs1, vuint32mf2_t rs2, vfloat32mf2x4_t vs3, size_t vl);
vfloat32mf2x4_t __riscv_vluxseg4ei64_v_f32mf2x4(const float *rs1, vuint64m1_t rs2, size_t vl);
vfloat32mf2x4_t __riscv_vloxseg4ei64_v_f32mf2x4(const float *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_f32mf2x4(float *rs1, vuint64m1_t rs2, vfloat32mf2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_f32mf2x4(float *rs1, vuint64m1_t rs2, vfloat32mf2x4_t vs3, size_t vl);
vuint32mf2x5_t __riscv_vundefined_u32mf2x5(void);
vuint32mf2x5_t __riscv_vcreate_v_u32mf2_u32mf2x5(vuint32mf2_t v0, vuint32mf2_t v1, vuint32mf2_t v2, vuint32mf2_t v3, vuint32mf2_t v4);
vuint32mf2_t __riscv_vget_v_u32mf2x5_u32mf2(vuint32mf2x5_t src, size_t index);
vuint32mf2x5_t __riscv_vset_v_u32mf2x5_u32mf2(vuint32mf2x5_t dest, size_t index, vuint32mf2_t value);
vuint32mf2x5_t __riscv_vlseg5e32_v_u32mf2x5(const unsigned int *rs1, size_t vl);
void __riscv_vsseg5e32_v_u32mf2x5(unsigned int *rs1, vuint32mf2x5_t vs3, size_t vl);
vuint32mf2x5_t __riscv_vlseg5e32ff_v_u32mf2x5(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32mf2x5_t __riscv_vlsseg5e32_v_u32mf2x5(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg5e32_v_u32mf2x5(unsigned int *rs1, long rs2, vuint32mf2x5_t vs3, size_t vl);
vuint32mf2x5_t __riscv_vluxseg5ei8_v_u32mf2x5(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
vuint32mf2x5_t __riscv_vloxseg5ei8_v_u32mf2x5(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u32mf2x5(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u32mf2x5(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x5_t vs3, size_t vl);
vuint32mf2x5_t __riscv_vluxseg5ei16_v_u32mf2x5(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
vuint32mf2x5_t __riscv_vloxseg5ei16_v_u32mf2x5(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u32mf2x5(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u32mf2x5(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x5_t vs3, size_t vl);
vuint32mf2x5_t __riscv_vluxseg5ei32_v_u32mf2x5(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
vuint32mf2x5_t __riscv_vloxseg5ei32_v_u32mf2x5(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u32mf2x5(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u32mf2x5(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x5_t vs3, size_t vl);
vuint32mf2x5_t __riscv_vluxseg5ei64_v_u32mf2x5(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
vuint32mf2x5_t __riscv_vloxseg5ei64_v_u32mf2x5(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u32mf2x5(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u32mf2x5(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x5_t vs3, size_t vl);
vint32mf2x5_t __riscv_vundefined_i32mf2x5(void);
vint32mf2x5_t __riscv_vcreate_v_i32mf2_i32mf2x5(vint32mf2_t v0, vint32mf2_t v1, vint32mf2_t v2, vint32mf2_t v3, vint32mf2_t v4);
vint32mf2_t __riscv_vget_v_i32mf2x5_i32mf2(vint32mf2x5_t src, size_t index);
vint32mf2x5_t __riscv_vset_v_i32mf2x5_i32mf2(vint32mf2x5_t dest, size_t index, vint32mf2_t value);
vint32mf2x5_t __riscv_vlseg5e32_v_i32mf2x5(const int *rs1, size_t vl);
void __riscv_vsseg5e32_v_i32mf2x5(int *rs1, vint32mf2x5_t vs3, size_t vl);
vint32mf2x5_t __riscv_vlseg5e32ff_v_i32mf2x5(const int *rs1, size_t *new_vl, size_t vl);
vint32mf2x5_t __riscv_vlsseg5e32_v_i32mf2x5(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg5e32_v_i32mf2x5(int *rs1, long rs2, vint32mf2x5_t vs3, size_t vl);
vint32mf2x5_t __riscv_vluxseg5ei8_v_i32mf2x5(const int *rs1, vuint8mf8_t rs2, size_t vl);
vint32mf2x5_t __riscv_vloxseg5ei8_v_i32mf2x5(const int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i32mf2x5(int *rs1, vuint8mf8_t rs2, vint32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i32mf2x5(int *rs1, vuint8mf8_t rs2, vint32mf2x5_t vs3, size_t vl);
vint32mf2x5_t __riscv_vluxseg5ei16_v_i32mf2x5(const int *rs1, vuint16mf4_t rs2, size_t vl);
vint32mf2x5_t __riscv_vloxseg5ei16_v_i32mf2x5(const int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i32mf2x5(int *rs1, vuint16mf4_t rs2, vint32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i32mf2x5(int *rs1, vuint16mf4_t rs2, vint32mf2x5_t vs3, size_t vl);
vint32mf2x5_t __riscv_vluxseg5ei32_v_i32mf2x5(const int *rs1, vuint32mf2_t rs2, size_t vl);
vint32mf2x5_t __riscv_vloxseg5ei32_v_i32mf2x5(const int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i32mf2x5(int *rs1, vuint32mf2_t rs2, vint32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i32mf2x5(int *rs1, vuint32mf2_t rs2, vint32mf2x5_t vs3, size_t vl);
vint32mf2x5_t __riscv_vluxseg5ei64_v_i32mf2x5(const int *rs1, vuint64m1_t rs2, size_t vl);
vint32mf2x5_t __riscv_vloxseg5ei64_v_i32mf2x5(const int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i32mf2x5(int *rs1, vuint64m1_t rs2, vint32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i32mf2x5(int *rs1, vuint64m1_t rs2, vint32mf2x5_t vs3, size_t vl);
vfloat32mf2x5_t __riscv_vundefined_f32mf2x5(void);
vfloat32mf2x5_t __riscv_vcreate_v_f32mf2_f32mf2x5(vfloat32mf2_t v0, vfloat32mf2_t v1, vfloat32mf2_t v2, vfloat32mf2_t v3, vfloat32mf2_t v4);
vfloat32mf2_t __riscv_vget_v_f32mf2x5_f32mf2(vfloat32mf2x5_t src, size_t index);
vfloat32mf2x5_t __riscv_vset_v_f32mf2x5_f32mf2(vfloat32mf2x5_t dest, size_t index, vfloat32mf2_t value);
vfloat32mf2x5_t __riscv_vlseg5e32_v_f32mf2x5(const float *rs1, size_t vl);
void __riscv_vsseg5e32_v_f32mf2x5(float *rs1, vfloat32mf2x5_t vs3, size_t vl);
vfloat32mf2x5_t __riscv_vlseg5e32ff_v_f32mf2x5(const float *rs1, size_t *new_vl, size_t vl);
vfloat32mf2x5_t __riscv_vlsseg5e32_v_f32mf2x5(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg5e32_v_f32mf2x5(float *rs1, long rs2, vfloat32mf2x5_t vs3, size_t vl);
vfloat32mf2x5_t __riscv_vluxseg5ei8_v_f32mf2x5(const float *rs1, vuint8mf8_t rs2, size_t vl);
vfloat32mf2x5_t __riscv_vloxseg5ei8_v_f32mf2x5(const float *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_f32mf2x5(float *rs1, vuint8mf8_t rs2, vfloat32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_f32mf2x5(float *rs1, vuint8mf8_t rs2, vfloat32mf2x5_t vs3, size_t vl);
vfloat32mf2x5_t __riscv_vluxseg5ei16_v_f32mf2x5(const float *rs1, vuint16mf4_t rs2, size_t vl);
vfloat32mf2x5_t __riscv_vloxseg5ei16_v_f32mf2x5(const float *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_f32mf2x5(float *rs1, vuint16mf4_t rs2, vfloat32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_f32mf2x5(float *rs1, vuint16mf4_t rs2, vfloat32mf2x5_t vs3, size_t vl);
vfloat32mf2x5_t __riscv_vluxseg5ei32_v_f32mf2x5(const float *rs1, vuint32mf2_t rs2, size_t vl);
vfloat32mf2x5_t __riscv_vloxseg5ei32_v_f32mf2x5(const float *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_f32mf2x5(float *rs1, vuint32mf2_t rs2, vfloat32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_f32mf2x5(float *rs1, vuint32mf2_t rs2, vfloat32mf2x5_t vs3, size_t vl);
vfloat32mf2x5_t __riscv_vluxseg5ei64_v_f32mf2x5(const float *rs1, vuint64m1_t rs2, size_t vl);
vfloat32mf2x5_t __riscv_vloxseg5ei64_v_f32mf2x5(const float *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_f32mf2x5(float *rs1, vuint64m1_t rs2, vfloat32mf2x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_f32mf2x5(float *rs1, vuint64m1_t rs2, vfloat32mf2x5_t vs3, size_t vl);
vuint32mf2x6_t __riscv_vundefined_u32mf2x6(void);
vuint32mf2x6_t __riscv_vcreate_v_u32mf2_u32mf2x6(vuint32mf2_t v0, vuint32mf2_t v1, vuint32mf2_t v2, vuint32mf2_t v3, vuint32mf2_t v4, vuint32mf2_t v5);
vuint32mf2_t __riscv_vget_v_u32mf2x6_u32mf2(vuint32mf2x6_t src, size_t index);
vuint32mf2x6_t __riscv_vset_v_u32mf2x6_u32mf2(vuint32mf2x6_t dest, size_t index, vuint32mf2_t value);
vuint32mf2x6_t __riscv_vlseg6e32_v_u32mf2x6(const unsigned int *rs1, size_t vl);
void __riscv_vsseg6e32_v_u32mf2x6(unsigned int *rs1, vuint32mf2x6_t vs3, size_t vl);
vuint32mf2x6_t __riscv_vlseg6e32ff_v_u32mf2x6(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32mf2x6_t __riscv_vlsseg6e32_v_u32mf2x6(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg6e32_v_u32mf2x6(unsigned int *rs1, long rs2, vuint32mf2x6_t vs3, size_t vl);
vuint32mf2x6_t __riscv_vluxseg6ei8_v_u32mf2x6(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
vuint32mf2x6_t __riscv_vloxseg6ei8_v_u32mf2x6(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u32mf2x6(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u32mf2x6(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x6_t vs3, size_t vl);
vuint32mf2x6_t __riscv_vluxseg6ei16_v_u32mf2x6(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
vuint32mf2x6_t __riscv_vloxseg6ei16_v_u32mf2x6(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u32mf2x6(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u32mf2x6(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x6_t vs3, size_t vl);
vuint32mf2x6_t __riscv_vluxseg6ei32_v_u32mf2x6(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
vuint32mf2x6_t __riscv_vloxseg6ei32_v_u32mf2x6(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u32mf2x6(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u32mf2x6(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x6_t vs3, size_t vl);
vuint32mf2x6_t __riscv_vluxseg6ei64_v_u32mf2x6(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
vuint32mf2x6_t __riscv_vloxseg6ei64_v_u32mf2x6(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u32mf2x6(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u32mf2x6(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x6_t vs3, size_t vl);
vint32mf2x6_t __riscv_vundefined_i32mf2x6(void);
vint32mf2x6_t __riscv_vcreate_v_i32mf2_i32mf2x6(vint32mf2_t v0, vint32mf2_t v1, vint32mf2_t v2, vint32mf2_t v3, vint32mf2_t v4, vint32mf2_t v5);
vint32mf2_t __riscv_vget_v_i32mf2x6_i32mf2(vint32mf2x6_t src, size_t index);
vint32mf2x6_t __riscv_vset_v_i32mf2x6_i32mf2(vint32mf2x6_t dest, size_t index, vint32mf2_t value);
vint32mf2x6_t __riscv_vlseg6e32_v_i32mf2x6(const int *rs1, size_t vl);
void __riscv_vsseg6e32_v_i32mf2x6(int *rs1, vint32mf2x6_t vs3, size_t vl);
vint32mf2x6_t __riscv_vlseg6e32ff_v_i32mf2x6(const int *rs1, size_t *new_vl, size_t vl);
vint32mf2x6_t __riscv_vlsseg6e32_v_i32mf2x6(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg6e32_v_i32mf2x6(int *rs1, long rs2, vint32mf2x6_t vs3, size_t vl);
vint32mf2x6_t __riscv_vluxseg6ei8_v_i32mf2x6(const int *rs1, vuint8mf8_t rs2, size_t vl);
vint32mf2x6_t __riscv_vloxseg6ei8_v_i32mf2x6(const int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i32mf2x6(int *rs1, vuint8mf8_t rs2, vint32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i32mf2x6(int *rs1, vuint8mf8_t rs2, vint32mf2x6_t vs3, size_t vl);
vint32mf2x6_t __riscv_vluxseg6ei16_v_i32mf2x6(const int *rs1, vuint16mf4_t rs2, size_t vl);
vint32mf2x6_t __riscv_vloxseg6ei16_v_i32mf2x6(const int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i32mf2x6(int *rs1, vuint16mf4_t rs2, vint32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i32mf2x6(int *rs1, vuint16mf4_t rs2, vint32mf2x6_t vs3, size_t vl);
vint32mf2x6_t __riscv_vluxseg6ei32_v_i32mf2x6(const int *rs1, vuint32mf2_t rs2, size_t vl);
vint32mf2x6_t __riscv_vloxseg6ei32_v_i32mf2x6(const int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i32mf2x6(int *rs1, vuint32mf2_t rs2, vint32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i32mf2x6(int *rs1, vuint32mf2_t rs2, vint32mf2x6_t vs3, size_t vl);
vint32mf2x6_t __riscv_vluxseg6ei64_v_i32mf2x6(const int *rs1, vuint64m1_t rs2, size_t vl);
vint32mf2x6_t __riscv_vloxseg6ei64_v_i32mf2x6(const int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i32mf2x6(int *rs1, vuint64m1_t rs2, vint32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i32mf2x6(int *rs1, vuint64m1_t rs2, vint32mf2x6_t vs3, size_t vl);
vfloat32mf2x6_t __riscv_vundefined_f32mf2x6(void);
vfloat32mf2x6_t __riscv_vcreate_v_f32mf2_f32mf2x6(vfloat32mf2_t v0, vfloat32mf2_t v1, vfloat32mf2_t v2, vfloat32mf2_t v3, vfloat32mf2_t v4, vfloat32mf2_t v5);
vfloat32mf2_t __riscv_vget_v_f32mf2x6_f32mf2(vfloat32mf2x6_t src, size_t index);
vfloat32mf2x6_t __riscv_vset_v_f32mf2x6_f32mf2(vfloat32mf2x6_t dest, size_t index, vfloat32mf2_t value);
vfloat32mf2x6_t __riscv_vlseg6e32_v_f32mf2x6(const float *rs1, size_t vl);
void __riscv_vsseg6e32_v_f32mf2x6(float *rs1, vfloat32mf2x6_t vs3, size_t vl);
vfloat32mf2x6_t __riscv_vlseg6e32ff_v_f32mf2x6(const float *rs1, size_t *new_vl, size_t vl);
vfloat32mf2x6_t __riscv_vlsseg6e32_v_f32mf2x6(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg6e32_v_f32mf2x6(float *rs1, long rs2, vfloat32mf2x6_t vs3, size_t vl);
vfloat32mf2x6_t __riscv_vluxseg6ei8_v_f32mf2x6(const float *rs1, vuint8mf8_t rs2, size_t vl);
vfloat32mf2x6_t __riscv_vloxseg6ei8_v_f32mf2x6(const float *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_f32mf2x6(float *rs1, vuint8mf8_t rs2, vfloat32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_f32mf2x6(float *rs1, vuint8mf8_t rs2, vfloat32mf2x6_t vs3, size_t vl);
vfloat32mf2x6_t __riscv_vluxseg6ei16_v_f32mf2x6(const float *rs1, vuint16mf4_t rs2, size_t vl);
vfloat32mf2x6_t __riscv_vloxseg6ei16_v_f32mf2x6(const float *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_f32mf2x6(float *rs1, vuint16mf4_t rs2, vfloat32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_f32mf2x6(float *rs1, vuint16mf4_t rs2, vfloat32mf2x6_t vs3, size_t vl);
vfloat32mf2x6_t __riscv_vluxseg6ei32_v_f32mf2x6(const float *rs1, vuint32mf2_t rs2, size_t vl);
vfloat32mf2x6_t __riscv_vloxseg6ei32_v_f32mf2x6(const float *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_f32mf2x6(float *rs1, vuint32mf2_t rs2, vfloat32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_f32mf2x6(float *rs1, vuint32mf2_t rs2, vfloat32mf2x6_t vs3, size_t vl);
vfloat32mf2x6_t __riscv_vluxseg6ei64_v_f32mf2x6(const float *rs1, vuint64m1_t rs2, size_t vl);
vfloat32mf2x6_t __riscv_vloxseg6ei64_v_f32mf2x6(const float *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_f32mf2x6(float *rs1, vuint64m1_t rs2, vfloat32mf2x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_f32mf2x6(float *rs1, vuint64m1_t rs2, vfloat32mf2x6_t vs3, size_t vl);
vuint32mf2x7_t __riscv_vundefined_u32mf2x7(void);
vuint32mf2x7_t __riscv_vcreate_v_u32mf2_u32mf2x7(vuint32mf2_t v0, vuint32mf2_t v1, vuint32mf2_t v2, vuint32mf2_t v3, vuint32mf2_t v4, vuint32mf2_t v5, vuint32mf2_t v6);
vuint32mf2_t __riscv_vget_v_u32mf2x7_u32mf2(vuint32mf2x7_t src, size_t index);
vuint32mf2x7_t __riscv_vset_v_u32mf2x7_u32mf2(vuint32mf2x7_t dest, size_t index, vuint32mf2_t value);
vuint32mf2x7_t __riscv_vlseg7e32_v_u32mf2x7(const unsigned int *rs1, size_t vl);
void __riscv_vsseg7e32_v_u32mf2x7(unsigned int *rs1, vuint32mf2x7_t vs3, size_t vl);
vuint32mf2x7_t __riscv_vlseg7e32ff_v_u32mf2x7(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32mf2x7_t __riscv_vlsseg7e32_v_u32mf2x7(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg7e32_v_u32mf2x7(unsigned int *rs1, long rs2, vuint32mf2x7_t vs3, size_t vl);
vuint32mf2x7_t __riscv_vluxseg7ei8_v_u32mf2x7(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
vuint32mf2x7_t __riscv_vloxseg7ei8_v_u32mf2x7(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u32mf2x7(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u32mf2x7(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x7_t vs3, size_t vl);
vuint32mf2x7_t __riscv_vluxseg7ei16_v_u32mf2x7(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
vuint32mf2x7_t __riscv_vloxseg7ei16_v_u32mf2x7(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u32mf2x7(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u32mf2x7(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x7_t vs3, size_t vl);
vuint32mf2x7_t __riscv_vluxseg7ei32_v_u32mf2x7(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
vuint32mf2x7_t __riscv_vloxseg7ei32_v_u32mf2x7(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u32mf2x7(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u32mf2x7(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x7_t vs3, size_t vl);
vuint32mf2x7_t __riscv_vluxseg7ei64_v_u32mf2x7(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
vuint32mf2x7_t __riscv_vloxseg7ei64_v_u32mf2x7(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u32mf2x7(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u32mf2x7(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x7_t vs3, size_t vl);
vint32mf2x7_t __riscv_vundefined_i32mf2x7(void);
vint32mf2x7_t __riscv_vcreate_v_i32mf2_i32mf2x7(vint32mf2_t v0, vint32mf2_t v1, vint32mf2_t v2, vint32mf2_t v3, vint32mf2_t v4, vint32mf2_t v5, vint32mf2_t v6);
vint32mf2_t __riscv_vget_v_i32mf2x7_i32mf2(vint32mf2x7_t src, size_t index);
vint32mf2x7_t __riscv_vset_v_i32mf2x7_i32mf2(vint32mf2x7_t dest, size_t index, vint32mf2_t value);
vint32mf2x7_t __riscv_vlseg7e32_v_i32mf2x7(const int *rs1, size_t vl);
void __riscv_vsseg7e32_v_i32mf2x7(int *rs1, vint32mf2x7_t vs3, size_t vl);
vint32mf2x7_t __riscv_vlseg7e32ff_v_i32mf2x7(const int *rs1, size_t *new_vl, size_t vl);
vint32mf2x7_t __riscv_vlsseg7e32_v_i32mf2x7(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg7e32_v_i32mf2x7(int *rs1, long rs2, vint32mf2x7_t vs3, size_t vl);
vint32mf2x7_t __riscv_vluxseg7ei8_v_i32mf2x7(const int *rs1, vuint8mf8_t rs2, size_t vl);
vint32mf2x7_t __riscv_vloxseg7ei8_v_i32mf2x7(const int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i32mf2x7(int *rs1, vuint8mf8_t rs2, vint32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i32mf2x7(int *rs1, vuint8mf8_t rs2, vint32mf2x7_t vs3, size_t vl);
vint32mf2x7_t __riscv_vluxseg7ei16_v_i32mf2x7(const int *rs1, vuint16mf4_t rs2, size_t vl);
vint32mf2x7_t __riscv_vloxseg7ei16_v_i32mf2x7(const int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i32mf2x7(int *rs1, vuint16mf4_t rs2, vint32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i32mf2x7(int *rs1, vuint16mf4_t rs2, vint32mf2x7_t vs3, size_t vl);
vint32mf2x7_t __riscv_vluxseg7ei32_v_i32mf2x7(const int *rs1, vuint32mf2_t rs2, size_t vl);
vint32mf2x7_t __riscv_vloxseg7ei32_v_i32mf2x7(const int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i32mf2x7(int *rs1, vuint32mf2_t rs2, vint32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i32mf2x7(int *rs1, vuint32mf2_t rs2, vint32mf2x7_t vs3, size_t vl);
vint32mf2x7_t __riscv_vluxseg7ei64_v_i32mf2x7(const int *rs1, vuint64m1_t rs2, size_t vl);
vint32mf2x7_t __riscv_vloxseg7ei64_v_i32mf2x7(const int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i32mf2x7(int *rs1, vuint64m1_t rs2, vint32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i32mf2x7(int *rs1, vuint64m1_t rs2, vint32mf2x7_t vs3, size_t vl);
vfloat32mf2x7_t __riscv_vundefined_f32mf2x7(void);
vfloat32mf2x7_t __riscv_vcreate_v_f32mf2_f32mf2x7(vfloat32mf2_t v0, vfloat32mf2_t v1, vfloat32mf2_t v2, vfloat32mf2_t v3, vfloat32mf2_t v4, vfloat32mf2_t v5, vfloat32mf2_t v6);
vfloat32mf2_t __riscv_vget_v_f32mf2x7_f32mf2(vfloat32mf2x7_t src, size_t index);
vfloat32mf2x7_t __riscv_vset_v_f32mf2x7_f32mf2(vfloat32mf2x7_t dest, size_t index, vfloat32mf2_t value);
vfloat32mf2x7_t __riscv_vlseg7e32_v_f32mf2x7(const float *rs1, size_t vl);
void __riscv_vsseg7e32_v_f32mf2x7(float *rs1, vfloat32mf2x7_t vs3, size_t vl);
vfloat32mf2x7_t __riscv_vlseg7e32ff_v_f32mf2x7(const float *rs1, size_t *new_vl, size_t vl);
vfloat32mf2x7_t __riscv_vlsseg7e32_v_f32mf2x7(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg7e32_v_f32mf2x7(float *rs1, long rs2, vfloat32mf2x7_t vs3, size_t vl);
vfloat32mf2x7_t __riscv_vluxseg7ei8_v_f32mf2x7(const float *rs1, vuint8mf8_t rs2, size_t vl);
vfloat32mf2x7_t __riscv_vloxseg7ei8_v_f32mf2x7(const float *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_f32mf2x7(float *rs1, vuint8mf8_t rs2, vfloat32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_f32mf2x7(float *rs1, vuint8mf8_t rs2, vfloat32mf2x7_t vs3, size_t vl);
vfloat32mf2x7_t __riscv_vluxseg7ei16_v_f32mf2x7(const float *rs1, vuint16mf4_t rs2, size_t vl);
vfloat32mf2x7_t __riscv_vloxseg7ei16_v_f32mf2x7(const float *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_f32mf2x7(float *rs1, vuint16mf4_t rs2, vfloat32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_f32mf2x7(float *rs1, vuint16mf4_t rs2, vfloat32mf2x7_t vs3, size_t vl);
vfloat32mf2x7_t __riscv_vluxseg7ei32_v_f32mf2x7(const float *rs1, vuint32mf2_t rs2, size_t vl);
vfloat32mf2x7_t __riscv_vloxseg7ei32_v_f32mf2x7(const float *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_f32mf2x7(float *rs1, vuint32mf2_t rs2, vfloat32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_f32mf2x7(float *rs1, vuint32mf2_t rs2, vfloat32mf2x7_t vs3, size_t vl);
vfloat32mf2x7_t __riscv_vluxseg7ei64_v_f32mf2x7(const float *rs1, vuint64m1_t rs2, size_t vl);
vfloat32mf2x7_t __riscv_vloxseg7ei64_v_f32mf2x7(const float *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_f32mf2x7(float *rs1, vuint64m1_t rs2, vfloat32mf2x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_f32mf2x7(float *rs1, vuint64m1_t rs2, vfloat32mf2x7_t vs3, size_t vl);
vuint32mf2x8_t __riscv_vundefined_u32mf2x8(void);
vuint32mf2x8_t __riscv_vcreate_v_u32mf2_u32mf2x8(vuint32mf2_t v0, vuint32mf2_t v1, vuint32mf2_t v2, vuint32mf2_t v3, vuint32mf2_t v4, vuint32mf2_t v5, vuint32mf2_t v6, vuint32mf2_t v7);
vuint32mf2_t __riscv_vget_v_u32mf2x8_u32mf2(vuint32mf2x8_t src, size_t index);
vuint32mf2x8_t __riscv_vset_v_u32mf2x8_u32mf2(vuint32mf2x8_t dest, size_t index, vuint32mf2_t value);
vuint32mf2x8_t __riscv_vlseg8e32_v_u32mf2x8(const unsigned int *rs1, size_t vl);
void __riscv_vsseg8e32_v_u32mf2x8(unsigned int *rs1, vuint32mf2x8_t vs3, size_t vl);
vuint32mf2x8_t __riscv_vlseg8e32ff_v_u32mf2x8(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32mf2x8_t __riscv_vlsseg8e32_v_u32mf2x8(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg8e32_v_u32mf2x8(unsigned int *rs1, long rs2, vuint32mf2x8_t vs3, size_t vl);
vuint32mf2x8_t __riscv_vluxseg8ei8_v_u32mf2x8(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
vuint32mf2x8_t __riscv_vloxseg8ei8_v_u32mf2x8(const unsigned int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u32mf2x8(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u32mf2x8(unsigned int *rs1, vuint8mf8_t rs2, vuint32mf2x8_t vs3, size_t vl);
vuint32mf2x8_t __riscv_vluxseg8ei16_v_u32mf2x8(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
vuint32mf2x8_t __riscv_vloxseg8ei16_v_u32mf2x8(const unsigned int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u32mf2x8(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u32mf2x8(unsigned int *rs1, vuint16mf4_t rs2, vuint32mf2x8_t vs3, size_t vl);
vuint32mf2x8_t __riscv_vluxseg8ei32_v_u32mf2x8(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
vuint32mf2x8_t __riscv_vloxseg8ei32_v_u32mf2x8(const unsigned int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u32mf2x8(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u32mf2x8(unsigned int *rs1, vuint32mf2_t rs2, vuint32mf2x8_t vs3, size_t vl);
vuint32mf2x8_t __riscv_vluxseg8ei64_v_u32mf2x8(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
vuint32mf2x8_t __riscv_vloxseg8ei64_v_u32mf2x8(const unsigned int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u32mf2x8(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u32mf2x8(unsigned int *rs1, vuint64m1_t rs2, vuint32mf2x8_t vs3, size_t vl);
vint32mf2x8_t __riscv_vundefined_i32mf2x8(void);
vint32mf2x8_t __riscv_vcreate_v_i32mf2_i32mf2x8(vint32mf2_t v0, vint32mf2_t v1, vint32mf2_t v2, vint32mf2_t v3, vint32mf2_t v4, vint32mf2_t v5, vint32mf2_t v6, vint32mf2_t v7);
vint32mf2_t __riscv_vget_v_i32mf2x8_i32mf2(vint32mf2x8_t src, size_t index);
vint32mf2x8_t __riscv_vset_v_i32mf2x8_i32mf2(vint32mf2x8_t dest, size_t index, vint32mf2_t value);
vint32mf2x8_t __riscv_vlseg8e32_v_i32mf2x8(const int *rs1, size_t vl);
void __riscv_vsseg8e32_v_i32mf2x8(int *rs1, vint32mf2x8_t vs3, size_t vl);
vint32mf2x8_t __riscv_vlseg8e32ff_v_i32mf2x8(const int *rs1, size_t *new_vl, size_t vl);
vint32mf2x8_t __riscv_vlsseg8e32_v_i32mf2x8(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg8e32_v_i32mf2x8(int *rs1, long rs2, vint32mf2x8_t vs3, size_t vl);
vint32mf2x8_t __riscv_vluxseg8ei8_v_i32mf2x8(const int *rs1, vuint8mf8_t rs2, size_t vl);
vint32mf2x8_t __riscv_vloxseg8ei8_v_i32mf2x8(const int *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i32mf2x8(int *rs1, vuint8mf8_t rs2, vint32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i32mf2x8(int *rs1, vuint8mf8_t rs2, vint32mf2x8_t vs3, size_t vl);
vint32mf2x8_t __riscv_vluxseg8ei16_v_i32mf2x8(const int *rs1, vuint16mf4_t rs2, size_t vl);
vint32mf2x8_t __riscv_vloxseg8ei16_v_i32mf2x8(const int *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i32mf2x8(int *rs1, vuint16mf4_t rs2, vint32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i32mf2x8(int *rs1, vuint16mf4_t rs2, vint32mf2x8_t vs3, size_t vl);
vint32mf2x8_t __riscv_vluxseg8ei32_v_i32mf2x8(const int *rs1, vuint32mf2_t rs2, size_t vl);
vint32mf2x8_t __riscv_vloxseg8ei32_v_i32mf2x8(const int *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i32mf2x8(int *rs1, vuint32mf2_t rs2, vint32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i32mf2x8(int *rs1, vuint32mf2_t rs2, vint32mf2x8_t vs3, size_t vl);
vint32mf2x8_t __riscv_vluxseg8ei64_v_i32mf2x8(const int *rs1, vuint64m1_t rs2, size_t vl);
vint32mf2x8_t __riscv_vloxseg8ei64_v_i32mf2x8(const int *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i32mf2x8(int *rs1, vuint64m1_t rs2, vint32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i32mf2x8(int *rs1, vuint64m1_t rs2, vint32mf2x8_t vs3, size_t vl);
vfloat32mf2x8_t __riscv_vundefined_f32mf2x8(void);
vfloat32mf2x8_t __riscv_vcreate_v_f32mf2_f32mf2x8(vfloat32mf2_t v0, vfloat32mf2_t v1, vfloat32mf2_t v2, vfloat32mf2_t v3, vfloat32mf2_t v4, vfloat32mf2_t v5, vfloat32mf2_t v6, vfloat32mf2_t v7);
vfloat32mf2_t __riscv_vget_v_f32mf2x8_f32mf2(vfloat32mf2x8_t src, size_t index);
vfloat32mf2x8_t __riscv_vset_v_f32mf2x8_f32mf2(vfloat32mf2x8_t dest, size_t index, vfloat32mf2_t value);
vfloat32mf2x8_t __riscv_vlseg8e32_v_f32mf2x8(const float *rs1, size_t vl);
void __riscv_vsseg8e32_v_f32mf2x8(float *rs1, vfloat32mf2x8_t vs3, size_t vl);
vfloat32mf2x8_t __riscv_vlseg8e32ff_v_f32mf2x8(const float *rs1, size_t *new_vl, size_t vl);
vfloat32mf2x8_t __riscv_vlsseg8e32_v_f32mf2x8(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg8e32_v_f32mf2x8(float *rs1, long rs2, vfloat32mf2x8_t vs3, size_t vl);
vfloat32mf2x8_t __riscv_vluxseg8ei8_v_f32mf2x8(const float *rs1, vuint8mf8_t rs2, size_t vl);
vfloat32mf2x8_t __riscv_vloxseg8ei8_v_f32mf2x8(const float *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_f32mf2x8(float *rs1, vuint8mf8_t rs2, vfloat32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_f32mf2x8(float *rs1, vuint8mf8_t rs2, vfloat32mf2x8_t vs3, size_t vl);
vfloat32mf2x8_t __riscv_vluxseg8ei16_v_f32mf2x8(const float *rs1, vuint16mf4_t rs2, size_t vl);
vfloat32mf2x8_t __riscv_vloxseg8ei16_v_f32mf2x8(const float *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_f32mf2x8(float *rs1, vuint16mf4_t rs2, vfloat32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_f32mf2x8(float *rs1, vuint16mf4_t rs2, vfloat32mf2x8_t vs3, size_t vl);
vfloat32mf2x8_t __riscv_vluxseg8ei32_v_f32mf2x8(const float *rs1, vuint32mf2_t rs2, size_t vl);
vfloat32mf2x8_t __riscv_vloxseg8ei32_v_f32mf2x8(const float *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_f32mf2x8(float *rs1, vuint32mf2_t rs2, vfloat32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_f32mf2x8(float *rs1, vuint32mf2_t rs2, vfloat32mf2x8_t vs3, size_t vl);
vfloat32mf2x8_t __riscv_vluxseg8ei64_v_f32mf2x8(const float *rs1, vuint64m1_t rs2, size_t vl);
vfloat32mf2x8_t __riscv_vloxseg8ei64_v_f32mf2x8(const float *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_f32mf2x8(float *rs1, vuint64m1_t rs2, vfloat32mf2x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_f32mf2x8(float *rs1, vuint64m1_t rs2, vfloat32mf2x8_t vs3, size_t vl);
vuint32m1x2_t __riscv_vundefined_u32m1x2(void);
vuint32m1x2_t __riscv_vcreate_v_u32m1_u32m1x2(vuint32m1_t v0, vuint32m1_t v1);
vuint32m1_t __riscv_vget_v_u32m1x2_u32m1(vuint32m1x2_t src, size_t index);
vuint32m1x2_t __riscv_vset_v_u32m1x2_u32m1(vuint32m1x2_t dest, size_t index, vuint32m1_t value);
vuint32m1x2_t __riscv_vlseg2e32_v_u32m1x2(const unsigned int *rs1, size_t vl);
void __riscv_vsseg2e32_v_u32m1x2(unsigned int *rs1, vuint32m1x2_t vs3, size_t vl);
vuint32m1x2_t __riscv_vlseg2e32ff_v_u32m1x2(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m1x2_t __riscv_vlsseg2e32_v_u32m1x2(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_u32m1x2(unsigned int *rs1, long rs2, vuint32m1x2_t vs3, size_t vl);
vuint32m1x2_t __riscv_vluxseg2ei8_v_u32m1x2(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
vuint32m1x2_t __riscv_vloxseg2ei8_v_u32m1x2(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u32m1x2(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u32m1x2(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x2_t vs3, size_t vl);
vuint32m1x2_t __riscv_vluxseg2ei16_v_u32m1x2(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
vuint32m1x2_t __riscv_vloxseg2ei16_v_u32m1x2(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u32m1x2(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u32m1x2(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x2_t vs3, size_t vl);
vuint32m1x2_t __riscv_vluxseg2ei32_v_u32m1x2(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
vuint32m1x2_t __riscv_vloxseg2ei32_v_u32m1x2(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u32m1x2(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u32m1x2(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x2_t vs3, size_t vl);
vuint32m1x2_t __riscv_vluxseg2ei64_v_u32m1x2(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
vuint32m1x2_t __riscv_vloxseg2ei64_v_u32m1x2(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u32m1x2(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u32m1x2(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x2_t vs3, size_t vl);
vint32m1x2_t __riscv_vundefined_i32m1x2(void);
vint32m1x2_t __riscv_vcreate_v_i32m1_i32m1x2(vint32m1_t v0, vint32m1_t v1);
vint32m1_t __riscv_vget_v_i32m1x2_i32m1(vint32m1x2_t src, size_t index);
vint32m1x2_t __riscv_vset_v_i32m1x2_i32m1(vint32m1x2_t dest, size_t index, vint32m1_t value);
vint32m1x2_t __riscv_vlseg2e32_v_i32m1x2(const int *rs1, size_t vl);
void __riscv_vsseg2e32_v_i32m1x2(int *rs1, vint32m1x2_t vs3, size_t vl);
vint32m1x2_t __riscv_vlseg2e32ff_v_i32m1x2(const int *rs1, size_t *new_vl, size_t vl);
vint32m1x2_t __riscv_vlsseg2e32_v_i32m1x2(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_i32m1x2(int *rs1, long rs2, vint32m1x2_t vs3, size_t vl);
vint32m1x2_t __riscv_vluxseg2ei8_v_i32m1x2(const int *rs1, vuint8mf4_t rs2, size_t vl);
vint32m1x2_t __riscv_vloxseg2ei8_v_i32m1x2(const int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i32m1x2(int *rs1, vuint8mf4_t rs2, vint32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i32m1x2(int *rs1, vuint8mf4_t rs2, vint32m1x2_t vs3, size_t vl);
vint32m1x2_t __riscv_vluxseg2ei16_v_i32m1x2(const int *rs1, vuint16mf2_t rs2, size_t vl);
vint32m1x2_t __riscv_vloxseg2ei16_v_i32m1x2(const int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i32m1x2(int *rs1, vuint16mf2_t rs2, vint32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i32m1x2(int *rs1, vuint16mf2_t rs2, vint32m1x2_t vs3, size_t vl);
vint32m1x2_t __riscv_vluxseg2ei32_v_i32m1x2(const int *rs1, vuint32m1_t rs2, size_t vl);
vint32m1x2_t __riscv_vloxseg2ei32_v_i32m1x2(const int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i32m1x2(int *rs1, vuint32m1_t rs2, vint32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i32m1x2(int *rs1, vuint32m1_t rs2, vint32m1x2_t vs3, size_t vl);
vint32m1x2_t __riscv_vluxseg2ei64_v_i32m1x2(const int *rs1, vuint64m2_t rs2, size_t vl);
vint32m1x2_t __riscv_vloxseg2ei64_v_i32m1x2(const int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i32m1x2(int *rs1, vuint64m2_t rs2, vint32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i32m1x2(int *rs1, vuint64m2_t rs2, vint32m1x2_t vs3, size_t vl);
vfloat32m1x2_t __riscv_vundefined_f32m1x2(void);
vfloat32m1x2_t __riscv_vcreate_v_f32m1_f32m1x2(vfloat32m1_t v0, vfloat32m1_t v1);
vfloat32m1_t __riscv_vget_v_f32m1x2_f32m1(vfloat32m1x2_t src, size_t index);
vfloat32m1x2_t __riscv_vset_v_f32m1x2_f32m1(vfloat32m1x2_t dest, size_t index, vfloat32m1_t value);
vfloat32m1x2_t __riscv_vlseg2e32_v_f32m1x2(const float *rs1, size_t vl);
void __riscv_vsseg2e32_v_f32m1x2(float *rs1, vfloat32m1x2_t vs3, size_t vl);
vfloat32m1x2_t __riscv_vlseg2e32ff_v_f32m1x2(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m1x2_t __riscv_vlsseg2e32_v_f32m1x2(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_f32m1x2(float *rs1, long rs2, vfloat32m1x2_t vs3, size_t vl);
vfloat32m1x2_t __riscv_vluxseg2ei8_v_f32m1x2(const float *rs1, vuint8mf4_t rs2, size_t vl);
vfloat32m1x2_t __riscv_vloxseg2ei8_v_f32m1x2(const float *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_f32m1x2(float *rs1, vuint8mf4_t rs2, vfloat32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_f32m1x2(float *rs1, vuint8mf4_t rs2, vfloat32m1x2_t vs3, size_t vl);
vfloat32m1x2_t __riscv_vluxseg2ei16_v_f32m1x2(const float *rs1, vuint16mf2_t rs2, size_t vl);
vfloat32m1x2_t __riscv_vloxseg2ei16_v_f32m1x2(const float *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_f32m1x2(float *rs1, vuint16mf2_t rs2, vfloat32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_f32m1x2(float *rs1, vuint16mf2_t rs2, vfloat32m1x2_t vs3, size_t vl);
vfloat32m1x2_t __riscv_vluxseg2ei32_v_f32m1x2(const float *rs1, vuint32m1_t rs2, size_t vl);
vfloat32m1x2_t __riscv_vloxseg2ei32_v_f32m1x2(const float *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_f32m1x2(float *rs1, vuint32m1_t rs2, vfloat32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_f32m1x2(float *rs1, vuint32m1_t rs2, vfloat32m1x2_t vs3, size_t vl);
vfloat32m1x2_t __riscv_vluxseg2ei64_v_f32m1x2(const float *rs1, vuint64m2_t rs2, size_t vl);
vfloat32m1x2_t __riscv_vloxseg2ei64_v_f32m1x2(const float *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_f32m1x2(float *rs1, vuint64m2_t rs2, vfloat32m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_f32m1x2(float *rs1, vuint64m2_t rs2, vfloat32m1x2_t vs3, size_t vl);
vuint32m1x3_t __riscv_vundefined_u32m1x3(void);
vuint32m1x3_t __riscv_vcreate_v_u32m1_u32m1x3(vuint32m1_t v0, vuint32m1_t v1, vuint32m1_t v2);
vuint32m1_t __riscv_vget_v_u32m1x3_u32m1(vuint32m1x3_t src, size_t index);
vuint32m1x3_t __riscv_vset_v_u32m1x3_u32m1(vuint32m1x3_t dest, size_t index, vuint32m1_t value);
vuint32m1x3_t __riscv_vlseg3e32_v_u32m1x3(const unsigned int *rs1, size_t vl);
void __riscv_vsseg3e32_v_u32m1x3(unsigned int *rs1, vuint32m1x3_t vs3, size_t vl);
vuint32m1x3_t __riscv_vlseg3e32ff_v_u32m1x3(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m1x3_t __riscv_vlsseg3e32_v_u32m1x3(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_u32m1x3(unsigned int *rs1, long rs2, vuint32m1x3_t vs3, size_t vl);
vuint32m1x3_t __riscv_vluxseg3ei8_v_u32m1x3(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
vuint32m1x3_t __riscv_vloxseg3ei8_v_u32m1x3(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u32m1x3(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u32m1x3(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x3_t vs3, size_t vl);
vuint32m1x3_t __riscv_vluxseg3ei16_v_u32m1x3(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
vuint32m1x3_t __riscv_vloxseg3ei16_v_u32m1x3(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u32m1x3(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u32m1x3(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x3_t vs3, size_t vl);
vuint32m1x3_t __riscv_vluxseg3ei32_v_u32m1x3(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
vuint32m1x3_t __riscv_vloxseg3ei32_v_u32m1x3(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u32m1x3(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u32m1x3(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x3_t vs3, size_t vl);
vuint32m1x3_t __riscv_vluxseg3ei64_v_u32m1x3(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
vuint32m1x3_t __riscv_vloxseg3ei64_v_u32m1x3(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u32m1x3(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u32m1x3(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x3_t vs3, size_t vl);
vint32m1x3_t __riscv_vundefined_i32m1x3(void);
vint32m1x3_t __riscv_vcreate_v_i32m1_i32m1x3(vint32m1_t v0, vint32m1_t v1, vint32m1_t v2);
vint32m1_t __riscv_vget_v_i32m1x3_i32m1(vint32m1x3_t src, size_t index);
vint32m1x3_t __riscv_vset_v_i32m1x3_i32m1(vint32m1x3_t dest, size_t index, vint32m1_t value);
vint32m1x3_t __riscv_vlseg3e32_v_i32m1x3(const int *rs1, size_t vl);
void __riscv_vsseg3e32_v_i32m1x3(int *rs1, vint32m1x3_t vs3, size_t vl);
vint32m1x3_t __riscv_vlseg3e32ff_v_i32m1x3(const int *rs1, size_t *new_vl, size_t vl);
vint32m1x3_t __riscv_vlsseg3e32_v_i32m1x3(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_i32m1x3(int *rs1, long rs2, vint32m1x3_t vs3, size_t vl);
vint32m1x3_t __riscv_vluxseg3ei8_v_i32m1x3(const int *rs1, vuint8mf4_t rs2, size_t vl);
vint32m1x3_t __riscv_vloxseg3ei8_v_i32m1x3(const int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i32m1x3(int *rs1, vuint8mf4_t rs2, vint32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i32m1x3(int *rs1, vuint8mf4_t rs2, vint32m1x3_t vs3, size_t vl);
vint32m1x3_t __riscv_vluxseg3ei16_v_i32m1x3(const int *rs1, vuint16mf2_t rs2, size_t vl);
vint32m1x3_t __riscv_vloxseg3ei16_v_i32m1x3(const int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i32m1x3(int *rs1, vuint16mf2_t rs2, vint32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i32m1x3(int *rs1, vuint16mf2_t rs2, vint32m1x3_t vs3, size_t vl);
vint32m1x3_t __riscv_vluxseg3ei32_v_i32m1x3(const int *rs1, vuint32m1_t rs2, size_t vl);
vint32m1x3_t __riscv_vloxseg3ei32_v_i32m1x3(const int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i32m1x3(int *rs1, vuint32m1_t rs2, vint32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i32m1x3(int *rs1, vuint32m1_t rs2, vint32m1x3_t vs3, size_t vl);
vint32m1x3_t __riscv_vluxseg3ei64_v_i32m1x3(const int *rs1, vuint64m2_t rs2, size_t vl);
vint32m1x3_t __riscv_vloxseg3ei64_v_i32m1x3(const int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i32m1x3(int *rs1, vuint64m2_t rs2, vint32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i32m1x3(int *rs1, vuint64m2_t rs2, vint32m1x3_t vs3, size_t vl);
vfloat32m1x3_t __riscv_vundefined_f32m1x3(void);
vfloat32m1x3_t __riscv_vcreate_v_f32m1_f32m1x3(vfloat32m1_t v0, vfloat32m1_t v1, vfloat32m1_t v2);
vfloat32m1_t __riscv_vget_v_f32m1x3_f32m1(vfloat32m1x3_t src, size_t index);
vfloat32m1x3_t __riscv_vset_v_f32m1x3_f32m1(vfloat32m1x3_t dest, size_t index, vfloat32m1_t value);
vfloat32m1x3_t __riscv_vlseg3e32_v_f32m1x3(const float *rs1, size_t vl);
void __riscv_vsseg3e32_v_f32m1x3(float *rs1, vfloat32m1x3_t vs3, size_t vl);
vfloat32m1x3_t __riscv_vlseg3e32ff_v_f32m1x3(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m1x3_t __riscv_vlsseg3e32_v_f32m1x3(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_f32m1x3(float *rs1, long rs2, vfloat32m1x3_t vs3, size_t vl);
vfloat32m1x3_t __riscv_vluxseg3ei8_v_f32m1x3(const float *rs1, vuint8mf4_t rs2, size_t vl);
vfloat32m1x3_t __riscv_vloxseg3ei8_v_f32m1x3(const float *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_f32m1x3(float *rs1, vuint8mf4_t rs2, vfloat32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_f32m1x3(float *rs1, vuint8mf4_t rs2, vfloat32m1x3_t vs3, size_t vl);
vfloat32m1x3_t __riscv_vluxseg3ei16_v_f32m1x3(const float *rs1, vuint16mf2_t rs2, size_t vl);
vfloat32m1x3_t __riscv_vloxseg3ei16_v_f32m1x3(const float *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_f32m1x3(float *rs1, vuint16mf2_t rs2, vfloat32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_f32m1x3(float *rs1, vuint16mf2_t rs2, vfloat32m1x3_t vs3, size_t vl);
vfloat32m1x3_t __riscv_vluxseg3ei32_v_f32m1x3(const float *rs1, vuint32m1_t rs2, size_t vl);
vfloat32m1x3_t __riscv_vloxseg3ei32_v_f32m1x3(const float *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_f32m1x3(float *rs1, vuint32m1_t rs2, vfloat32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_f32m1x3(float *rs1, vuint32m1_t rs2, vfloat32m1x3_t vs3, size_t vl);
vfloat32m1x3_t __riscv_vluxseg3ei64_v_f32m1x3(const float *rs1, vuint64m2_t rs2, size_t vl);
vfloat32m1x3_t __riscv_vloxseg3ei64_v_f32m1x3(const float *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_f32m1x3(float *rs1, vuint64m2_t rs2, vfloat32m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_f32m1x3(float *rs1, vuint64m2_t rs2, vfloat32m1x3_t vs3, size_t vl);
vuint32m1x4_t __riscv_vundefined_u32m1x4(void);
vuint32m1x4_t __riscv_vcreate_v_u32m1_u32m1x4(vuint32m1_t v0, vuint32m1_t v1, vuint32m1_t v2, vuint32m1_t v3);
vuint32m1_t __riscv_vget_v_u32m1x4_u32m1(vuint32m1x4_t src, size_t index);
vuint32m1x4_t __riscv_vset_v_u32m1x4_u32m1(vuint32m1x4_t dest, size_t index, vuint32m1_t value);
vuint32m1x4_t __riscv_vlseg4e32_v_u32m1x4(const unsigned int *rs1, size_t vl);
void __riscv_vsseg4e32_v_u32m1x4(unsigned int *rs1, vuint32m1x4_t vs3, size_t vl);
vuint32m1x4_t __riscv_vlseg4e32ff_v_u32m1x4(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m1x4_t __riscv_vlsseg4e32_v_u32m1x4(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_u32m1x4(unsigned int *rs1, long rs2, vuint32m1x4_t vs3, size_t vl);
vuint32m1x4_t __riscv_vluxseg4ei8_v_u32m1x4(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
vuint32m1x4_t __riscv_vloxseg4ei8_v_u32m1x4(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u32m1x4(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u32m1x4(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x4_t vs3, size_t vl);
vuint32m1x4_t __riscv_vluxseg4ei16_v_u32m1x4(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
vuint32m1x4_t __riscv_vloxseg4ei16_v_u32m1x4(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u32m1x4(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u32m1x4(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x4_t vs3, size_t vl);
vuint32m1x4_t __riscv_vluxseg4ei32_v_u32m1x4(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
vuint32m1x4_t __riscv_vloxseg4ei32_v_u32m1x4(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u32m1x4(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u32m1x4(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x4_t vs3, size_t vl);
vuint32m1x4_t __riscv_vluxseg4ei64_v_u32m1x4(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
vuint32m1x4_t __riscv_vloxseg4ei64_v_u32m1x4(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u32m1x4(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u32m1x4(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x4_t vs3, size_t vl);
vint32m1x4_t __riscv_vundefined_i32m1x4(void);
vint32m1x4_t __riscv_vcreate_v_i32m1_i32m1x4(vint32m1_t v0, vint32m1_t v1, vint32m1_t v2, vint32m1_t v3);
vint32m1_t __riscv_vget_v_i32m1x4_i32m1(vint32m1x4_t src, size_t index);
vint32m1x4_t __riscv_vset_v_i32m1x4_i32m1(vint32m1x4_t dest, size_t index, vint32m1_t value);
vint32m1x4_t __riscv_vlseg4e32_v_i32m1x4(const int *rs1, size_t vl);
void __riscv_vsseg4e32_v_i32m1x4(int *rs1, vint32m1x4_t vs3, size_t vl);
vint32m1x4_t __riscv_vlseg4e32ff_v_i32m1x4(const int *rs1, size_t *new_vl, size_t vl);
vint32m1x4_t __riscv_vlsseg4e32_v_i32m1x4(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_i32m1x4(int *rs1, long rs2, vint32m1x4_t vs3, size_t vl);
vint32m1x4_t __riscv_vluxseg4ei8_v_i32m1x4(const int *rs1, vuint8mf4_t rs2, size_t vl);
vint32m1x4_t __riscv_vloxseg4ei8_v_i32m1x4(const int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i32m1x4(int *rs1, vuint8mf4_t rs2, vint32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i32m1x4(int *rs1, vuint8mf4_t rs2, vint32m1x4_t vs3, size_t vl);
vint32m1x4_t __riscv_vluxseg4ei16_v_i32m1x4(const int *rs1, vuint16mf2_t rs2, size_t vl);
vint32m1x4_t __riscv_vloxseg4ei16_v_i32m1x4(const int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i32m1x4(int *rs1, vuint16mf2_t rs2, vint32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i32m1x4(int *rs1, vuint16mf2_t rs2, vint32m1x4_t vs3, size_t vl);
vint32m1x4_t __riscv_vluxseg4ei32_v_i32m1x4(const int *rs1, vuint32m1_t rs2, size_t vl);
vint32m1x4_t __riscv_vloxseg4ei32_v_i32m1x4(const int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i32m1x4(int *rs1, vuint32m1_t rs2, vint32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i32m1x4(int *rs1, vuint32m1_t rs2, vint32m1x4_t vs3, size_t vl);
vint32m1x4_t __riscv_vluxseg4ei64_v_i32m1x4(const int *rs1, vuint64m2_t rs2, size_t vl);
vint32m1x4_t __riscv_vloxseg4ei64_v_i32m1x4(const int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i32m1x4(int *rs1, vuint64m2_t rs2, vint32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i32m1x4(int *rs1, vuint64m2_t rs2, vint32m1x4_t vs3, size_t vl);
vfloat32m1x4_t __riscv_vundefined_f32m1x4(void);
vfloat32m1x4_t __riscv_vcreate_v_f32m1_f32m1x4(vfloat32m1_t v0, vfloat32m1_t v1, vfloat32m1_t v2, vfloat32m1_t v3);
vfloat32m1_t __riscv_vget_v_f32m1x4_f32m1(vfloat32m1x4_t src, size_t index);
vfloat32m1x4_t __riscv_vset_v_f32m1x4_f32m1(vfloat32m1x4_t dest, size_t index, vfloat32m1_t value);
vfloat32m1x4_t __riscv_vlseg4e32_v_f32m1x4(const float *rs1, size_t vl);
void __riscv_vsseg4e32_v_f32m1x4(float *rs1, vfloat32m1x4_t vs3, size_t vl);
vfloat32m1x4_t __riscv_vlseg4e32ff_v_f32m1x4(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m1x4_t __riscv_vlsseg4e32_v_f32m1x4(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_f32m1x4(float *rs1, long rs2, vfloat32m1x4_t vs3, size_t vl);
vfloat32m1x4_t __riscv_vluxseg4ei8_v_f32m1x4(const float *rs1, vuint8mf4_t rs2, size_t vl);
vfloat32m1x4_t __riscv_vloxseg4ei8_v_f32m1x4(const float *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_f32m1x4(float *rs1, vuint8mf4_t rs2, vfloat32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_f32m1x4(float *rs1, vuint8mf4_t rs2, vfloat32m1x4_t vs3, size_t vl);
vfloat32m1x4_t __riscv_vluxseg4ei16_v_f32m1x4(const float *rs1, vuint16mf2_t rs2, size_t vl);
vfloat32m1x4_t __riscv_vloxseg4ei16_v_f32m1x4(const float *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_f32m1x4(float *rs1, vuint16mf2_t rs2, vfloat32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_f32m1x4(float *rs1, vuint16mf2_t rs2, vfloat32m1x4_t vs3, size_t vl);
vfloat32m1x4_t __riscv_vluxseg4ei32_v_f32m1x4(const float *rs1, vuint32m1_t rs2, size_t vl);
vfloat32m1x4_t __riscv_vloxseg4ei32_v_f32m1x4(const float *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_f32m1x4(float *rs1, vuint32m1_t rs2, vfloat32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_f32m1x4(float *rs1, vuint32m1_t rs2, vfloat32m1x4_t vs3, size_t vl);
vfloat32m1x4_t __riscv_vluxseg4ei64_v_f32m1x4(const float *rs1, vuint64m2_t rs2, size_t vl);
vfloat32m1x4_t __riscv_vloxseg4ei64_v_f32m1x4(const float *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_f32m1x4(float *rs1, vuint64m2_t rs2, vfloat32m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_f32m1x4(float *rs1, vuint64m2_t rs2, vfloat32m1x4_t vs3, size_t vl);
vuint32m1x5_t __riscv_vundefined_u32m1x5(void);
vuint32m1x5_t __riscv_vcreate_v_u32m1_u32m1x5(vuint32m1_t v0, vuint32m1_t v1, vuint32m1_t v2, vuint32m1_t v3, vuint32m1_t v4);
vuint32m1_t __riscv_vget_v_u32m1x5_u32m1(vuint32m1x5_t src, size_t index);
vuint32m1x5_t __riscv_vset_v_u32m1x5_u32m1(vuint32m1x5_t dest, size_t index, vuint32m1_t value);
vuint32m1x5_t __riscv_vlseg5e32_v_u32m1x5(const unsigned int *rs1, size_t vl);
void __riscv_vsseg5e32_v_u32m1x5(unsigned int *rs1, vuint32m1x5_t vs3, size_t vl);
vuint32m1x5_t __riscv_vlseg5e32ff_v_u32m1x5(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m1x5_t __riscv_vlsseg5e32_v_u32m1x5(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg5e32_v_u32m1x5(unsigned int *rs1, long rs2, vuint32m1x5_t vs3, size_t vl);
vuint32m1x5_t __riscv_vluxseg5ei8_v_u32m1x5(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
vuint32m1x5_t __riscv_vloxseg5ei8_v_u32m1x5(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u32m1x5(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u32m1x5(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x5_t vs3, size_t vl);
vuint32m1x5_t __riscv_vluxseg5ei16_v_u32m1x5(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
vuint32m1x5_t __riscv_vloxseg5ei16_v_u32m1x5(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u32m1x5(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u32m1x5(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x5_t vs3, size_t vl);
vuint32m1x5_t __riscv_vluxseg5ei32_v_u32m1x5(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
vuint32m1x5_t __riscv_vloxseg5ei32_v_u32m1x5(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u32m1x5(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u32m1x5(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x5_t vs3, size_t vl);
vuint32m1x5_t __riscv_vluxseg5ei64_v_u32m1x5(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
vuint32m1x5_t __riscv_vloxseg5ei64_v_u32m1x5(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u32m1x5(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u32m1x5(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x5_t vs3, size_t vl);
vint32m1x5_t __riscv_vundefined_i32m1x5(void);
vint32m1x5_t __riscv_vcreate_v_i32m1_i32m1x5(vint32m1_t v0, vint32m1_t v1, vint32m1_t v2, vint32m1_t v3, vint32m1_t v4);
vint32m1_t __riscv_vget_v_i32m1x5_i32m1(vint32m1x5_t src, size_t index);
vint32m1x5_t __riscv_vset_v_i32m1x5_i32m1(vint32m1x5_t dest, size_t index, vint32m1_t value);
vint32m1x5_t __riscv_vlseg5e32_v_i32m1x5(const int *rs1, size_t vl);
void __riscv_vsseg5e32_v_i32m1x5(int *rs1, vint32m1x5_t vs3, size_t vl);
vint32m1x5_t __riscv_vlseg5e32ff_v_i32m1x5(const int *rs1, size_t *new_vl, size_t vl);
vint32m1x5_t __riscv_vlsseg5e32_v_i32m1x5(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg5e32_v_i32m1x5(int *rs1, long rs2, vint32m1x5_t vs3, size_t vl);
vint32m1x5_t __riscv_vluxseg5ei8_v_i32m1x5(const int *rs1, vuint8mf4_t rs2, size_t vl);
vint32m1x5_t __riscv_vloxseg5ei8_v_i32m1x5(const int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i32m1x5(int *rs1, vuint8mf4_t rs2, vint32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i32m1x5(int *rs1, vuint8mf4_t rs2, vint32m1x5_t vs3, size_t vl);
vint32m1x5_t __riscv_vluxseg5ei16_v_i32m1x5(const int *rs1, vuint16mf2_t rs2, size_t vl);
vint32m1x5_t __riscv_vloxseg5ei16_v_i32m1x5(const int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i32m1x5(int *rs1, vuint16mf2_t rs2, vint32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i32m1x5(int *rs1, vuint16mf2_t rs2, vint32m1x5_t vs3, size_t vl);
vint32m1x5_t __riscv_vluxseg5ei32_v_i32m1x5(const int *rs1, vuint32m1_t rs2, size_t vl);
vint32m1x5_t __riscv_vloxseg5ei32_v_i32m1x5(const int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i32m1x5(int *rs1, vuint32m1_t rs2, vint32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i32m1x5(int *rs1, vuint32m1_t rs2, vint32m1x5_t vs3, size_t vl);
vint32m1x5_t __riscv_vluxseg5ei64_v_i32m1x5(const int *rs1, vuint64m2_t rs2, size_t vl);
vint32m1x5_t __riscv_vloxseg5ei64_v_i32m1x5(const int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i32m1x5(int *rs1, vuint64m2_t rs2, vint32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i32m1x5(int *rs1, vuint64m2_t rs2, vint32m1x5_t vs3, size_t vl);
vfloat32m1x5_t __riscv_vundefined_f32m1x5(void);
vfloat32m1x5_t __riscv_vcreate_v_f32m1_f32m1x5(vfloat32m1_t v0, vfloat32m1_t v1, vfloat32m1_t v2, vfloat32m1_t v3, vfloat32m1_t v4);
vfloat32m1_t __riscv_vget_v_f32m1x5_f32m1(vfloat32m1x5_t src, size_t index);
vfloat32m1x5_t __riscv_vset_v_f32m1x5_f32m1(vfloat32m1x5_t dest, size_t index, vfloat32m1_t value);
vfloat32m1x5_t __riscv_vlseg5e32_v_f32m1x5(const float *rs1, size_t vl);
void __riscv_vsseg5e32_v_f32m1x5(float *rs1, vfloat32m1x5_t vs3, size_t vl);
vfloat32m1x5_t __riscv_vlseg5e32ff_v_f32m1x5(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m1x5_t __riscv_vlsseg5e32_v_f32m1x5(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg5e32_v_f32m1x5(float *rs1, long rs2, vfloat32m1x5_t vs3, size_t vl);
vfloat32m1x5_t __riscv_vluxseg5ei8_v_f32m1x5(const float *rs1, vuint8mf4_t rs2, size_t vl);
vfloat32m1x5_t __riscv_vloxseg5ei8_v_f32m1x5(const float *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_f32m1x5(float *rs1, vuint8mf4_t rs2, vfloat32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_f32m1x5(float *rs1, vuint8mf4_t rs2, vfloat32m1x5_t vs3, size_t vl);
vfloat32m1x5_t __riscv_vluxseg5ei16_v_f32m1x5(const float *rs1, vuint16mf2_t rs2, size_t vl);
vfloat32m1x5_t __riscv_vloxseg5ei16_v_f32m1x5(const float *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_f32m1x5(float *rs1, vuint16mf2_t rs2, vfloat32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_f32m1x5(float *rs1, vuint16mf2_t rs2, vfloat32m1x5_t vs3, size_t vl);
vfloat32m1x5_t __riscv_vluxseg5ei32_v_f32m1x5(const float *rs1, vuint32m1_t rs2, size_t vl);
vfloat32m1x5_t __riscv_vloxseg5ei32_v_f32m1x5(const float *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_f32m1x5(float *rs1, vuint32m1_t rs2, vfloat32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_f32m1x5(float *rs1, vuint32m1_t rs2, vfloat32m1x5_t vs3, size_t vl);
vfloat32m1x5_t __riscv_vluxseg5ei64_v_f32m1x5(const float *rs1, vuint64m2_t rs2, size_t vl);
vfloat32m1x5_t __riscv_vloxseg5ei64_v_f32m1x5(const float *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_f32m1x5(float *rs1, vuint64m2_t rs2, vfloat32m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_f32m1x5(float *rs1, vuint64m2_t rs2, vfloat32m1x5_t vs3, size_t vl);
vuint32m1x6_t __riscv_vundefined_u32m1x6(void);
vuint32m1x6_t __riscv_vcreate_v_u32m1_u32m1x6(vuint32m1_t v0, vuint32m1_t v1, vuint32m1_t v2, vuint32m1_t v3, vuint32m1_t v4, vuint32m1_t v5);
vuint32m1_t __riscv_vget_v_u32m1x6_u32m1(vuint32m1x6_t src, size_t index);
vuint32m1x6_t __riscv_vset_v_u32m1x6_u32m1(vuint32m1x6_t dest, size_t index, vuint32m1_t value);
vuint32m1x6_t __riscv_vlseg6e32_v_u32m1x6(const unsigned int *rs1, size_t vl);
void __riscv_vsseg6e32_v_u32m1x6(unsigned int *rs1, vuint32m1x6_t vs3, size_t vl);
vuint32m1x6_t __riscv_vlseg6e32ff_v_u32m1x6(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m1x6_t __riscv_vlsseg6e32_v_u32m1x6(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg6e32_v_u32m1x6(unsigned int *rs1, long rs2, vuint32m1x6_t vs3, size_t vl);
vuint32m1x6_t __riscv_vluxseg6ei8_v_u32m1x6(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
vuint32m1x6_t __riscv_vloxseg6ei8_v_u32m1x6(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u32m1x6(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u32m1x6(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x6_t vs3, size_t vl);
vuint32m1x6_t __riscv_vluxseg6ei16_v_u32m1x6(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
vuint32m1x6_t __riscv_vloxseg6ei16_v_u32m1x6(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u32m1x6(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u32m1x6(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x6_t vs3, size_t vl);
vuint32m1x6_t __riscv_vluxseg6ei32_v_u32m1x6(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
vuint32m1x6_t __riscv_vloxseg6ei32_v_u32m1x6(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u32m1x6(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u32m1x6(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x6_t vs3, size_t vl);
vuint32m1x6_t __riscv_vluxseg6ei64_v_u32m1x6(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
vuint32m1x6_t __riscv_vloxseg6ei64_v_u32m1x6(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u32m1x6(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u32m1x6(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x6_t vs3, size_t vl);
vint32m1x6_t __riscv_vundefined_i32m1x6(void);
vint32m1x6_t __riscv_vcreate_v_i32m1_i32m1x6(vint32m1_t v0, vint32m1_t v1, vint32m1_t v2, vint32m1_t v3, vint32m1_t v4, vint32m1_t v5);
vint32m1_t __riscv_vget_v_i32m1x6_i32m1(vint32m1x6_t src, size_t index);
vint32m1x6_t __riscv_vset_v_i32m1x6_i32m1(vint32m1x6_t dest, size_t index, vint32m1_t value);
vint32m1x6_t __riscv_vlseg6e32_v_i32m1x6(const int *rs1, size_t vl);
void __riscv_vsseg6e32_v_i32m1x6(int *rs1, vint32m1x6_t vs3, size_t vl);
vint32m1x6_t __riscv_vlseg6e32ff_v_i32m1x6(const int *rs1, size_t *new_vl, size_t vl);
vint32m1x6_t __riscv_vlsseg6e32_v_i32m1x6(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg6e32_v_i32m1x6(int *rs1, long rs2, vint32m1x6_t vs3, size_t vl);
vint32m1x6_t __riscv_vluxseg6ei8_v_i32m1x6(const int *rs1, vuint8mf4_t rs2, size_t vl);
vint32m1x6_t __riscv_vloxseg6ei8_v_i32m1x6(const int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i32m1x6(int *rs1, vuint8mf4_t rs2, vint32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i32m1x6(int *rs1, vuint8mf4_t rs2, vint32m1x6_t vs3, size_t vl);
vint32m1x6_t __riscv_vluxseg6ei16_v_i32m1x6(const int *rs1, vuint16mf2_t rs2, size_t vl);
vint32m1x6_t __riscv_vloxseg6ei16_v_i32m1x6(const int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i32m1x6(int *rs1, vuint16mf2_t rs2, vint32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i32m1x6(int *rs1, vuint16mf2_t rs2, vint32m1x6_t vs3, size_t vl);
vint32m1x6_t __riscv_vluxseg6ei32_v_i32m1x6(const int *rs1, vuint32m1_t rs2, size_t vl);
vint32m1x6_t __riscv_vloxseg6ei32_v_i32m1x6(const int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i32m1x6(int *rs1, vuint32m1_t rs2, vint32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i32m1x6(int *rs1, vuint32m1_t rs2, vint32m1x6_t vs3, size_t vl);
vint32m1x6_t __riscv_vluxseg6ei64_v_i32m1x6(const int *rs1, vuint64m2_t rs2, size_t vl);
vint32m1x6_t __riscv_vloxseg6ei64_v_i32m1x6(const int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i32m1x6(int *rs1, vuint64m2_t rs2, vint32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i32m1x6(int *rs1, vuint64m2_t rs2, vint32m1x6_t vs3, size_t vl);
vfloat32m1x6_t __riscv_vundefined_f32m1x6(void);
vfloat32m1x6_t __riscv_vcreate_v_f32m1_f32m1x6(vfloat32m1_t v0, vfloat32m1_t v1, vfloat32m1_t v2, vfloat32m1_t v3, vfloat32m1_t v4, vfloat32m1_t v5);
vfloat32m1_t __riscv_vget_v_f32m1x6_f32m1(vfloat32m1x6_t src, size_t index);
vfloat32m1x6_t __riscv_vset_v_f32m1x6_f32m1(vfloat32m1x6_t dest, size_t index, vfloat32m1_t value);
vfloat32m1x6_t __riscv_vlseg6e32_v_f32m1x6(const float *rs1, size_t vl);
void __riscv_vsseg6e32_v_f32m1x6(float *rs1, vfloat32m1x6_t vs3, size_t vl);
vfloat32m1x6_t __riscv_vlseg6e32ff_v_f32m1x6(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m1x6_t __riscv_vlsseg6e32_v_f32m1x6(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg6e32_v_f32m1x6(float *rs1, long rs2, vfloat32m1x6_t vs3, size_t vl);
vfloat32m1x6_t __riscv_vluxseg6ei8_v_f32m1x6(const float *rs1, vuint8mf4_t rs2, size_t vl);
vfloat32m1x6_t __riscv_vloxseg6ei8_v_f32m1x6(const float *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_f32m1x6(float *rs1, vuint8mf4_t rs2, vfloat32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_f32m1x6(float *rs1, vuint8mf4_t rs2, vfloat32m1x6_t vs3, size_t vl);
vfloat32m1x6_t __riscv_vluxseg6ei16_v_f32m1x6(const float *rs1, vuint16mf2_t rs2, size_t vl);
vfloat32m1x6_t __riscv_vloxseg6ei16_v_f32m1x6(const float *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_f32m1x6(float *rs1, vuint16mf2_t rs2, vfloat32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_f32m1x6(float *rs1, vuint16mf2_t rs2, vfloat32m1x6_t vs3, size_t vl);
vfloat32m1x6_t __riscv_vluxseg6ei32_v_f32m1x6(const float *rs1, vuint32m1_t rs2, size_t vl);
vfloat32m1x6_t __riscv_vloxseg6ei32_v_f32m1x6(const float *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_f32m1x6(float *rs1, vuint32m1_t rs2, vfloat32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_f32m1x6(float *rs1, vuint32m1_t rs2, vfloat32m1x6_t vs3, size_t vl);
vfloat32m1x6_t __riscv_vluxseg6ei64_v_f32m1x6(const float *rs1, vuint64m2_t rs2, size_t vl);
vfloat32m1x6_t __riscv_vloxseg6ei64_v_f32m1x6(const float *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_f32m1x6(float *rs1, vuint64m2_t rs2, vfloat32m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_f32m1x6(float *rs1, vuint64m2_t rs2, vfloat32m1x6_t vs3, size_t vl);
vuint32m1x7_t __riscv_vundefined_u32m1x7(void);
vuint32m1x7_t __riscv_vcreate_v_u32m1_u32m1x7(vuint32m1_t v0, vuint32m1_t v1, vuint32m1_t v2, vuint32m1_t v3, vuint32m1_t v4, vuint32m1_t v5, vuint32m1_t v6);
vuint32m1_t __riscv_vget_v_u32m1x7_u32m1(vuint32m1x7_t src, size_t index);
vuint32m1x7_t __riscv_vset_v_u32m1x7_u32m1(vuint32m1x7_t dest, size_t index, vuint32m1_t value);
vuint32m1x7_t __riscv_vlseg7e32_v_u32m1x7(const unsigned int *rs1, size_t vl);
void __riscv_vsseg7e32_v_u32m1x7(unsigned int *rs1, vuint32m1x7_t vs3, size_t vl);
vuint32m1x7_t __riscv_vlseg7e32ff_v_u32m1x7(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m1x7_t __riscv_vlsseg7e32_v_u32m1x7(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg7e32_v_u32m1x7(unsigned int *rs1, long rs2, vuint32m1x7_t vs3, size_t vl);
vuint32m1x7_t __riscv_vluxseg7ei8_v_u32m1x7(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
vuint32m1x7_t __riscv_vloxseg7ei8_v_u32m1x7(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u32m1x7(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u32m1x7(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x7_t vs3, size_t vl);
vuint32m1x7_t __riscv_vluxseg7ei16_v_u32m1x7(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
vuint32m1x7_t __riscv_vloxseg7ei16_v_u32m1x7(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u32m1x7(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u32m1x7(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x7_t vs3, size_t vl);
vuint32m1x7_t __riscv_vluxseg7ei32_v_u32m1x7(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
vuint32m1x7_t __riscv_vloxseg7ei32_v_u32m1x7(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u32m1x7(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u32m1x7(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x7_t vs3, size_t vl);
vuint32m1x7_t __riscv_vluxseg7ei64_v_u32m1x7(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
vuint32m1x7_t __riscv_vloxseg7ei64_v_u32m1x7(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u32m1x7(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u32m1x7(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x7_t vs3, size_t vl);
vint32m1x7_t __riscv_vundefined_i32m1x7(void);
vint32m1x7_t __riscv_vcreate_v_i32m1_i32m1x7(vint32m1_t v0, vint32m1_t v1, vint32m1_t v2, vint32m1_t v3, vint32m1_t v4, vint32m1_t v5, vint32m1_t v6);
vint32m1_t __riscv_vget_v_i32m1x7_i32m1(vint32m1x7_t src, size_t index);
vint32m1x7_t __riscv_vset_v_i32m1x7_i32m1(vint32m1x7_t dest, size_t index, vint32m1_t value);
vint32m1x7_t __riscv_vlseg7e32_v_i32m1x7(const int *rs1, size_t vl);
void __riscv_vsseg7e32_v_i32m1x7(int *rs1, vint32m1x7_t vs3, size_t vl);
vint32m1x7_t __riscv_vlseg7e32ff_v_i32m1x7(const int *rs1, size_t *new_vl, size_t vl);
vint32m1x7_t __riscv_vlsseg7e32_v_i32m1x7(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg7e32_v_i32m1x7(int *rs1, long rs2, vint32m1x7_t vs3, size_t vl);
vint32m1x7_t __riscv_vluxseg7ei8_v_i32m1x7(const int *rs1, vuint8mf4_t rs2, size_t vl);
vint32m1x7_t __riscv_vloxseg7ei8_v_i32m1x7(const int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i32m1x7(int *rs1, vuint8mf4_t rs2, vint32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i32m1x7(int *rs1, vuint8mf4_t rs2, vint32m1x7_t vs3, size_t vl);
vint32m1x7_t __riscv_vluxseg7ei16_v_i32m1x7(const int *rs1, vuint16mf2_t rs2, size_t vl);
vint32m1x7_t __riscv_vloxseg7ei16_v_i32m1x7(const int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i32m1x7(int *rs1, vuint16mf2_t rs2, vint32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i32m1x7(int *rs1, vuint16mf2_t rs2, vint32m1x7_t vs3, size_t vl);
vint32m1x7_t __riscv_vluxseg7ei32_v_i32m1x7(const int *rs1, vuint32m1_t rs2, size_t vl);
vint32m1x7_t __riscv_vloxseg7ei32_v_i32m1x7(const int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i32m1x7(int *rs1, vuint32m1_t rs2, vint32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i32m1x7(int *rs1, vuint32m1_t rs2, vint32m1x7_t vs3, size_t vl);
vint32m1x7_t __riscv_vluxseg7ei64_v_i32m1x7(const int *rs1, vuint64m2_t rs2, size_t vl);
vint32m1x7_t __riscv_vloxseg7ei64_v_i32m1x7(const int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i32m1x7(int *rs1, vuint64m2_t rs2, vint32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i32m1x7(int *rs1, vuint64m2_t rs2, vint32m1x7_t vs3, size_t vl);
vfloat32m1x7_t __riscv_vundefined_f32m1x7(void);
vfloat32m1x7_t __riscv_vcreate_v_f32m1_f32m1x7(vfloat32m1_t v0, vfloat32m1_t v1, vfloat32m1_t v2, vfloat32m1_t v3, vfloat32m1_t v4, vfloat32m1_t v5, vfloat32m1_t v6);
vfloat32m1_t __riscv_vget_v_f32m1x7_f32m1(vfloat32m1x7_t src, size_t index);
vfloat32m1x7_t __riscv_vset_v_f32m1x7_f32m1(vfloat32m1x7_t dest, size_t index, vfloat32m1_t value);
vfloat32m1x7_t __riscv_vlseg7e32_v_f32m1x7(const float *rs1, size_t vl);
void __riscv_vsseg7e32_v_f32m1x7(float *rs1, vfloat32m1x7_t vs3, size_t vl);
vfloat32m1x7_t __riscv_vlseg7e32ff_v_f32m1x7(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m1x7_t __riscv_vlsseg7e32_v_f32m1x7(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg7e32_v_f32m1x7(float *rs1, long rs2, vfloat32m1x7_t vs3, size_t vl);
vfloat32m1x7_t __riscv_vluxseg7ei8_v_f32m1x7(const float *rs1, vuint8mf4_t rs2, size_t vl);
vfloat32m1x7_t __riscv_vloxseg7ei8_v_f32m1x7(const float *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_f32m1x7(float *rs1, vuint8mf4_t rs2, vfloat32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_f32m1x7(float *rs1, vuint8mf4_t rs2, vfloat32m1x7_t vs3, size_t vl);
vfloat32m1x7_t __riscv_vluxseg7ei16_v_f32m1x7(const float *rs1, vuint16mf2_t rs2, size_t vl);
vfloat32m1x7_t __riscv_vloxseg7ei16_v_f32m1x7(const float *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_f32m1x7(float *rs1, vuint16mf2_t rs2, vfloat32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_f32m1x7(float *rs1, vuint16mf2_t rs2, vfloat32m1x7_t vs3, size_t vl);
vfloat32m1x7_t __riscv_vluxseg7ei32_v_f32m1x7(const float *rs1, vuint32m1_t rs2, size_t vl);
vfloat32m1x7_t __riscv_vloxseg7ei32_v_f32m1x7(const float *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_f32m1x7(float *rs1, vuint32m1_t rs2, vfloat32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_f32m1x7(float *rs1, vuint32m1_t rs2, vfloat32m1x7_t vs3, size_t vl);
vfloat32m1x7_t __riscv_vluxseg7ei64_v_f32m1x7(const float *rs1, vuint64m2_t rs2, size_t vl);
vfloat32m1x7_t __riscv_vloxseg7ei64_v_f32m1x7(const float *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_f32m1x7(float *rs1, vuint64m2_t rs2, vfloat32m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_f32m1x7(float *rs1, vuint64m2_t rs2, vfloat32m1x7_t vs3, size_t vl);
vuint32m1x8_t __riscv_vundefined_u32m1x8(void);
vuint32m1x8_t __riscv_vcreate_v_u32m1_u32m1x8(vuint32m1_t v0, vuint32m1_t v1, vuint32m1_t v2, vuint32m1_t v3, vuint32m1_t v4, vuint32m1_t v5, vuint32m1_t v6, vuint32m1_t v7);
vuint32m1_t __riscv_vget_v_u32m1x8_u32m1(vuint32m1x8_t src, size_t index);
vuint32m1x8_t __riscv_vset_v_u32m1x8_u32m1(vuint32m1x8_t dest, size_t index, vuint32m1_t value);
vuint32m1x8_t __riscv_vlseg8e32_v_u32m1x8(const unsigned int *rs1, size_t vl);
void __riscv_vsseg8e32_v_u32m1x8(unsigned int *rs1, vuint32m1x8_t vs3, size_t vl);
vuint32m1x8_t __riscv_vlseg8e32ff_v_u32m1x8(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m1x8_t __riscv_vlsseg8e32_v_u32m1x8(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg8e32_v_u32m1x8(unsigned int *rs1, long rs2, vuint32m1x8_t vs3, size_t vl);
vuint32m1x8_t __riscv_vluxseg8ei8_v_u32m1x8(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
vuint32m1x8_t __riscv_vloxseg8ei8_v_u32m1x8(const unsigned int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u32m1x8(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u32m1x8(unsigned int *rs1, vuint8mf4_t rs2, vuint32m1x8_t vs3, size_t vl);
vuint32m1x8_t __riscv_vluxseg8ei16_v_u32m1x8(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
vuint32m1x8_t __riscv_vloxseg8ei16_v_u32m1x8(const unsigned int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u32m1x8(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u32m1x8(unsigned int *rs1, vuint16mf2_t rs2, vuint32m1x8_t vs3, size_t vl);
vuint32m1x8_t __riscv_vluxseg8ei32_v_u32m1x8(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
vuint32m1x8_t __riscv_vloxseg8ei32_v_u32m1x8(const unsigned int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u32m1x8(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u32m1x8(unsigned int *rs1, vuint32m1_t rs2, vuint32m1x8_t vs3, size_t vl);
vuint32m1x8_t __riscv_vluxseg8ei64_v_u32m1x8(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
vuint32m1x8_t __riscv_vloxseg8ei64_v_u32m1x8(const unsigned int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u32m1x8(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u32m1x8(unsigned int *rs1, vuint64m2_t rs2, vuint32m1x8_t vs3, size_t vl);
vint32m1x8_t __riscv_vundefined_i32m1x8(void);
vint32m1x8_t __riscv_vcreate_v_i32m1_i32m1x8(vint32m1_t v0, vint32m1_t v1, vint32m1_t v2, vint32m1_t v3, vint32m1_t v4, vint32m1_t v5, vint32m1_t v6, vint32m1_t v7);
vint32m1_t __riscv_vget_v_i32m1x8_i32m1(vint32m1x8_t src, size_t index);
vint32m1x8_t __riscv_vset_v_i32m1x8_i32m1(vint32m1x8_t dest, size_t index, vint32m1_t value);
vint32m1x8_t __riscv_vlseg8e32_v_i32m1x8(const int *rs1, size_t vl);
void __riscv_vsseg8e32_v_i32m1x8(int *rs1, vint32m1x8_t vs3, size_t vl);
vint32m1x8_t __riscv_vlseg8e32ff_v_i32m1x8(const int *rs1, size_t *new_vl, size_t vl);
vint32m1x8_t __riscv_vlsseg8e32_v_i32m1x8(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg8e32_v_i32m1x8(int *rs1, long rs2, vint32m1x8_t vs3, size_t vl);
vint32m1x8_t __riscv_vluxseg8ei8_v_i32m1x8(const int *rs1, vuint8mf4_t rs2, size_t vl);
vint32m1x8_t __riscv_vloxseg8ei8_v_i32m1x8(const int *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i32m1x8(int *rs1, vuint8mf4_t rs2, vint32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i32m1x8(int *rs1, vuint8mf4_t rs2, vint32m1x8_t vs3, size_t vl);
vint32m1x8_t __riscv_vluxseg8ei16_v_i32m1x8(const int *rs1, vuint16mf2_t rs2, size_t vl);
vint32m1x8_t __riscv_vloxseg8ei16_v_i32m1x8(const int *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i32m1x8(int *rs1, vuint16mf2_t rs2, vint32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i32m1x8(int *rs1, vuint16mf2_t rs2, vint32m1x8_t vs3, size_t vl);
vint32m1x8_t __riscv_vluxseg8ei32_v_i32m1x8(const int *rs1, vuint32m1_t rs2, size_t vl);
vint32m1x8_t __riscv_vloxseg8ei32_v_i32m1x8(const int *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i32m1x8(int *rs1, vuint32m1_t rs2, vint32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i32m1x8(int *rs1, vuint32m1_t rs2, vint32m1x8_t vs3, size_t vl);
vint32m1x8_t __riscv_vluxseg8ei64_v_i32m1x8(const int *rs1, vuint64m2_t rs2, size_t vl);
vint32m1x8_t __riscv_vloxseg8ei64_v_i32m1x8(const int *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i32m1x8(int *rs1, vuint64m2_t rs2, vint32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i32m1x8(int *rs1, vuint64m2_t rs2, vint32m1x8_t vs3, size_t vl);
vfloat32m1x8_t __riscv_vundefined_f32m1x8(void);
vfloat32m1x8_t __riscv_vcreate_v_f32m1_f32m1x8(vfloat32m1_t v0, vfloat32m1_t v1, vfloat32m1_t v2, vfloat32m1_t v3, vfloat32m1_t v4, vfloat32m1_t v5, vfloat32m1_t v6, vfloat32m1_t v7);
vfloat32m1_t __riscv_vget_v_f32m1x8_f32m1(vfloat32m1x8_t src, size_t index);
vfloat32m1x8_t __riscv_vset_v_f32m1x8_f32m1(vfloat32m1x8_t dest, size_t index, vfloat32m1_t value);
vfloat32m1x8_t __riscv_vlseg8e32_v_f32m1x8(const float *rs1, size_t vl);
void __riscv_vsseg8e32_v_f32m1x8(float *rs1, vfloat32m1x8_t vs3, size_t vl);
vfloat32m1x8_t __riscv_vlseg8e32ff_v_f32m1x8(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m1x8_t __riscv_vlsseg8e32_v_f32m1x8(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg8e32_v_f32m1x8(float *rs1, long rs2, vfloat32m1x8_t vs3, size_t vl);
vfloat32m1x8_t __riscv_vluxseg8ei8_v_f32m1x8(const float *rs1, vuint8mf4_t rs2, size_t vl);
vfloat32m1x8_t __riscv_vloxseg8ei8_v_f32m1x8(const float *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_f32m1x8(float *rs1, vuint8mf4_t rs2, vfloat32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_f32m1x8(float *rs1, vuint8mf4_t rs2, vfloat32m1x8_t vs3, size_t vl);
vfloat32m1x8_t __riscv_vluxseg8ei16_v_f32m1x8(const float *rs1, vuint16mf2_t rs2, size_t vl);
vfloat32m1x8_t __riscv_vloxseg8ei16_v_f32m1x8(const float *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_f32m1x8(float *rs1, vuint16mf2_t rs2, vfloat32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_f32m1x8(float *rs1, vuint16mf2_t rs2, vfloat32m1x8_t vs3, size_t vl);
vfloat32m1x8_t __riscv_vluxseg8ei32_v_f32m1x8(const float *rs1, vuint32m1_t rs2, size_t vl);
vfloat32m1x8_t __riscv_vloxseg8ei32_v_f32m1x8(const float *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_f32m1x8(float *rs1, vuint32m1_t rs2, vfloat32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_f32m1x8(float *rs1, vuint32m1_t rs2, vfloat32m1x8_t vs3, size_t vl);
vfloat32m1x8_t __riscv_vluxseg8ei64_v_f32m1x8(const float *rs1, vuint64m2_t rs2, size_t vl);
vfloat32m1x8_t __riscv_vloxseg8ei64_v_f32m1x8(const float *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_f32m1x8(float *rs1, vuint64m2_t rs2, vfloat32m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_f32m1x8(float *rs1, vuint64m2_t rs2, vfloat32m1x8_t vs3, size_t vl);
vuint32m2x2_t __riscv_vundefined_u32m2x2(void);
vuint32m2x2_t __riscv_vcreate_v_u32m2_u32m2x2(vuint32m2_t v0, vuint32m2_t v1);
vuint32m2_t __riscv_vget_v_u32m2x2_u32m2(vuint32m2x2_t src, size_t index);
vuint32m2x2_t __riscv_vset_v_u32m2x2_u32m2(vuint32m2x2_t dest, size_t index, vuint32m2_t value);
vuint32m2x2_t __riscv_vlseg2e32_v_u32m2x2(const unsigned int *rs1, size_t vl);
void __riscv_vsseg2e32_v_u32m2x2(unsigned int *rs1, vuint32m2x2_t vs3, size_t vl);
vuint32m2x2_t __riscv_vlseg2e32ff_v_u32m2x2(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m2x2_t __riscv_vlsseg2e32_v_u32m2x2(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_u32m2x2(unsigned int *rs1, long rs2, vuint32m2x2_t vs3, size_t vl);
vuint32m2x2_t __riscv_vluxseg2ei8_v_u32m2x2(const unsigned int *rs1, vuint8mf2_t rs2, size_t vl);
vuint32m2x2_t __riscv_vloxseg2ei8_v_u32m2x2(const unsigned int *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u32m2x2(unsigned int *rs1, vuint8mf2_t rs2, vuint32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u32m2x2(unsigned int *rs1, vuint8mf2_t rs2, vuint32m2x2_t vs3, size_t vl);
vuint32m2x2_t __riscv_vluxseg2ei16_v_u32m2x2(const unsigned int *rs1, vuint16m1_t rs2, size_t vl);
vuint32m2x2_t __riscv_vloxseg2ei16_v_u32m2x2(const unsigned int *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u32m2x2(unsigned int *rs1, vuint16m1_t rs2, vuint32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u32m2x2(unsigned int *rs1, vuint16m1_t rs2, vuint32m2x2_t vs3, size_t vl);
vuint32m2x2_t __riscv_vluxseg2ei32_v_u32m2x2(const unsigned int *rs1, vuint32m2_t rs2, size_t vl);
vuint32m2x2_t __riscv_vloxseg2ei32_v_u32m2x2(const unsigned int *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u32m2x2(unsigned int *rs1, vuint32m2_t rs2, vuint32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u32m2x2(unsigned int *rs1, vuint32m2_t rs2, vuint32m2x2_t vs3, size_t vl);
vuint32m2x2_t __riscv_vluxseg2ei64_v_u32m2x2(const unsigned int *rs1, vuint64m4_t rs2, size_t vl);
vuint32m2x2_t __riscv_vloxseg2ei64_v_u32m2x2(const unsigned int *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u32m2x2(unsigned int *rs1, vuint64m4_t rs2, vuint32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u32m2x2(unsigned int *rs1, vuint64m4_t rs2, vuint32m2x2_t vs3, size_t vl);
vint32m2x2_t __riscv_vundefined_i32m2x2(void);
vint32m2x2_t __riscv_vcreate_v_i32m2_i32m2x2(vint32m2_t v0, vint32m2_t v1);
vint32m2_t __riscv_vget_v_i32m2x2_i32m2(vint32m2x2_t src, size_t index);
vint32m2x2_t __riscv_vset_v_i32m2x2_i32m2(vint32m2x2_t dest, size_t index, vint32m2_t value);
vint32m2x2_t __riscv_vlseg2e32_v_i32m2x2(const int *rs1, size_t vl);
void __riscv_vsseg2e32_v_i32m2x2(int *rs1, vint32m2x2_t vs3, size_t vl);
vint32m2x2_t __riscv_vlseg2e32ff_v_i32m2x2(const int *rs1, size_t *new_vl, size_t vl);
vint32m2x2_t __riscv_vlsseg2e32_v_i32m2x2(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_i32m2x2(int *rs1, long rs2, vint32m2x2_t vs3, size_t vl);
vint32m2x2_t __riscv_vluxseg2ei8_v_i32m2x2(const int *rs1, vuint8mf2_t rs2, size_t vl);
vint32m2x2_t __riscv_vloxseg2ei8_v_i32m2x2(const int *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i32m2x2(int *rs1, vuint8mf2_t rs2, vint32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i32m2x2(int *rs1, vuint8mf2_t rs2, vint32m2x2_t vs3, size_t vl);
vint32m2x2_t __riscv_vluxseg2ei16_v_i32m2x2(const int *rs1, vuint16m1_t rs2, size_t vl);
vint32m2x2_t __riscv_vloxseg2ei16_v_i32m2x2(const int *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i32m2x2(int *rs1, vuint16m1_t rs2, vint32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i32m2x2(int *rs1, vuint16m1_t rs2, vint32m2x2_t vs3, size_t vl);
vint32m2x2_t __riscv_vluxseg2ei32_v_i32m2x2(const int *rs1, vuint32m2_t rs2, size_t vl);
vint32m2x2_t __riscv_vloxseg2ei32_v_i32m2x2(const int *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i32m2x2(int *rs1, vuint32m2_t rs2, vint32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i32m2x2(int *rs1, vuint32m2_t rs2, vint32m2x2_t vs3, size_t vl);
vint32m2x2_t __riscv_vluxseg2ei64_v_i32m2x2(const int *rs1, vuint64m4_t rs2, size_t vl);
vint32m2x2_t __riscv_vloxseg2ei64_v_i32m2x2(const int *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i32m2x2(int *rs1, vuint64m4_t rs2, vint32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i32m2x2(int *rs1, vuint64m4_t rs2, vint32m2x2_t vs3, size_t vl);
vfloat32m2x2_t __riscv_vundefined_f32m2x2(void);
vfloat32m2x2_t __riscv_vcreate_v_f32m2_f32m2x2(vfloat32m2_t v0, vfloat32m2_t v1);
vfloat32m2_t __riscv_vget_v_f32m2x2_f32m2(vfloat32m2x2_t src, size_t index);
vfloat32m2x2_t __riscv_vset_v_f32m2x2_f32m2(vfloat32m2x2_t dest, size_t index, vfloat32m2_t value);
vfloat32m2x2_t __riscv_vlseg2e32_v_f32m2x2(const float *rs1, size_t vl);
void __riscv_vsseg2e32_v_f32m2x2(float *rs1, vfloat32m2x2_t vs3, size_t vl);
vfloat32m2x2_t __riscv_vlseg2e32ff_v_f32m2x2(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m2x2_t __riscv_vlsseg2e32_v_f32m2x2(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_f32m2x2(float *rs1, long rs2, vfloat32m2x2_t vs3, size_t vl);
vfloat32m2x2_t __riscv_vluxseg2ei8_v_f32m2x2(const float *rs1, vuint8mf2_t rs2, size_t vl);
vfloat32m2x2_t __riscv_vloxseg2ei8_v_f32m2x2(const float *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_f32m2x2(float *rs1, vuint8mf2_t rs2, vfloat32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_f32m2x2(float *rs1, vuint8mf2_t rs2, vfloat32m2x2_t vs3, size_t vl);
vfloat32m2x2_t __riscv_vluxseg2ei16_v_f32m2x2(const float *rs1, vuint16m1_t rs2, size_t vl);
vfloat32m2x2_t __riscv_vloxseg2ei16_v_f32m2x2(const float *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_f32m2x2(float *rs1, vuint16m1_t rs2, vfloat32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_f32m2x2(float *rs1, vuint16m1_t rs2, vfloat32m2x2_t vs3, size_t vl);
vfloat32m2x2_t __riscv_vluxseg2ei32_v_f32m2x2(const float *rs1, vuint32m2_t rs2, size_t vl);
vfloat32m2x2_t __riscv_vloxseg2ei32_v_f32m2x2(const float *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_f32m2x2(float *rs1, vuint32m2_t rs2, vfloat32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_f32m2x2(float *rs1, vuint32m2_t rs2, vfloat32m2x2_t vs3, size_t vl);
vfloat32m2x2_t __riscv_vluxseg2ei64_v_f32m2x2(const float *rs1, vuint64m4_t rs2, size_t vl);
vfloat32m2x2_t __riscv_vloxseg2ei64_v_f32m2x2(const float *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_f32m2x2(float *rs1, vuint64m4_t rs2, vfloat32m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_f32m2x2(float *rs1, vuint64m4_t rs2, vfloat32m2x2_t vs3, size_t vl);
vuint32m2x3_t __riscv_vundefined_u32m2x3(void);
vuint32m2x3_t __riscv_vcreate_v_u32m2_u32m2x3(vuint32m2_t v0, vuint32m2_t v1, vuint32m2_t v2);
vuint32m2_t __riscv_vget_v_u32m2x3_u32m2(vuint32m2x3_t src, size_t index);
vuint32m2x3_t __riscv_vset_v_u32m2x3_u32m2(vuint32m2x3_t dest, size_t index, vuint32m2_t value);
vuint32m2x3_t __riscv_vlseg3e32_v_u32m2x3(const unsigned int *rs1, size_t vl);
void __riscv_vsseg3e32_v_u32m2x3(unsigned int *rs1, vuint32m2x3_t vs3, size_t vl);
vuint32m2x3_t __riscv_vlseg3e32ff_v_u32m2x3(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m2x3_t __riscv_vlsseg3e32_v_u32m2x3(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_u32m2x3(unsigned int *rs1, long rs2, vuint32m2x3_t vs3, size_t vl);
vuint32m2x3_t __riscv_vluxseg3ei8_v_u32m2x3(const unsigned int *rs1, vuint8mf2_t rs2, size_t vl);
vuint32m2x3_t __riscv_vloxseg3ei8_v_u32m2x3(const unsigned int *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u32m2x3(unsigned int *rs1, vuint8mf2_t rs2, vuint32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u32m2x3(unsigned int *rs1, vuint8mf2_t rs2, vuint32m2x3_t vs3, size_t vl);
vuint32m2x3_t __riscv_vluxseg3ei16_v_u32m2x3(const unsigned int *rs1, vuint16m1_t rs2, size_t vl);
vuint32m2x3_t __riscv_vloxseg3ei16_v_u32m2x3(const unsigned int *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u32m2x3(unsigned int *rs1, vuint16m1_t rs2, vuint32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u32m2x3(unsigned int *rs1, vuint16m1_t rs2, vuint32m2x3_t vs3, size_t vl);
vuint32m2x3_t __riscv_vluxseg3ei32_v_u32m2x3(const unsigned int *rs1, vuint32m2_t rs2, size_t vl);
vuint32m2x3_t __riscv_vloxseg3ei32_v_u32m2x3(const unsigned int *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u32m2x3(unsigned int *rs1, vuint32m2_t rs2, vuint32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u32m2x3(unsigned int *rs1, vuint32m2_t rs2, vuint32m2x3_t vs3, size_t vl);
vuint32m2x3_t __riscv_vluxseg3ei64_v_u32m2x3(const unsigned int *rs1, vuint64m4_t rs2, size_t vl);
vuint32m2x3_t __riscv_vloxseg3ei64_v_u32m2x3(const unsigned int *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u32m2x3(unsigned int *rs1, vuint64m4_t rs2, vuint32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u32m2x3(unsigned int *rs1, vuint64m4_t rs2, vuint32m2x3_t vs3, size_t vl);
vint32m2x3_t __riscv_vundefined_i32m2x3(void);
vint32m2x3_t __riscv_vcreate_v_i32m2_i32m2x3(vint32m2_t v0, vint32m2_t v1, vint32m2_t v2);
vint32m2_t __riscv_vget_v_i32m2x3_i32m2(vint32m2x3_t src, size_t index);
vint32m2x3_t __riscv_vset_v_i32m2x3_i32m2(vint32m2x3_t dest, size_t index, vint32m2_t value);
vint32m2x3_t __riscv_vlseg3e32_v_i32m2x3(const int *rs1, size_t vl);
void __riscv_vsseg3e32_v_i32m2x3(int *rs1, vint32m2x3_t vs3, size_t vl);
vint32m2x3_t __riscv_vlseg3e32ff_v_i32m2x3(const int *rs1, size_t *new_vl, size_t vl);
vint32m2x3_t __riscv_vlsseg3e32_v_i32m2x3(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_i32m2x3(int *rs1, long rs2, vint32m2x3_t vs3, size_t vl);
vint32m2x3_t __riscv_vluxseg3ei8_v_i32m2x3(const int *rs1, vuint8mf2_t rs2, size_t vl);
vint32m2x3_t __riscv_vloxseg3ei8_v_i32m2x3(const int *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i32m2x3(int *rs1, vuint8mf2_t rs2, vint32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i32m2x3(int *rs1, vuint8mf2_t rs2, vint32m2x3_t vs3, size_t vl);
vint32m2x3_t __riscv_vluxseg3ei16_v_i32m2x3(const int *rs1, vuint16m1_t rs2, size_t vl);
vint32m2x3_t __riscv_vloxseg3ei16_v_i32m2x3(const int *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i32m2x3(int *rs1, vuint16m1_t rs2, vint32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i32m2x3(int *rs1, vuint16m1_t rs2, vint32m2x3_t vs3, size_t vl);
vint32m2x3_t __riscv_vluxseg3ei32_v_i32m2x3(const int *rs1, vuint32m2_t rs2, size_t vl);
vint32m2x3_t __riscv_vloxseg3ei32_v_i32m2x3(const int *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i32m2x3(int *rs1, vuint32m2_t rs2, vint32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i32m2x3(int *rs1, vuint32m2_t rs2, vint32m2x3_t vs3, size_t vl);
vint32m2x3_t __riscv_vluxseg3ei64_v_i32m2x3(const int *rs1, vuint64m4_t rs2, size_t vl);
vint32m2x3_t __riscv_vloxseg3ei64_v_i32m2x3(const int *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i32m2x3(int *rs1, vuint64m4_t rs2, vint32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i32m2x3(int *rs1, vuint64m4_t rs2, vint32m2x3_t vs3, size_t vl);
vfloat32m2x3_t __riscv_vundefined_f32m2x3(void);
vfloat32m2x3_t __riscv_vcreate_v_f32m2_f32m2x3(vfloat32m2_t v0, vfloat32m2_t v1, vfloat32m2_t v2);
vfloat32m2_t __riscv_vget_v_f32m2x3_f32m2(vfloat32m2x3_t src, size_t index);
vfloat32m2x3_t __riscv_vset_v_f32m2x3_f32m2(vfloat32m2x3_t dest, size_t index, vfloat32m2_t value);
vfloat32m2x3_t __riscv_vlseg3e32_v_f32m2x3(const float *rs1, size_t vl);
void __riscv_vsseg3e32_v_f32m2x3(float *rs1, vfloat32m2x3_t vs3, size_t vl);
vfloat32m2x3_t __riscv_vlseg3e32ff_v_f32m2x3(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m2x3_t __riscv_vlsseg3e32_v_f32m2x3(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg3e32_v_f32m2x3(float *rs1, long rs2, vfloat32m2x3_t vs3, size_t vl);
vfloat32m2x3_t __riscv_vluxseg3ei8_v_f32m2x3(const float *rs1, vuint8mf2_t rs2, size_t vl);
vfloat32m2x3_t __riscv_vloxseg3ei8_v_f32m2x3(const float *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_f32m2x3(float *rs1, vuint8mf2_t rs2, vfloat32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_f32m2x3(float *rs1, vuint8mf2_t rs2, vfloat32m2x3_t vs3, size_t vl);
vfloat32m2x3_t __riscv_vluxseg3ei16_v_f32m2x3(const float *rs1, vuint16m1_t rs2, size_t vl);
vfloat32m2x3_t __riscv_vloxseg3ei16_v_f32m2x3(const float *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_f32m2x3(float *rs1, vuint16m1_t rs2, vfloat32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_f32m2x3(float *rs1, vuint16m1_t rs2, vfloat32m2x3_t vs3, size_t vl);
vfloat32m2x3_t __riscv_vluxseg3ei32_v_f32m2x3(const float *rs1, vuint32m2_t rs2, size_t vl);
vfloat32m2x3_t __riscv_vloxseg3ei32_v_f32m2x3(const float *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_f32m2x3(float *rs1, vuint32m2_t rs2, vfloat32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_f32m2x3(float *rs1, vuint32m2_t rs2, vfloat32m2x3_t vs3, size_t vl);
vfloat32m2x3_t __riscv_vluxseg3ei64_v_f32m2x3(const float *rs1, vuint64m4_t rs2, size_t vl);
vfloat32m2x3_t __riscv_vloxseg3ei64_v_f32m2x3(const float *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_f32m2x3(float *rs1, vuint64m4_t rs2, vfloat32m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_f32m2x3(float *rs1, vuint64m4_t rs2, vfloat32m2x3_t vs3, size_t vl);
vuint32m2x4_t __riscv_vundefined_u32m2x4(void);
vuint32m2x4_t __riscv_vcreate_v_u32m2_u32m2x4(vuint32m2_t v0, vuint32m2_t v1, vuint32m2_t v2, vuint32m2_t v3);
vuint32m2_t __riscv_vget_v_u32m2x4_u32m2(vuint32m2x4_t src, size_t index);
vuint32m2x4_t __riscv_vset_v_u32m2x4_u32m2(vuint32m2x4_t dest, size_t index, vuint32m2_t value);
vuint32m2x4_t __riscv_vlseg4e32_v_u32m2x4(const unsigned int *rs1, size_t vl);
void __riscv_vsseg4e32_v_u32m2x4(unsigned int *rs1, vuint32m2x4_t vs3, size_t vl);
vuint32m2x4_t __riscv_vlseg4e32ff_v_u32m2x4(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m2x4_t __riscv_vlsseg4e32_v_u32m2x4(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_u32m2x4(unsigned int *rs1, long rs2, vuint32m2x4_t vs3, size_t vl);
vuint32m2x4_t __riscv_vluxseg4ei8_v_u32m2x4(const unsigned int *rs1, vuint8mf2_t rs2, size_t vl);
vuint32m2x4_t __riscv_vloxseg4ei8_v_u32m2x4(const unsigned int *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u32m2x4(unsigned int *rs1, vuint8mf2_t rs2, vuint32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u32m2x4(unsigned int *rs1, vuint8mf2_t rs2, vuint32m2x4_t vs3, size_t vl);
vuint32m2x4_t __riscv_vluxseg4ei16_v_u32m2x4(const unsigned int *rs1, vuint16m1_t rs2, size_t vl);
vuint32m2x4_t __riscv_vloxseg4ei16_v_u32m2x4(const unsigned int *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u32m2x4(unsigned int *rs1, vuint16m1_t rs2, vuint32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u32m2x4(unsigned int *rs1, vuint16m1_t rs2, vuint32m2x4_t vs3, size_t vl);
vuint32m2x4_t __riscv_vluxseg4ei32_v_u32m2x4(const unsigned int *rs1, vuint32m2_t rs2, size_t vl);
vuint32m2x4_t __riscv_vloxseg4ei32_v_u32m2x4(const unsigned int *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u32m2x4(unsigned int *rs1, vuint32m2_t rs2, vuint32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u32m2x4(unsigned int *rs1, vuint32m2_t rs2, vuint32m2x4_t vs3, size_t vl);
vuint32m2x4_t __riscv_vluxseg4ei64_v_u32m2x4(const unsigned int *rs1, vuint64m4_t rs2, size_t vl);
vuint32m2x4_t __riscv_vloxseg4ei64_v_u32m2x4(const unsigned int *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u32m2x4(unsigned int *rs1, vuint64m4_t rs2, vuint32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u32m2x4(unsigned int *rs1, vuint64m4_t rs2, vuint32m2x4_t vs3, size_t vl);
vint32m2x4_t __riscv_vundefined_i32m2x4(void);
vint32m2x4_t __riscv_vcreate_v_i32m2_i32m2x4(vint32m2_t v0, vint32m2_t v1, vint32m2_t v2, vint32m2_t v3);
vint32m2_t __riscv_vget_v_i32m2x4_i32m2(vint32m2x4_t src, size_t index);
vint32m2x4_t __riscv_vset_v_i32m2x4_i32m2(vint32m2x4_t dest, size_t index, vint32m2_t value);
vint32m2x4_t __riscv_vlseg4e32_v_i32m2x4(const int *rs1, size_t vl);
void __riscv_vsseg4e32_v_i32m2x4(int *rs1, vint32m2x4_t vs3, size_t vl);
vint32m2x4_t __riscv_vlseg4e32ff_v_i32m2x4(const int *rs1, size_t *new_vl, size_t vl);
vint32m2x4_t __riscv_vlsseg4e32_v_i32m2x4(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_i32m2x4(int *rs1, long rs2, vint32m2x4_t vs3, size_t vl);
vint32m2x4_t __riscv_vluxseg4ei8_v_i32m2x4(const int *rs1, vuint8mf2_t rs2, size_t vl);
vint32m2x4_t __riscv_vloxseg4ei8_v_i32m2x4(const int *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i32m2x4(int *rs1, vuint8mf2_t rs2, vint32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i32m2x4(int *rs1, vuint8mf2_t rs2, vint32m2x4_t vs3, size_t vl);
vint32m2x4_t __riscv_vluxseg4ei16_v_i32m2x4(const int *rs1, vuint16m1_t rs2, size_t vl);
vint32m2x4_t __riscv_vloxseg4ei16_v_i32m2x4(const int *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i32m2x4(int *rs1, vuint16m1_t rs2, vint32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i32m2x4(int *rs1, vuint16m1_t rs2, vint32m2x4_t vs3, size_t vl);
vint32m2x4_t __riscv_vluxseg4ei32_v_i32m2x4(const int *rs1, vuint32m2_t rs2, size_t vl);
vint32m2x4_t __riscv_vloxseg4ei32_v_i32m2x4(const int *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i32m2x4(int *rs1, vuint32m2_t rs2, vint32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i32m2x4(int *rs1, vuint32m2_t rs2, vint32m2x4_t vs3, size_t vl);
vint32m2x4_t __riscv_vluxseg4ei64_v_i32m2x4(const int *rs1, vuint64m4_t rs2, size_t vl);
vint32m2x4_t __riscv_vloxseg4ei64_v_i32m2x4(const int *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i32m2x4(int *rs1, vuint64m4_t rs2, vint32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i32m2x4(int *rs1, vuint64m4_t rs2, vint32m2x4_t vs3, size_t vl);
vfloat32m2x4_t __riscv_vundefined_f32m2x4(void);
vfloat32m2x4_t __riscv_vcreate_v_f32m2_f32m2x4(vfloat32m2_t v0, vfloat32m2_t v1, vfloat32m2_t v2, vfloat32m2_t v3);
vfloat32m2_t __riscv_vget_v_f32m2x4_f32m2(vfloat32m2x4_t src, size_t index);
vfloat32m2x4_t __riscv_vset_v_f32m2x4_f32m2(vfloat32m2x4_t dest, size_t index, vfloat32m2_t value);
vfloat32m2x4_t __riscv_vlseg4e32_v_f32m2x4(const float *rs1, size_t vl);
void __riscv_vsseg4e32_v_f32m2x4(float *rs1, vfloat32m2x4_t vs3, size_t vl);
vfloat32m2x4_t __riscv_vlseg4e32ff_v_f32m2x4(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m2x4_t __riscv_vlsseg4e32_v_f32m2x4(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg4e32_v_f32m2x4(float *rs1, long rs2, vfloat32m2x4_t vs3, size_t vl);
vfloat32m2x4_t __riscv_vluxseg4ei8_v_f32m2x4(const float *rs1, vuint8mf2_t rs2, size_t vl);
vfloat32m2x4_t __riscv_vloxseg4ei8_v_f32m2x4(const float *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_f32m2x4(float *rs1, vuint8mf2_t rs2, vfloat32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_f32m2x4(float *rs1, vuint8mf2_t rs2, vfloat32m2x4_t vs3, size_t vl);
vfloat32m2x4_t __riscv_vluxseg4ei16_v_f32m2x4(const float *rs1, vuint16m1_t rs2, size_t vl);
vfloat32m2x4_t __riscv_vloxseg4ei16_v_f32m2x4(const float *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_f32m2x4(float *rs1, vuint16m1_t rs2, vfloat32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_f32m2x4(float *rs1, vuint16m1_t rs2, vfloat32m2x4_t vs3, size_t vl);
vfloat32m2x4_t __riscv_vluxseg4ei32_v_f32m2x4(const float *rs1, vuint32m2_t rs2, size_t vl);
vfloat32m2x4_t __riscv_vloxseg4ei32_v_f32m2x4(const float *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_f32m2x4(float *rs1, vuint32m2_t rs2, vfloat32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_f32m2x4(float *rs1, vuint32m2_t rs2, vfloat32m2x4_t vs3, size_t vl);
vfloat32m2x4_t __riscv_vluxseg4ei64_v_f32m2x4(const float *rs1, vuint64m4_t rs2, size_t vl);
vfloat32m2x4_t __riscv_vloxseg4ei64_v_f32m2x4(const float *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_f32m2x4(float *rs1, vuint64m4_t rs2, vfloat32m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_f32m2x4(float *rs1, vuint64m4_t rs2, vfloat32m2x4_t vs3, size_t vl);
vuint32m4x2_t __riscv_vundefined_u32m4x2(void);
vuint32m4x2_t __riscv_vcreate_v_u32m4_u32m4x2(vuint32m4_t v0, vuint32m4_t v1);
vuint32m4_t __riscv_vget_v_u32m4x2_u32m4(vuint32m4x2_t src, size_t index);
vuint32m4x2_t __riscv_vset_v_u32m4x2_u32m4(vuint32m4x2_t dest, size_t index, vuint32m4_t value);
vuint32m4x2_t __riscv_vlseg2e32_v_u32m4x2(const unsigned int *rs1, size_t vl);
void __riscv_vsseg2e32_v_u32m4x2(unsigned int *rs1, vuint32m4x2_t vs3, size_t vl);
vuint32m4x2_t __riscv_vlseg2e32ff_v_u32m4x2(const unsigned int *rs1, size_t *new_vl, size_t vl);
vuint32m4x2_t __riscv_vlsseg2e32_v_u32m4x2(const unsigned int *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_u32m4x2(unsigned int *rs1, long rs2, vuint32m4x2_t vs3, size_t vl);
vuint32m4x2_t __riscv_vluxseg2ei8_v_u32m4x2(const unsigned int *rs1, vuint8m1_t rs2, size_t vl);
vuint32m4x2_t __riscv_vloxseg2ei8_v_u32m4x2(const unsigned int *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u32m4x2(unsigned int *rs1, vuint8m1_t rs2, vuint32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u32m4x2(unsigned int *rs1, vuint8m1_t rs2, vuint32m4x2_t vs3, size_t vl);
vuint32m4x2_t __riscv_vluxseg2ei16_v_u32m4x2(const unsigned int *rs1, vuint16m2_t rs2, size_t vl);
vuint32m4x2_t __riscv_vloxseg2ei16_v_u32m4x2(const unsigned int *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u32m4x2(unsigned int *rs1, vuint16m2_t rs2, vuint32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u32m4x2(unsigned int *rs1, vuint16m2_t rs2, vuint32m4x2_t vs3, size_t vl);
vuint32m4x2_t __riscv_vluxseg2ei32_v_u32m4x2(const unsigned int *rs1, vuint32m4_t rs2, size_t vl);
vuint32m4x2_t __riscv_vloxseg2ei32_v_u32m4x2(const unsigned int *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u32m4x2(unsigned int *rs1, vuint32m4_t rs2, vuint32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u32m4x2(unsigned int *rs1, vuint32m4_t rs2, vuint32m4x2_t vs3, size_t vl);
vuint32m4x2_t __riscv_vluxseg2ei64_v_u32m4x2(const unsigned int *rs1, vuint64m8_t rs2, size_t vl);
vuint32m4x2_t __riscv_vloxseg2ei64_v_u32m4x2(const unsigned int *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u32m4x2(unsigned int *rs1, vuint64m8_t rs2, vuint32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u32m4x2(unsigned int *rs1, vuint64m8_t rs2, vuint32m4x2_t vs3, size_t vl);
vint32m4x2_t __riscv_vundefined_i32m4x2(void);
vint32m4x2_t __riscv_vcreate_v_i32m4_i32m4x2(vint32m4_t v0, vint32m4_t v1);
vint32m4_t __riscv_vget_v_i32m4x2_i32m4(vint32m4x2_t src, size_t index);
vint32m4x2_t __riscv_vset_v_i32m4x2_i32m4(vint32m4x2_t dest, size_t index, vint32m4_t value);
vint32m4x2_t __riscv_vlseg2e32_v_i32m4x2(const int *rs1, size_t vl);
void __riscv_vsseg2e32_v_i32m4x2(int *rs1, vint32m4x2_t vs3, size_t vl);
vint32m4x2_t __riscv_vlseg2e32ff_v_i32m4x2(const int *rs1, size_t *new_vl, size_t vl);
vint32m4x2_t __riscv_vlsseg2e32_v_i32m4x2(const int *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_i32m4x2(int *rs1, long rs2, vint32m4x2_t vs3, size_t vl);
vint32m4x2_t __riscv_vluxseg2ei8_v_i32m4x2(const int *rs1, vuint8m1_t rs2, size_t vl);
vint32m4x2_t __riscv_vloxseg2ei8_v_i32m4x2(const int *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i32m4x2(int *rs1, vuint8m1_t rs2, vint32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i32m4x2(int *rs1, vuint8m1_t rs2, vint32m4x2_t vs3, size_t vl);
vint32m4x2_t __riscv_vluxseg2ei16_v_i32m4x2(const int *rs1, vuint16m2_t rs2, size_t vl);
vint32m4x2_t __riscv_vloxseg2ei16_v_i32m4x2(const int *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i32m4x2(int *rs1, vuint16m2_t rs2, vint32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i32m4x2(int *rs1, vuint16m2_t rs2, vint32m4x2_t vs3, size_t vl);
vint32m4x2_t __riscv_vluxseg2ei32_v_i32m4x2(const int *rs1, vuint32m4_t rs2, size_t vl);
vint32m4x2_t __riscv_vloxseg2ei32_v_i32m4x2(const int *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i32m4x2(int *rs1, vuint32m4_t rs2, vint32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i32m4x2(int *rs1, vuint32m4_t rs2, vint32m4x2_t vs3, size_t vl);
vint32m4x2_t __riscv_vluxseg2ei64_v_i32m4x2(const int *rs1, vuint64m8_t rs2, size_t vl);
vint32m4x2_t __riscv_vloxseg2ei64_v_i32m4x2(const int *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i32m4x2(int *rs1, vuint64m8_t rs2, vint32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i32m4x2(int *rs1, vuint64m8_t rs2, vint32m4x2_t vs3, size_t vl);
vfloat32m4x2_t __riscv_vundefined_f32m4x2(void);
vfloat32m4x2_t __riscv_vcreate_v_f32m4_f32m4x2(vfloat32m4_t v0, vfloat32m4_t v1);
vfloat32m4_t __riscv_vget_v_f32m4x2_f32m4(vfloat32m4x2_t src, size_t index);
vfloat32m4x2_t __riscv_vset_v_f32m4x2_f32m4(vfloat32m4x2_t dest, size_t index, vfloat32m4_t value);
vfloat32m4x2_t __riscv_vlseg2e32_v_f32m4x2(const float *rs1, size_t vl);
void __riscv_vsseg2e32_v_f32m4x2(float *rs1, vfloat32m4x2_t vs3, size_t vl);
vfloat32m4x2_t __riscv_vlseg2e32ff_v_f32m4x2(const float *rs1, size_t *new_vl, size_t vl);
vfloat32m4x2_t __riscv_vlsseg2e32_v_f32m4x2(const float *rs1, long rs2, size_t vl);
void __riscv_vssseg2e32_v_f32m4x2(float *rs1, long rs2, vfloat32m4x2_t vs3, size_t vl);
vfloat32m4x2_t __riscv_vluxseg2ei8_v_f32m4x2(const float *rs1, vuint8m1_t rs2, size_t vl);
vfloat32m4x2_t __riscv_vloxseg2ei8_v_f32m4x2(const float *rs1, vuint8m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_f32m4x2(float *rs1, vuint8m1_t rs2, vfloat32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_f32m4x2(float *rs1, vuint8m1_t rs2, vfloat32m4x2_t vs3, size_t vl);
vfloat32m4x2_t __riscv_vluxseg2ei16_v_f32m4x2(const float *rs1, vuint16m2_t rs2, size_t vl);
vfloat32m4x2_t __riscv_vloxseg2ei16_v_f32m4x2(const float *rs1, vuint16m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_f32m4x2(float *rs1, vuint16m2_t rs2, vfloat32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_f32m4x2(float *rs1, vuint16m2_t rs2, vfloat32m4x2_t vs3, size_t vl);
vfloat32m4x2_t __riscv_vluxseg2ei32_v_f32m4x2(const float *rs1, vuint32m4_t rs2, size_t vl);
vfloat32m4x2_t __riscv_vloxseg2ei32_v_f32m4x2(const float *rs1, vuint32m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_f32m4x2(float *rs1, vuint32m4_t rs2, vfloat32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_f32m4x2(float *rs1, vuint32m4_t rs2, vfloat32m4x2_t vs3, size_t vl);
vfloat32m4x2_t __riscv_vluxseg2ei64_v_f32m4x2(const float *rs1, vuint64m8_t rs2, size_t vl);
vfloat32m4x2_t __riscv_vloxseg2ei64_v_f32m4x2(const float *rs1, vuint64m8_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_f32m4x2(float *rs1, vuint64m8_t rs2, vfloat32m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_f32m4x2(float *rs1, vuint64m8_t rs2, vfloat32m4x2_t vs3, size_t vl);
vuint64m1x2_t __riscv_vundefined_u64m1x2(void);
vuint64m1x2_t __riscv_vcreate_v_u64m1_u64m1x2(vuint64m1_t v0, vuint64m1_t v1);
vuint64m1_t __riscv_vget_v_u64m1x2_u64m1(vuint64m1x2_t src, size_t index);
vuint64m1x2_t __riscv_vset_v_u64m1x2_u64m1(vuint64m1x2_t dest, size_t index, vuint64m1_t value);
vuint64m1x2_t __riscv_vlseg2e64_v_u64m1x2(const uint64_t *rs1, size_t vl);
void __riscv_vsseg2e64_v_u64m1x2(uint64_t *rs1, vuint64m1x2_t vs3, size_t vl);
vuint64m1x2_t __riscv_vlseg2e64ff_v_u64m1x2(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m1x2_t __riscv_vlsseg2e64_v_u64m1x2(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_u64m1x2(uint64_t *rs1, long rs2, vuint64m1x2_t vs3, size_t vl);
vuint64m1x2_t __riscv_vluxseg2ei8_v_u64m1x2(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
vuint64m1x2_t __riscv_vloxseg2ei8_v_u64m1x2(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u64m1x2(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u64m1x2(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x2_t vs3, size_t vl);
vuint64m1x2_t __riscv_vluxseg2ei16_v_u64m1x2(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
vuint64m1x2_t __riscv_vloxseg2ei16_v_u64m1x2(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u64m1x2(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u64m1x2(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x2_t vs3, size_t vl);
vuint64m1x2_t __riscv_vluxseg2ei32_v_u64m1x2(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
vuint64m1x2_t __riscv_vloxseg2ei32_v_u64m1x2(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u64m1x2(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u64m1x2(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x2_t vs3, size_t vl);
vuint64m1x2_t __riscv_vluxseg2ei64_v_u64m1x2(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
vuint64m1x2_t __riscv_vloxseg2ei64_v_u64m1x2(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u64m1x2(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u64m1x2(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x2_t vs3, size_t vl);
vint64m1x2_t __riscv_vundefined_i64m1x2(void);
vint64m1x2_t __riscv_vcreate_v_i64m1_i64m1x2(vint64m1_t v0, vint64m1_t v1);
vint64m1_t __riscv_vget_v_i64m1x2_i64m1(vint64m1x2_t src, size_t index);
vint64m1x2_t __riscv_vset_v_i64m1x2_i64m1(vint64m1x2_t dest, size_t index, vint64m1_t value);
vint64m1x2_t __riscv_vlseg2e64_v_i64m1x2(const int64_t *rs1, size_t vl);
void __riscv_vsseg2e64_v_i64m1x2(int64_t *rs1, vint64m1x2_t vs3, size_t vl);
vint64m1x2_t __riscv_vlseg2e64ff_v_i64m1x2(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m1x2_t __riscv_vlsseg2e64_v_i64m1x2(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_i64m1x2(int64_t *rs1, long rs2, vint64m1x2_t vs3, size_t vl);
vint64m1x2_t __riscv_vluxseg2ei8_v_i64m1x2(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
vint64m1x2_t __riscv_vloxseg2ei8_v_i64m1x2(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i64m1x2(int64_t *rs1, vuint8mf8_t rs2, vint64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i64m1x2(int64_t *rs1, vuint8mf8_t rs2, vint64m1x2_t vs3, size_t vl);
vint64m1x2_t __riscv_vluxseg2ei16_v_i64m1x2(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
vint64m1x2_t __riscv_vloxseg2ei16_v_i64m1x2(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i64m1x2(int64_t *rs1, vuint16mf4_t rs2, vint64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i64m1x2(int64_t *rs1, vuint16mf4_t rs2, vint64m1x2_t vs3, size_t vl);
vint64m1x2_t __riscv_vluxseg2ei32_v_i64m1x2(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
vint64m1x2_t __riscv_vloxseg2ei32_v_i64m1x2(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i64m1x2(int64_t *rs1, vuint32mf2_t rs2, vint64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i64m1x2(int64_t *rs1, vuint32mf2_t rs2, vint64m1x2_t vs3, size_t vl);
vint64m1x2_t __riscv_vluxseg2ei64_v_i64m1x2(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
vint64m1x2_t __riscv_vloxseg2ei64_v_i64m1x2(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i64m1x2(int64_t *rs1, vuint64m1_t rs2, vint64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i64m1x2(int64_t *rs1, vuint64m1_t rs2, vint64m1x2_t vs3, size_t vl);
vfloat64m1x2_t __riscv_vundefined_f64m1x2(void);
vfloat64m1x2_t __riscv_vcreate_v_f64m1_f64m1x2(vfloat64m1_t v0, vfloat64m1_t v1);
vfloat64m1_t __riscv_vget_v_f64m1x2_f64m1(vfloat64m1x2_t src, size_t index);
vfloat64m1x2_t __riscv_vset_v_f64m1x2_f64m1(vfloat64m1x2_t dest, size_t index, vfloat64m1_t value);
vfloat64m1x2_t __riscv_vlseg2e64_v_f64m1x2(const double *rs1, size_t vl);
void __riscv_vsseg2e64_v_f64m1x2(double *rs1, vfloat64m1x2_t vs3, size_t vl);
vfloat64m1x2_t __riscv_vlseg2e64ff_v_f64m1x2(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m1x2_t __riscv_vlsseg2e64_v_f64m1x2(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_f64m1x2(double *rs1, long rs2, vfloat64m1x2_t vs3, size_t vl);
vfloat64m1x2_t __riscv_vluxseg2ei8_v_f64m1x2(const double *rs1, vuint8mf8_t rs2, size_t vl);
vfloat64m1x2_t __riscv_vloxseg2ei8_v_f64m1x2(const double *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_f64m1x2(double *rs1, vuint8mf8_t rs2, vfloat64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_f64m1x2(double *rs1, vuint8mf8_t rs2, vfloat64m1x2_t vs3, size_t vl);
vfloat64m1x2_t __riscv_vluxseg2ei16_v_f64m1x2(const double *rs1, vuint16mf4_t rs2, size_t vl);
vfloat64m1x2_t __riscv_vloxseg2ei16_v_f64m1x2(const double *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_f64m1x2(double *rs1, vuint16mf4_t rs2, vfloat64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_f64m1x2(double *rs1, vuint16mf4_t rs2, vfloat64m1x2_t vs3, size_t vl);
vfloat64m1x2_t __riscv_vluxseg2ei32_v_f64m1x2(const double *rs1, vuint32mf2_t rs2, size_t vl);
vfloat64m1x2_t __riscv_vloxseg2ei32_v_f64m1x2(const double *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_f64m1x2(double *rs1, vuint32mf2_t rs2, vfloat64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_f64m1x2(double *rs1, vuint32mf2_t rs2, vfloat64m1x2_t vs3, size_t vl);
vfloat64m1x2_t __riscv_vluxseg2ei64_v_f64m1x2(const double *rs1, vuint64m1_t rs2, size_t vl);
vfloat64m1x2_t __riscv_vloxseg2ei64_v_f64m1x2(const double *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_f64m1x2(double *rs1, vuint64m1_t rs2, vfloat64m1x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_f64m1x2(double *rs1, vuint64m1_t rs2, vfloat64m1x2_t vs3, size_t vl);
vuint64m1x3_t __riscv_vundefined_u64m1x3(void);
vuint64m1x3_t __riscv_vcreate_v_u64m1_u64m1x3(vuint64m1_t v0, vuint64m1_t v1, vuint64m1_t v2);
vuint64m1_t __riscv_vget_v_u64m1x3_u64m1(vuint64m1x3_t src, size_t index);
vuint64m1x3_t __riscv_vset_v_u64m1x3_u64m1(vuint64m1x3_t dest, size_t index, vuint64m1_t value);
vuint64m1x3_t __riscv_vlseg3e64_v_u64m1x3(const uint64_t *rs1, size_t vl);
void __riscv_vsseg3e64_v_u64m1x3(uint64_t *rs1, vuint64m1x3_t vs3, size_t vl);
vuint64m1x3_t __riscv_vlseg3e64ff_v_u64m1x3(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m1x3_t __riscv_vlsseg3e64_v_u64m1x3(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg3e64_v_u64m1x3(uint64_t *rs1, long rs2, vuint64m1x3_t vs3, size_t vl);
vuint64m1x3_t __riscv_vluxseg3ei8_v_u64m1x3(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
vuint64m1x3_t __riscv_vloxseg3ei8_v_u64m1x3(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u64m1x3(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u64m1x3(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x3_t vs3, size_t vl);
vuint64m1x3_t __riscv_vluxseg3ei16_v_u64m1x3(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
vuint64m1x3_t __riscv_vloxseg3ei16_v_u64m1x3(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u64m1x3(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u64m1x3(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x3_t vs3, size_t vl);
vuint64m1x3_t __riscv_vluxseg3ei32_v_u64m1x3(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
vuint64m1x3_t __riscv_vloxseg3ei32_v_u64m1x3(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u64m1x3(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u64m1x3(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x3_t vs3, size_t vl);
vuint64m1x3_t __riscv_vluxseg3ei64_v_u64m1x3(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
vuint64m1x3_t __riscv_vloxseg3ei64_v_u64m1x3(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u64m1x3(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u64m1x3(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x3_t vs3, size_t vl);
vint64m1x3_t __riscv_vundefined_i64m1x3(void);
vint64m1x3_t __riscv_vcreate_v_i64m1_i64m1x3(vint64m1_t v0, vint64m1_t v1, vint64m1_t v2);
vint64m1_t __riscv_vget_v_i64m1x3_i64m1(vint64m1x3_t src, size_t index);
vint64m1x3_t __riscv_vset_v_i64m1x3_i64m1(vint64m1x3_t dest, size_t index, vint64m1_t value);
vint64m1x3_t __riscv_vlseg3e64_v_i64m1x3(const int64_t *rs1, size_t vl);
void __riscv_vsseg3e64_v_i64m1x3(int64_t *rs1, vint64m1x3_t vs3, size_t vl);
vint64m1x3_t __riscv_vlseg3e64ff_v_i64m1x3(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m1x3_t __riscv_vlsseg3e64_v_i64m1x3(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg3e64_v_i64m1x3(int64_t *rs1, long rs2, vint64m1x3_t vs3, size_t vl);
vint64m1x3_t __riscv_vluxseg3ei8_v_i64m1x3(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
vint64m1x3_t __riscv_vloxseg3ei8_v_i64m1x3(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i64m1x3(int64_t *rs1, vuint8mf8_t rs2, vint64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i64m1x3(int64_t *rs1, vuint8mf8_t rs2, vint64m1x3_t vs3, size_t vl);
vint64m1x3_t __riscv_vluxseg3ei16_v_i64m1x3(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
vint64m1x3_t __riscv_vloxseg3ei16_v_i64m1x3(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i64m1x3(int64_t *rs1, vuint16mf4_t rs2, vint64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i64m1x3(int64_t *rs1, vuint16mf4_t rs2, vint64m1x3_t vs3, size_t vl);
vint64m1x3_t __riscv_vluxseg3ei32_v_i64m1x3(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
vint64m1x3_t __riscv_vloxseg3ei32_v_i64m1x3(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i64m1x3(int64_t *rs1, vuint32mf2_t rs2, vint64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i64m1x3(int64_t *rs1, vuint32mf2_t rs2, vint64m1x3_t vs3, size_t vl);
vint64m1x3_t __riscv_vluxseg3ei64_v_i64m1x3(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
vint64m1x3_t __riscv_vloxseg3ei64_v_i64m1x3(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i64m1x3(int64_t *rs1, vuint64m1_t rs2, vint64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i64m1x3(int64_t *rs1, vuint64m1_t rs2, vint64m1x3_t vs3, size_t vl);
vfloat64m1x3_t __riscv_vundefined_f64m1x3(void);
vfloat64m1x3_t __riscv_vcreate_v_f64m1_f64m1x3(vfloat64m1_t v0, vfloat64m1_t v1, vfloat64m1_t v2);
vfloat64m1_t __riscv_vget_v_f64m1x3_f64m1(vfloat64m1x3_t src, size_t index);
vfloat64m1x3_t __riscv_vset_v_f64m1x3_f64m1(vfloat64m1x3_t dest, size_t index, vfloat64m1_t value);
vfloat64m1x3_t __riscv_vlseg3e64_v_f64m1x3(const double *rs1, size_t vl);
void __riscv_vsseg3e64_v_f64m1x3(double *rs1, vfloat64m1x3_t vs3, size_t vl);
vfloat64m1x3_t __riscv_vlseg3e64ff_v_f64m1x3(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m1x3_t __riscv_vlsseg3e64_v_f64m1x3(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg3e64_v_f64m1x3(double *rs1, long rs2, vfloat64m1x3_t vs3, size_t vl);
vfloat64m1x3_t __riscv_vluxseg3ei8_v_f64m1x3(const double *rs1, vuint8mf8_t rs2, size_t vl);
vfloat64m1x3_t __riscv_vloxseg3ei8_v_f64m1x3(const double *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_f64m1x3(double *rs1, vuint8mf8_t rs2, vfloat64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_f64m1x3(double *rs1, vuint8mf8_t rs2, vfloat64m1x3_t vs3, size_t vl);
vfloat64m1x3_t __riscv_vluxseg3ei16_v_f64m1x3(const double *rs1, vuint16mf4_t rs2, size_t vl);
vfloat64m1x3_t __riscv_vloxseg3ei16_v_f64m1x3(const double *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_f64m1x3(double *rs1, vuint16mf4_t rs2, vfloat64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_f64m1x3(double *rs1, vuint16mf4_t rs2, vfloat64m1x3_t vs3, size_t vl);
vfloat64m1x3_t __riscv_vluxseg3ei32_v_f64m1x3(const double *rs1, vuint32mf2_t rs2, size_t vl);
vfloat64m1x3_t __riscv_vloxseg3ei32_v_f64m1x3(const double *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_f64m1x3(double *rs1, vuint32mf2_t rs2, vfloat64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_f64m1x3(double *rs1, vuint32mf2_t rs2, vfloat64m1x3_t vs3, size_t vl);
vfloat64m1x3_t __riscv_vluxseg3ei64_v_f64m1x3(const double *rs1, vuint64m1_t rs2, size_t vl);
vfloat64m1x3_t __riscv_vloxseg3ei64_v_f64m1x3(const double *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_f64m1x3(double *rs1, vuint64m1_t rs2, vfloat64m1x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_f64m1x3(double *rs1, vuint64m1_t rs2, vfloat64m1x3_t vs3, size_t vl);
vuint64m1x4_t __riscv_vundefined_u64m1x4(void);
vuint64m1x4_t __riscv_vcreate_v_u64m1_u64m1x4(vuint64m1_t v0, vuint64m1_t v1, vuint64m1_t v2, vuint64m1_t v3);
vuint64m1_t __riscv_vget_v_u64m1x4_u64m1(vuint64m1x4_t src, size_t index);
vuint64m1x4_t __riscv_vset_v_u64m1x4_u64m1(vuint64m1x4_t dest, size_t index, vuint64m1_t value);
vuint64m1x4_t __riscv_vlseg4e64_v_u64m1x4(const uint64_t *rs1, size_t vl);
void __riscv_vsseg4e64_v_u64m1x4(uint64_t *rs1, vuint64m1x4_t vs3, size_t vl);
vuint64m1x4_t __riscv_vlseg4e64ff_v_u64m1x4(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m1x4_t __riscv_vlsseg4e64_v_u64m1x4(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg4e64_v_u64m1x4(uint64_t *rs1, long rs2, vuint64m1x4_t vs3, size_t vl);
vuint64m1x4_t __riscv_vluxseg4ei8_v_u64m1x4(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
vuint64m1x4_t __riscv_vloxseg4ei8_v_u64m1x4(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u64m1x4(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u64m1x4(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x4_t vs3, size_t vl);
vuint64m1x4_t __riscv_vluxseg4ei16_v_u64m1x4(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
vuint64m1x4_t __riscv_vloxseg4ei16_v_u64m1x4(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u64m1x4(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u64m1x4(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x4_t vs3, size_t vl);
vuint64m1x4_t __riscv_vluxseg4ei32_v_u64m1x4(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
vuint64m1x4_t __riscv_vloxseg4ei32_v_u64m1x4(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u64m1x4(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u64m1x4(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x4_t vs3, size_t vl);
vuint64m1x4_t __riscv_vluxseg4ei64_v_u64m1x4(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
vuint64m1x4_t __riscv_vloxseg4ei64_v_u64m1x4(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u64m1x4(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u64m1x4(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x4_t vs3, size_t vl);
vint64m1x4_t __riscv_vundefined_i64m1x4(void);
vint64m1x4_t __riscv_vcreate_v_i64m1_i64m1x4(vint64m1_t v0, vint64m1_t v1, vint64m1_t v2, vint64m1_t v3);
vint64m1_t __riscv_vget_v_i64m1x4_i64m1(vint64m1x4_t src, size_t index);
vint64m1x4_t __riscv_vset_v_i64m1x4_i64m1(vint64m1x4_t dest, size_t index, vint64m1_t value);
vint64m1x4_t __riscv_vlseg4e64_v_i64m1x4(const int64_t *rs1, size_t vl);
void __riscv_vsseg4e64_v_i64m1x4(int64_t *rs1, vint64m1x4_t vs3, size_t vl);
vint64m1x4_t __riscv_vlseg4e64ff_v_i64m1x4(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m1x4_t __riscv_vlsseg4e64_v_i64m1x4(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg4e64_v_i64m1x4(int64_t *rs1, long rs2, vint64m1x4_t vs3, size_t vl);
vint64m1x4_t __riscv_vluxseg4ei8_v_i64m1x4(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
vint64m1x4_t __riscv_vloxseg4ei8_v_i64m1x4(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i64m1x4(int64_t *rs1, vuint8mf8_t rs2, vint64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i64m1x4(int64_t *rs1, vuint8mf8_t rs2, vint64m1x4_t vs3, size_t vl);
vint64m1x4_t __riscv_vluxseg4ei16_v_i64m1x4(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
vint64m1x4_t __riscv_vloxseg4ei16_v_i64m1x4(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i64m1x4(int64_t *rs1, vuint16mf4_t rs2, vint64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i64m1x4(int64_t *rs1, vuint16mf4_t rs2, vint64m1x4_t vs3, size_t vl);
vint64m1x4_t __riscv_vluxseg4ei32_v_i64m1x4(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
vint64m1x4_t __riscv_vloxseg4ei32_v_i64m1x4(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i64m1x4(int64_t *rs1, vuint32mf2_t rs2, vint64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i64m1x4(int64_t *rs1, vuint32mf2_t rs2, vint64m1x4_t vs3, size_t vl);
vint64m1x4_t __riscv_vluxseg4ei64_v_i64m1x4(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
vint64m1x4_t __riscv_vloxseg4ei64_v_i64m1x4(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i64m1x4(int64_t *rs1, vuint64m1_t rs2, vint64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i64m1x4(int64_t *rs1, vuint64m1_t rs2, vint64m1x4_t vs3, size_t vl);
vfloat64m1x4_t __riscv_vundefined_f64m1x4(void);
vfloat64m1x4_t __riscv_vcreate_v_f64m1_f64m1x4(vfloat64m1_t v0, vfloat64m1_t v1, vfloat64m1_t v2, vfloat64m1_t v3);
vfloat64m1_t __riscv_vget_v_f64m1x4_f64m1(vfloat64m1x4_t src, size_t index);
vfloat64m1x4_t __riscv_vset_v_f64m1x4_f64m1(vfloat64m1x4_t dest, size_t index, vfloat64m1_t value);
vfloat64m1x4_t __riscv_vlseg4e64_v_f64m1x4(const double *rs1, size_t vl);
void __riscv_vsseg4e64_v_f64m1x4(double *rs1, vfloat64m1x4_t vs3, size_t vl);
vfloat64m1x4_t __riscv_vlseg4e64ff_v_f64m1x4(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m1x4_t __riscv_vlsseg4e64_v_f64m1x4(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg4e64_v_f64m1x4(double *rs1, long rs2, vfloat64m1x4_t vs3, size_t vl);
vfloat64m1x4_t __riscv_vluxseg4ei8_v_f64m1x4(const double *rs1, vuint8mf8_t rs2, size_t vl);
vfloat64m1x4_t __riscv_vloxseg4ei8_v_f64m1x4(const double *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_f64m1x4(double *rs1, vuint8mf8_t rs2, vfloat64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_f64m1x4(double *rs1, vuint8mf8_t rs2, vfloat64m1x4_t vs3, size_t vl);
vfloat64m1x4_t __riscv_vluxseg4ei16_v_f64m1x4(const double *rs1, vuint16mf4_t rs2, size_t vl);
vfloat64m1x4_t __riscv_vloxseg4ei16_v_f64m1x4(const double *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_f64m1x4(double *rs1, vuint16mf4_t rs2, vfloat64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_f64m1x4(double *rs1, vuint16mf4_t rs2, vfloat64m1x4_t vs3, size_t vl);
vfloat64m1x4_t __riscv_vluxseg4ei32_v_f64m1x4(const double *rs1, vuint32mf2_t rs2, size_t vl);
vfloat64m1x4_t __riscv_vloxseg4ei32_v_f64m1x4(const double *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_f64m1x4(double *rs1, vuint32mf2_t rs2, vfloat64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_f64m1x4(double *rs1, vuint32mf2_t rs2, vfloat64m1x4_t vs3, size_t vl);
vfloat64m1x4_t __riscv_vluxseg4ei64_v_f64m1x4(const double *rs1, vuint64m1_t rs2, size_t vl);
vfloat64m1x4_t __riscv_vloxseg4ei64_v_f64m1x4(const double *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_f64m1x4(double *rs1, vuint64m1_t rs2, vfloat64m1x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_f64m1x4(double *rs1, vuint64m1_t rs2, vfloat64m1x4_t vs3, size_t vl);
vuint64m1x5_t __riscv_vundefined_u64m1x5(void);
vuint64m1x5_t __riscv_vcreate_v_u64m1_u64m1x5(vuint64m1_t v0, vuint64m1_t v1, vuint64m1_t v2, vuint64m1_t v3, vuint64m1_t v4);
vuint64m1_t __riscv_vget_v_u64m1x5_u64m1(vuint64m1x5_t src, size_t index);
vuint64m1x5_t __riscv_vset_v_u64m1x5_u64m1(vuint64m1x5_t dest, size_t index, vuint64m1_t value);
vuint64m1x5_t __riscv_vlseg5e64_v_u64m1x5(const uint64_t *rs1, size_t vl);
void __riscv_vsseg5e64_v_u64m1x5(uint64_t *rs1, vuint64m1x5_t vs3, size_t vl);
vuint64m1x5_t __riscv_vlseg5e64ff_v_u64m1x5(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m1x5_t __riscv_vlsseg5e64_v_u64m1x5(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg5e64_v_u64m1x5(uint64_t *rs1, long rs2, vuint64m1x5_t vs3, size_t vl);
vuint64m1x5_t __riscv_vluxseg5ei8_v_u64m1x5(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
vuint64m1x5_t __riscv_vloxseg5ei8_v_u64m1x5(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_u64m1x5(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_u64m1x5(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x5_t vs3, size_t vl);
vuint64m1x5_t __riscv_vluxseg5ei16_v_u64m1x5(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
vuint64m1x5_t __riscv_vloxseg5ei16_v_u64m1x5(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_u64m1x5(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_u64m1x5(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x5_t vs3, size_t vl);
vuint64m1x5_t __riscv_vluxseg5ei32_v_u64m1x5(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
vuint64m1x5_t __riscv_vloxseg5ei32_v_u64m1x5(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_u64m1x5(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_u64m1x5(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x5_t vs3, size_t vl);
vuint64m1x5_t __riscv_vluxseg5ei64_v_u64m1x5(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
vuint64m1x5_t __riscv_vloxseg5ei64_v_u64m1x5(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_u64m1x5(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_u64m1x5(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x5_t vs3, size_t vl);
vint64m1x5_t __riscv_vundefined_i64m1x5(void);
vint64m1x5_t __riscv_vcreate_v_i64m1_i64m1x5(vint64m1_t v0, vint64m1_t v1, vint64m1_t v2, vint64m1_t v3, vint64m1_t v4);
vint64m1_t __riscv_vget_v_i64m1x5_i64m1(vint64m1x5_t src, size_t index);
vint64m1x5_t __riscv_vset_v_i64m1x5_i64m1(vint64m1x5_t dest, size_t index, vint64m1_t value);
vint64m1x5_t __riscv_vlseg5e64_v_i64m1x5(const int64_t *rs1, size_t vl);
void __riscv_vsseg5e64_v_i64m1x5(int64_t *rs1, vint64m1x5_t vs3, size_t vl);
vint64m1x5_t __riscv_vlseg5e64ff_v_i64m1x5(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m1x5_t __riscv_vlsseg5e64_v_i64m1x5(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg5e64_v_i64m1x5(int64_t *rs1, long rs2, vint64m1x5_t vs3, size_t vl);
vint64m1x5_t __riscv_vluxseg5ei8_v_i64m1x5(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
vint64m1x5_t __riscv_vloxseg5ei8_v_i64m1x5(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_i64m1x5(int64_t *rs1, vuint8mf8_t rs2, vint64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_i64m1x5(int64_t *rs1, vuint8mf8_t rs2, vint64m1x5_t vs3, size_t vl);
vint64m1x5_t __riscv_vluxseg5ei16_v_i64m1x5(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
vint64m1x5_t __riscv_vloxseg5ei16_v_i64m1x5(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_i64m1x5(int64_t *rs1, vuint16mf4_t rs2, vint64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_i64m1x5(int64_t *rs1, vuint16mf4_t rs2, vint64m1x5_t vs3, size_t vl);
vint64m1x5_t __riscv_vluxseg5ei32_v_i64m1x5(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
vint64m1x5_t __riscv_vloxseg5ei32_v_i64m1x5(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_i64m1x5(int64_t *rs1, vuint32mf2_t rs2, vint64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_i64m1x5(int64_t *rs1, vuint32mf2_t rs2, vint64m1x5_t vs3, size_t vl);
vint64m1x5_t __riscv_vluxseg5ei64_v_i64m1x5(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
vint64m1x5_t __riscv_vloxseg5ei64_v_i64m1x5(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_i64m1x5(int64_t *rs1, vuint64m1_t rs2, vint64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_i64m1x5(int64_t *rs1, vuint64m1_t rs2, vint64m1x5_t vs3, size_t vl);
vfloat64m1x5_t __riscv_vundefined_f64m1x5(void);
vfloat64m1x5_t __riscv_vcreate_v_f64m1_f64m1x5(vfloat64m1_t v0, vfloat64m1_t v1, vfloat64m1_t v2, vfloat64m1_t v3, vfloat64m1_t v4);
vfloat64m1_t __riscv_vget_v_f64m1x5_f64m1(vfloat64m1x5_t src, size_t index);
vfloat64m1x5_t __riscv_vset_v_f64m1x5_f64m1(vfloat64m1x5_t dest, size_t index, vfloat64m1_t value);
vfloat64m1x5_t __riscv_vlseg5e64_v_f64m1x5(const double *rs1, size_t vl);
void __riscv_vsseg5e64_v_f64m1x5(double *rs1, vfloat64m1x5_t vs3, size_t vl);
vfloat64m1x5_t __riscv_vlseg5e64ff_v_f64m1x5(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m1x5_t __riscv_vlsseg5e64_v_f64m1x5(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg5e64_v_f64m1x5(double *rs1, long rs2, vfloat64m1x5_t vs3, size_t vl);
vfloat64m1x5_t __riscv_vluxseg5ei8_v_f64m1x5(const double *rs1, vuint8mf8_t rs2, size_t vl);
vfloat64m1x5_t __riscv_vloxseg5ei8_v_f64m1x5(const double *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg5ei8_v_f64m1x5(double *rs1, vuint8mf8_t rs2, vfloat64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei8_v_f64m1x5(double *rs1, vuint8mf8_t rs2, vfloat64m1x5_t vs3, size_t vl);
vfloat64m1x5_t __riscv_vluxseg5ei16_v_f64m1x5(const double *rs1, vuint16mf4_t rs2, size_t vl);
vfloat64m1x5_t __riscv_vloxseg5ei16_v_f64m1x5(const double *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg5ei16_v_f64m1x5(double *rs1, vuint16mf4_t rs2, vfloat64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei16_v_f64m1x5(double *rs1, vuint16mf4_t rs2, vfloat64m1x5_t vs3, size_t vl);
vfloat64m1x5_t __riscv_vluxseg5ei32_v_f64m1x5(const double *rs1, vuint32mf2_t rs2, size_t vl);
vfloat64m1x5_t __riscv_vloxseg5ei32_v_f64m1x5(const double *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg5ei32_v_f64m1x5(double *rs1, vuint32mf2_t rs2, vfloat64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei32_v_f64m1x5(double *rs1, vuint32mf2_t rs2, vfloat64m1x5_t vs3, size_t vl);
vfloat64m1x5_t __riscv_vluxseg5ei64_v_f64m1x5(const double *rs1, vuint64m1_t rs2, size_t vl);
vfloat64m1x5_t __riscv_vloxseg5ei64_v_f64m1x5(const double *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg5ei64_v_f64m1x5(double *rs1, vuint64m1_t rs2, vfloat64m1x5_t vs3, size_t vl);
void __riscv_vsoxseg5ei64_v_f64m1x5(double *rs1, vuint64m1_t rs2, vfloat64m1x5_t vs3, size_t vl);
vuint64m1x6_t __riscv_vundefined_u64m1x6(void);
vuint64m1x6_t __riscv_vcreate_v_u64m1_u64m1x6(vuint64m1_t v0, vuint64m1_t v1, vuint64m1_t v2, vuint64m1_t v3, vuint64m1_t v4, vuint64m1_t v5);
vuint64m1_t __riscv_vget_v_u64m1x6_u64m1(vuint64m1x6_t src, size_t index);
vuint64m1x6_t __riscv_vset_v_u64m1x6_u64m1(vuint64m1x6_t dest, size_t index, vuint64m1_t value);
vuint64m1x6_t __riscv_vlseg6e64_v_u64m1x6(const uint64_t *rs1, size_t vl);
void __riscv_vsseg6e64_v_u64m1x6(uint64_t *rs1, vuint64m1x6_t vs3, size_t vl);
vuint64m1x6_t __riscv_vlseg6e64ff_v_u64m1x6(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m1x6_t __riscv_vlsseg6e64_v_u64m1x6(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg6e64_v_u64m1x6(uint64_t *rs1, long rs2, vuint64m1x6_t vs3, size_t vl);
vuint64m1x6_t __riscv_vluxseg6ei8_v_u64m1x6(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
vuint64m1x6_t __riscv_vloxseg6ei8_v_u64m1x6(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_u64m1x6(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_u64m1x6(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x6_t vs3, size_t vl);
vuint64m1x6_t __riscv_vluxseg6ei16_v_u64m1x6(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
vuint64m1x6_t __riscv_vloxseg6ei16_v_u64m1x6(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_u64m1x6(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_u64m1x6(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x6_t vs3, size_t vl);
vuint64m1x6_t __riscv_vluxseg6ei32_v_u64m1x6(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
vuint64m1x6_t __riscv_vloxseg6ei32_v_u64m1x6(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_u64m1x6(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_u64m1x6(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x6_t vs3, size_t vl);
vuint64m1x6_t __riscv_vluxseg6ei64_v_u64m1x6(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
vuint64m1x6_t __riscv_vloxseg6ei64_v_u64m1x6(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_u64m1x6(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_u64m1x6(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x6_t vs3, size_t vl);
vint64m1x6_t __riscv_vundefined_i64m1x6(void);
vint64m1x6_t __riscv_vcreate_v_i64m1_i64m1x6(vint64m1_t v0, vint64m1_t v1, vint64m1_t v2, vint64m1_t v3, vint64m1_t v4, vint64m1_t v5);
vint64m1_t __riscv_vget_v_i64m1x6_i64m1(vint64m1x6_t src, size_t index);
vint64m1x6_t __riscv_vset_v_i64m1x6_i64m1(vint64m1x6_t dest, size_t index, vint64m1_t value);
vint64m1x6_t __riscv_vlseg6e64_v_i64m1x6(const int64_t *rs1, size_t vl);
void __riscv_vsseg6e64_v_i64m1x6(int64_t *rs1, vint64m1x6_t vs3, size_t vl);
vint64m1x6_t __riscv_vlseg6e64ff_v_i64m1x6(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m1x6_t __riscv_vlsseg6e64_v_i64m1x6(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg6e64_v_i64m1x6(int64_t *rs1, long rs2, vint64m1x6_t vs3, size_t vl);
vint64m1x6_t __riscv_vluxseg6ei8_v_i64m1x6(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
vint64m1x6_t __riscv_vloxseg6ei8_v_i64m1x6(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_i64m1x6(int64_t *rs1, vuint8mf8_t rs2, vint64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_i64m1x6(int64_t *rs1, vuint8mf8_t rs2, vint64m1x6_t vs3, size_t vl);
vint64m1x6_t __riscv_vluxseg6ei16_v_i64m1x6(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
vint64m1x6_t __riscv_vloxseg6ei16_v_i64m1x6(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_i64m1x6(int64_t *rs1, vuint16mf4_t rs2, vint64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_i64m1x6(int64_t *rs1, vuint16mf4_t rs2, vint64m1x6_t vs3, size_t vl);
vint64m1x6_t __riscv_vluxseg6ei32_v_i64m1x6(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
vint64m1x6_t __riscv_vloxseg6ei32_v_i64m1x6(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_i64m1x6(int64_t *rs1, vuint32mf2_t rs2, vint64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_i64m1x6(int64_t *rs1, vuint32mf2_t rs2, vint64m1x6_t vs3, size_t vl);
vint64m1x6_t __riscv_vluxseg6ei64_v_i64m1x6(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
vint64m1x6_t __riscv_vloxseg6ei64_v_i64m1x6(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_i64m1x6(int64_t *rs1, vuint64m1_t rs2, vint64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_i64m1x6(int64_t *rs1, vuint64m1_t rs2, vint64m1x6_t vs3, size_t vl);
vfloat64m1x6_t __riscv_vundefined_f64m1x6(void);
vfloat64m1x6_t __riscv_vcreate_v_f64m1_f64m1x6(vfloat64m1_t v0, vfloat64m1_t v1, vfloat64m1_t v2, vfloat64m1_t v3, vfloat64m1_t v4, vfloat64m1_t v5);
vfloat64m1_t __riscv_vget_v_f64m1x6_f64m1(vfloat64m1x6_t src, size_t index);
vfloat64m1x6_t __riscv_vset_v_f64m1x6_f64m1(vfloat64m1x6_t dest, size_t index, vfloat64m1_t value);
vfloat64m1x6_t __riscv_vlseg6e64_v_f64m1x6(const double *rs1, size_t vl);
void __riscv_vsseg6e64_v_f64m1x6(double *rs1, vfloat64m1x6_t vs3, size_t vl);
vfloat64m1x6_t __riscv_vlseg6e64ff_v_f64m1x6(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m1x6_t __riscv_vlsseg6e64_v_f64m1x6(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg6e64_v_f64m1x6(double *rs1, long rs2, vfloat64m1x6_t vs3, size_t vl);
vfloat64m1x6_t __riscv_vluxseg6ei8_v_f64m1x6(const double *rs1, vuint8mf8_t rs2, size_t vl);
vfloat64m1x6_t __riscv_vloxseg6ei8_v_f64m1x6(const double *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg6ei8_v_f64m1x6(double *rs1, vuint8mf8_t rs2, vfloat64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei8_v_f64m1x6(double *rs1, vuint8mf8_t rs2, vfloat64m1x6_t vs3, size_t vl);
vfloat64m1x6_t __riscv_vluxseg6ei16_v_f64m1x6(const double *rs1, vuint16mf4_t rs2, size_t vl);
vfloat64m1x6_t __riscv_vloxseg6ei16_v_f64m1x6(const double *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg6ei16_v_f64m1x6(double *rs1, vuint16mf4_t rs2, vfloat64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei16_v_f64m1x6(double *rs1, vuint16mf4_t rs2, vfloat64m1x6_t vs3, size_t vl);
vfloat64m1x6_t __riscv_vluxseg6ei32_v_f64m1x6(const double *rs1, vuint32mf2_t rs2, size_t vl);
vfloat64m1x6_t __riscv_vloxseg6ei32_v_f64m1x6(const double *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg6ei32_v_f64m1x6(double *rs1, vuint32mf2_t rs2, vfloat64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei32_v_f64m1x6(double *rs1, vuint32mf2_t rs2, vfloat64m1x6_t vs3, size_t vl);
vfloat64m1x6_t __riscv_vluxseg6ei64_v_f64m1x6(const double *rs1, vuint64m1_t rs2, size_t vl);
vfloat64m1x6_t __riscv_vloxseg6ei64_v_f64m1x6(const double *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg6ei64_v_f64m1x6(double *rs1, vuint64m1_t rs2, vfloat64m1x6_t vs3, size_t vl);
void __riscv_vsoxseg6ei64_v_f64m1x6(double *rs1, vuint64m1_t rs2, vfloat64m1x6_t vs3, size_t vl);
vuint64m1x7_t __riscv_vundefined_u64m1x7(void);
vuint64m1x7_t __riscv_vcreate_v_u64m1_u64m1x7(vuint64m1_t v0, vuint64m1_t v1, vuint64m1_t v2, vuint64m1_t v3, vuint64m1_t v4, vuint64m1_t v5, vuint64m1_t v6);
vuint64m1_t __riscv_vget_v_u64m1x7_u64m1(vuint64m1x7_t src, size_t index);
vuint64m1x7_t __riscv_vset_v_u64m1x7_u64m1(vuint64m1x7_t dest, size_t index, vuint64m1_t value);
vuint64m1x7_t __riscv_vlseg7e64_v_u64m1x7(const uint64_t *rs1, size_t vl);
void __riscv_vsseg7e64_v_u64m1x7(uint64_t *rs1, vuint64m1x7_t vs3, size_t vl);
vuint64m1x7_t __riscv_vlseg7e64ff_v_u64m1x7(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m1x7_t __riscv_vlsseg7e64_v_u64m1x7(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg7e64_v_u64m1x7(uint64_t *rs1, long rs2, vuint64m1x7_t vs3, size_t vl);
vuint64m1x7_t __riscv_vluxseg7ei8_v_u64m1x7(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
vuint64m1x7_t __riscv_vloxseg7ei8_v_u64m1x7(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_u64m1x7(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_u64m1x7(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x7_t vs3, size_t vl);
vuint64m1x7_t __riscv_vluxseg7ei16_v_u64m1x7(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
vuint64m1x7_t __riscv_vloxseg7ei16_v_u64m1x7(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_u64m1x7(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_u64m1x7(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x7_t vs3, size_t vl);
vuint64m1x7_t __riscv_vluxseg7ei32_v_u64m1x7(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
vuint64m1x7_t __riscv_vloxseg7ei32_v_u64m1x7(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_u64m1x7(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_u64m1x7(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x7_t vs3, size_t vl);
vuint64m1x7_t __riscv_vluxseg7ei64_v_u64m1x7(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
vuint64m1x7_t __riscv_vloxseg7ei64_v_u64m1x7(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_u64m1x7(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_u64m1x7(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x7_t vs3, size_t vl);
vint64m1x7_t __riscv_vundefined_i64m1x7(void);
vint64m1x7_t __riscv_vcreate_v_i64m1_i64m1x7(vint64m1_t v0, vint64m1_t v1, vint64m1_t v2, vint64m1_t v3, vint64m1_t v4, vint64m1_t v5, vint64m1_t v6);
vint64m1_t __riscv_vget_v_i64m1x7_i64m1(vint64m1x7_t src, size_t index);
vint64m1x7_t __riscv_vset_v_i64m1x7_i64m1(vint64m1x7_t dest, size_t index, vint64m1_t value);
vint64m1x7_t __riscv_vlseg7e64_v_i64m1x7(const int64_t *rs1, size_t vl);
void __riscv_vsseg7e64_v_i64m1x7(int64_t *rs1, vint64m1x7_t vs3, size_t vl);
vint64m1x7_t __riscv_vlseg7e64ff_v_i64m1x7(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m1x7_t __riscv_vlsseg7e64_v_i64m1x7(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg7e64_v_i64m1x7(int64_t *rs1, long rs2, vint64m1x7_t vs3, size_t vl);
vint64m1x7_t __riscv_vluxseg7ei8_v_i64m1x7(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
vint64m1x7_t __riscv_vloxseg7ei8_v_i64m1x7(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_i64m1x7(int64_t *rs1, vuint8mf8_t rs2, vint64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_i64m1x7(int64_t *rs1, vuint8mf8_t rs2, vint64m1x7_t vs3, size_t vl);
vint64m1x7_t __riscv_vluxseg7ei16_v_i64m1x7(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
vint64m1x7_t __riscv_vloxseg7ei16_v_i64m1x7(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_i64m1x7(int64_t *rs1, vuint16mf4_t rs2, vint64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_i64m1x7(int64_t *rs1, vuint16mf4_t rs2, vint64m1x7_t vs3, size_t vl);
vint64m1x7_t __riscv_vluxseg7ei32_v_i64m1x7(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
vint64m1x7_t __riscv_vloxseg7ei32_v_i64m1x7(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_i64m1x7(int64_t *rs1, vuint32mf2_t rs2, vint64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_i64m1x7(int64_t *rs1, vuint32mf2_t rs2, vint64m1x7_t vs3, size_t vl);
vint64m1x7_t __riscv_vluxseg7ei64_v_i64m1x7(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
vint64m1x7_t __riscv_vloxseg7ei64_v_i64m1x7(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_i64m1x7(int64_t *rs1, vuint64m1_t rs2, vint64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_i64m1x7(int64_t *rs1, vuint64m1_t rs2, vint64m1x7_t vs3, size_t vl);
vfloat64m1x7_t __riscv_vundefined_f64m1x7(void);
vfloat64m1x7_t __riscv_vcreate_v_f64m1_f64m1x7(vfloat64m1_t v0, vfloat64m1_t v1, vfloat64m1_t v2, vfloat64m1_t v3, vfloat64m1_t v4, vfloat64m1_t v5, vfloat64m1_t v6);
vfloat64m1_t __riscv_vget_v_f64m1x7_f64m1(vfloat64m1x7_t src, size_t index);
vfloat64m1x7_t __riscv_vset_v_f64m1x7_f64m1(vfloat64m1x7_t dest, size_t index, vfloat64m1_t value);
vfloat64m1x7_t __riscv_vlseg7e64_v_f64m1x7(const double *rs1, size_t vl);
void __riscv_vsseg7e64_v_f64m1x7(double *rs1, vfloat64m1x7_t vs3, size_t vl);
vfloat64m1x7_t __riscv_vlseg7e64ff_v_f64m1x7(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m1x7_t __riscv_vlsseg7e64_v_f64m1x7(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg7e64_v_f64m1x7(double *rs1, long rs2, vfloat64m1x7_t vs3, size_t vl);
vfloat64m1x7_t __riscv_vluxseg7ei8_v_f64m1x7(const double *rs1, vuint8mf8_t rs2, size_t vl);
vfloat64m1x7_t __riscv_vloxseg7ei8_v_f64m1x7(const double *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg7ei8_v_f64m1x7(double *rs1, vuint8mf8_t rs2, vfloat64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei8_v_f64m1x7(double *rs1, vuint8mf8_t rs2, vfloat64m1x7_t vs3, size_t vl);
vfloat64m1x7_t __riscv_vluxseg7ei16_v_f64m1x7(const double *rs1, vuint16mf4_t rs2, size_t vl);
vfloat64m1x7_t __riscv_vloxseg7ei16_v_f64m1x7(const double *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg7ei16_v_f64m1x7(double *rs1, vuint16mf4_t rs2, vfloat64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei16_v_f64m1x7(double *rs1, vuint16mf4_t rs2, vfloat64m1x7_t vs3, size_t vl);
vfloat64m1x7_t __riscv_vluxseg7ei32_v_f64m1x7(const double *rs1, vuint32mf2_t rs2, size_t vl);
vfloat64m1x7_t __riscv_vloxseg7ei32_v_f64m1x7(const double *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg7ei32_v_f64m1x7(double *rs1, vuint32mf2_t rs2, vfloat64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei32_v_f64m1x7(double *rs1, vuint32mf2_t rs2, vfloat64m1x7_t vs3, size_t vl);
vfloat64m1x7_t __riscv_vluxseg7ei64_v_f64m1x7(const double *rs1, vuint64m1_t rs2, size_t vl);
vfloat64m1x7_t __riscv_vloxseg7ei64_v_f64m1x7(const double *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg7ei64_v_f64m1x7(double *rs1, vuint64m1_t rs2, vfloat64m1x7_t vs3, size_t vl);
void __riscv_vsoxseg7ei64_v_f64m1x7(double *rs1, vuint64m1_t rs2, vfloat64m1x7_t vs3, size_t vl);
vuint64m1x8_t __riscv_vundefined_u64m1x8(void);
vuint64m1x8_t __riscv_vcreate_v_u64m1_u64m1x8(vuint64m1_t v0, vuint64m1_t v1, vuint64m1_t v2, vuint64m1_t v3, vuint64m1_t v4, vuint64m1_t v5, vuint64m1_t v6, vuint64m1_t v7);
vuint64m1_t __riscv_vget_v_u64m1x8_u64m1(vuint64m1x8_t src, size_t index);
vuint64m1x8_t __riscv_vset_v_u64m1x8_u64m1(vuint64m1x8_t dest, size_t index, vuint64m1_t value);
vuint64m1x8_t __riscv_vlseg8e64_v_u64m1x8(const uint64_t *rs1, size_t vl);
void __riscv_vsseg8e64_v_u64m1x8(uint64_t *rs1, vuint64m1x8_t vs3, size_t vl);
vuint64m1x8_t __riscv_vlseg8e64ff_v_u64m1x8(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m1x8_t __riscv_vlsseg8e64_v_u64m1x8(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg8e64_v_u64m1x8(uint64_t *rs1, long rs2, vuint64m1x8_t vs3, size_t vl);
vuint64m1x8_t __riscv_vluxseg8ei8_v_u64m1x8(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
vuint64m1x8_t __riscv_vloxseg8ei8_v_u64m1x8(const uint64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_u64m1x8(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_u64m1x8(uint64_t *rs1, vuint8mf8_t rs2, vuint64m1x8_t vs3, size_t vl);
vuint64m1x8_t __riscv_vluxseg8ei16_v_u64m1x8(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
vuint64m1x8_t __riscv_vloxseg8ei16_v_u64m1x8(const uint64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_u64m1x8(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_u64m1x8(uint64_t *rs1, vuint16mf4_t rs2, vuint64m1x8_t vs3, size_t vl);
vuint64m1x8_t __riscv_vluxseg8ei32_v_u64m1x8(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
vuint64m1x8_t __riscv_vloxseg8ei32_v_u64m1x8(const uint64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_u64m1x8(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_u64m1x8(uint64_t *rs1, vuint32mf2_t rs2, vuint64m1x8_t vs3, size_t vl);
vuint64m1x8_t __riscv_vluxseg8ei64_v_u64m1x8(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
vuint64m1x8_t __riscv_vloxseg8ei64_v_u64m1x8(const uint64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_u64m1x8(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_u64m1x8(uint64_t *rs1, vuint64m1_t rs2, vuint64m1x8_t vs3, size_t vl);
vint64m1x8_t __riscv_vundefined_i64m1x8(void);
vint64m1x8_t __riscv_vcreate_v_i64m1_i64m1x8(vint64m1_t v0, vint64m1_t v1, vint64m1_t v2, vint64m1_t v3, vint64m1_t v4, vint64m1_t v5, vint64m1_t v6, vint64m1_t v7);
vint64m1_t __riscv_vget_v_i64m1x8_i64m1(vint64m1x8_t src, size_t index);
vint64m1x8_t __riscv_vset_v_i64m1x8_i64m1(vint64m1x8_t dest, size_t index, vint64m1_t value);
vint64m1x8_t __riscv_vlseg8e64_v_i64m1x8(const int64_t *rs1, size_t vl);
void __riscv_vsseg8e64_v_i64m1x8(int64_t *rs1, vint64m1x8_t vs3, size_t vl);
vint64m1x8_t __riscv_vlseg8e64ff_v_i64m1x8(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m1x8_t __riscv_vlsseg8e64_v_i64m1x8(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg8e64_v_i64m1x8(int64_t *rs1, long rs2, vint64m1x8_t vs3, size_t vl);
vint64m1x8_t __riscv_vluxseg8ei8_v_i64m1x8(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
vint64m1x8_t __riscv_vloxseg8ei8_v_i64m1x8(const int64_t *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_i64m1x8(int64_t *rs1, vuint8mf8_t rs2, vint64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_i64m1x8(int64_t *rs1, vuint8mf8_t rs2, vint64m1x8_t vs3, size_t vl);
vint64m1x8_t __riscv_vluxseg8ei16_v_i64m1x8(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
vint64m1x8_t __riscv_vloxseg8ei16_v_i64m1x8(const int64_t *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_i64m1x8(int64_t *rs1, vuint16mf4_t rs2, vint64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_i64m1x8(int64_t *rs1, vuint16mf4_t rs2, vint64m1x8_t vs3, size_t vl);
vint64m1x8_t __riscv_vluxseg8ei32_v_i64m1x8(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
vint64m1x8_t __riscv_vloxseg8ei32_v_i64m1x8(const int64_t *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_i64m1x8(int64_t *rs1, vuint32mf2_t rs2, vint64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_i64m1x8(int64_t *rs1, vuint32mf2_t rs2, vint64m1x8_t vs3, size_t vl);
vint64m1x8_t __riscv_vluxseg8ei64_v_i64m1x8(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
vint64m1x8_t __riscv_vloxseg8ei64_v_i64m1x8(const int64_t *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_i64m1x8(int64_t *rs1, vuint64m1_t rs2, vint64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_i64m1x8(int64_t *rs1, vuint64m1_t rs2, vint64m1x8_t vs3, size_t vl);
vfloat64m1x8_t __riscv_vundefined_f64m1x8(void);
vfloat64m1x8_t __riscv_vcreate_v_f64m1_f64m1x8(vfloat64m1_t v0, vfloat64m1_t v1, vfloat64m1_t v2, vfloat64m1_t v3, vfloat64m1_t v4, vfloat64m1_t v5, vfloat64m1_t v6, vfloat64m1_t v7);
vfloat64m1_t __riscv_vget_v_f64m1x8_f64m1(vfloat64m1x8_t src, size_t index);
vfloat64m1x8_t __riscv_vset_v_f64m1x8_f64m1(vfloat64m1x8_t dest, size_t index, vfloat64m1_t value);
vfloat64m1x8_t __riscv_vlseg8e64_v_f64m1x8(const double *rs1, size_t vl);
void __riscv_vsseg8e64_v_f64m1x8(double *rs1, vfloat64m1x8_t vs3, size_t vl);
vfloat64m1x8_t __riscv_vlseg8e64ff_v_f64m1x8(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m1x8_t __riscv_vlsseg8e64_v_f64m1x8(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg8e64_v_f64m1x8(double *rs1, long rs2, vfloat64m1x8_t vs3, size_t vl);
vfloat64m1x8_t __riscv_vluxseg8ei8_v_f64m1x8(const double *rs1, vuint8mf8_t rs2, size_t vl);
vfloat64m1x8_t __riscv_vloxseg8ei8_v_f64m1x8(const double *rs1, vuint8mf8_t rs2, size_t vl);
void __riscv_vsuxseg8ei8_v_f64m1x8(double *rs1, vuint8mf8_t rs2, vfloat64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei8_v_f64m1x8(double *rs1, vuint8mf8_t rs2, vfloat64m1x8_t vs3, size_t vl);
vfloat64m1x8_t __riscv_vluxseg8ei16_v_f64m1x8(const double *rs1, vuint16mf4_t rs2, size_t vl);
vfloat64m1x8_t __riscv_vloxseg8ei16_v_f64m1x8(const double *rs1, vuint16mf4_t rs2, size_t vl);
void __riscv_vsuxseg8ei16_v_f64m1x8(double *rs1, vuint16mf4_t rs2, vfloat64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei16_v_f64m1x8(double *rs1, vuint16mf4_t rs2, vfloat64m1x8_t vs3, size_t vl);
vfloat64m1x8_t __riscv_vluxseg8ei32_v_f64m1x8(const double *rs1, vuint32mf2_t rs2, size_t vl);
vfloat64m1x8_t __riscv_vloxseg8ei32_v_f64m1x8(const double *rs1, vuint32mf2_t rs2, size_t vl);
void __riscv_vsuxseg8ei32_v_f64m1x8(double *rs1, vuint32mf2_t rs2, vfloat64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei32_v_f64m1x8(double *rs1, vuint32mf2_t rs2, vfloat64m1x8_t vs3, size_t vl);
vfloat64m1x8_t __riscv_vluxseg8ei64_v_f64m1x8(const double *rs1, vuint64m1_t rs2, size_t vl);
vfloat64m1x8_t __riscv_vloxseg8ei64_v_f64m1x8(const double *rs1, vuint64m1_t rs2, size_t vl);
void __riscv_vsuxseg8ei64_v_f64m1x8(double *rs1, vuint64m1_t rs2, vfloat64m1x8_t vs3, size_t vl);
void __riscv_vsoxseg8ei64_v_f64m1x8(double *rs1, vuint64m1_t rs2, vfloat64m1x8_t vs3, size_t vl);
vuint64m2x2_t __riscv_vundefined_u64m2x2(void);
vuint64m2x2_t __riscv_vcreate_v_u64m2_u64m2x2(vuint64m2_t v0, vuint64m2_t v1);
vuint64m2_t __riscv_vget_v_u64m2x2_u64m2(vuint64m2x2_t src, size_t index);
vuint64m2x2_t __riscv_vset_v_u64m2x2_u64m2(vuint64m2x2_t dest, size_t index, vuint64m2_t value);
vuint64m2x2_t __riscv_vlseg2e64_v_u64m2x2(const uint64_t *rs1, size_t vl);
void __riscv_vsseg2e64_v_u64m2x2(uint64_t *rs1, vuint64m2x2_t vs3, size_t vl);
vuint64m2x2_t __riscv_vlseg2e64ff_v_u64m2x2(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m2x2_t __riscv_vlsseg2e64_v_u64m2x2(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_u64m2x2(uint64_t *rs1, long rs2, vuint64m2x2_t vs3, size_t vl);
vuint64m2x2_t __riscv_vluxseg2ei8_v_u64m2x2(const uint64_t *rs1, vuint8mf4_t rs2, size_t vl);
vuint64m2x2_t __riscv_vloxseg2ei8_v_u64m2x2(const uint64_t *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u64m2x2(uint64_t *rs1, vuint8mf4_t rs2, vuint64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u64m2x2(uint64_t *rs1, vuint8mf4_t rs2, vuint64m2x2_t vs3, size_t vl);
vuint64m2x2_t __riscv_vluxseg2ei16_v_u64m2x2(const uint64_t *rs1, vuint16mf2_t rs2, size_t vl);
vuint64m2x2_t __riscv_vloxseg2ei16_v_u64m2x2(const uint64_t *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u64m2x2(uint64_t *rs1, vuint16mf2_t rs2, vuint64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u64m2x2(uint64_t *rs1, vuint16mf2_t rs2, vuint64m2x2_t vs3, size_t vl);
vuint64m2x2_t __riscv_vluxseg2ei32_v_u64m2x2(const uint64_t *rs1, vuint32m1_t rs2, size_t vl);
vuint64m2x2_t __riscv_vloxseg2ei32_v_u64m2x2(const uint64_t *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u64m2x2(uint64_t *rs1, vuint32m1_t rs2, vuint64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u64m2x2(uint64_t *rs1, vuint32m1_t rs2, vuint64m2x2_t vs3, size_t vl);
vuint64m2x2_t __riscv_vluxseg2ei64_v_u64m2x2(const uint64_t *rs1, vuint64m2_t rs2, size_t vl);
vuint64m2x2_t __riscv_vloxseg2ei64_v_u64m2x2(const uint64_t *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u64m2x2(uint64_t *rs1, vuint64m2_t rs2, vuint64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u64m2x2(uint64_t *rs1, vuint64m2_t rs2, vuint64m2x2_t vs3, size_t vl);
vint64m2x2_t __riscv_vundefined_i64m2x2(void);
vint64m2x2_t __riscv_vcreate_v_i64m2_i64m2x2(vint64m2_t v0, vint64m2_t v1);
vint64m2_t __riscv_vget_v_i64m2x2_i64m2(vint64m2x2_t src, size_t index);
vint64m2x2_t __riscv_vset_v_i64m2x2_i64m2(vint64m2x2_t dest, size_t index, vint64m2_t value);
vint64m2x2_t __riscv_vlseg2e64_v_i64m2x2(const int64_t *rs1, size_t vl);
void __riscv_vsseg2e64_v_i64m2x2(int64_t *rs1, vint64m2x2_t vs3, size_t vl);
vint64m2x2_t __riscv_vlseg2e64ff_v_i64m2x2(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m2x2_t __riscv_vlsseg2e64_v_i64m2x2(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_i64m2x2(int64_t *rs1, long rs2, vint64m2x2_t vs3, size_t vl);
vint64m2x2_t __riscv_vluxseg2ei8_v_i64m2x2(const int64_t *rs1, vuint8mf4_t rs2, size_t vl);
vint64m2x2_t __riscv_vloxseg2ei8_v_i64m2x2(const int64_t *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i64m2x2(int64_t *rs1, vuint8mf4_t rs2, vint64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i64m2x2(int64_t *rs1, vuint8mf4_t rs2, vint64m2x2_t vs3, size_t vl);
vint64m2x2_t __riscv_vluxseg2ei16_v_i64m2x2(const int64_t *rs1, vuint16mf2_t rs2, size_t vl);
vint64m2x2_t __riscv_vloxseg2ei16_v_i64m2x2(const int64_t *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i64m2x2(int64_t *rs1, vuint16mf2_t rs2, vint64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i64m2x2(int64_t *rs1, vuint16mf2_t rs2, vint64m2x2_t vs3, size_t vl);
vint64m2x2_t __riscv_vluxseg2ei32_v_i64m2x2(const int64_t *rs1, vuint32m1_t rs2, size_t vl);
vint64m2x2_t __riscv_vloxseg2ei32_v_i64m2x2(const int64_t *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i64m2x2(int64_t *rs1, vuint32m1_t rs2, vint64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i64m2x2(int64_t *rs1, vuint32m1_t rs2, vint64m2x2_t vs3, size_t vl);
vint64m2x2_t __riscv_vluxseg2ei64_v_i64m2x2(const int64_t *rs1, vuint64m2_t rs2, size_t vl);
vint64m2x2_t __riscv_vloxseg2ei64_v_i64m2x2(const int64_t *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i64m2x2(int64_t *rs1, vuint64m2_t rs2, vint64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i64m2x2(int64_t *rs1, vuint64m2_t rs2, vint64m2x2_t vs3, size_t vl);
vfloat64m2x2_t __riscv_vundefined_f64m2x2(void);
vfloat64m2x2_t __riscv_vcreate_v_f64m2_f64m2x2(vfloat64m2_t v0, vfloat64m2_t v1);
vfloat64m2_t __riscv_vget_v_f64m2x2_f64m2(vfloat64m2x2_t src, size_t index);
vfloat64m2x2_t __riscv_vset_v_f64m2x2_f64m2(vfloat64m2x2_t dest, size_t index, vfloat64m2_t value);
vfloat64m2x2_t __riscv_vlseg2e64_v_f64m2x2(const double *rs1, size_t vl);
void __riscv_vsseg2e64_v_f64m2x2(double *rs1, vfloat64m2x2_t vs3, size_t vl);
vfloat64m2x2_t __riscv_vlseg2e64ff_v_f64m2x2(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m2x2_t __riscv_vlsseg2e64_v_f64m2x2(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_f64m2x2(double *rs1, long rs2, vfloat64m2x2_t vs3, size_t vl);
vfloat64m2x2_t __riscv_vluxseg2ei8_v_f64m2x2(const double *rs1, vuint8mf4_t rs2, size_t vl);
vfloat64m2x2_t __riscv_vloxseg2ei8_v_f64m2x2(const double *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_f64m2x2(double *rs1, vuint8mf4_t rs2, vfloat64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_f64m2x2(double *rs1, vuint8mf4_t rs2, vfloat64m2x2_t vs3, size_t vl);
vfloat64m2x2_t __riscv_vluxseg2ei16_v_f64m2x2(const double *rs1, vuint16mf2_t rs2, size_t vl);
vfloat64m2x2_t __riscv_vloxseg2ei16_v_f64m2x2(const double *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_f64m2x2(double *rs1, vuint16mf2_t rs2, vfloat64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_f64m2x2(double *rs1, vuint16mf2_t rs2, vfloat64m2x2_t vs3, size_t vl);
vfloat64m2x2_t __riscv_vluxseg2ei32_v_f64m2x2(const double *rs1, vuint32m1_t rs2, size_t vl);
vfloat64m2x2_t __riscv_vloxseg2ei32_v_f64m2x2(const double *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_f64m2x2(double *rs1, vuint32m1_t rs2, vfloat64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_f64m2x2(double *rs1, vuint32m1_t rs2, vfloat64m2x2_t vs3, size_t vl);
vfloat64m2x2_t __riscv_vluxseg2ei64_v_f64m2x2(const double *rs1, vuint64m2_t rs2, size_t vl);
vfloat64m2x2_t __riscv_vloxseg2ei64_v_f64m2x2(const double *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_f64m2x2(double *rs1, vuint64m2_t rs2, vfloat64m2x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_f64m2x2(double *rs1, vuint64m2_t rs2, vfloat64m2x2_t vs3, size_t vl);
vuint64m2x3_t __riscv_vundefined_u64m2x3(void);
vuint64m2x3_t __riscv_vcreate_v_u64m2_u64m2x3(vuint64m2_t v0, vuint64m2_t v1, vuint64m2_t v2);
vuint64m2_t __riscv_vget_v_u64m2x3_u64m2(vuint64m2x3_t src, size_t index);
vuint64m2x3_t __riscv_vset_v_u64m2x3_u64m2(vuint64m2x3_t dest, size_t index, vuint64m2_t value);
vuint64m2x3_t __riscv_vlseg3e64_v_u64m2x3(const uint64_t *rs1, size_t vl);
void __riscv_vsseg3e64_v_u64m2x3(uint64_t *rs1, vuint64m2x3_t vs3, size_t vl);
vuint64m2x3_t __riscv_vlseg3e64ff_v_u64m2x3(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m2x3_t __riscv_vlsseg3e64_v_u64m2x3(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg3e64_v_u64m2x3(uint64_t *rs1, long rs2, vuint64m2x3_t vs3, size_t vl);
vuint64m2x3_t __riscv_vluxseg3ei8_v_u64m2x3(const uint64_t *rs1, vuint8mf4_t rs2, size_t vl);
vuint64m2x3_t __riscv_vloxseg3ei8_v_u64m2x3(const uint64_t *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_u64m2x3(uint64_t *rs1, vuint8mf4_t rs2, vuint64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_u64m2x3(uint64_t *rs1, vuint8mf4_t rs2, vuint64m2x3_t vs3, size_t vl);
vuint64m2x3_t __riscv_vluxseg3ei16_v_u64m2x3(const uint64_t *rs1, vuint16mf2_t rs2, size_t vl);
vuint64m2x3_t __riscv_vloxseg3ei16_v_u64m2x3(const uint64_t *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_u64m2x3(uint64_t *rs1, vuint16mf2_t rs2, vuint64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_u64m2x3(uint64_t *rs1, vuint16mf2_t rs2, vuint64m2x3_t vs3, size_t vl);
vuint64m2x3_t __riscv_vluxseg3ei32_v_u64m2x3(const uint64_t *rs1, vuint32m1_t rs2, size_t vl);
vuint64m2x3_t __riscv_vloxseg3ei32_v_u64m2x3(const uint64_t *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_u64m2x3(uint64_t *rs1, vuint32m1_t rs2, vuint64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_u64m2x3(uint64_t *rs1, vuint32m1_t rs2, vuint64m2x3_t vs3, size_t vl);
vuint64m2x3_t __riscv_vluxseg3ei64_v_u64m2x3(const uint64_t *rs1, vuint64m2_t rs2, size_t vl);
vuint64m2x3_t __riscv_vloxseg3ei64_v_u64m2x3(const uint64_t *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_u64m2x3(uint64_t *rs1, vuint64m2_t rs2, vuint64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_u64m2x3(uint64_t *rs1, vuint64m2_t rs2, vuint64m2x3_t vs3, size_t vl);
vint64m2x3_t __riscv_vundefined_i64m2x3(void);
vint64m2x3_t __riscv_vcreate_v_i64m2_i64m2x3(vint64m2_t v0, vint64m2_t v1, vint64m2_t v2);
vint64m2_t __riscv_vget_v_i64m2x3_i64m2(vint64m2x3_t src, size_t index);
vint64m2x3_t __riscv_vset_v_i64m2x3_i64m2(vint64m2x3_t dest, size_t index, vint64m2_t value);
vint64m2x3_t __riscv_vlseg3e64_v_i64m2x3(const int64_t *rs1, size_t vl);
void __riscv_vsseg3e64_v_i64m2x3(int64_t *rs1, vint64m2x3_t vs3, size_t vl);
vint64m2x3_t __riscv_vlseg3e64ff_v_i64m2x3(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m2x3_t __riscv_vlsseg3e64_v_i64m2x3(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg3e64_v_i64m2x3(int64_t *rs1, long rs2, vint64m2x3_t vs3, size_t vl);
vint64m2x3_t __riscv_vluxseg3ei8_v_i64m2x3(const int64_t *rs1, vuint8mf4_t rs2, size_t vl);
vint64m2x3_t __riscv_vloxseg3ei8_v_i64m2x3(const int64_t *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_i64m2x3(int64_t *rs1, vuint8mf4_t rs2, vint64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_i64m2x3(int64_t *rs1, vuint8mf4_t rs2, vint64m2x3_t vs3, size_t vl);
vint64m2x3_t __riscv_vluxseg3ei16_v_i64m2x3(const int64_t *rs1, vuint16mf2_t rs2, size_t vl);
vint64m2x3_t __riscv_vloxseg3ei16_v_i64m2x3(const int64_t *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_i64m2x3(int64_t *rs1, vuint16mf2_t rs2, vint64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_i64m2x3(int64_t *rs1, vuint16mf2_t rs2, vint64m2x3_t vs3, size_t vl);
vint64m2x3_t __riscv_vluxseg3ei32_v_i64m2x3(const int64_t *rs1, vuint32m1_t rs2, size_t vl);
vint64m2x3_t __riscv_vloxseg3ei32_v_i64m2x3(const int64_t *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_i64m2x3(int64_t *rs1, vuint32m1_t rs2, vint64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_i64m2x3(int64_t *rs1, vuint32m1_t rs2, vint64m2x3_t vs3, size_t vl);
vint64m2x3_t __riscv_vluxseg3ei64_v_i64m2x3(const int64_t *rs1, vuint64m2_t rs2, size_t vl);
vint64m2x3_t __riscv_vloxseg3ei64_v_i64m2x3(const int64_t *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_i64m2x3(int64_t *rs1, vuint64m2_t rs2, vint64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_i64m2x3(int64_t *rs1, vuint64m2_t rs2, vint64m2x3_t vs3, size_t vl);
vfloat64m2x3_t __riscv_vundefined_f64m2x3(void);
vfloat64m2x3_t __riscv_vcreate_v_f64m2_f64m2x3(vfloat64m2_t v0, vfloat64m2_t v1, vfloat64m2_t v2);
vfloat64m2_t __riscv_vget_v_f64m2x3_f64m2(vfloat64m2x3_t src, size_t index);
vfloat64m2x3_t __riscv_vset_v_f64m2x3_f64m2(vfloat64m2x3_t dest, size_t index, vfloat64m2_t value);
vfloat64m2x3_t __riscv_vlseg3e64_v_f64m2x3(const double *rs1, size_t vl);
void __riscv_vsseg3e64_v_f64m2x3(double *rs1, vfloat64m2x3_t vs3, size_t vl);
vfloat64m2x3_t __riscv_vlseg3e64ff_v_f64m2x3(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m2x3_t __riscv_vlsseg3e64_v_f64m2x3(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg3e64_v_f64m2x3(double *rs1, long rs2, vfloat64m2x3_t vs3, size_t vl);
vfloat64m2x3_t __riscv_vluxseg3ei8_v_f64m2x3(const double *rs1, vuint8mf4_t rs2, size_t vl);
vfloat64m2x3_t __riscv_vloxseg3ei8_v_f64m2x3(const double *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg3ei8_v_f64m2x3(double *rs1, vuint8mf4_t rs2, vfloat64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei8_v_f64m2x3(double *rs1, vuint8mf4_t rs2, vfloat64m2x3_t vs3, size_t vl);
vfloat64m2x3_t __riscv_vluxseg3ei16_v_f64m2x3(const double *rs1, vuint16mf2_t rs2, size_t vl);
vfloat64m2x3_t __riscv_vloxseg3ei16_v_f64m2x3(const double *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg3ei16_v_f64m2x3(double *rs1, vuint16mf2_t rs2, vfloat64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei16_v_f64m2x3(double *rs1, vuint16mf2_t rs2, vfloat64m2x3_t vs3, size_t vl);
vfloat64m2x3_t __riscv_vluxseg3ei32_v_f64m2x3(const double *rs1, vuint32m1_t rs2, size_t vl);
vfloat64m2x3_t __riscv_vloxseg3ei32_v_f64m2x3(const double *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg3ei32_v_f64m2x3(double *rs1, vuint32m1_t rs2, vfloat64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei32_v_f64m2x3(double *rs1, vuint32m1_t rs2, vfloat64m2x3_t vs3, size_t vl);
vfloat64m2x3_t __riscv_vluxseg3ei64_v_f64m2x3(const double *rs1, vuint64m2_t rs2, size_t vl);
vfloat64m2x3_t __riscv_vloxseg3ei64_v_f64m2x3(const double *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg3ei64_v_f64m2x3(double *rs1, vuint64m2_t rs2, vfloat64m2x3_t vs3, size_t vl);
void __riscv_vsoxseg3ei64_v_f64m2x3(double *rs1, vuint64m2_t rs2, vfloat64m2x3_t vs3, size_t vl);
vuint64m2x4_t __riscv_vundefined_u64m2x4(void);
vuint64m2x4_t __riscv_vcreate_v_u64m2_u64m2x4(vuint64m2_t v0, vuint64m2_t v1, vuint64m2_t v2, vuint64m2_t v3);
vuint64m2_t __riscv_vget_v_u64m2x4_u64m2(vuint64m2x4_t src, size_t index);
vuint64m2x4_t __riscv_vset_v_u64m2x4_u64m2(vuint64m2x4_t dest, size_t index, vuint64m2_t value);
vuint64m2x4_t __riscv_vlseg4e64_v_u64m2x4(const uint64_t *rs1, size_t vl);
void __riscv_vsseg4e64_v_u64m2x4(uint64_t *rs1, vuint64m2x4_t vs3, size_t vl);
vuint64m2x4_t __riscv_vlseg4e64ff_v_u64m2x4(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m2x4_t __riscv_vlsseg4e64_v_u64m2x4(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg4e64_v_u64m2x4(uint64_t *rs1, long rs2, vuint64m2x4_t vs3, size_t vl);
vuint64m2x4_t __riscv_vluxseg4ei8_v_u64m2x4(const uint64_t *rs1, vuint8mf4_t rs2, size_t vl);
vuint64m2x4_t __riscv_vloxseg4ei8_v_u64m2x4(const uint64_t *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_u64m2x4(uint64_t *rs1, vuint8mf4_t rs2, vuint64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_u64m2x4(uint64_t *rs1, vuint8mf4_t rs2, vuint64m2x4_t vs3, size_t vl);
vuint64m2x4_t __riscv_vluxseg4ei16_v_u64m2x4(const uint64_t *rs1, vuint16mf2_t rs2, size_t vl);
vuint64m2x4_t __riscv_vloxseg4ei16_v_u64m2x4(const uint64_t *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_u64m2x4(uint64_t *rs1, vuint16mf2_t rs2, vuint64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_u64m2x4(uint64_t *rs1, vuint16mf2_t rs2, vuint64m2x4_t vs3, size_t vl);
vuint64m2x4_t __riscv_vluxseg4ei32_v_u64m2x4(const uint64_t *rs1, vuint32m1_t rs2, size_t vl);
vuint64m2x4_t __riscv_vloxseg4ei32_v_u64m2x4(const uint64_t *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_u64m2x4(uint64_t *rs1, vuint32m1_t rs2, vuint64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_u64m2x4(uint64_t *rs1, vuint32m1_t rs2, vuint64m2x4_t vs3, size_t vl);
vuint64m2x4_t __riscv_vluxseg4ei64_v_u64m2x4(const uint64_t *rs1, vuint64m2_t rs2, size_t vl);
vuint64m2x4_t __riscv_vloxseg4ei64_v_u64m2x4(const uint64_t *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_u64m2x4(uint64_t *rs1, vuint64m2_t rs2, vuint64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_u64m2x4(uint64_t *rs1, vuint64m2_t rs2, vuint64m2x4_t vs3, size_t vl);
vint64m2x4_t __riscv_vundefined_i64m2x4(void);
vint64m2x4_t __riscv_vcreate_v_i64m2_i64m2x4(vint64m2_t v0, vint64m2_t v1, vint64m2_t v2, vint64m2_t v3);
vint64m2_t __riscv_vget_v_i64m2x4_i64m2(vint64m2x4_t src, size_t index);
vint64m2x4_t __riscv_vset_v_i64m2x4_i64m2(vint64m2x4_t dest, size_t index, vint64m2_t value);
vint64m2x4_t __riscv_vlseg4e64_v_i64m2x4(const int64_t *rs1, size_t vl);
void __riscv_vsseg4e64_v_i64m2x4(int64_t *rs1, vint64m2x4_t vs3, size_t vl);
vint64m2x4_t __riscv_vlseg4e64ff_v_i64m2x4(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m2x4_t __riscv_vlsseg4e64_v_i64m2x4(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg4e64_v_i64m2x4(int64_t *rs1, long rs2, vint64m2x4_t vs3, size_t vl);
vint64m2x4_t __riscv_vluxseg4ei8_v_i64m2x4(const int64_t *rs1, vuint8mf4_t rs2, size_t vl);
vint64m2x4_t __riscv_vloxseg4ei8_v_i64m2x4(const int64_t *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_i64m2x4(int64_t *rs1, vuint8mf4_t rs2, vint64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_i64m2x4(int64_t *rs1, vuint8mf4_t rs2, vint64m2x4_t vs3, size_t vl);
vint64m2x4_t __riscv_vluxseg4ei16_v_i64m2x4(const int64_t *rs1, vuint16mf2_t rs2, size_t vl);
vint64m2x4_t __riscv_vloxseg4ei16_v_i64m2x4(const int64_t *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_i64m2x4(int64_t *rs1, vuint16mf2_t rs2, vint64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_i64m2x4(int64_t *rs1, vuint16mf2_t rs2, vint64m2x4_t vs3, size_t vl);
vint64m2x4_t __riscv_vluxseg4ei32_v_i64m2x4(const int64_t *rs1, vuint32m1_t rs2, size_t vl);
vint64m2x4_t __riscv_vloxseg4ei32_v_i64m2x4(const int64_t *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_i64m2x4(int64_t *rs1, vuint32m1_t rs2, vint64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_i64m2x4(int64_t *rs1, vuint32m1_t rs2, vint64m2x4_t vs3, size_t vl);
vint64m2x4_t __riscv_vluxseg4ei64_v_i64m2x4(const int64_t *rs1, vuint64m2_t rs2, size_t vl);
vint64m2x4_t __riscv_vloxseg4ei64_v_i64m2x4(const int64_t *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_i64m2x4(int64_t *rs1, vuint64m2_t rs2, vint64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_i64m2x4(int64_t *rs1, vuint64m2_t rs2, vint64m2x4_t vs3, size_t vl);
vfloat64m2x4_t __riscv_vundefined_f64m2x4(void);
vfloat64m2x4_t __riscv_vcreate_v_f64m2_f64m2x4(vfloat64m2_t v0, vfloat64m2_t v1, vfloat64m2_t v2, vfloat64m2_t v3);
vfloat64m2_t __riscv_vget_v_f64m2x4_f64m2(vfloat64m2x4_t src, size_t index);
vfloat64m2x4_t __riscv_vset_v_f64m2x4_f64m2(vfloat64m2x4_t dest, size_t index, vfloat64m2_t value);
vfloat64m2x4_t __riscv_vlseg4e64_v_f64m2x4(const double *rs1, size_t vl);
void __riscv_vsseg4e64_v_f64m2x4(double *rs1, vfloat64m2x4_t vs3, size_t vl);
vfloat64m2x4_t __riscv_vlseg4e64ff_v_f64m2x4(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m2x4_t __riscv_vlsseg4e64_v_f64m2x4(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg4e64_v_f64m2x4(double *rs1, long rs2, vfloat64m2x4_t vs3, size_t vl);
vfloat64m2x4_t __riscv_vluxseg4ei8_v_f64m2x4(const double *rs1, vuint8mf4_t rs2, size_t vl);
vfloat64m2x4_t __riscv_vloxseg4ei8_v_f64m2x4(const double *rs1, vuint8mf4_t rs2, size_t vl);
void __riscv_vsuxseg4ei8_v_f64m2x4(double *rs1, vuint8mf4_t rs2, vfloat64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei8_v_f64m2x4(double *rs1, vuint8mf4_t rs2, vfloat64m2x4_t vs3, size_t vl);
vfloat64m2x4_t __riscv_vluxseg4ei16_v_f64m2x4(const double *rs1, vuint16mf2_t rs2, size_t vl);
vfloat64m2x4_t __riscv_vloxseg4ei16_v_f64m2x4(const double *rs1, vuint16mf2_t rs2, size_t vl);
void __riscv_vsuxseg4ei16_v_f64m2x4(double *rs1, vuint16mf2_t rs2, vfloat64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei16_v_f64m2x4(double *rs1, vuint16mf2_t rs2, vfloat64m2x4_t vs3, size_t vl);
vfloat64m2x4_t __riscv_vluxseg4ei32_v_f64m2x4(const double *rs1, vuint32m1_t rs2, size_t vl);
vfloat64m2x4_t __riscv_vloxseg4ei32_v_f64m2x4(const double *rs1, vuint32m1_t rs2, size_t vl);
void __riscv_vsuxseg4ei32_v_f64m2x4(double *rs1, vuint32m1_t rs2, vfloat64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei32_v_f64m2x4(double *rs1, vuint32m1_t rs2, vfloat64m2x4_t vs3, size_t vl);
vfloat64m2x4_t __riscv_vluxseg4ei64_v_f64m2x4(const double *rs1, vuint64m2_t rs2, size_t vl);
vfloat64m2x4_t __riscv_vloxseg4ei64_v_f64m2x4(const double *rs1, vuint64m2_t rs2, size_t vl);
void __riscv_vsuxseg4ei64_v_f64m2x4(double *rs1, vuint64m2_t rs2, vfloat64m2x4_t vs3, size_t vl);
void __riscv_vsoxseg4ei64_v_f64m2x4(double *rs1, vuint64m2_t rs2, vfloat64m2x4_t vs3, size_t vl);
vuint64m4x2_t __riscv_vundefined_u64m4x2(void);
vuint64m4x2_t __riscv_vcreate_v_u64m4_u64m4x2(vuint64m4_t v0, vuint64m4_t v1);
vuint64m4_t __riscv_vget_v_u64m4x2_u64m4(vuint64m4x2_t src, size_t index);
vuint64m4x2_t __riscv_vset_v_u64m4x2_u64m4(vuint64m4x2_t dest, size_t index, vuint64m4_t value);
vuint64m4x2_t __riscv_vlseg2e64_v_u64m4x2(const uint64_t *rs1, size_t vl);
void __riscv_vsseg2e64_v_u64m4x2(uint64_t *rs1, vuint64m4x2_t vs3, size_t vl);
vuint64m4x2_t __riscv_vlseg2e64ff_v_u64m4x2(const uint64_t *rs1, size_t *new_vl, size_t vl);
vuint64m4x2_t __riscv_vlsseg2e64_v_u64m4x2(const uint64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_u64m4x2(uint64_t *rs1, long rs2, vuint64m4x2_t vs3, size_t vl);
vuint64m4x2_t __riscv_vluxseg2ei8_v_u64m4x2(const uint64_t *rs1, vuint8mf2_t rs2, size_t vl);
vuint64m4x2_t __riscv_vloxseg2ei8_v_u64m4x2(const uint64_t *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_u64m4x2(uint64_t *rs1, vuint8mf2_t rs2, vuint64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_u64m4x2(uint64_t *rs1, vuint8mf2_t rs2, vuint64m4x2_t vs3, size_t vl);
vuint64m4x2_t __riscv_vluxseg2ei16_v_u64m4x2(const uint64_t *rs1, vuint16m1_t rs2, size_t vl);
vuint64m4x2_t __riscv_vloxseg2ei16_v_u64m4x2(const uint64_t *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_u64m4x2(uint64_t *rs1, vuint16m1_t rs2, vuint64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_u64m4x2(uint64_t *rs1, vuint16m1_t rs2, vuint64m4x2_t vs3, size_t vl);
vuint64m4x2_t __riscv_vluxseg2ei32_v_u64m4x2(const uint64_t *rs1, vuint32m2_t rs2, size_t vl);
vuint64m4x2_t __riscv_vloxseg2ei32_v_u64m4x2(const uint64_t *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_u64m4x2(uint64_t *rs1, vuint32m2_t rs2, vuint64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_u64m4x2(uint64_t *rs1, vuint32m2_t rs2, vuint64m4x2_t vs3, size_t vl);
vuint64m4x2_t __riscv_vluxseg2ei64_v_u64m4x2(const uint64_t *rs1, vuint64m4_t rs2, size_t vl);
vuint64m4x2_t __riscv_vloxseg2ei64_v_u64m4x2(const uint64_t *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_u64m4x2(uint64_t *rs1, vuint64m4_t rs2, vuint64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_u64m4x2(uint64_t *rs1, vuint64m4_t rs2, vuint64m4x2_t vs3, size_t vl);
vint64m4x2_t __riscv_vundefined_i64m4x2(void);
vint64m4x2_t __riscv_vcreate_v_i64m4_i64m4x2(vint64m4_t v0, vint64m4_t v1);
vint64m4_t __riscv_vget_v_i64m4x2_i64m4(vint64m4x2_t src, size_t index);
vint64m4x2_t __riscv_vset_v_i64m4x2_i64m4(vint64m4x2_t dest, size_t index, vint64m4_t value);
vint64m4x2_t __riscv_vlseg2e64_v_i64m4x2(const int64_t *rs1, size_t vl);
void __riscv_vsseg2e64_v_i64m4x2(int64_t *rs1, vint64m4x2_t vs3, size_t vl);
vint64m4x2_t __riscv_vlseg2e64ff_v_i64m4x2(const int64_t *rs1, size_t *new_vl, size_t vl);
vint64m4x2_t __riscv_vlsseg2e64_v_i64m4x2(const int64_t *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_i64m4x2(int64_t *rs1, long rs2, vint64m4x2_t vs3, size_t vl);
vint64m4x2_t __riscv_vluxseg2ei8_v_i64m4x2(const int64_t *rs1, vuint8mf2_t rs2, size_t vl);
vint64m4x2_t __riscv_vloxseg2ei8_v_i64m4x2(const int64_t *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_i64m4x2(int64_t *rs1, vuint8mf2_t rs2, vint64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_i64m4x2(int64_t *rs1, vuint8mf2_t rs2, vint64m4x2_t vs3, size_t vl);
vint64m4x2_t __riscv_vluxseg2ei16_v_i64m4x2(const int64_t *rs1, vuint16m1_t rs2, size_t vl);
vint64m4x2_t __riscv_vloxseg2ei16_v_i64m4x2(const int64_t *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_i64m4x2(int64_t *rs1, vuint16m1_t rs2, vint64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_i64m4x2(int64_t *rs1, vuint16m1_t rs2, vint64m4x2_t vs3, size_t vl);
vint64m4x2_t __riscv_vluxseg2ei32_v_i64m4x2(const int64_t *rs1, vuint32m2_t rs2, size_t vl);
vint64m4x2_t __riscv_vloxseg2ei32_v_i64m4x2(const int64_t *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_i64m4x2(int64_t *rs1, vuint32m2_t rs2, vint64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_i64m4x2(int64_t *rs1, vuint32m2_t rs2, vint64m4x2_t vs3, size_t vl);
vint64m4x2_t __riscv_vluxseg2ei64_v_i64m4x2(const int64_t *rs1, vuint64m4_t rs2, size_t vl);
vint64m4x2_t __riscv_vloxseg2ei64_v_i64m4x2(const int64_t *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_i64m4x2(int64_t *rs1, vuint64m4_t rs2, vint64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_i64m4x2(int64_t *rs1, vuint64m4_t rs2, vint64m4x2_t vs3, size_t vl);
vfloat64m4x2_t __riscv_vundefined_f64m4x2(void);
vfloat64m4x2_t __riscv_vcreate_v_f64m4_f64m4x2(vfloat64m4_t v0, vfloat64m4_t v1);
vfloat64m4_t __riscv_vget_v_f64m4x2_f64m4(vfloat64m4x2_t src, size_t index);
vfloat64m4x2_t __riscv_vset_v_f64m4x2_f64m4(vfloat64m4x2_t dest, size_t index, vfloat64m4_t value);
vfloat64m4x2_t __riscv_vlseg2e64_v_f64m4x2(const double *rs1, size_t vl);
void __riscv_vsseg2e64_v_f64m4x2(double *rs1, vfloat64m4x2_t vs3, size_t vl);
vfloat64m4x2_t __riscv_vlseg2e64ff_v_f64m4x2(const double *rs1, size_t *new_vl, size_t vl);
vfloat64m4x2_t __riscv_vlsseg2e64_v_f64m4x2(const double *rs1, long rs2, size_t vl);
void __riscv_vssseg2e64_v_f64m4x2(double *rs1, long rs2, vfloat64m4x2_t vs3, size_t vl);
vfloat64m4x2_t __riscv_vluxseg2ei8_v_f64m4x2(const double *rs1, vuint8mf2_t rs2, size_t vl);
vfloat64m4x2_t __riscv_vloxseg2ei8_v_f64m4x2(const double *rs1, vuint8mf2_t rs2, size_t vl);
void __riscv_vsuxseg2ei8_v_f64m4x2(double *rs1, vuint8mf2_t rs2, vfloat64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei8_v_f64m4x2(double *rs1, vuint8mf2_t rs2, vfloat64m4x2_t vs3, size_t vl);
vfloat64m4x2_t __riscv_vluxseg2ei16_v_f64m4x2(const double *rs1, vuint16m1_t rs2, size_t vl);
vfloat64m4x2_t __riscv_vloxseg2ei16_v_f64m4x2(const double *rs1, vuint16m1_t rs2, size_t vl);
void __riscv_vsuxseg2ei16_v_f64m4x2(double *rs1, vuint16m1_t rs2, vfloat64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei16_v_f64m4x2(double *rs1, vuint16m1_t rs2, vfloat64m4x2_t vs3, size_t vl);
vfloat64m4x2_t __riscv_vluxseg2ei32_v_f64m4x2(const double *rs1, vuint32m2_t rs2, size_t vl);
vfloat64m4x2_t __riscv_vloxseg2ei32_v_f64m4x2(const double *rs1, vuint32m2_t rs2, size_t vl);
void __riscv_vsuxseg2ei32_v_f64m4x2(double *rs1, vuint32m2_t rs2, vfloat64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei32_v_f64m4x2(double *rs1, vuint32m2_t rs2, vfloat64m4x2_t vs3, size_t vl);
vfloat64m4x2_t __riscv_vluxseg2ei64_v_f64m4x2(const double *rs1, vuint64m4_t rs2, size_t vl);
vfloat64m4x2_t __riscv_vloxseg2ei64_v_f64m4x2(const double *rs1, vuint64m4_t rs2, size_t vl);
void __riscv_vsuxseg2ei64_v_f64m4x2(double *rs1, vuint64m4_t rs2, vfloat64m4x2_t vs3, size_t vl);
void __riscv_vsoxseg2ei64_v_f64m4x2(double *rs1, vuint64m4_t rs2, vfloat64m4x2_t vs3, size_t vl);

/* The spellings the vector API writes in terms of another instruction */
vbool1_t __riscv_vmmv_m_b1(vbool1_t vs2, size_t vl);
vbool1_t __riscv_vmnot_m_b1(vbool1_t vs2, size_t vl);
vbool1_t __riscv_vmclr_m_b1(size_t vl);
vbool1_t __riscv_vmset_m_b1(size_t vl);
vbool2_t __riscv_vmmv_m_b2(vbool2_t vs2, size_t vl);
vbool2_t __riscv_vmnot_m_b2(vbool2_t vs2, size_t vl);
vbool2_t __riscv_vmclr_m_b2(size_t vl);
vbool2_t __riscv_vmset_m_b2(size_t vl);
vbool4_t __riscv_vmmv_m_b4(vbool4_t vs2, size_t vl);
vbool4_t __riscv_vmnot_m_b4(vbool4_t vs2, size_t vl);
vbool4_t __riscv_vmclr_m_b4(size_t vl);
vbool4_t __riscv_vmset_m_b4(size_t vl);
vbool8_t __riscv_vmmv_m_b8(vbool8_t vs2, size_t vl);
vbool8_t __riscv_vmnot_m_b8(vbool8_t vs2, size_t vl);
vbool8_t __riscv_vmclr_m_b8(size_t vl);
vbool8_t __riscv_vmset_m_b8(size_t vl);
vbool16_t __riscv_vmmv_m_b16(vbool16_t vs2, size_t vl);
vbool16_t __riscv_vmnot_m_b16(vbool16_t vs2, size_t vl);
vbool16_t __riscv_vmclr_m_b16(size_t vl);
vbool16_t __riscv_vmset_m_b16(size_t vl);
vbool32_t __riscv_vmmv_m_b32(vbool32_t vs2, size_t vl);
vbool32_t __riscv_vmnot_m_b32(vbool32_t vs2, size_t vl);
vbool32_t __riscv_vmclr_m_b32(size_t vl);
vbool32_t __riscv_vmset_m_b32(size_t vl);
vbool64_t __riscv_vmmv_m_b64(vbool64_t vs2, size_t vl);
vbool64_t __riscv_vmnot_m_b64(vbool64_t vs2, size_t vl);
vbool64_t __riscv_vmclr_m_b64(size_t vl);
vbool64_t __riscv_vmset_m_b64(size_t vl);
_RVV_UNARY(vuint8mf8_t, u8mf8, vbool64_t, vnot, v, vuint8mf8_t)
_RVV_CMP(vuint8mf8_t, u8mf8, vbool64_t, 64, vmsgtu, vv, vuint8mf8_t)
_RVV_CMP(vuint8mf8_t, u8mf8, vbool64_t, 64, vmsgeu, vv, vuint8mf8_t)
_RVV_CMP(vuint8mf8_t, u8mf8, vbool64_t, 64, vmsgeu, vx, unsigned char)
_RVV_UNARY(vint8mf8_t, i8mf8, vbool64_t, vnot, v, vint8mf8_t)
_RVV_UNARY(vint8mf8_t, i8mf8, vbool64_t, vneg, v, vint8mf8_t)
_RVV_CMP(vint8mf8_t, i8mf8, vbool64_t, 64, vmsgt, vv, vint8mf8_t)
_RVV_CMP(vint8mf8_t, i8mf8, vbool64_t, 64, vmsge, vv, vint8mf8_t)
_RVV_CMP(vint8mf8_t, i8mf8, vbool64_t, 64, vmsge, vx, signed char)
_RVV_UNARY(vuint8mf4_t, u8mf4, vbool32_t, vnot, v, vuint8mf4_t)
_RVV_CMP(vuint8mf4_t, u8mf4, vbool32_t, 32, vmsgtu, vv, vuint8mf4_t)
_RVV_CMP(vuint8mf4_t, u8mf4, vbool32_t, 32, vmsgeu, vv, vuint8mf4_t)
_RVV_CMP(vuint8mf4_t, u8mf4, vbool32_t, 32, vmsgeu, vx, unsigned char)
_RVV_UNARY(vint8mf4_t, i8mf4, vbool32_t, vnot, v, vint8mf4_t)
_RVV_UNARY(vint8mf4_t, i8mf4, vbool32_t, vneg, v, vint8mf4_t)
_RVV_CMP(vint8mf4_t, i8mf4, vbool32_t, 32, vmsgt, vv, vint8mf4_t)
_RVV_CMP(vint8mf4_t, i8mf4, vbool32_t, 32, vmsge, vv, vint8mf4_t)
_RVV_CMP(vint8mf4_t, i8mf4, vbool32_t, 32, vmsge, vx, signed char)
_RVV_UNARY(vuint8mf2_t, u8mf2, vbool16_t, vnot, v, vuint8mf2_t)
_RVV_CMP(vuint8mf2_t, u8mf2, vbool16_t, 16, vmsgtu, vv, vuint8mf2_t)
_RVV_CMP(vuint8mf2_t, u8mf2, vbool16_t, 16, vmsgeu, vv, vuint8mf2_t)
_RVV_CMP(vuint8mf2_t, u8mf2, vbool16_t, 16, vmsgeu, vx, unsigned char)
_RVV_UNARY(vint8mf2_t, i8mf2, vbool16_t, vnot, v, vint8mf2_t)
_RVV_UNARY(vint8mf2_t, i8mf2, vbool16_t, vneg, v, vint8mf2_t)
_RVV_CMP(vint8mf2_t, i8mf2, vbool16_t, 16, vmsgt, vv, vint8mf2_t)
_RVV_CMP(vint8mf2_t, i8mf2, vbool16_t, 16, vmsge, vv, vint8mf2_t)
_RVV_CMP(vint8mf2_t, i8mf2, vbool16_t, 16, vmsge, vx, signed char)
_RVV_UNARY(vuint8m1_t, u8m1, vbool8_t, vnot, v, vuint8m1_t)
_RVV_CMP(vuint8m1_t, u8m1, vbool8_t, 8, vmsgtu, vv, vuint8m1_t)
_RVV_CMP(vuint8m1_t, u8m1, vbool8_t, 8, vmsgeu, vv, vuint8m1_t)
_RVV_CMP(vuint8m1_t, u8m1, vbool8_t, 8, vmsgeu, vx, unsigned char)
_RVV_UNARY(vint8m1_t, i8m1, vbool8_t, vnot, v, vint8m1_t)
_RVV_UNARY(vint8m1_t, i8m1, vbool8_t, vneg, v, vint8m1_t)
_RVV_CMP(vint8m1_t, i8m1, vbool8_t, 8, vmsgt, vv, vint8m1_t)
_RVV_CMP(vint8m1_t, i8m1, vbool8_t, 8, vmsge, vv, vint8m1_t)
_RVV_CMP(vint8m1_t, i8m1, vbool8_t, 8, vmsge, vx, signed char)
_RVV_UNARY(vuint8m2_t, u8m2, vbool4_t, vnot, v, vuint8m2_t)
_RVV_CMP(vuint8m2_t, u8m2, vbool4_t, 4, vmsgtu, vv, vuint8m2_t)
_RVV_CMP(vuint8m2_t, u8m2, vbool4_t, 4, vmsgeu, vv, vuint8m2_t)
_RVV_CMP(vuint8m2_t, u8m2, vbool4_t, 4, vmsgeu, vx, unsigned char)
_RVV_UNARY(vint8m2_t, i8m2, vbool4_t, vnot, v, vint8m2_t)
_RVV_UNARY(vint8m2_t, i8m2, vbool4_t, vneg, v, vint8m2_t)
_RVV_CMP(vint8m2_t, i8m2, vbool4_t, 4, vmsgt, vv, vint8m2_t)
_RVV_CMP(vint8m2_t, i8m2, vbool4_t, 4, vmsge, vv, vint8m2_t)
_RVV_CMP(vint8m2_t, i8m2, vbool4_t, 4, vmsge, vx, signed char)
_RVV_UNARY(vuint8m4_t, u8m4, vbool2_t, vnot, v, vuint8m4_t)
_RVV_CMP(vuint8m4_t, u8m4, vbool2_t, 2, vmsgtu, vv, vuint8m4_t)
_RVV_CMP(vuint8m4_t, u8m4, vbool2_t, 2, vmsgeu, vv, vuint8m4_t)
_RVV_CMP(vuint8m4_t, u8m4, vbool2_t, 2, vmsgeu, vx, unsigned char)
_RVV_UNARY(vint8m4_t, i8m4, vbool2_t, vnot, v, vint8m4_t)
_RVV_UNARY(vint8m4_t, i8m4, vbool2_t, vneg, v, vint8m4_t)
_RVV_CMP(vint8m4_t, i8m4, vbool2_t, 2, vmsgt, vv, vint8m4_t)
_RVV_CMP(vint8m4_t, i8m4, vbool2_t, 2, vmsge, vv, vint8m4_t)
_RVV_CMP(vint8m4_t, i8m4, vbool2_t, 2, vmsge, vx, signed char)
_RVV_UNARY(vuint8m8_t, u8m8, vbool1_t, vnot, v, vuint8m8_t)
_RVV_CMP(vuint8m8_t, u8m8, vbool1_t, 1, vmsgtu, vv, vuint8m8_t)
_RVV_CMP(vuint8m8_t, u8m8, vbool1_t, 1, vmsgeu, vv, vuint8m8_t)
_RVV_CMP(vuint8m8_t, u8m8, vbool1_t, 1, vmsgeu, vx, unsigned char)
_RVV_UNARY(vint8m8_t, i8m8, vbool1_t, vnot, v, vint8m8_t)
_RVV_UNARY(vint8m8_t, i8m8, vbool1_t, vneg, v, vint8m8_t)
_RVV_CMP(vint8m8_t, i8m8, vbool1_t, 1, vmsgt, vv, vint8m8_t)
_RVV_CMP(vint8m8_t, i8m8, vbool1_t, 1, vmsge, vv, vint8m8_t)
_RVV_CMP(vint8m8_t, i8m8, vbool1_t, 1, vmsge, vx, signed char)
_RVV_UNARY(vuint16mf4_t, u16mf4, vbool64_t, vnot, v, vuint16mf4_t)
_RVV_CMP(vuint16mf4_t, u16mf4, vbool64_t, 64, vmsgtu, vv, vuint16mf4_t)
_RVV_CMP(vuint16mf4_t, u16mf4, vbool64_t, 64, vmsgeu, vv, vuint16mf4_t)
_RVV_CMP(vuint16mf4_t, u16mf4, vbool64_t, 64, vmsgeu, vx, unsigned short)
_RVV_UNARY(vint16mf4_t, i16mf4, vbool64_t, vnot, v, vint16mf4_t)
_RVV_UNARY(vint16mf4_t, i16mf4, vbool64_t, vneg, v, vint16mf4_t)
_RVV_CMP(vint16mf4_t, i16mf4, vbool64_t, 64, vmsgt, vv, vint16mf4_t)
_RVV_CMP(vint16mf4_t, i16mf4, vbool64_t, 64, vmsge, vv, vint16mf4_t)
_RVV_CMP(vint16mf4_t, i16mf4, vbool64_t, 64, vmsge, vx, short)
_RVV_UNARY(vuint16mf2_t, u16mf2, vbool32_t, vnot, v, vuint16mf2_t)
_RVV_CMP(vuint16mf2_t, u16mf2, vbool32_t, 32, vmsgtu, vv, vuint16mf2_t)
_RVV_CMP(vuint16mf2_t, u16mf2, vbool32_t, 32, vmsgeu, vv, vuint16mf2_t)
_RVV_CMP(vuint16mf2_t, u16mf2, vbool32_t, 32, vmsgeu, vx, unsigned short)
_RVV_UNARY(vint16mf2_t, i16mf2, vbool32_t, vnot, v, vint16mf2_t)
_RVV_UNARY(vint16mf2_t, i16mf2, vbool32_t, vneg, v, vint16mf2_t)
_RVV_CMP(vint16mf2_t, i16mf2, vbool32_t, 32, vmsgt, vv, vint16mf2_t)
_RVV_CMP(vint16mf2_t, i16mf2, vbool32_t, 32, vmsge, vv, vint16mf2_t)
_RVV_CMP(vint16mf2_t, i16mf2, vbool32_t, 32, vmsge, vx, short)
_RVV_UNARY(vuint16m1_t, u16m1, vbool16_t, vnot, v, vuint16m1_t)
_RVV_CMP(vuint16m1_t, u16m1, vbool16_t, 16, vmsgtu, vv, vuint16m1_t)
_RVV_CMP(vuint16m1_t, u16m1, vbool16_t, 16, vmsgeu, vv, vuint16m1_t)
_RVV_CMP(vuint16m1_t, u16m1, vbool16_t, 16, vmsgeu, vx, unsigned short)
_RVV_UNARY(vint16m1_t, i16m1, vbool16_t, vnot, v, vint16m1_t)
_RVV_UNARY(vint16m1_t, i16m1, vbool16_t, vneg, v, vint16m1_t)
_RVV_CMP(vint16m1_t, i16m1, vbool16_t, 16, vmsgt, vv, vint16m1_t)
_RVV_CMP(vint16m1_t, i16m1, vbool16_t, 16, vmsge, vv, vint16m1_t)
_RVV_CMP(vint16m1_t, i16m1, vbool16_t, 16, vmsge, vx, short)
_RVV_UNARY(vuint16m2_t, u16m2, vbool8_t, vnot, v, vuint16m2_t)
_RVV_CMP(vuint16m2_t, u16m2, vbool8_t, 8, vmsgtu, vv, vuint16m2_t)
_RVV_CMP(vuint16m2_t, u16m2, vbool8_t, 8, vmsgeu, vv, vuint16m2_t)
_RVV_CMP(vuint16m2_t, u16m2, vbool8_t, 8, vmsgeu, vx, unsigned short)
_RVV_UNARY(vint16m2_t, i16m2, vbool8_t, vnot, v, vint16m2_t)
_RVV_UNARY(vint16m2_t, i16m2, vbool8_t, vneg, v, vint16m2_t)
_RVV_CMP(vint16m2_t, i16m2, vbool8_t, 8, vmsgt, vv, vint16m2_t)
_RVV_CMP(vint16m2_t, i16m2, vbool8_t, 8, vmsge, vv, vint16m2_t)
_RVV_CMP(vint16m2_t, i16m2, vbool8_t, 8, vmsge, vx, short)
_RVV_UNARY(vuint16m4_t, u16m4, vbool4_t, vnot, v, vuint16m4_t)
_RVV_CMP(vuint16m4_t, u16m4, vbool4_t, 4, vmsgtu, vv, vuint16m4_t)
_RVV_CMP(vuint16m4_t, u16m4, vbool4_t, 4, vmsgeu, vv, vuint16m4_t)
_RVV_CMP(vuint16m4_t, u16m4, vbool4_t, 4, vmsgeu, vx, unsigned short)
_RVV_UNARY(vint16m4_t, i16m4, vbool4_t, vnot, v, vint16m4_t)
_RVV_UNARY(vint16m4_t, i16m4, vbool4_t, vneg, v, vint16m4_t)
_RVV_CMP(vint16m4_t, i16m4, vbool4_t, 4, vmsgt, vv, vint16m4_t)
_RVV_CMP(vint16m4_t, i16m4, vbool4_t, 4, vmsge, vv, vint16m4_t)
_RVV_CMP(vint16m4_t, i16m4, vbool4_t, 4, vmsge, vx, short)
_RVV_UNARY(vuint16m8_t, u16m8, vbool2_t, vnot, v, vuint16m8_t)
_RVV_CMP(vuint16m8_t, u16m8, vbool2_t, 2, vmsgtu, vv, vuint16m8_t)
_RVV_CMP(vuint16m8_t, u16m8, vbool2_t, 2, vmsgeu, vv, vuint16m8_t)
_RVV_CMP(vuint16m8_t, u16m8, vbool2_t, 2, vmsgeu, vx, unsigned short)
_RVV_UNARY(vint16m8_t, i16m8, vbool2_t, vnot, v, vint16m8_t)
_RVV_UNARY(vint16m8_t, i16m8, vbool2_t, vneg, v, vint16m8_t)
_RVV_CMP(vint16m8_t, i16m8, vbool2_t, 2, vmsgt, vv, vint16m8_t)
_RVV_CMP(vint16m8_t, i16m8, vbool2_t, 2, vmsge, vv, vint16m8_t)
_RVV_CMP(vint16m8_t, i16m8, vbool2_t, 2, vmsge, vx, short)
_RVV_UNARY(vuint32mf2_t, u32mf2, vbool64_t, vnot, v, vuint32mf2_t)
_RVV_CMP(vuint32mf2_t, u32mf2, vbool64_t, 64, vmsgtu, vv, vuint32mf2_t)
_RVV_CMP(vuint32mf2_t, u32mf2, vbool64_t, 64, vmsgeu, vv, vuint32mf2_t)
_RVV_CMP(vuint32mf2_t, u32mf2, vbool64_t, 64, vmsgeu, vx, unsigned int)
_RVV_UNARY(vint32mf2_t, i32mf2, vbool64_t, vnot, v, vint32mf2_t)
_RVV_UNARY(vint32mf2_t, i32mf2, vbool64_t, vneg, v, vint32mf2_t)
_RVV_CMP(vint32mf2_t, i32mf2, vbool64_t, 64, vmsgt, vv, vint32mf2_t)
_RVV_CMP(vint32mf2_t, i32mf2, vbool64_t, 64, vmsge, vv, vint32mf2_t)
_RVV_CMP(vint32mf2_t, i32mf2, vbool64_t, 64, vmsge, vx, int)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfneg, v, vfloat32mf2_t)
_RVV_UNARY(vfloat32mf2_t, f32mf2, vbool64_t, vfabs, v, vfloat32mf2_t)
_RVV_CMP(vfloat32mf2_t, f32mf2, vbool64_t, 64, vmfgt, vv, vfloat32mf2_t)
_RVV_CMP(vfloat32mf2_t, f32mf2, vbool64_t, 64, vmfge, vv, vfloat32mf2_t)
_RVV_UNARY(vuint32m1_t, u32m1, vbool32_t, vnot, v, vuint32m1_t)
_RVV_CMP(vuint32m1_t, u32m1, vbool32_t, 32, vmsgtu, vv, vuint32m1_t)
_RVV_CMP(vuint32m1_t, u32m1, vbool32_t, 32, vmsgeu, vv, vuint32m1_t)
_RVV_CMP(vuint32m1_t, u32m1, vbool32_t, 32, vmsgeu, vx, unsigned int)
_RVV_UNARY(vint32m1_t, i32m1, vbool32_t, vnot, v, vint32m1_t)
_RVV_UNARY(vint32m1_t, i32m1, vbool32_t, vneg, v, vint32m1_t)
_RVV_CMP(vint32m1_t, i32m1, vbool32_t, 32, vmsgt, vv, vint32m1_t)
_RVV_CMP(vint32m1_t, i32m1, vbool32_t, 32, vmsge, vv, vint32m1_t)
_RVV_CMP(vint32m1_t, i32m1, vbool32_t, 32, vmsge, vx, int)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfneg, v, vfloat32m1_t)
_RVV_UNARY(vfloat32m1_t, f32m1, vbool32_t, vfabs, v, vfloat32m1_t)
_RVV_CMP(vfloat32m1_t, f32m1, vbool32_t, 32, vmfgt, vv, vfloat32m1_t)
_RVV_CMP(vfloat32m1_t, f32m1, vbool32_t, 32, vmfge, vv, vfloat32m1_t)
_RVV_UNARY(vuint32m2_t, u32m2, vbool16_t, vnot, v, vuint32m2_t)
_RVV_CMP(vuint32m2_t, u32m2, vbool16_t, 16, vmsgtu, vv, vuint32m2_t)
_RVV_CMP(vuint32m2_t, u32m2, vbool16_t, 16, vmsgeu, vv, vuint32m2_t)
_RVV_CMP(vuint32m2_t, u32m2, vbool16_t, 16, vmsgeu, vx, unsigned int)
_RVV_UNARY(vint32m2_t, i32m2, vbool16_t, vnot, v, vint32m2_t)
_RVV_UNARY(vint32m2_t, i32m2, vbool16_t, vneg, v, vint32m2_t)
_RVV_CMP(vint32m2_t, i32m2, vbool16_t, 16, vmsgt, vv, vint32m2_t)
_RVV_CMP(vint32m2_t, i32m2, vbool16_t, 16, vmsge, vv, vint32m2_t)
_RVV_CMP(vint32m2_t, i32m2, vbool16_t, 16, vmsge, vx, int)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfneg, v, vfloat32m2_t)
_RVV_UNARY(vfloat32m2_t, f32m2, vbool16_t, vfabs, v, vfloat32m2_t)
_RVV_CMP(vfloat32m2_t, f32m2, vbool16_t, 16, vmfgt, vv, vfloat32m2_t)
_RVV_CMP(vfloat32m2_t, f32m2, vbool16_t, 16, vmfge, vv, vfloat32m2_t)
_RVV_UNARY(vuint32m4_t, u32m4, vbool8_t, vnot, v, vuint32m4_t)
_RVV_CMP(vuint32m4_t, u32m4, vbool8_t, 8, vmsgtu, vv, vuint32m4_t)
_RVV_CMP(vuint32m4_t, u32m4, vbool8_t, 8, vmsgeu, vv, vuint32m4_t)
_RVV_CMP(vuint32m4_t, u32m4, vbool8_t, 8, vmsgeu, vx, unsigned int)
_RVV_UNARY(vint32m4_t, i32m4, vbool8_t, vnot, v, vint32m4_t)
_RVV_UNARY(vint32m4_t, i32m4, vbool8_t, vneg, v, vint32m4_t)
_RVV_CMP(vint32m4_t, i32m4, vbool8_t, 8, vmsgt, vv, vint32m4_t)
_RVV_CMP(vint32m4_t, i32m4, vbool8_t, 8, vmsge, vv, vint32m4_t)
_RVV_CMP(vint32m4_t, i32m4, vbool8_t, 8, vmsge, vx, int)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfneg, v, vfloat32m4_t)
_RVV_UNARY(vfloat32m4_t, f32m4, vbool8_t, vfabs, v, vfloat32m4_t)
_RVV_CMP(vfloat32m4_t, f32m4, vbool8_t, 8, vmfgt, vv, vfloat32m4_t)
_RVV_CMP(vfloat32m4_t, f32m4, vbool8_t, 8, vmfge, vv, vfloat32m4_t)
_RVV_UNARY(vuint32m8_t, u32m8, vbool4_t, vnot, v, vuint32m8_t)
_RVV_CMP(vuint32m8_t, u32m8, vbool4_t, 4, vmsgtu, vv, vuint32m8_t)
_RVV_CMP(vuint32m8_t, u32m8, vbool4_t, 4, vmsgeu, vv, vuint32m8_t)
_RVV_CMP(vuint32m8_t, u32m8, vbool4_t, 4, vmsgeu, vx, unsigned int)
_RVV_UNARY(vint32m8_t, i32m8, vbool4_t, vnot, v, vint32m8_t)
_RVV_UNARY(vint32m8_t, i32m8, vbool4_t, vneg, v, vint32m8_t)
_RVV_CMP(vint32m8_t, i32m8, vbool4_t, 4, vmsgt, vv, vint32m8_t)
_RVV_CMP(vint32m8_t, i32m8, vbool4_t, 4, vmsge, vv, vint32m8_t)
_RVV_CMP(vint32m8_t, i32m8, vbool4_t, 4, vmsge, vx, int)
_RVV_UNARY(vfloat32m8_t, f32m8, vbool4_t, vfneg, v, vfloat32m8_t)
_RVV_UNARY(vfloat32m8_t, f32m8, vbool4_t, vfabs, v, vfloat32m8_t)
_RVV_CMP(vfloat32m8_t, f32m8, vbool4_t, 4, vmfgt, vv, vfloat32m8_t)
_RVV_CMP(vfloat32m8_t, f32m8, vbool4_t, 4, vmfge, vv, vfloat32m8_t)
_RVV_UNARY(vuint64m1_t, u64m1, vbool64_t, vnot, v, vuint64m1_t)
_RVV_CMP(vuint64m1_t, u64m1, vbool64_t, 64, vmsgtu, vv, vuint64m1_t)
_RVV_CMP(vuint64m1_t, u64m1, vbool64_t, 64, vmsgeu, vv, vuint64m1_t)
_RVV_CMP(vuint64m1_t, u64m1, vbool64_t, 64, vmsgeu, vx, uint64_t)
_RVV_UNARY(vint64m1_t, i64m1, vbool64_t, vnot, v, vint64m1_t)
_RVV_UNARY(vint64m1_t, i64m1, vbool64_t, vneg, v, vint64m1_t)
_RVV_CMP(vint64m1_t, i64m1, vbool64_t, 64, vmsgt, vv, vint64m1_t)
_RVV_CMP(vint64m1_t, i64m1, vbool64_t, 64, vmsge, vv, vint64m1_t)
_RVV_CMP(vint64m1_t, i64m1, vbool64_t, 64, vmsge, vx, int64_t)
_RVV_UNARY(vfloat64m1_t, f64m1, vbool64_t, vfneg, v, vfloat64m1_t)
_RVV_UNARY(vfloat64m1_t, f64m1, vbool64_t, vfabs, v, vfloat64m1_t)
_RVV_CMP(vfloat64m1_t, f64m1, vbool64_t, 64, vmfgt, vv, vfloat64m1_t)
_RVV_CMP(vfloat64m1_t, f64m1, vbool64_t, 64, vmfge, vv, vfloat64m1_t)
_RVV_UNARY(vuint64m2_t, u64m2, vbool32_t, vnot, v, vuint64m2_t)
_RVV_CMP(vuint64m2_t, u64m2, vbool32_t, 32, vmsgtu, vv, vuint64m2_t)
_RVV_CMP(vuint64m2_t, u64m2, vbool32_t, 32, vmsgeu, vv, vuint64m2_t)
_RVV_CMP(vuint64m2_t, u64m2, vbool32_t, 32, vmsgeu, vx, uint64_t)
_RVV_UNARY(vint64m2_t, i64m2, vbool32_t, vnot, v, vint64m2_t)
_RVV_UNARY(vint64m2_t, i64m2, vbool32_t, vneg, v, vint64m2_t)
_RVV_CMP(vint64m2_t, i64m2, vbool32_t, 32, vmsgt, vv, vint64m2_t)
_RVV_CMP(vint64m2_t, i64m2, vbool32_t, 32, vmsge, vv, vint64m2_t)
_RVV_CMP(vint64m2_t, i64m2, vbool32_t, 32, vmsge, vx, int64_t)
_RVV_UNARY(vfloat64m2_t, f64m2, vbool32_t, vfneg, v, vfloat64m2_t)
_RVV_UNARY(vfloat64m2_t, f64m2, vbool32_t, vfabs, v, vfloat64m2_t)
_RVV_CMP(vfloat64m2_t, f64m2, vbool32_t, 32, vmfgt, vv, vfloat64m2_t)
_RVV_CMP(vfloat64m2_t, f64m2, vbool32_t, 32, vmfge, vv, vfloat64m2_t)
_RVV_UNARY(vuint64m4_t, u64m4, vbool16_t, vnot, v, vuint64m4_t)
_RVV_CMP(vuint64m4_t, u64m4, vbool16_t, 16, vmsgtu, vv, vuint64m4_t)
_RVV_CMP(vuint64m4_t, u64m4, vbool16_t, 16, vmsgeu, vv, vuint64m4_t)
_RVV_CMP(vuint64m4_t, u64m4, vbool16_t, 16, vmsgeu, vx, uint64_t)
_RVV_UNARY(vint64m4_t, i64m4, vbool16_t, vnot, v, vint64m4_t)
_RVV_UNARY(vint64m4_t, i64m4, vbool16_t, vneg, v, vint64m4_t)
_RVV_CMP(vint64m4_t, i64m4, vbool16_t, 16, vmsgt, vv, vint64m4_t)
_RVV_CMP(vint64m4_t, i64m4, vbool16_t, 16, vmsge, vv, vint64m4_t)
_RVV_CMP(vint64m4_t, i64m4, vbool16_t, 16, vmsge, vx, int64_t)
_RVV_UNARY(vfloat64m4_t, f64m4, vbool16_t, vfneg, v, vfloat64m4_t)
_RVV_UNARY(vfloat64m4_t, f64m4, vbool16_t, vfabs, v, vfloat64m4_t)
_RVV_CMP(vfloat64m4_t, f64m4, vbool16_t, 16, vmfgt, vv, vfloat64m4_t)
_RVV_CMP(vfloat64m4_t, f64m4, vbool16_t, 16, vmfge, vv, vfloat64m4_t)
_RVV_UNARY(vuint64m8_t, u64m8, vbool8_t, vnot, v, vuint64m8_t)
_RVV_CMP(vuint64m8_t, u64m8, vbool8_t, 8, vmsgtu, vv, vuint64m8_t)
_RVV_CMP(vuint64m8_t, u64m8, vbool8_t, 8, vmsgeu, vv, vuint64m8_t)
_RVV_CMP(vuint64m8_t, u64m8, vbool8_t, 8, vmsgeu, vx, uint64_t)
_RVV_UNARY(vint64m8_t, i64m8, vbool8_t, vnot, v, vint64m8_t)
_RVV_UNARY(vint64m8_t, i64m8, vbool8_t, vneg, v, vint64m8_t)
_RVV_CMP(vint64m8_t, i64m8, vbool8_t, 8, vmsgt, vv, vint64m8_t)
_RVV_CMP(vint64m8_t, i64m8, vbool8_t, 8, vmsge, vv, vint64m8_t)
_RVV_CMP(vint64m8_t, i64m8, vbool8_t, 8, vmsge, vx, int64_t)
_RVV_UNARY(vfloat64m8_t, f64m8, vbool8_t, vfneg, v, vfloat64m8_t)
_RVV_UNARY(vfloat64m8_t, f64m8, vbool8_t, vfabs, v, vfloat64m8_t)
_RVV_CMP(vfloat64m8_t, f64m8, vbool8_t, 8, vmfgt, vv, vfloat64m8_t)
_RVV_CMP(vfloat64m8_t, f64m8, vbool8_t, 8, vmfge, vv, vfloat64m8_t)
_RVV_UNARY(vint16mf4_t, i16mf4, vbool64_t, vwcvt_x_x, v, vint8mf8_t)
_RVV_UNARY(vuint16mf4_t, u16mf4, vbool64_t, vwcvtu_x_x, v, vuint8mf8_t)
_RVV_UNARY(vuint8mf8_t, u8mf8, vbool64_t, vncvt_x_x, w, vuint16mf4_t)
_RVV_UNARY(vint8mf8_t, i8mf8, vbool64_t, vncvt_x_x, w, vint16mf4_t)
_RVV_UNARY(vint16mf2_t, i16mf2, vbool32_t, vwcvt_x_x, v, vint8mf4_t)
_RVV_UNARY(vuint16mf2_t, u16mf2, vbool32_t, vwcvtu_x_x, v, vuint8mf4_t)
_RVV_UNARY(vuint8mf4_t, u8mf4, vbool32_t, vncvt_x_x, w, vuint16mf2_t)
_RVV_UNARY(vint8mf4_t, i8mf4, vbool32_t, vncvt_x_x, w, vint16mf2_t)
_RVV_UNARY(vint16m1_t, i16m1, vbool16_t, vwcvt_x_x, v, vint8mf2_t)
_RVV_UNARY(vuint16m1_t, u16m1, vbool16_t, vwcvtu_x_x, v, vuint8mf2_t)
_RVV_UNARY(vuint8mf2_t, u8mf2, vbool16_t, vncvt_x_x, w, vuint16m1_t)
_RVV_UNARY(vint8mf2_t, i8mf2, vbool16_t, vncvt_x_x, w, vint16m1_t)
_RVV_UNARY(vint16m2_t, i16m2, vbool8_t, vwcvt_x_x, v, vint8m1_t)
_RVV_UNARY(vuint16m2_t, u16m2, vbool8_t, vwcvtu_x_x, v, vuint8m1_t)
_RVV_UNARY(vuint8m1_t, u8m1, vbool8_t, vncvt_x_x, w, vuint16m2_t)
_RVV_UNARY(vint8m1_t, i8m1, vbool8_t, vncvt_x_x, w, vint16m2_t)
_RVV_UNARY(vint16m4_t, i16m4, vbool4_t, vwcvt_x_x, v, vint8m2_t)
_RVV_UNARY(vuint16m4_t, u16m4, vbool4_t, vwcvtu_x_x, v, vuint8m2_t)
_RVV_UNARY(vuint8m2_t, u8m2, vbool4_t, vncvt_x_x, w, vuint16m4_t)
_RVV_UNARY(vint8m2_t, i8m2, vbool4_t, vncvt_x_x, w, vint16m4_t)
_RVV_UNARY(vint16m8_t, i16m8, vbool2_t, vwcvt_x_x, v, vint8m4_t)
_RVV_UNARY(vuint16m8_t, u16m8, vbool2_t, vwcvtu_x_x, v, vuint8m4_t)
_RVV_UNARY(vuint8m4_t, u8m4, vbool2_t, vncvt_x_x, w, vuint16m8_t)
_RVV_UNARY(vint8m4_t, i8m4, vbool2_t, vncvt_x_x, w, vint16m8_t)
_RVV_UNARY(vint32mf2_t, i32mf2, vbool64_t, vwcvt_x_x, v, vint16mf4_t)
_RVV_UNARY(vuint32mf2_t, u32mf2, vbool64_t, vwcvtu_x_x, v, vuint16mf4_t)
_RVV_UNARY(vuint16mf4_t, u16mf4, vbool64_t, vncvt_x_x, w, vuint32mf2_t)
_RVV_UNARY(vint16mf4_t, i16mf4, vbool64_t, vncvt_x_x, w, vint32mf2_t)
_RVV_UNARY(vint32m1_t, i32m1, vbool32_t, vwcvt_x_x, v, vint16mf2_t)
_RVV_UNARY(vuint32m1_t, u32m1, vbool32_t, vwcvtu_x_x, v, vuint16mf2_t)
_RVV_UNARY(vuint16mf2_t, u16mf2, vbool32_t, vncvt_x_x, w, vuint32m1_t)
_RVV_UNARY(vint16mf2_t, i16mf2, vbool32_t, vncvt_x_x, w, vint32m1_t)
_RVV_UNARY(vint32m2_t, i32m2, vbool16_t, vwcvt_x_x, v, vint16m1_t)
_RVV_UNARY(vuint32m2_t, u32m2, vbool16_t, vwcvtu_x_x, v, vuint16m1_t)
_RVV_UNARY(vuint16m1_t, u16m1, vbool16_t, vncvt_x_x, w, vuint32m2_t)
_RVV_UNARY(vint16m1_t, i16m1, vbool16_t, vncvt_x_x, w, vint32m2_t)
_RVV_UNARY(vint32m4_t, i32m4, vbool8_t, vwcvt_x_x, v, vint16m2_t)
_RVV_UNARY(vuint32m4_t, u32m4, vbool8_t, vwcvtu_x_x, v, vuint16m2_t)
_RVV_UNARY(vuint16m2_t, u16m2, vbool8_t, vncvt_x_x, w, vuint32m4_t)
_RVV_UNARY(vint16m2_t, i16m2, vbool8_t, vncvt_x_x, w, vint32m4_t)
_RVV_UNARY(vint32m8_t, i32m8, vbool4_t, vwcvt_x_x, v, vint16m4_t)
_RVV_UNARY(vuint32m8_t, u32m8, vbool4_t, vwcvtu_x_x, v, vuint16m4_t)
_RVV_UNARY(vuint16m4_t, u16m4, vbool4_t, vncvt_x_x, w, vuint32m8_t)
_RVV_UNARY(vint16m4_t, i16m4, vbool4_t, vncvt_x_x, w, vint32m8_t)
_RVV_UNARY(vint64m1_t, i64m1, vbool64_t, vwcvt_x_x, v, vint32mf2_t)
_RVV_UNARY(vuint64m1_t, u64m1, vbool64_t, vwcvtu_x_x, v, vuint32mf2_t)
_RVV_UNARY(vuint32mf2_t, u32mf2, vbool64_t, vncvt_x_x, w, vuint64m1_t)
_RVV_UNARY(vint32mf2_t, i32mf2, vbool64_t, vncvt_x_x, w, vint64m1_t)
_RVV_UNARY(vint64m2_t, i64m2, vbool32_t, vwcvt_x_x, v, vint32m1_t)
_RVV_UNARY(vuint64m2_t, u64m2, vbool32_t, vwcvtu_x_x, v, vuint32m1_t)
_RVV_UNARY(vuint32m1_t, u32m1, vbool32_t, vncvt_x_x, w, vuint64m2_t)
_RVV_UNARY(vint32m1_t, i32m1, vbool32_t, vncvt_x_x, w, vint64m2_t)
_RVV_UNARY(vint64m4_t, i64m4, vbool16_t, vwcvt_x_x, v, vint32m2_t)
_RVV_UNARY(vuint64m4_t, u64m4, vbool16_t, vwcvtu_x_x, v, vuint32m2_t)
_RVV_UNARY(vuint32m2_t, u32m2, vbool16_t, vncvt_x_x, w, vuint64m4_t)
_RVV_UNARY(vint32m2_t, i32m2, vbool16_t, vncvt_x_x, w, vint64m4_t)
_RVV_UNARY(vint64m8_t, i64m8, vbool8_t, vwcvt_x_x, v, vint32m4_t)
_RVV_UNARY(vuint64m8_t, u64m8, vbool8_t, vwcvtu_x_x, v, vuint32m4_t)
_RVV_UNARY(vuint32m4_t, u32m4, vbool8_t, vncvt_x_x, w, vuint64m8_t)
_RVV_UNARY(vint32m4_t, i32m4, vbool8_t, vncvt_x_x, w, vint64m8_t)
#endif

#endif
