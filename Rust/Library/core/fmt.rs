pub struct Arguments<'a> {
    pieces: &'a [&'static str],
}

impl<'a> Arguments<'a> {
    #[doc(hidden)]
    pub fn new_const(pieces: &'a [&'static str]) -> Self {
        Arguments { pieces }
    }
}
