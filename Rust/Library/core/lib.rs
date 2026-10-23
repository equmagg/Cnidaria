#![no_core]
#![feature(no_core, rustc_attrs, intrinsics, prelude_import, lang_items)]

#[prelude_import]
use prelude::rust_2024::*;

#[macro_use]
mod macros;

pub mod clone;
pub mod cmp;
pub mod convert;
pub mod default;
pub mod fmt;
pub mod intrinsics;
pub mod marker;
pub mod ops;
pub mod option;
pub mod panic;
pub mod panicking;
pub mod prelude;
pub mod result;
