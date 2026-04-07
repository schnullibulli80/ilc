#!/usr/bin/env python3

import http.server
import socketserver
import sys


class DemoHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = "hello:ilc"
        self.send_response(200)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(body.encode("utf-8"))))
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(body.encode("utf-8"))

    def log_message(self, format, *args):
        return


def main() -> int:
    if len(sys.argv) != 2:
        return 1

    port_file = sys.argv[1]

    class ReusableTCPServer(socketserver.TCPServer):
        allow_reuse_address = True

    with ReusableTCPServer(("127.0.0.1", 0), DemoHandler) as server:
        with open(port_file, "w", encoding="utf-8") as handle:
            handle.write(str(server.server_address[1]))
        server.handle_request()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
