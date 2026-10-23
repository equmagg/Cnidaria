#[lang = "add"]
pub trait Add<Rhs = Self> {
    type Output;
    fn add(self, rhs: Rhs) -> Self::Output;
}

#[lang = "sub"]
pub trait Sub<Rhs = Self> {
    type Output;
    fn sub(self, rhs: Rhs) -> Self::Output;
}

#[lang = "mul"]
pub trait Mul<Rhs = Self> {
    type Output;
    fn mul(self, rhs: Rhs) -> Self::Output;
}

#[lang = "div"]
pub trait Div<Rhs = Self> {
    type Output;
    fn div(self, rhs: Rhs) -> Self::Output;
}

#[lang = "rem"]
pub trait Rem<Rhs = Self> {
    type Output;
    fn rem(self, rhs: Rhs) -> Self::Output;
}

#[lang = "neg"]
pub trait Neg {
    type Output;
    fn neg(self) -> Self::Output;
}

#[lang = "add_assign"]
pub trait AddAssign<Rhs = Self> {
    fn add_assign(&mut self, rhs: Rhs);
}

#[lang = "sub_assign"]
pub trait SubAssign<Rhs = Self> {
    fn sub_assign(&mut self, rhs: Rhs);
}

#[lang = "mul_assign"]
pub trait MulAssign<Rhs = Self> {
    fn mul_assign(&mut self, rhs: Rhs);
}

#[lang = "div_assign"]
pub trait DivAssign<Rhs = Self> {
    fn div_assign(&mut self, rhs: Rhs);
}

#[lang = "rem_assign"]
pub trait RemAssign<Rhs = Self> {
    fn rem_assign(&mut self, rhs: Rhs);
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

binary_impls! { Add, add, +, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
binary_impls! { Sub, sub, -, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
binary_impls! { Mul, mul, *, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
binary_impls! { Div, div, /, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
binary_impls! { Rem, rem, %, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
assign_impls! { AddAssign, add_assign, +=, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
assign_impls! { SubAssign, sub_assign, -=, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
assign_impls! { MulAssign, mul_assign, *=, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
assign_impls! { DivAssign, div_assign, /=, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
assign_impls! { RemAssign, rem_assign, %=, usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }

macro_rules! neg_impls {
    ($($t:ty)*) => {
        $(
            impl Neg for $t {
                type Output = $t;
                fn neg(self) -> $t {
                    -self
                }
            }
        )*
    };
}

neg_impls! { isize i8 i16 i32 i64 i128 f32 f64 }
