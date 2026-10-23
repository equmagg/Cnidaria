#[lang = "not"]
pub trait Not {
    type Output;
    fn not(self) -> Self::Output;
}

#[lang = "bitand"]
pub trait BitAnd<Rhs = Self> {
    type Output;
    fn bitand(self, rhs: Rhs) -> Self::Output;
}

#[lang = "bitor"]
pub trait BitOr<Rhs = Self> {
    type Output;
    fn bitor(self, rhs: Rhs) -> Self::Output;
}

#[lang = "bitxor"]
pub trait BitXor<Rhs = Self> {
    type Output;
    fn bitxor(self, rhs: Rhs) -> Self::Output;
}

#[lang = "shl"]
pub trait Shl<Rhs = Self> {
    type Output;
    fn shl(self, rhs: Rhs) -> Self::Output;
}

#[lang = "shr"]
pub trait Shr<Rhs = Self> {
    type Output;
    fn shr(self, rhs: Rhs) -> Self::Output;
}

#[lang = "bitand_assign"]
pub trait BitAndAssign<Rhs = Self> {
    fn bitand_assign(&mut self, rhs: Rhs);
}

#[lang = "bitor_assign"]
pub trait BitOrAssign<Rhs = Self> {
    fn bitor_assign(&mut self, rhs: Rhs);
}

#[lang = "bitxor_assign"]
pub trait BitXorAssign<Rhs = Self> {
    fn bitxor_assign(&mut self, rhs: Rhs);
}

#[lang = "shl_assign"]
pub trait ShlAssign<Rhs = Self> {
    fn shl_assign(&mut self, rhs: Rhs);
}

#[lang = "shr_assign"]
pub trait ShrAssign<Rhs = Self> {
    fn shr_assign(&mut self, rhs: Rhs);
}

macro_rules! not_impls {
    ($($t:ty)*) => {
        $(
            impl Not for $t {
                type Output = $t;
                fn not(self) -> $t {
                    !self
                }
            }
        )*
    };
}

macro_rules! binary_impls {
    ($trait:ident, $method:ident, $op:tt, $($t:ty)*) => {
        $(
            impl $trait for $t {
                type Output = $t;
                fn $method(self, other: $t) -> $t {
                    self $op other
                }
            }
        )*
    };
}

macro_rules! assign_impls {
    ($trait:ident, $method:ident, $op:tt, $($t:ty)*) => {
        $(
            impl $trait for $t {
                fn $method(&mut self, other: $t) {
                    *self $op other;
                }
            }
        )*
    };
}

// Shifts accept any integer amount, so every left-hand integer type is paired with every right-hand one
macro_rules! shift_impls {
    ($t:ty, $($f:ty)*) => {
        $(
            impl Shl<$f> for $t {
                type Output = $t;
                fn shl(self, other: $f) -> $t {
                    self << other
                }
            }

            impl Shr<$f> for $t {
                type Output = $t;
                fn shr(self, other: $f) -> $t {
                    self >> other
                }
            }

            impl ShlAssign<$f> for $t {
                fn shl_assign(&mut self, other: $f) {
                    *self <<= other;
                }
            }

            impl ShrAssign<$f> for $t {
                fn shr_assign(&mut self, other: $f) {
                    *self >>= other;
                }
            }
        )*
    };
}

macro_rules! shift_impls_all {
    ($($t:ty)*) => {
        $(
            shift_impls! { $t, u8 u16 u32 u64 u128 usize i8 i16 i32 i64 i128 isize }
        )*
    };
}

not_impls! { bool usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
binary_impls! { BitAnd, bitand, &, bool usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
binary_impls! { BitOr, bitor, |, bool usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
binary_impls! { BitXor, bitxor, ^, bool usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
assign_impls! { BitAndAssign, bitand_assign, &=, bool usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
assign_impls! { BitOrAssign, bitor_assign, |=, bool usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
assign_impls! { BitXorAssign, bitxor_assign, ^=, bool usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
shift_impls_all! { usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
