use crate::fmt;

// There is no panic runtime to hand the message to, so every panic aborts as with panic=abort
pub fn panic_fmt(fmt: fmt::Arguments<'_>) -> ! {
    crate::intrinsics::abort()
}

pub fn panic(expr: &'static str) -> ! {
    panic_fmt(fmt::Arguments::new_const(&[expr]))
}

#[lang = "panic_bounds_check"]
fn panic_bounds_check(index: usize, len: usize) -> ! {
    panic!("index out of bounds: the len is {len} but the index is {index}")
}

pub mod panic_const {
    use super::panic_fmt;
    use crate::fmt;

    #[lang = "panic_const_div_by_zero"]
    pub fn panic_const_div_by_zero() -> ! {
        panic_fmt(fmt::Arguments::new_const(&["attempt to divide by zero"]))
    }

    #[lang = "panic_const_rem_by_zero"]
    pub fn panic_const_rem_by_zero() -> ! {
        panic_fmt(fmt::Arguments::new_const(&["attempt to calculate the remainder with a divisor of zero"]))
    }

    #[lang = "panic_const_div_overflow"]
    pub fn panic_const_div_overflow() -> ! {
        panic_fmt(fmt::Arguments::new_const(&["attempt to divide with overflow"]))
    }

    #[lang = "panic_const_rem_overflow"]
    pub fn panic_const_rem_overflow() -> ! {
        panic_fmt(fmt::Arguments::new_const(&["attempt to calculate the remainder with overflow"]))
    }
}
