pub trait From<T>: Sized {
    fn from(value: T) -> Self;
}

pub trait Into<T>: Sized {
    fn into(self) -> T;
}

impl<T, U> Into<U> for T
where
    U: From<T>,
{
    fn into(self) -> U {
        U::from(self)
    }
}

impl<T> From<T> for T {
    fn from(t: T) -> T {
        t
    }
}

// Lossless numeric conversions, as in rustc's impl_from!
macro_rules! from_impls {
    ($small:ty => $($large:ty)*) => {
        $(
            impl From<$small> for $large {
                fn from(small: $small) -> $large {
                    small as $large
                }
            }
        )*
    };
}

from_impls! { u8 => u16 u32 u64 u128 usize i16 i32 i64 i128 isize }
from_impls! { u16 => u32 u64 u128 usize i32 i64 i128 }
from_impls! { u32 => u64 u128 i64 i128 }
from_impls! { u64 => u128 i128 }
from_impls! { i8 => i16 i32 i64 i128 isize }
from_impls! { i16 => i32 i64 i128 isize }
from_impls! { i32 => i64 i128 }
from_impls! { i64 => i128 }
from_impls! { bool => u8 u16 u32 u64 u128 usize i8 i16 i32 i64 i128 isize }
from_impls! { char => u32 u64 u128 }
from_impls! { u8 => char }
from_impls! { i8 => f32 f64 }
from_impls! { u8 => f32 f64 }
from_impls! { i16 => f32 f64 }
from_impls! { u16 => f32 f64 }
from_impls! { i32 => f64 }
from_impls! { u32 => f64 }
from_impls! { f32 => f64 }
