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
    [[nodiscard]] virtual std::vector<std::string> get_command_line_args() const = 0;
};
} // namespace ilcvm
