#[lang = "clone"]
pub trait Clone: Sized {
    fn clone(&self) -> Self;

    fn clone_from(&mut self, source: &Self) {
        *self = source.clone()
    }
}

macro_rules! clone_impls {
    ($($t:ty)*) => {
        $(
            impl Clone for $t {
                fn clone(&self) -> Self {
                    *self
                }
            }
        )*
    };
}

clone_impls! { usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 bool char }

impl<T: ?Sized> Clone for &T {
    fn clone(&self) -> Self {
        *self
    }
}

impl<T: ?Sized> Clone for *const T {
    fn clone(&self) -> Self {
        *self
    }
}

impl<T: ?Sized> Clone for *mut T {
    fn clone(&self) -> Self {
        *self
    }
}
