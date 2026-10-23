#[lang = "sized"]
pub trait Sized {}

#[lang = "copy"]
pub trait Copy: Clone {}

macro_rules! copy_impls {
    ($($t:ty)*) => {
        $(
            impl Copy for $t {}
        )*
    };
}

copy_impls! { usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 bool char }

impl<T: ?Sized> Copy for &T {}
impl<T: ?Sized> Copy for *const T {}
impl<T: ?Sized> Copy for *mut T {}
