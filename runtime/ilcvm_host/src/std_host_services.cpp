#include "ilcvm/std_host_services.h"

#include <array>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <netdb.h>
#include <sys/socket.h>
#include <sstream>
#include <string>
#include <thread>
#include <vector>
#include <unistd.h>

namespace ilcvm
{
namespace
{
constexpr std::string_view websocket_guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
constexpr char base64_alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

std::string base64_encode(std::string_view input)
{
    std::string encoded;
    encoded.reserve(((input.size() + 2) / 3) * 4);
    std::size_t index = 0;
    while (index + 3 <= input.size())
    {
        const auto chunk =
            (static_cast<std::uint32_t>(static_cast<unsigned char>(input[index])) << 16) |
            (static_cast<std::uint32_t>(static_cast<unsigned char>(input[index + 1])) << 8) |
            static_cast<std::uint32_t>(static_cast<unsigned char>(input[index + 2]));
        encoded.push_back(base64_alphabet[(chunk >> 18) & 0x3F]);
        encoded.push_back(base64_alphabet[(chunk >> 12) & 0x3F]);
        encoded.push_back(base64_alphabet[(chunk >> 6) & 0x3F]);
        encoded.push_back(base64_alphabet[chunk & 0x3F]);
        index += 3;
    }

    const auto remaining = input.size() - index;
    if (remaining == 1)
    {
        const auto chunk = static_cast<std::uint32_t>(static_cast<unsigned char>(input[index])) << 16;
        encoded.push_back(base64_alphabet[(chunk >> 18) & 0x3F]);
        encoded.push_back(base64_alphabet[(chunk >> 12) & 0x3F]);
        encoded.push_back('=');
        encoded.push_back('=');
    }
    else if (remaining == 2)
    {
        const auto chunk =
            (static_cast<std::uint32_t>(static_cast<unsigned char>(input[index])) << 16) |
            (static_cast<std::uint32_t>(static_cast<unsigned char>(input[index + 1])) << 8);
        encoded.push_back(base64_alphabet[(chunk >> 18) & 0x3F]);
        encoded.push_back(base64_alphabet[(chunk >> 12) & 0x3F]);
        encoded.push_back(base64_alphabet[(chunk >> 6) & 0x3F]);
        encoded.push_back('=');
    }

    return encoded;
}

std::array<std::uint8_t, 20> sha1_digest(std::string_view input)
{
    std::vector<std::uint8_t> bytes(input.begin(), input.end());
    const auto bit_length = static_cast<std::uint64_t>(bytes.size()) * 8u;
    bytes.push_back(0x80);
    while ((bytes.size() % 64) != 56)
    {
        bytes.push_back(0);
    }

    for (int shift = 56; shift >= 0; shift -= 8)
    {
        bytes.push_back(static_cast<std::uint8_t>((bit_length >> shift) & 0xFF));
    }

    auto rotl = [](std::uint32_t value, int shift)
    {
        return static_cast<std::uint32_t>((value << shift) | (value >> (32 - shift)));
    };

    std::uint32_t h0 = 0x67452301;
    std::uint32_t h1 = 0xEFCDAB89;
    std::uint32_t h2 = 0x98BADCFE;
    std::uint32_t h3 = 0x10325476;
    std::uint32_t h4 = 0xC3D2E1F0;

    for (std::size_t chunk_start = 0; chunk_start < bytes.size(); chunk_start += 64)
    {
        std::uint32_t words[80] {};
        for (std::size_t index = 0; index < 16; ++index)
        {
            const auto offset = chunk_start + (index * 4);
            words[index] =
                (static_cast<std::uint32_t>(bytes[offset]) << 24) |
                (static_cast<std::uint32_t>(bytes[offset + 1]) << 16) |
                (static_cast<std::uint32_t>(bytes[offset + 2]) << 8) |
                static_cast<std::uint32_t>(bytes[offset + 3]);
        }

        for (std::size_t index = 16; index < 80; ++index)
        {
            words[index] = rotl(words[index - 3] ^ words[index - 8] ^ words[index - 14] ^ words[index - 16], 1);
        }

        auto a = h0;
        auto b = h1;
        auto c = h2;
        auto d = h3;
        auto e = h4;

        for (std::size_t index = 0; index < 80; ++index)
        {
            std::uint32_t f = 0;
            std::uint32_t k = 0;
            if (index < 20)
            {
                f = (b & c) | ((~b) & d);
                k = 0x5A827999;
            }
            else if (index < 40)
            {
                f = b ^ c ^ d;
                k = 0x6ED9EBA1;
            }
            else if (index < 60)
            {
                f = (b & c) | (b & d) | (c & d);
                k = 0x8F1BBCDC;
            }
            else
            {
                f = b ^ c ^ d;
                k = 0xCA62C1D6;
            }

            const auto temp = rotl(a, 5) + f + e + k + words[index];
            e = d;
            d = c;
            c = rotl(b, 30);
            b = a;
            a = temp;
        }

        h0 += a;
        h1 += b;
        h2 += c;
        h3 += d;
        h4 += e;
    }

    std::array<std::uint8_t, 20> digest {};
    const std::uint32_t values[5] { h0, h1, h2, h3, h4 };
    for (std::size_t index = 0; index < 5; ++index)
    {
        digest[index * 4] = static_cast<std::uint8_t>((values[index] >> 24) & 0xFF);
        digest[(index * 4) + 1] = static_cast<std::uint8_t>((values[index] >> 16) & 0xFF);
        digest[(index * 4) + 2] = static_cast<std::uint8_t>((values[index] >> 8) & 0xFF);
        digest[(index * 4) + 3] = static_cast<std::uint8_t>(values[index] & 0xFF);
    }

    return digest;
}

bool send_all(int socket_fd, const std::vector<std::uint8_t>& bytes)
{
    const auto* cursor = reinterpret_cast<const char*>(bytes.data());
    std::size_t remaining = bytes.size();
    while (remaining > 0)
    {
        const auto written = send(socket_fd, cursor, remaining, 0);
        if (written <= 0)
        {
            return false;
        }

        cursor += written;
        remaining -= static_cast<std::size_t>(written);
    }

    return true;
}

bool recv_all(int socket_fd, void* buffer, std::size_t size)
{
    auto* cursor = static_cast<char*>(buffer);
    std::size_t remaining = size;
    while (remaining > 0)
    {
        const auto read_count = recv(socket_fd, cursor, remaining, 0);
        if (read_count <= 0)
        {
            return false;
        }

        cursor += read_count;
        remaining -= static_cast<std::size_t>(read_count);
    }

    return true;
}

std::string make_websocket_accept_key(std::string_view key)
{
    const auto digest = sha1_digest(std::string(key) + std::string(websocket_guid));
    return base64_encode(std::string_view(reinterpret_cast<const char*>(digest.data()), digest.size()));
}
} // namespace

StandardHostServices::StandardHostServices(std::vector<std::string> command_line_args)
    : command_line_args_(std::move(command_line_args))
{
}

StandardHostServices::~StandardHostServices()
{
    for (const auto& [connection_id, socket_fd] : tcp_connections_)
    {
        (void)connection_id;
        close(socket_fd);
    }

    for (const auto& [connection_id, socket_fd] : websocket_connections_)
    {
        (void)connection_id;
        close(socket_fd);
    }

    for (const auto& [mutex_id, mutex] : mutexes_)
    {
        (void)mutex_id;
        if (mutex != nullptr && mutex->is_locked && mutex->owner_thread == std::this_thread::get_id())
        {
            mutex->native_mutex.unlock();
        }
    }
}

void StandardHostServices::console_write(std::string_view text) const
{
    std::cout << text << std::flush;
}

void StandardHostServices::console_write_line(std::string_view text) const
{
    std::cout << text << '\n' << std::flush;
}

std::string StandardHostServices::console_read_line() const
{
    std::string line;
    std::getline(std::cin, line);
    return line;
}

std::uint64_t StandardHostServices::get_monotonic_timestamp_ms() const
{
    const auto now = std::chrono::steady_clock::now().time_since_epoch();
    return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(now).count());
}

std::uint64_t StandardHostServices::get_wall_timestamp_ms() const
{
    const auto now = std::chrono::system_clock::now().time_since_epoch();
    return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(now).count());
}

std::string StandardHostServices::get_wall_datetime_text() const
{
    const auto now = std::chrono::system_clock::now();
    const auto time = std::chrono::system_clock::to_time_t(now);
    std::tm local_time {};
    localtime_r(&time, &local_time);

    std::ostringstream buffer;
    buffer << std::put_time(&local_time, "%Y-%m-%d %H:%M:%S");
    return buffer.str();
}

std::string StandardHostServices::get_current_working_directory() const
{
    return std::filesystem::current_path().string();
}

std::string StandardHostServices::get_user_name() const
{
    if (const auto* value = std::getenv("USER"); value != nullptr)
    {
        return std::string(value);
    }

    return std::string();
}

std::string StandardHostServices::get_machine_name() const
{
    char buffer[256] = {};
    if (gethostname(buffer, sizeof(buffer)) != 0)
    {
        return std::string();
    }

    buffer[sizeof(buffer) - 1] = '\0';
    return std::string(buffer);
}

std::string StandardHostServices::get_home_directory() const
{
    if (const auto* value = std::getenv("HOME"); value != nullptr)
    {
        return std::string(value);
    }

    return std::string();
}

std::string StandardHostServices::get_temp_directory() const
{
    if (const auto* value = std::getenv("TMPDIR"); value != nullptr)
    {
        return std::string(value);
    }

    return std::filesystem::temp_directory_path().string();
}

std::string StandardHostServices::get_environment_variable(std::string_view name) const
{
    const auto* value = std::getenv(std::string(name).c_str());
    return value != nullptr ? std::string(value) : std::string();
}

void StandardHostServices::set_environment_variable(std::string_view name, std::string_view value) const
{
    if (value.empty())
    {
        unsetenv(std::string(name).c_str());
        return;
    }

    setenv(std::string(name).c_str(), std::string(value).c_str(), 1);
}

bool StandardHostServices::file_exists(std::string_view path) const
{
    return std::filesystem::exists(std::filesystem::path(path));
}

std::string StandardHostServices::file_read_all_text(std::string_view path) const
{
    std::ifstream input(std::filesystem::path(path), std::ios::binary);
    if (!input)
    {
        return std::string();
    }

    std::ostringstream buffer;
    buffer << input.rdbuf();
    return buffer.str();
}

void StandardHostServices::file_write_all_text(std::string_view path, std::string_view text) const
{
    std::ofstream output(std::filesystem::path(path), std::ios::binary | std::ios::trunc);
    if (!output)
    {
        return;
    }

    output.write(text.data(), static_cast<std::streamsize>(text.size()));
}

void StandardHostServices::file_append_all_text(std::string_view path, std::string_view text) const
{
    std::ofstream output(std::filesystem::path(path), std::ios::binary | std::ios::app);
    if (!output)
    {
        return;
    }

    output.write(text.data(), static_cast<std::streamsize>(text.size()));
}

std::string StandardHostServices::path_combine(std::string_view left, std::string_view right) const
{
    return (std::filesystem::path(left) / std::filesystem::path(right)).string();
}

std::string StandardHostServices::path_get_file_name(std::string_view path) const
{
    return std::filesystem::path(path).filename().string();
}

std::string StandardHostServices::path_get_directory_name(std::string_view path) const
{
    return std::filesystem::path(path).parent_path().string();
}

std::string StandardHostServices::path_get_extension(std::string_view path) const
{
    return std::filesystem::path(path).extension().string();
}

std::int32_t StandardHostServices::tcp_connect(std::string_view host, std::int32_t port) const
{
    if (host.empty() || port <= 0)
    {
        return 0;
    }

    struct addrinfo hints {};
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;

    struct addrinfo* results = nullptr;
    const auto port_text = std::to_string(port);
    if (getaddrinfo(std::string(host).c_str(), port_text.c_str(), &hints, &results) != 0)
    {
        return 0;
    }

    int connected_socket = -1;
    for (auto* current = results; current != nullptr; current = current->ai_next)
    {
        const auto socket_fd = socket(current->ai_family, current->ai_socktype, current->ai_protocol);
        if (socket_fd < 0)
        {
            continue;
        }

        if (connect(socket_fd, current->ai_addr, current->ai_addrlen) == 0)
        {
            connected_socket = socket_fd;
            break;
        }

        close(socket_fd);
    }

    freeaddrinfo(results);

    if (connected_socket < 0)
    {
        return 0;
    }

    const auto connection_id = next_tcp_connection_id_++;
    tcp_connections_[connection_id] = connected_socket;
    return connection_id;
}

std::string StandardHostServices::tcp_read_line(std::int32_t connection_id) const
{
    const auto connection_it = tcp_connections_.find(connection_id);
    if (connection_it == tcp_connections_.end())
    {
        return std::string();
    }

    std::string line;
    char value = '\0';
    while (true)
    {
        const auto bytes_read = recv(connection_it->second, &value, 1, 0);
        if (bytes_read <= 0)
        {
            break;
        }

        if (value == '\n')
        {
            break;
        }

        if (value != '\r')
        {
            line.push_back(value);
        }
    }

    return line;
}

void StandardHostServices::tcp_write_line(std::int32_t connection_id, std::string_view text) const
{
    const auto connection_it = tcp_connections_.find(connection_id);
    if (connection_it == tcp_connections_.end())
    {
        return;
    }

    auto payload = std::string(text);
    payload.push_back('\n');

    const char* cursor = payload.data();
    std::size_t remaining = payload.size();
    while (remaining > 0)
    {
        const auto written = send(connection_it->second, cursor, remaining, 0);
        if (written <= 0)
        {
            return;
        }

        cursor += written;
        remaining -= static_cast<std::size_t>(written);
    }
}

void StandardHostServices::tcp_close(std::int32_t connection_id) const
{
    const auto connection_it = tcp_connections_.find(connection_id);
    if (connection_it == tcp_connections_.end())
    {
        return;
    }

    close(connection_it->second);
    tcp_connections_.erase(connection_it);
}

std::string StandardHostServices::http_get_string(std::string_view url) const
{
    constexpr std::string_view http_prefix = "http://";
    if (!url.starts_with(http_prefix))
    {
        return std::string();
    }

    auto remaining = std::string(url.substr(http_prefix.size()));
    std::string host;
    std::int32_t port = 80;
    std::string path = "/";

    const auto path_separator = remaining.find('/');
    auto authority = path_separator == std::string::npos ? remaining : remaining.substr(0, path_separator);
    if (path_separator != std::string::npos)
    {
        path = remaining.substr(path_separator);
    }

    const auto port_separator = authority.find(':');
    if (port_separator == std::string::npos)
    {
        host = authority;
    }
    else
    {
        host = authority.substr(0, port_separator);
        const auto port_text = authority.substr(port_separator + 1);
        if (port_text.empty())
        {
            return std::string();
        }

        port = std::atoi(port_text.c_str());
        if (port <= 0)
        {
            return std::string();
        }
    }

    if (host.empty())
    {
        return std::string();
    }

    struct addrinfo hints {};
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;

    struct addrinfo* results = nullptr;
    const auto port_text = std::to_string(port);
    if (getaddrinfo(host.c_str(), port_text.c_str(), &hints, &results) != 0)
    {
        return std::string();
    }

    int socket_fd = -1;
    for (auto* current = results; current != nullptr; current = current->ai_next)
    {
        const auto candidate = socket(current->ai_family, current->ai_socktype, current->ai_protocol);
        if (candidate < 0)
        {
            continue;
        }

        if (connect(candidate, current->ai_addr, current->ai_addrlen) == 0)
        {
            socket_fd = candidate;
            break;
        }

        close(candidate);
    }

    freeaddrinfo(results);

    if (socket_fd < 0)
    {
        return std::string();
    }

    auto request = std::string("GET ") + path + " HTTP/1.1\r\nHost: " + host + "\r\nConnection: close\r\n\r\n";
    const char* cursor = request.data();
    std::size_t remaining_bytes = request.size();
    while (remaining_bytes > 0)
    {
        const auto written = send(socket_fd, cursor, remaining_bytes, 0);
        if (written <= 0)
        {
            close(socket_fd);
            return std::string();
        }

        cursor += written;
        remaining_bytes -= static_cast<std::size_t>(written);
    }

    std::string response;
    char buffer[1024];
    while (true)
    {
        const auto read_count = recv(socket_fd, buffer, sizeof(buffer), 0);
        if (read_count <= 0)
        {
            break;
        }

        response.append(buffer, static_cast<std::size_t>(read_count));
    }

    close(socket_fd);

    const auto header_separator = response.find("\r\n\r\n");
    if (header_separator == std::string::npos)
    {
        return response;
    }

    return response.substr(header_separator + 4);
}

std::int32_t StandardHostServices::websocket_connect(std::string_view url) const
{
    constexpr std::string_view websocket_prefix = "ws://";
    if (!url.starts_with(websocket_prefix))
    {
        return 0;
    }

    auto remaining = std::string(url.substr(websocket_prefix.size()));
    std::string host;
    std::int32_t port = 80;
    std::string path = "/";

    const auto path_separator = remaining.find('/');
    auto authority = path_separator == std::string::npos ? remaining : remaining.substr(0, path_separator);
    if (path_separator != std::string::npos)
    {
        path = remaining.substr(path_separator);
    }

    const auto port_separator = authority.find(':');
    if (port_separator == std::string::npos)
    {
        host = authority;
    }
    else
    {
        host = authority.substr(0, port_separator);
        const auto port_text = authority.substr(port_separator + 1);
        if (port_text.empty())
        {
            return 0;
        }

        port = std::atoi(port_text.c_str());
        if (port <= 0)
        {
            return 0;
        }
    }

    if (host.empty())
    {
        return 0;
    }

    struct addrinfo hints {};
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;

    struct addrinfo* results = nullptr;
    const auto port_text = std::to_string(port);
    if (getaddrinfo(host.c_str(), port_text.c_str(), &hints, &results) != 0)
    {
        return 0;
    }

    int socket_fd = -1;
    for (auto* current = results; current != nullptr; current = current->ai_next)
    {
        const auto candidate = socket(current->ai_family, current->ai_socktype, current->ai_protocol);
        if (candidate < 0)
        {
            continue;
        }

        if (connect(candidate, current->ai_addr, current->ai_addrlen) == 0)
        {
            socket_fd = candidate;
            break;
        }

        close(candidate);
    }

    freeaddrinfo(results);

    if (socket_fd < 0)
    {
        return 0;
    }

    const std::string key = "aWxjLXdpcmUtZGVidWc=";
    auto request = std::string("GET ") + path + " HTTP/1.1\r\n"
        + "Host: " + host + ":" + std::to_string(port) + "\r\n"
        + "Upgrade: websocket\r\n"
        + "Connection: Upgrade\r\n"
        + "Sec-WebSocket-Version: 13\r\n"
        + "Sec-WebSocket-Key: " + key + "\r\n\r\n";

    if (!send_all(socket_fd, std::vector<std::uint8_t>(request.begin(), request.end())))
    {
        close(socket_fd);
        return 0;
    }

    std::string response;
    char buffer[256] {};
    while (response.find("\r\n\r\n") == std::string::npos)
    {
        const auto read_count = recv(socket_fd, buffer, sizeof(buffer), 0);
        if (read_count <= 0)
        {
            close(socket_fd);
            return 0;
        }

        response.append(buffer, static_cast<std::size_t>(read_count));
        if (response.size() > 8192)
        {
            close(socket_fd);
            return 0;
        }
    }

    if (response.find(" 101 ") == std::string::npos &&
        response.find(" 101\r") == std::string::npos)
    {
        close(socket_fd);
        return 0;
    }

    const auto expected_accept = make_websocket_accept_key(key);
    if (response.find(std::string("Sec-WebSocket-Accept: ") + expected_accept) == std::string::npos)
    {
        close(socket_fd);
        return 0;
    }

    const auto connection_id = next_websocket_connection_id_++;
    websocket_connections_[connection_id] = socket_fd;
    return connection_id;
}

std::string StandardHostServices::websocket_receive_text(std::int32_t connection_id) const
{
    const auto connection_it = websocket_connections_.find(connection_id);
    if (connection_it == websocket_connections_.end())
    {
        return std::string();
    }

    std::uint8_t header[2] {};
    if (!recv_all(connection_it->second, header, sizeof(header)))
    {
        return std::string();
    }

    const auto opcode = static_cast<std::uint8_t>(header[0] & 0x0F);
    if (opcode == 0x8)
    {
        return std::string();
    }

    if (opcode != 0x1)
    {
        return std::string();
    }

    auto payload_length = static_cast<std::uint64_t>(header[1] & 0x7F);
    const auto masked = (header[1] & 0x80) != 0;
    if (payload_length == 126)
    {
        std::uint8_t extended[2] {};
        if (!recv_all(connection_it->second, extended, sizeof(extended)))
        {
            return std::string();
        }

        payload_length = (static_cast<std::uint64_t>(extended[0]) << 8) |
            static_cast<std::uint64_t>(extended[1]);
    }
    else if (payload_length == 127)
    {
        return std::string();
    }

    std::uint8_t mask[4] {};
    if (masked && !recv_all(connection_it->second, mask, sizeof(mask)))
    {
        return std::string();
    }

    std::string payload(payload_length, '\0');
    if (payload_length > 0 && !recv_all(connection_it->second, payload.data(), static_cast<std::size_t>(payload_length)))
    {
        return std::string();
    }

    if (masked)
    {
        for (std::size_t index = 0; index < payload.size(); ++index)
        {
            payload[index] = static_cast<char>(static_cast<std::uint8_t>(payload[index]) ^ mask[index % 4]);
        }
    }

    return payload;
}

void StandardHostServices::websocket_send_text(std::int32_t connection_id, std::string_view text) const
{
    const auto connection_it = websocket_connections_.find(connection_id);
    if (connection_it == websocket_connections_.end())
    {
        return;
    }

    std::vector<std::uint8_t> frame;
    frame.push_back(0x81);
    if (text.size() < 126)
    {
        frame.push_back(static_cast<std::uint8_t>(0x80 | text.size()));
    }
    else
    {
        frame.push_back(0x80 | 126);
        frame.push_back(static_cast<std::uint8_t>((text.size() >> 8) & 0xFF));
        frame.push_back(static_cast<std::uint8_t>(text.size() & 0xFF));
    }

    constexpr std::uint8_t mask[4] { 0x49, 0x4C, 0x43, 0x21 };
    frame.insert(frame.end(), std::begin(mask), std::end(mask));
    for (std::size_t index = 0; index < text.size(); ++index)
    {
        frame.push_back(static_cast<std::uint8_t>(static_cast<unsigned char>(text[index])) ^ mask[index % 4]);
    }

    (void)send_all(connection_it->second, frame);
}

void StandardHostServices::websocket_close(std::int32_t connection_id) const
{
    const auto connection_it = websocket_connections_.find(connection_id);
    if (connection_it == websocket_connections_.end())
    {
        return;
    }

    const std::vector<std::uint8_t> close_frame { 0x88, 0x80, 0x49, 0x4C, 0x43, 0x21 };
    (void)send_all(connection_it->second, close_frame);
    close(connection_it->second);
    websocket_connections_.erase(connection_it);
}

void StandardHostServices::thread_sleep(std::int32_t milliseconds) const
{
    if (milliseconds <= 0)
    {
        return;
    }

    std::this_thread::sleep_for(std::chrono::milliseconds(milliseconds));
}

std::int32_t StandardHostServices::thread_get_current_managed_id() const
{
    const auto raw_id = std::hash<std::thread::id> {}(std::this_thread::get_id());
    return static_cast<std::int32_t>((raw_id % static_cast<std::size_t>(std::numeric_limits<std::int32_t>::max() - 1)) + 1);
}

std::int32_t StandardHostServices::mutex_create() const
{
    const auto mutex_id = next_mutex_id_++;
    mutexes_[mutex_id] = std::make_unique<HostMutex>();
    return mutex_id;
}

bool StandardHostServices::mutex_wait_one(std::int32_t mutex_id) const
{
    const auto mutex_it = mutexes_.find(mutex_id);
    if (mutex_it == mutexes_.end() || mutex_it->second == nullptr)
    {
        return false;
    }

    mutex_it->second->native_mutex.lock();
    mutex_it->second->is_locked = true;
    mutex_it->second->owner_thread = std::this_thread::get_id();
    return true;
}

void StandardHostServices::mutex_release(std::int32_t mutex_id) const
{
    const auto mutex_it = mutexes_.find(mutex_id);
    if (mutex_it == mutexes_.end() || mutex_it->second == nullptr)
    {
        return;
    }

    if (!mutex_it->second->is_locked || mutex_it->second->owner_thread != std::this_thread::get_id())
    {
        return;
    }

    mutex_it->second->is_locked = false;
    mutex_it->second->owner_thread = std::thread::id();
    mutex_it->second->native_mutex.unlock();
}

void StandardHostServices::mutex_close(std::int32_t mutex_id) const
{
    const auto mutex_it = mutexes_.find(mutex_id);
    if (mutex_it == mutexes_.end() || mutex_it->second == nullptr)
    {
        return;
    }

    if (mutex_it->second->is_locked && mutex_it->second->owner_thread == std::this_thread::get_id())
    {
        mutex_it->second->is_locked = false;
        mutex_it->second->owner_thread = std::thread::id();
        mutex_it->second->native_mutex.unlock();
    }

    mutexes_.erase(mutex_it);
}

std::vector<std::string> StandardHostServices::get_command_line_args() const
{
    return command_line_args_;
}
} // namespace ilcvm
