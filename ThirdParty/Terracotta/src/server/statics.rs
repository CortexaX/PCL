use std::{path::PathBuf, sync::{Arc, RwLock}, thread, time::Duration};

use rocket::http::Status;

static WEB_STATIC: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/webstatics.7z"));

struct MemoryFile(Arc<Storage>);
struct Storage {
    path: PathBuf,
    data: Box<[u8]>,
}

impl AsRef<[u8]> for MemoryFile {
    fn as_ref(&self) -> &[u8] {
        return self.0.as_ref().data.as_ref();
    }
}

impl<'r> rocket::response::Responder<'r, 'static> for MemoryFile {
    fn respond_to(self, _: &'r rocket::Request<'_>) -> rocket::response::Result<'static> {
        use rocket::http::ContentType;
        use std::io::Cursor;

        let ct = self
            .0
            .as_ref()
            .path
            .extension()
            .and_then(|ext| ContentType::from_extension(&ext.to_string_lossy()));

        let mut response = rocket::Response::build()
            .header(ContentType::Binary)
            .sized_body(self.0.as_ref().data.len(), Cursor::new(self))
            .ok()?;

        if let Some(ct) = ct {
            response.set_header(ct);
        }

        Ok(response)
    }
}

#[get("/<path..>")]
fn static_files(path: PathBuf) -> Result<MemoryFile, Status> {
    fn compute_static_pages() -> Vec<Arc<Storage>> {
        let mut reader = sevenz_rust2::ArchiveReader::new(
            std::io::Cursor::new(WEB_STATIC),
            sevenz_rust2::Password::empty(),
        )
        .unwrap();
        let mut pages: Vec<Arc<Storage>> = vec![];
        let _ = reader.for_each_entries(|entry, reader| {
            if entry.is_directory() {
                return Ok(true);
            }

            let mut buffer: Vec<u8> = vec![];
            reader.read_to_end(&mut buffer).unwrap();
            pages.push(Arc::new(Storage {
                path: PathBuf::from(entry.name()),
                data: buffer.into_boxed_slice(),
            }));

            return Ok(true);
        });

        pages.shrink_to_fit();
        return pages;
    }

    use std::sync::mpsc::{self, Sender};
    lazy_static::lazy_static! {
        static ref MAIN_PAGE: RwLock<Option<(Sender<()>, Vec<Arc<Storage>>)>> = RwLock::new(None);
    }

    fn respond(mut path: PathBuf, storages: &Vec<Arc<Storage>>) -> Result<MemoryFile, Status> {
        if path.as_os_str().is_empty() {
            path = PathBuf::from("_.html");
        }
        return match storages.iter().find(|storage| path == storage.path) {
            Some(storage) => Ok(MemoryFile(storage.clone())),
            None => Err(Status { code: 404 }),
        };
    }

    let lock = MAIN_PAGE.read().unwrap();
    match lock.as_ref() {
        Some((sender, storages)) => {
            let _ = sender.send(());
            return respond(path, storages);
        }
        None => {
            drop(lock);

            let mut lock = MAIN_PAGE.write().unwrap();
            match lock.as_ref() {
                Some((sender, storages)) => {
                    let _ = sender.send(());
                    return respond(path, storages);
                }
                None => {
                    let pages = compute_static_pages();
                    let respond = respond(path, &pages);

                    let (sender, receiver) = mpsc::channel();
                    thread::spawn(move || {
                        loop {
                            if let Err(_) = receiver.recv_timeout(Duration::from_secs(60)) {
                                let mut lock = MAIN_PAGE.write().unwrap();
                                logging!(
                                    "UI",
                                    "Invaliding static page cache to reduce memory usage."
                                );
                                *lock = None;
                                return;
                            }
                        }
                    });

                    *lock = Some((sender, pages));
                    return respond;
                }
            }
        }
    }
}

pub fn configure(rocket: rocket::Rocket<rocket::Build>) -> rocket::Rocket<rocket::Build> {
    rocket.mount("/", routes![static_files])
}