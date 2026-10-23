pub enum Option<T> {
    None,
    Some(T),
}

impl<T> Option<T> {
    pub fn is_some(&self) -> bool {
        matches!(*self, Some(_))
    }

    pub fn is_none(&self) -> bool {
        !self.is_some()
    }

    pub fn unwrap(self) -> T {
        match self {
            Some(val) => val,
            None => crate::panicking::panic("called `Option::unwrap()` on a `None` value"),
        }
    }

    pub fn expect(self, msg: &str) -> T {
        match self {
            Some(val) => val,
            None => panic!("{}", msg),
        }
    }

    pub fn unwrap_or(self, default: T) -> T {
        match self {
            Some(x) => x,
            None => default,
        }
    }

    pub fn unwrap_or_default(self) -> T
    where
        T: Default,
    {
        match self {
            Some(x) => x,
            None => T::default(),
        }
    }

    pub fn as_ref(&self) -> Option<&T> {
        match *self {
            Some(ref x) => Some(x),
            None => None,
        }
    }

    pub fn as_mut(&mut self) -> Option<&mut T> {
        match *self {
            Some(ref mut x) => Some(x),
            None => None,
        }
    }

    pub fn ok_or<E>(self, err: E) -> Result<T, E> {
        match self {
            Some(v) => Ok(v),
            None => Err(err),
        }
    }

    pub fn and<U>(self, optb: Option<U>) -> Option<U> {
        match self {
            Some(_) => optb,
            None => None,
        }
    }

    pub fn or(self, optb: Option<T>) -> Option<T> {
        match self {
            Some(x) => Some(x),
            None => optb,
        }
    }
}

impl<T: Clone> Clone for Option<T> {
    fn clone(&self) -> Self {
        match *self {
            Some(ref x) => Some(x.clone()),
            None => None,
        }
    }
}

impl<T: Copy> Copy for Option<T> {}

impl<T: PartialEq> PartialEq for Option<T> {
    fn eq(&self, other: &Self) -> bool {
        match (self, other) {
            (&Some(ref a), &Some(ref b)) => *a == *b,
            (&None, &None) => true,
            _ => false,
        }
    }
}

impl<T: Eq> Eq for Option<T> {}

impl<T> Default for Option<T> {
    fn default() -> Option<T> {
        None
    }
}

impl<T> From<T> for Option<T> {
    fn from(val: T) -> Option<T> {
        Some(val)
    }
}
