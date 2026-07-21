extern crate proc_macro;
use proc_macro::TokenStream;
use std::time::{SystemTime, UNIX_EPOCH};

#[proc_macro]
pub fn compile_time(_: TokenStream) -> TokenStream {
    lazy_static::lazy_static! {
        static ref TIMESTAMP: u128 = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_millis();
    }

    return format!("({}u128)", *TIMESTAMP).parse().unwrap();
}