pub enum Result<T, E> {
    Ok(T),
    Err(E),
}

impl<T, E> Result<T, E> {
    pub fn is_ok(&self) -> bool {
        matches!(*self, Ok(_))
    }

    pub fn is_err(&self) -> bool {
        !self.is_ok()
    }

    pub fn ok(self) -> Option<T> {
        match self {
            Ok(x) => Some(x),
            Err(_) => None,
        }
    }

    pub fn err(self) -> Option<E> {
        match self {
            Ok(_) => None,
            Err(x) => Some(x),
        }
    }

    pub fn unwrap(self) -> T {
        match self {
            Ok(t) => t,
            Err(_) => crate::panicking::panic("called `Result::unwrap()` on an `Err` value"),
        }
    }

    pub fn unwrap_err(self) -> E {
        match self {
            Ok(_) => crate::panicking::panic("called `Result::unwrap_err()` on an `Ok` value"),
            Err(e) => e,
        }
    }

    pub fn expect(self, msg: &str) -> T {
        match self {
            Ok(t) => t,
            Err(_) => panic!("{}", msg),
        }
    }

    pub fn unwrap_or(self, default: T) -> T {
        match self {
            Ok(t) => t,
            Err(_) => default,
        }
    }

    pub fn as_ref(&self) -> Result<&T, &E> {
        match *self {
            Ok(ref x) => Ok(x),
            Err(ref x) => Err(x),
        }
    }
}

impl<T: Clone, E: Clone> Clone for Result<T, E> {
    fn clone(&self) -> Self {
        match *self {
            Ok(ref x) => Ok(x.clone()),
            Err(ref x) => Err(x.clone()),
        }
    }
}

impl<T: Copy, E: Copy> Copy for Result<T, E> {}

impl<T: PartialEq, E: PartialEq> PartialEq for Result<T, E> {
    fn eq(&self, other: &Self) -> bool {
        match (self, other) {
            (&Ok(ref a), &Ok(ref b)) => *a == *b,
            (&Err(ref a), &Err(ref b)) => *a == *b,
            _ => false,
        }
    }
}

impl<T: Eq, E: Eq> Eq for Result<T, E> {}
