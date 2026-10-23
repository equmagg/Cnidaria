#[macro_export]
#[doc(hidden)]
macro_rules! panic_2021 {
    () => (
        $crate::panicking::panic("explicit panic")
    );
    ($($t:tt)+) => ({
        $crate::panicking::panic_fmt($crate::const_format_args!($($t)+));
    });
}

#[macro_export]
#[doc(hidden)]
macro_rules! unreachable_2021 {
    () => (
        $crate::panicking::panic("internal error: entered unreachable code")
    );
    ($($t:tt)+) => (
        $crate::panic!("internal error: entered unreachable code: {}", $crate::format_args!($($t)+))
    );
}

pub use crate::panic_2021;
pub use crate::unreachable_2021;
