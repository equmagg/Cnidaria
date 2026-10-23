#[macro_export]
#[rustc_builtin_macro(core_panic)]
macro_rules! panic {
    ($($arg:tt)*) => {
        /* compiler built-in */
    };
}

#[macro_export]
macro_rules! assert_eq {
    ($left:expr, $right:expr $(,)?) => {
        match (&$left, &$right) {
            (left_val, right_val) => {
                if !(*left_val == *right_val) {
                    $crate::panicking::panic("assertion `left == right` failed");
                }
            }
        }
    };
    ($left:expr, $right:expr, $($arg:tt)+) => {
        match (&$left, &$right) {
            (left_val, right_val) => {
                if !(*left_val == *right_val) {
                    $crate::panic!("assertion `left == right` failed: {}", $crate::format_args!($($arg)+));
                }
            }
        }
    };
}

#[macro_export]
macro_rules! assert_ne {
    ($left:expr, $right:expr $(,)?) => {
        match (&$left, &$right) {
            (left_val, right_val) => {
                if *left_val == *right_val {
                    $crate::panicking::panic("assertion `left != right` failed");
                }
            }
        }
    };
    ($left:expr, $right:expr, $($arg:tt)+) => {
        match (&$left, &$right) {
            (left_val, right_val) => {
                if *left_val == *right_val {
                    $crate::panic!("assertion `left != right` failed: {}", $crate::format_args!($($arg)+));
                }
            }
        }
    };
}

#[macro_export]
macro_rules! debug_assert {
    ($($arg:tt)*) => {
        if $crate::cfg!(debug_assertions) {
            $crate::assert!($($arg)*);
        }
    };
}

#[macro_export]
macro_rules! debug_assert_eq {
    ($($arg:tt)*) => {
        if $crate::cfg!(debug_assertions) {
            $crate::assert_eq!($($arg)*);
        }
    };
}

#[macro_export]
macro_rules! debug_assert_ne {
    ($($arg:tt)*) => {
        if $crate::cfg!(debug_assertions) {
            $crate::assert_ne!($($arg)*);
        }
    };
}

#[macro_export]
macro_rules! matches {
    ($expression:expr, $pattern:pat $(if $guard:expr)? $(,)?) => {
        match $expression {
            $pattern $(if $guard)? => true,
            _ => false
        }
    };
}

#[macro_export]
#[rustc_builtin_macro(unreachable)]
macro_rules! unreachable {
    ($($arg:tt)*) => {
        /* compiler built-in */
    };
}

#[macro_export]
macro_rules! unimplemented {
    () => {
        $crate::panicking::panic("not implemented")
    };
    ($($arg:tt)+) => {
        $crate::panic!("not implemented: {}", $crate::format_args!($($arg)+))
    };
}

#[macro_export]
macro_rules! todo {
    () => {
        $crate::panicking::panic("not yet implemented")
    };
    ($($arg:tt)+) => {
        $crate::panic!("not yet implemented: {}", $crate::format_args!($($arg)+))
    };
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! compile_error {
    ($msg:expr $(,)?) => {{ /* compiler built-in */ }};
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! format_args {
    ($fmt:expr) => {{ /* compiler built-in */ }};
    ($fmt:expr, $($args:tt)*) => {{ /* compiler built-in */ }};
}

#[macro_export]
#[rustc_builtin_macro]
#[doc(hidden)]
macro_rules! const_format_args {
    ($fmt:expr) => {{ /* compiler built-in */ }};
    ($fmt:expr, $($args:tt)*) => {{ /* compiler built-in */ }};
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! concat {
    ($($e:expr),* $(,)?) => {{ /* compiler built-in */ }};
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! line {
    () => {
        /* compiler built-in */
    };
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! column {
    () => {
        /* compiler built-in */
    };
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! file {
    () => {
        /* compiler built-in */
    };
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! stringify {
    ($($t:tt)*) => {
        /* compiler built-in */
    };
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! module_path {
    () => {
        /* compiler built-in */
    };
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! cfg {
    ($($cfg:tt)*) => {
        /* compiler built-in */
    };
}

#[macro_export]
#[rustc_builtin_macro]
macro_rules! assert {
    ($cond:expr $(,)?) => {{ /* compiler built-in */ }};
    ($cond:expr, $($arg:tt)+) => {{ /* compiler built-in */ }};
}
