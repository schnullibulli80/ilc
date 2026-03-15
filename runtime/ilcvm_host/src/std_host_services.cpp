#include "ilcvm/std_host_services.h"

#include <chrono>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
#include <unistd.h>

namespace ilcvm
{
StandardHostServices::StandardHostServices(std::vector<std::string> command_line_args)
    : command_line_args_(std::move(command_line_args))
{
}

void StandardHostServices::console_write(std::string_view text) const
{
    std::cout << text;
}

void StandardHostServices::console_write_line(std::string_view text) const
{
    std::cout << text << '\n';
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

std::vector<std::string> StandardHostServices::get_command_line_args() const
{
    return command_line_args_;
}
} // namespace ilcvm
