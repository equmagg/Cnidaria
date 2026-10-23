#[lang = "eq"]
pub trait PartialEq<Rhs: ?Sized = Self> {
    fn eq(&self, other: &Rhs) -> bool;

    fn ne(&self, other: &Rhs) -> bool {
        !self.eq(other)
    }
}

pub trait Eq: PartialEq<Self> {}

pub enum Ordering {
    Less = -1,
    Equal = 0,
    Greater = 1,
}

impl Ordering {
    pub fn is_eq(self) -> bool {
        matches!(self, Ordering::Equal)
    }

    pub fn is_ne(self) -> bool {
        !matches!(self, Ordering::Equal)
    }

    pub fn is_lt(self) -> bool {
        matches!(self, Ordering::Less)
    }

    pub fn is_gt(self) -> bool {
        matches!(self, Ordering::Greater)
    }

    pub fn is_le(self) -> bool {
        !matches!(self, Ordering::Greater)
    }

    pub fn is_ge(self) -> bool {
        !matches!(self, Ordering::Less)
    }

    pub fn reverse(self) -> Ordering {
        match self {
            Ordering::Less => Ordering::Greater,
            Ordering::Equal => Ordering::Equal,
            Ordering::Greater => Ordering::Less,
        }
    }

    pub fn then(self, other: Ordering) -> Ordering {
        match self {
            Ordering::Equal => other,
            _ => self,
        }
    }
}

impl Clone for Ordering {
    fn clone(&self) -> Ordering {
        *self
    }
}

impl Copy for Ordering {}

impl PartialEq for Ordering {
    fn eq(&self, other: &Ordering) -> bool {
        matches!((*self, *other), (Ordering::Less, Ordering::Less) | (Ordering::Equal, Ordering::Equal) | (Ordering::Greater, Ordering::Greater))
    }
}

impl Eq for Ordering {}

#[lang = "partial_ord"]
pub trait PartialOrd<Rhs: ?Sized = Self>: PartialEq<Rhs> {
    fn partial_cmp(&self, other: &Rhs) -> Option<Ordering>;

    fn lt(&self, other: &Rhs) -> bool {
        matches!(self.partial_cmp(other), Some(Ordering::Less))
    }

    fn le(&self, other: &Rhs) -> bool {
        matches!(self.partial_cmp(other), Some(Ordering::Less | Ordering::Equal))
    }

    fn gt(&self, other: &Rhs) -> bool {
        matches!(self.partial_cmp(other), Some(Ordering::Greater))
    }

    fn ge(&self, other: &Rhs) -> bool {
        matches!(self.partial_cmp(other), Some(Ordering::Greater | Ordering::Equal))
    }
}

pub trait Ord: Eq + PartialOrd<Self> {
    fn cmp(&self, other: &Self) -> Ordering;

    fn max(self, other: Self) -> Self
    where
        Self: Sized,
    {
        match self.cmp(&other) {
            Ordering::Greater => self,
            _ => other,
        }
    }

    fn min(self, other: Self) -> Self
    where
        Self: Sized,
    {
        match self.cmp(&other) {
            Ordering::Greater => other,
            _ => self,
        }
    }
}

macro_rules! partial_eq_impls {
    ($($t:ty)*) => {
        $(
            impl PartialEq for $t {
                fn eq(&self, other: &$t) -> bool {
                    *self == *other
                }

                fn ne(&self, other: &$t) -> bool {
                    *self != *other
                }
            }
        )*
    };
}

macro_rules! eq_impls {
    ($($t:ty)*) => {
        $(
            impl Eq for $t {}
        )*
    };
}

macro_rules! partial_ord_impls {
    ($($t:ty)*) => {
        $(
            impl PartialOrd for $t {
                fn partial_cmp(&self, other: &$t) -> Option<Ordering> {
                    if *self < *other {
                        Some(Ordering::Less)
                    } else if *self > *other {
                        Some(Ordering::Greater)
                    } else if *self == *other {
                        Some(Ordering::Equal)
                    } else {
                        None
                    }
                }

                fn lt(&self, other: &$t) -> bool {
                    *self < *other
                }

                fn le(&self, other: &$t) -> bool {
                    *self <= *other
                }

                fn gt(&self, other: &$t) -> bool {
                    *self > *other
                }

                fn ge(&self, other: &$t) -> bool {
                    *self >= *other
                }
            }
        )*
    };
}

macro_rules! ord_impls {
    ($($t:ty)*) => {
        $(
            impl Ord for $t {
                fn cmp(&self, other: &$t) -> Ordering {
                    if *self < *other {
                        Ordering::Less
                    } else if *self == *other {
                        Ordering::Equal
                    } else {
                        Ordering::Greater
                    }
                }
            }
        )*
    };
}

partial_eq_impls! { bool char usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
eq_impls! { bool char usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }
partial_ord_impls! { bool char usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 f32 f64 }
ord_impls! { bool char usize u8 u16 u32 u64 u128 isize i8 i16 i32 i64 i128 }

impl PartialEq for () {
    fn eq(&self, _other: &()) -> bool {
        true
    }
}

impl Eq for () {}

impl PartialOrd for () {
    fn partial_cmp(&self, _other: &()) -> Option<Ordering> {
        Some(Ordering::Equal)
    }
}

impl Ord for () {
    fn cmp(&self, _other: &()) -> Ordering {
        Ordering::Equal
    }
}

impl<'a, 'b, A: ?Sized, B: ?Sized> PartialEq<&'b B> for &'a A
where
    A: PartialEq<B>,
{
    fn eq(&self, other: &&'b B) -> bool {
        PartialEq::eq(*self, *other)
    }

    fn ne(&self, other: &&'b B) -> bool {
        PartialEq::ne(*self, *other)
    }
}

impl<'a, 'b, A: ?Sized, B: ?Sized> PartialOrd<&'b B> for &'a A
where
    A: PartialOrd<B>,
{
    fn partial_cmp(&self, other: &&'b B) -> Option<Ordering> {
        PartialOrd::partial_cmp(*self, *other)
    }

    fn lt(&self, other: &&'b B) -> bool {
        PartialOrd::lt(*self, *other)
    }

    fn le(&self, other: &&'b B) -> bool {
        PartialOrd::le(*self, *other)
    }

    fn gt(&self, other: &&'b B) -> bool {
        PartialOrd::gt(*self, *other)
    }

    fn ge(&self, other: &&'b B) -> bool {
        PartialOrd::ge(*self, *other)
    }
}

impl<'a, A: ?Sized + Eq> Eq for &'a A {}

impl<'a, A: ?Sized + Ord> Ord for &'a A {
    fn cmp(&self, other: &Self) -> Ordering {
        Ord::cmp(*self, *other)
    }
}
