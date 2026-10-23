pub trait Default: Sized {
    fn default() -> Self;
}

macro_rules! default_impl {
    ($t:ty, $v:expr) => {
        impl Default for $t {
            fn default() -> $t {
                $v
            }
        }
    };
}

default_impl! { (), () }
default_impl! { bool, false }
default_impl! { char, '\x00' }
default_impl! { usize, 0 }
default_impl! { u8, 0 }
default_impl! { u16, 0 }
default_impl! { u32, 0 }
default_impl! { u64, 0 }
default_impl! { u128, 0 }
default_impl! { isize, 0 }
default_impl! { i8, 0 }
default_impl! { i16, 0 }
default_impl! { i32, 0 }
default_impl! { i64, 0 }
default_impl! { i128, 0 }
default_impl! { f32, 0.0 }
default_impl! { f64, 0.0 }
