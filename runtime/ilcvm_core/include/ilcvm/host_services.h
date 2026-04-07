#pragma once

#include <string>
#include <string_view>
#include <cstdint>
#include <vector>

namespace ilcvm
{
class IHostServices
{
public:
    virtual ~IHostServices() = default;

    virtual void console_write(std::string_view text) const = 0;
    virtual void console_write_line(std::string_view text) const = 0;
    [[nodiscard]] virtual std::string console_read_line() const = 0;
    [[nodiscard]] virtual std::uint64_t get_monotonic_timestamp_ms() const = 0;
    [[nodiscard]] virtual std::uint64_t get_wall_timestamp_ms() const = 0;
    [[nodiscard]] virtual std::string get_wall_datetime_text() const = 0;
    [[nodiscard]] virtual std::string get_current_working_directory() const = 0;
    [[nodiscard]] virtual std::string get_user_name() const = 0;
    [[nodiscard]] virtual std::string get_machine_name() const = 0;
    [[nodiscard]] virtual std::string get_home_directory() const = 0;
    [[nodiscard]] virtual std::string get_temp_directory() const = 0;
    [[nodiscard]] virtual std::string get_environment_variable(std::string_view name) const = 0;
    virtual void set_environment_variable(std::string_view name, std::string_view value) const = 0;
    [[nodiscard]] virtual bool file_exists(std::string_view path) const = 0;
    [[nodiscard]] virtual std::string file_read_all_text(std::string_view path) const = 0;
    virtual void file_write_all_text(std::string_view path, std::string_view text) const = 0;
    virtual void file_append_all_text(std::string_view path, std::string_view text) const = 0;
    [[nodiscard]] virtual std::string path_combine(std::string_view left, std::string_view right) const = 0;
    [[nodiscard]] virtual std::string path_get_file_name(std::string_view path) const = 0;
    [[nodiscard]] virtual std::string path_get_directory_name(std::string_view path) const = 0;
    [[nodiscard]] virtual std::string path_get_extension(std::string_view path) const = 0;
    [[nodiscard]] virtual std::int32_t tcp_connect(std::string_view host, std::int32_t port) const = 0;
    [[nodiscard]] virtual std::string tcp_read_line(std::int32_t connection_id) const = 0;
    virtual void tcp_write_line(std::int32_t connection_id, std::string_view text) const = 0;
    virtual void tcp_close(std::int32_t connection_id) const = 0;
    [[nodiscard]] virtual std::string http_get_string(std::string_view url) const = 0;
    virtual void thread_sleep(std::int32_t milliseconds) const = 0;
    [[nodiscard]] virtual std::int32_t thread_get_current_managed_id() const = 0;
    [[nodiscard]] virtual std::int32_t mutex_create() const = 0;
    [[nodiscard]] virtual bool mutex_wait_one(std::int32_t mutex_id) const = 0;
    virtual void mutex_release(std::int32_t mutex_id) const = 0;
    virtual void mutex_close(std::int32_t mutex_id) const = 0;
    [[nodiscard]] virtual std::vector<std::string> get_command_line_args() const = 0;
};
} // namespace ilcvm
