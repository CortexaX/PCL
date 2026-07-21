use std::net::{IpAddr, Ipv4Addr, Ipv6Addr, SocketAddr, UdpSocket};
use std::sync::mpsc::{self, Receiver, Sender};
use std::{io, thread};
use std::time::Duration;

pub struct FakeServer {
    pub port: u16,
    _holder: Sender<()>,
}

impl FakeServer {
    pub fn create(port: u16, motd: &'static str) -> FakeServer {
        let (tx, rx) = mpsc::channel::<()>();
        thread::spawn(move || run(port, motd, rx));

        FakeServer { port, _holder: tx }
    }
}

fn run(port: u16, motd: &'static str, signal: Receiver<()>) {
    lazy_static::lazy_static! {
        static ref ADDR_V4: SocketAddr = SocketAddr::new(IpAddr::V4(Ipv4Addr::new(224, 0, 2, 60)), 4445);
        static ref ADDR_V6: SocketAddr = SocketAddr::new(IpAddr::V6(Ipv6Addr::new(0xFF75, 0x230, 0, 0, 0, 0, 0, 0x60)), 4445);
    }

    let sockets: Vec<(UdpSocket, &'static SocketAddr)> = crate::ADDRESSES
        .iter()
        .map(|address| -> io::Result<(UdpSocket, &'static SocketAddr)> {
            let socket = UdpSocket::bind((*address, 0))?;
            let ip: &SocketAddr = match address {
                IpAddr::V4(_) => {
                    socket.set_broadcast(true)?;
                    socket.set_multicast_ttl_v4(4)?;
                    socket.set_multicast_loop_v4(true)?;
                    &ADDR_V4
                }
                IpAddr::V6(_) => {
                    socket.set_multicast_loop_v6(true)?;
                    &ADDR_V6
                }
            };

            Ok((socket, ip))
        })
        .filter_map(|r| r.ok())
        .collect();

    let message: String = format!("[MOTD]{}[/MOTD][AD]{}[/AD]", motd, port);

    loop {
        if let Err(mpsc::TryRecvError::Disconnected) = signal.try_recv() {
            return;
        }

        for (socket, address) in sockets.iter() {
            let _ = socket.send_to(message.as_bytes(), address);
        }

        thread::sleep(Duration::from_millis(1500));
    }
}
