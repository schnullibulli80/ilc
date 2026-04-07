#!/usr/bin/env python3

import socket
import sys


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: tcp_line_server.py <port-file>", file=sys.stderr)
        return 1

    port_file = sys.argv[1]
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind(("127.0.0.1", 0))
        server.listen(1)

        port = server.getsockname()[1]
        with open(port_file, "w", encoding="utf-8") as handle:
            handle.write(str(port))

        connection, _ = server.accept()
        with connection:
            reader = connection.makefile("r", encoding="utf-8", newline="\n")
            writer = connection.makefile("w", encoding="utf-8", newline="\n")
            line = reader.readline()
            if line.endswith("\n"):
                line = line[:-1]
            writer.write(f"echo:{line}\n")
            writer.flush()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
