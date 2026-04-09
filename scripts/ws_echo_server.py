#!/usr/bin/env python3

import base64
import hashlib
import socket
import sys


GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"


def recv_exact(conn: socket.socket, size: int) -> bytes:
    data = b""
    while len(data) < size:
        chunk = conn.recv(size - len(data))
        if not chunk:
            raise RuntimeError("connection closed")
        data += chunk
    return data


def read_frame(conn: socket.socket) -> tuple[int, bytes]:
    header = recv_exact(conn, 2)
    opcode = header[0] & 0x0F
    masked = (header[1] & 0x80) != 0
    length = header[1] & 0x7F
    if length == 126:
        extended = recv_exact(conn, 2)
        length = (extended[0] << 8) | extended[1]
    elif length == 127:
        raise RuntimeError("unsupported frame length")

    mask = recv_exact(conn, 4) if masked else b""
    payload = recv_exact(conn, length) if length > 0 else b""
    if masked:
        payload = bytes(value ^ mask[index % 4] for index, value in enumerate(payload))
    return opcode, payload


def write_text_frame(conn: socket.socket, text: str) -> None:
    payload = text.encode("utf-8")
    frame = bytearray()
    frame.append(0x81)
    if len(payload) < 126:
        frame.append(len(payload))
    else:
        frame.append(126)
        frame.extend(len(payload).to_bytes(2, "big"))
    frame.extend(payload)
    conn.sendall(frame)


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: ws_echo_server.py <port-file>", file=sys.stderr)
        return 1

    port_file = sys.argv[1]
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind(("127.0.0.1", 0))
        server.listen(1)
        with open(port_file, "w", encoding="utf-8") as handle:
            handle.write(str(server.getsockname()[1]))

        conn, _ = server.accept()
        with conn:
            request = b""
            while b"\r\n\r\n" not in request:
                chunk = conn.recv(4096)
                if not chunk:
                    return 1
                request += chunk

            key = ""
            for line in request.decode("utf-8").split("\r\n"):
                if line.lower().startswith("sec-websocket-key:"):
                    key = line.split(":", 1)[1].strip()
                    break

            accept = base64.b64encode(hashlib.sha1((key + GUID).encode("utf-8")).digest()).decode("ascii")
            response = (
                "HTTP/1.1 101 Switching Protocols\r\n"
                "Upgrade: websocket\r\n"
                "Connection: Upgrade\r\n"
                f"Sec-WebSocket-Accept: {accept}\r\n\r\n"
            )
            conn.sendall(response.encode("utf-8"))

            while True:
                opcode, payload = read_frame(conn)
                if opcode == 0x8:
                    return 0
                if opcode != 0x1:
                    return 1
                write_text_frame(conn, "echo:" + payload.decode("utf-8"))


if __name__ == "__main__":
    raise SystemExit(main())
