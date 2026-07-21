use std::{
    fmt::Arguments,
    fs::File,
    io::Write,
    sync::{Arc, Mutex},
};

static CELL: Mutex<Option<File>> = Mutex::new(None);

pub fn redirect(file: File) {
    let mut lock = CELL.lock().unwrap();
    if let Some(_) = lock.as_ref() {
        panic!("Cannot redirect log for multiple times.");
    }
    *lock = Some(file);

    std::panic::update_hook(|prev, info| {
        let mut lock = CELL.lock().unwrap();
        match lock.as_mut() {
            Some(file) => {
                let data = Arc::new(Mutex::new(Vec::<u8>::new()));
                std::io::set_output_capture(Some(data.clone()));
                prev(info);
                std::io::set_output_capture(None);

                file.write_all(data.lock().unwrap().as_ref()).unwrap();
            },
            None => prev(info),
        }
    });
}

pub fn logging(prefix: &'static str, argument: Arguments) {
    let mut lock = CELL.lock().unwrap();

    match lock.as_mut() {
        Some(file) => writeln!(file, "[{}]: {}", prefix, argument).unwrap(),
        None => println!("[{}]: {}", prefix, argument),
    }
}
