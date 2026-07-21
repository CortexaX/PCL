use crate::scaffolding::{PacketResponse, TIMEOUT};
use socket2::{Domain, SockAddr, Socket, Type};
use std::io::{Read, Write};
use std::net::{IpAddr, SocketAddr};
use std::sync::{mpsc, Arc, OnceLock};
use std::{io, thread};
use std::time::Duration;

struct Packet {
    data: Vec<u8>,
    handle: Box<dyn FnOnce(Option<PacketResponse>) + Send + 'static>,
}

pub struct ClientSession {
    channel: mpsc::Sender<Packet>,
    alive: Arc<OnceLock<()>>,
}

impl ClientSession {
    pub fn open(address: IpAddr, port: u16) -> io::Result<ClientSession> {
        let mut socket = Socket::new(
            match address {
                IpAddr::V4(_) => Domain::IPV4,
                IpAddr::V6(_) => Domain::IPV6,
            },
            Type::STREAM,
            None,
        )?;

        socket.set_read_timeout(Some(TIMEOUT))?;
        socket.set_write_timeout(Some(TIMEOUT))?;
        socket.connect_timeout(
            &SockAddr::from(SocketAddr::new(address, port)),
            TIMEOUT,
        )?;

        let (sender, receiver) = mpsc::channel::<Packet>();
        let alive = Arc::new(OnceLock::new());
        let alive2 = alive.clone();

        thread::spawn(move || {
            let handle_packet =
                &mut move |data: Vec<u8>| -> io::Result<PacketResponse> {
                    socket.write_all(data.as_ref())?;
                    socket.flush()?;

                    drop(data);

                    let (status, body_size) = {
                        let mut buf = [0u8; 5];
                        socket.read_exact(&mut buf)?;

                        (
                            buf[0],
                            u32::from_be_bytes([buf[1], buf[2], buf[3], buf[4]]) as usize,
                        )
                    };

                    let mut data = vec![0u8; body_size];
                    socket.read_exact(&mut data)?;

                    Ok(if status == 0 {
                        PacketResponse::Ok { data }
                    } else {
                        PacketResponse::Fail { status, data }
                    })
                };

            while let Ok(packet) = receiver.recv() {
                let Packet { data, handle } = packet;
                match handle_packet(data) {
                    Ok(response) => handle(Some(response)),
                    Err(e) => {
                        logging!("ScaffoldingClient", "Session is closed: {:?}", e);
                        alive.get_or_init(|| ());

                        handle(None);
                        return;
                    }
                }
            }
        });

        Ok(ClientSession {
            channel: sender,
            alive: alive2,
        })
    }

    pub fn is_alive(&self) -> bool {
        self.alive.get().is_none()
    }

    pub fn send_sync<P>(&mut self, kind: (&str, &str), encoder: P) -> Option<PacketResponse>
    where
        P: FnOnce(&mut Vec<u8>),
    {
        let (sender, receiver) = mpsc::channel();
        self.send(kind, encoder, move |response| {
            let _ = sender.send(response);
        });

        match loop {
            match receiver.recv_timeout(Duration::from_millis(100)) {
                Ok(response) => break response,
                Err(mpsc::RecvTimeoutError::Disconnected) => unreachable!(),
                Err(mpsc::RecvTimeoutError::Timeout) if !self.is_alive() => {
                    logging!("ScaffoldingClient", "API {}:{} invocation failed: Session has been closed.", kind.0, kind.1);
                    return None;
                },
                Err(mpsc::RecvTimeoutError::Timeout) => continue,
            }
        } {
            Some(PacketResponse::Ok { data }) => Some(PacketResponse::Ok { data }),
            Some(PacketResponse::Fail { status, data }) => {
                logging!("ScaffoldingClient", "API {}:{} invocation failed with status {}: {}", kind.0, kind.1, status, String::from_utf8_lossy(&data));
                None
            },
            None => {
                logging!("ScaffoldingClient", "API {}:{} invocation failed: Session has been closed.", kind.0, kind.1);
                None
            }
        }
    }

    fn send<P, R>(&mut self, kind: (&str, &str), encoder: P, receiver: R)
    where
        P: FnOnce(&mut Vec<u8>),
        R: FnOnce(Option<PacketResponse>) + Send + 'static,
    {
        let mut data: Vec<u8> = vec![];

        {
            let length = kind.0.len() + kind.1.len() + 1;
            data.push(length as u8);
            data.reserve(length);

            data.extend_from_slice(kind.0.as_bytes());
            data.push(b':');
            data.extend_from_slice(kind.1.as_bytes());
        }
        {
            let pos = data.len();
            data.resize(pos + 4, 0);

            encoder(&mut data);

            let length = data.len() - pos - size_of::<u32>();
            data[pos..pos + size_of::<u32>()].copy_from_slice(&(length as u32).to_be_bytes());
        }

        self.channel
            .send(Packet {
                data,
                handle: Box::new(receiver),
            })
            .unwrap();
    }
}
