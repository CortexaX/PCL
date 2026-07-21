use socket2::{Domain, SockAddr, Socket, Type};
use std::borrow::Cow;
use std::io::Result;
use std::mem::MaybeUninit;
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr, SocketAddrV4, SocketAddrV6};
use std::str::FromStr;
use std::sync::mpsc::{self, Receiver, Sender};
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime};
use std::{mem, thread};

pub struct MinecraftScanner {
    port: Arc<Mutex<Vec<u16>>>,
    _holder: Sender<()>,
}

impl MinecraftScanner {
    pub fn create(filter: fn(&str) -> bool) -> MinecraftScanner {
        let (tx, rx) = mpsc::channel::<()>();
        let port = Arc::new(Mutex::new(vec![]));

        let port_cloned = Arc::clone(&port);
        thread::spawn(move || {
            let result = Self::run(rx, port_cloned, filter);

            match result {
                Ok(_) => {}
                Err(err) => {
                    logging!("Server Scanner", "Cannot scan: {}", err);
                }
            }
        });

        return MinecraftScanner { _holder: tx, port };
    }

    fn run(signal: Receiver<()>, output: Arc<Mutex<Vec<u16>>>, filter: fn(&str) -> bool) -> Result<()> {
        let sockets: Vec<(Socket, &IpAddr)> = crate::ADDRESSES
            .iter()
            .map(|address| match address {
                IpAddr::V4(ip) => {
                    let socket = Socket::new(Domain::IPV4, Type::DGRAM, None)?;
                    socket.set_reuse_address(true)?;
                    socket.bind(&SockAddr::from(SocketAddrV4::new(*ip, 4445)))?;
                    socket.join_multicast_v4(
                        &Ipv4Addr::from_str("224.0.2.60").unwrap(),
                        &Ipv4Addr::UNSPECIFIED,
                    )?;
                    socket.set_read_timeout(Some(Duration::from_millis(500)))?;
                    Ok((socket, address))
                }
                IpAddr::V6(ip) => {
                    let socket = Socket::new(Domain::IPV6, Type::DGRAM, None)?;
                    socket.set_only_v6(true)?;
                    socket.set_reuse_address(true)?;
                    socket.bind(&SockAddr::from(SocketAddrV6::new(*ip, 4445, 0, 0)))?;
                    socket.join_multicast_v6(&Ipv6Addr::from_str("FF75:230::60").unwrap(), 0)?;
                    socket.set_read_timeout(Some(Duration::from_millis(500)))?;
                    Ok((socket, address))
                }
            })
            .filter_map(|r: Result<(Socket, &IpAddr)>| r.ok())
            .collect();

        logging!("Server Scanner", "Starting server scanner at IP: {:?}", sockets.iter().map(|p| p.1).collect::<Vec<_>>());

        let mut buf: [MaybeUninit<u8>; _] = [MaybeUninit::uninit(); 8192];

        let mut ports: Vec<(u16, SystemTime)> = vec![];

        loop {
            let mut dirty = false;

            if let Err(mpsc::TryRecvError::Disconnected) = signal.try_recv() {
                return Ok(());
            }

            let now = SystemTime::now();
            for i in (0..ports.len()).rev() {
                if match now.duration_since(ports[i].1) {
                    Ok(value) => value.as_millis() >= 5_000,
                    Err(_) => false,
                } {
                    dirty = true;
                    ports.remove(i);
                }
            }

            for (socket, _) in sockets.iter() {
                if let Ok((length, _)) = socket.recv_from(&mut buf) {
                    // SAFETY: 0..length has been initialized by recv_from.
                    let buf = unsafe { mem::transmute::<&[MaybeUninit<u8>], &[u8]>(&buf[..length]) };

                    let data: Cow<'_, str> = String::from_utf8_lossy(buf);
                    {
                        let begin = data.find("[MOTD]");
                        let end = data.find("[/MOTD]");
                        if let Some(begin) = begin
                            && let Some(end) = end
                            && end - begin >= "[MOTD]".len() + 1
                            && let Some(motd) = data.as_ref().get((begin + "[MOTD]".len())..end)
                            && filter(motd)
                        {} else {
                            continue;
                        }
                    }

                    {
                        let begin = data.find("[AD]");
                        let end = data.find("[/AD]");
                        if let Some(begin) = begin
                            && let Some(end) = end
                            && end - begin >= "[AD]".len() + 1
                            && let Some(port) = data.as_ref().get((begin + "[AD]".len())..end)
                            && let Ok(port) = port.parse::<u16>()
                        {
                            let mut existed = false;
                            for i in 0..ports.len() {
                                if ports[i].0 == port {
                                    existed = true;
                                    ports.remove(i);
                                    break;
                                }
                            }

                            ports.push((port, SystemTime::now()));
                            if !existed {
                                dirty = true;
                            }
                            break;
                        }
                    }
                }
            }

            if dirty {
                let mut output = output.lock().unwrap();
                output.clear();

                let mut message = String::from("Updating server list to [");
                for i in 0..ports.len() {
                    output.push(ports[0].0);
                    message += &ports[0].0.to_string();
                    if i != ports.len() - 1 {
                        message.push_str(", ");
                    }
                }
                message.push(']');
                logging!("Server Scanner", "{}", message);
            }
        }
    }

    pub fn get_ports(&self) -> Vec<u16> {
        return self.port.lock().unwrap().clone();
    }
}
