#pragma once

#include "ilcvm/host_services.h"

#include <vector>

namespace ilcvm
{
class StandardHostServices final : public IHostServices
{
public:
    explicit StandardHostServices(std::vector<std::string> command_line_args = {});

    void console_write(std::string_view text) const override;
    void console_write_line(std::string_view text) const override;
    [[nodiscard]] std::string console_read_line() const override;
    [[nodiscard]] std::uint64_t get_monotonic_timestamp_ms() const override;
    [[nodiscard]] std::uint64_t get_wall_timestamp_ms() const override;
    [[nodiscard]] std::string get_wall_datetime_text() const override;
    [[nodiscard]] std::string get_current_working_directory() const override;
    [[nodiscard]] std::string get_user_name() const override;
    [[nodiscard]] std::string get_machine_name() const override;
    [[nodiscard]] std::string get_home_directory() const override;
    [[nodiscard]] std::string get_temp_directory() const override;
    [[nodiscard]] std::string get_environment_variable(std::string_view name) const override;
    void set_environment_variable(std::string_view name, std::string_view value) const override;
    [[nodiscard]] bool file_exists(std::string_view path) const override;
    [[nodiscard]] std::string file_read_all_text(std::string_view path) const override;
    void file_write_all_text(std::string_view path, std::string_view text) const override;
    void file_append_all_text(std::string_view path, std::string_view text) const override;
    [[nodiscard]] std::string path_combine(std::string_view left, std::string_view right) const override;
    [[nodiscard]] std::string path_get_file_name(std::string_view path) const override;
    [[nodiscard]] std::string path_get_directory_name(std::string_view path) const override;
    [[nodiscard]] std::string path_get_extension(std::string_view path) const override;
    [[nodiscard]] std::vector<std::string> get_command_line_args() const override;

private:
    std::vector<std::string> command_line_args_;
};
} // namespace ilcvm
