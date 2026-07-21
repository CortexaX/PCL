use std::io;
use std::time::Duration;

pub mod client;
pub mod server;
pub mod profile;

pub(crate) static TIMEOUT: Duration = Duration::from_secs(64);

pub enum PacketResponse {
    Ok { data: Vec<u8> },
    Fail { status: u8, data: Vec<u8> },
}

impl PacketResponse {
    pub fn ok(data: Vec<u8>) -> io::Result<PacketResponse<>> {
        Ok(PacketResponse::Ok { data })
    }

    pub fn fail(status: u8, data: Vec<u8>) -> io::Result<PacketResponse<>> {
        Ok(PacketResponse::Fail { status, data })
    }
}
