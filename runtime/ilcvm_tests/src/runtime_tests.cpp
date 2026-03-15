#include "ilcvm/heap.h"
#include "ilcvm/host_services.h"
#include "ilcvm/module.h"
#include "ilcvm/std_host_services.h"
#include "ilcvm/virtual_machine.h"

#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <stdexcept>
#include <vector>

namespace
{
class TestHostServices final : public ilcvm::IHostServices
{
public:
    mutable std::string output;
    mutable std::string input_line;
    mutable std::vector<std::string> command_line_args;
    mutable std::string environment_variable_value = "/home/test";

    void console_write(std::string_view text) const override
    {
        output.append(text);
    }

    void console_write_line(std::string_view text) const override
    {
        output.append(text);
        output.push_back('\n');
    }

    [[nodiscard]] std::string console_read_line() const override
    {
        return input_line;
    }

    [[nodiscard]] std::uint64_t get_monotonic_timestamp_ms() const override
    {
        return 1;
    }

    [[nodiscard]] std::uint64_t get_wall_timestamp_ms() const override
    {
        return 2;
    }

    [[nodiscard]] std::string get_current_working_directory() const override
    {
        return "/test";
    }

    [[nodiscard]] std::string get_user_name() const override
    {
        return "tester";
    }

    [[nodiscard]] std::string get_machine_name() const override
    {
        return "devbox";
    }

    [[nodiscard]] std::string get_home_directory() const override
    {
        return "/home/test";
    }

    [[nodiscard]] std::string get_temp_directory() const override
    {
        return "/tmp";
    }

    [[nodiscard]] std::string get_environment_variable(std::string_view name) const override
    {
        return name == "HOME" ? environment_variable_value : std::string();
    }

    void set_environment_variable(std::string_view name, std::string_view value) const override
    {
        if (name == "HOME")
        {
            environment_variable_value.assign(value);
        }
    }

    [[nodiscard]] bool file_exists(std::string_view path) const override
    {
        return path == "/test/input.txt";
    }

    [[nodiscard]] std::string file_read_all_text(std::string_view path) const override
    {
        return path == "/test/input.txt" ? "demo-text" : std::string();
    }

    void file_write_all_text(std::string_view path, std::string_view text) const override
    {
        if (path == "/test/output.txt")
        {
            output.assign(text);
        }
    }

    void file_append_all_text(std::string_view path, std::string_view text) const override
    {
        if (path == "/test/output.txt")
        {
            output.append(text);
        }
    }

    [[nodiscard]] std::string path_combine(std::string_view left, std::string_view right) const override
    {
        return std::string(left) + "/" + std::string(right);
    }

    [[nodiscard]] std::string path_get_file_name(std::string_view path) const override
    {
        const auto separator = path.find_last_of('/');
        if (separator == std::string_view::npos)
        {
            return std::string(path);
        }

        return std::string(path.substr(separator + 1));
    }

    [[nodiscard]] std::string path_get_directory_name(std::string_view path) const override
    {
        const auto separator = path.find_last_of('/');
        if (separator == std::string_view::npos)
        {
            return std::string();
        }

        return std::string(path.substr(0, separator));
    }

    [[nodiscard]] std::string path_get_extension(std::string_view path) const override
    {
        const auto separator = path.find_last_of('.');
        if (separator == std::string_view::npos)
        {
            return std::string();
        }

        return std::string(path.substr(separator));
    }

    [[nodiscard]] std::vector<std::string> get_command_line_args() const override
    {
        return command_line_args;
    }
};

void write_u16(std::vector<std::uint8_t>& bytes, std::uint16_t value)
{
    bytes.push_back(static_cast<std::uint8_t>(value & 0xFF));
    bytes.push_back(static_cast<std::uint8_t>((value >> 8) & 0xFF));
}

void write_u32(std::vector<std::uint8_t>& bytes, std::uint32_t value)
{
    bytes.push_back(static_cast<std::uint8_t>(value & 0xFF));
    bytes.push_back(static_cast<std::uint8_t>((value >> 8) & 0xFF));
    bytes.push_back(static_cast<std::uint8_t>((value >> 16) & 0xFF));
    bytes.push_back(static_cast<std::uint8_t>((value >> 24) & 0xFF));
}

void emit_instruction(
    std::vector<std::uint8_t>& bytes,
    ilcvm::OpCode opcode,
    std::uint16_t destination,
    std::uint16_t left,
    std::uint16_t right,
    std::int32_t immediate)
{
    bytes.push_back(static_cast<std::uint8_t>(opcode));
    write_u16(bytes, destination);
    write_u16(bytes, left);
    write_u16(bytes, right);
    write_u32(bytes, static_cast<std::uint32_t>(immediate));
}

void emit_method_row(
    std::vector<std::uint8_t>& bytes,
    std::uint32_t name_string_id,
    std::uint32_t flags,
    std::uint16_t register_count,
    std::uint16_t parameter_count,
    std::uint16_t local_count,
    std::uint32_t return_type_id,
    std::uint32_t code_offset,
    std::uint32_t code_size)
{
    write_u32(bytes, 0);
    write_u32(bytes, name_string_id);
    write_u32(bytes, 0);
    write_u32(bytes, flags);
    write_u16(bytes, register_count);
    write_u16(bytes, parameter_count);
    write_u16(bytes, local_count);
    write_u32(bytes, return_type_id);
    write_u32(bytes, code_offset);
    write_u32(bytes, code_size);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
}

void emit_field_row(
    std::vector<std::uint8_t>& bytes,
    std::uint32_t owner_type_id,
    std::uint32_t name_string_id,
    std::uint16_t flags)
{
    write_u32(bytes, owner_type_id);
    write_u32(bytes, name_string_id);
    write_u32(bytes, 0);
    write_u16(bytes, flags);
    write_u16(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
}

void emit_type_row(
    std::vector<std::uint8_t>& bytes,
    std::uint32_t simple_name_string_id,
    std::uint32_t first_field_id,
    std::uint32_t field_count,
    std::uint32_t first_method_id,
    std::uint32_t method_count)
{
    write_u32(bytes, 0);
    write_u32(bytes, simple_name_string_id);
    write_u16(bytes, 1);
    write_u16(bytes, 1u << 6);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, first_field_id);
    write_u32(bytes, field_count);
    write_u32(bytes, first_method_id);
    write_u32(bytes, method_count);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
    write_u32(bytes, 0);
}

std::vector<std::uint8_t> make_test_ilb()
{
    constexpr std::uint32_t header_size = 64;
    constexpr std::uint32_t directory_entry_size = 24;
    constexpr std::uint32_t section_count = 5;
    const std::uint32_t directory_offset = header_size;

    std::vector<std::uint8_t> strings;
    write_u32(strings, 4);
    write_u32(strings, 11);
    strings.insert(strings.end(), { 'A', 'c', 'c', 'u', 'm', 'u', 'l', 'a', 't', 'o', 'r' });
    write_u32(strings, 3);
    strings.insert(strings.end(), { 'A', 'd', 'd' });
    write_u32(strings, 5);
    strings.insert(strings.end(), { 'S', 'c', 'a', 'l', 'e' });
    write_u32(strings, 4);
    strings.insert(strings.end(), { 'M', 'a', 'i', 'n' });

    std::vector<std::uint8_t> fields;
    emit_field_row(fields, 0, 1, 1u << 4);

    std::vector<std::uint8_t> code;
    emit_instruction(code, ilcvm::OpCode::add_i32, 2, 0, 1, 0);
    emit_instruction(code, ilcvm::OpCode::ret, 0, 0, 0, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 2, 0, 1, 0);
    emit_instruction(code, ilcvm::OpCode::ret, 0, 0, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 1, 0, 0, 4);
    emit_instruction(code, ilcvm::OpCode::new_arr, 2, 1, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 3, 0, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 4, 0, 0, 7);
    emit_instruction(code, ilcvm::OpCode::st_elem, 2, 3, 4, 0);
    emit_instruction(code, ilcvm::OpCode::ld_elem, 5, 2, 3, 0);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 6, 0, 0, 2);
    emit_instruction(code, ilcvm::OpCode::call, 0, 5, 2, 1);
    emit_instruction(code, ilcvm::OpCode::st_sfield, 0, 0, 0, 1);
    emit_instruction(code, ilcvm::OpCode::ld_sfield, 7, 0, 0, 1);
    emit_instruction(code, ilcvm::OpCode::ld_len, 8, 2, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 9, 0, 0, 10);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 10, 0, 0, 3);
    emit_instruction(code, ilcvm::OpCode::call_virt, 11, 9, 1, 2);
    emit_instruction(code, ilcvm::OpCode::add_i32, 12, 7, 8, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 0, 12, 11, 0);
    emit_instruction(code, ilcvm::OpCode::ret, 0, 0, 0, 0);

    std::vector<std::uint8_t> methods;
    emit_method_row(methods, 2, 1u << 4, 3, 2, 0, 1, 0, 22);
    emit_method_row(methods, 3, 0, 3, 1, 0, 1, 22, 22);
    emit_method_row(methods, 4, (1u << 12) | (1u << 4), 13, 0, 12, 1, 44, 187);

    std::vector<std::uint8_t> entry;
    write_u32(entry, 3);
    write_u32(entry, 0);

    const auto align = [](std::uint32_t value) { return (value + 7u) / 8u * 8u; };
    auto payload_offset = align(header_size + section_count * directory_entry_size);
    const auto string_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(strings.size()));
    const auto field_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(fields.size()));
    const auto method_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(methods.size()));
    const auto code_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(code.size()));
    const auto entry_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(entry.size()));

    std::vector<std::uint8_t> bytes(payload_offset, 0);
    bytes[0] = 'I';
    bytes[1] = 'L';
    bytes[2] = 'B';
    bytes[3] = '1';
    bytes[4] = 1;
    bytes[8] = static_cast<std::uint8_t>(header_size);
    bytes[12] = static_cast<std::uint8_t>(section_count);
    bytes[16] = static_cast<std::uint8_t>(directory_offset);
    bytes[20] = static_cast<std::uint8_t>(payload_offset & 0xFF);
    bytes[21] = static_cast<std::uint8_t>((payload_offset >> 8) & 0xFF);

    auto write_directory = [&bytes](std::size_t offset, ilcvm::SectionKind kind, std::uint32_t section_offset, std::uint32_t size, std::uint32_t count)
    {
        bytes[offset + 0] = static_cast<std::uint8_t>(static_cast<std::uint32_t>(kind) & 0xFF);
        bytes[offset + 4] = static_cast<std::uint8_t>(section_offset & 0xFF);
        bytes[offset + 5] = static_cast<std::uint8_t>((section_offset >> 8) & 0xFF);
        bytes[offset + 8] = static_cast<std::uint8_t>(size & 0xFF);
        bytes[offset + 9] = static_cast<std::uint8_t>((size >> 8) & 0xFF);
        bytes[offset + 12] = static_cast<std::uint8_t>(count & 0xFF);
        bytes[offset + 16] = 8;
    };

    write_directory(directory_offset + 0 * directory_entry_size, ilcvm::SectionKind::string_table, string_offset, static_cast<std::uint32_t>(strings.size()), 4);
    write_directory(directory_offset + 1 * directory_entry_size, ilcvm::SectionKind::field_table, field_offset, static_cast<std::uint32_t>(fields.size()), 1);
    write_directory(directory_offset + 2 * directory_entry_size, ilcvm::SectionKind::method_table, method_offset, static_cast<std::uint32_t>(methods.size()), 3);
    write_directory(directory_offset + 3 * directory_entry_size, ilcvm::SectionKind::code_section, code_offset, static_cast<std::uint32_t>(code.size()), 3);
    write_directory(directory_offset + 4 * directory_entry_size, ilcvm::SectionKind::entry_point, entry_offset, static_cast<std::uint32_t>(entry.size()), 1);

    std::copy(strings.begin(), strings.end(), bytes.begin() + string_offset);
    std::copy(fields.begin(), fields.end(), bytes.begin() + field_offset);
    std::copy(methods.begin(), methods.end(), bytes.begin() + method_offset);
    std::copy(code.begin(), code.end(), bytes.begin() + code_offset);
    std::copy(entry.begin(), entry.end(), bytes.begin() + entry_offset);
    return bytes;
}

std::vector<std::uint8_t> make_object_test_ilb()
{
    constexpr std::uint32_t header_size = 64;
    constexpr std::uint32_t directory_entry_size = 24;
    constexpr std::uint32_t section_count = 6;
    const std::uint32_t directory_offset = header_size;

    std::vector<std::uint8_t> strings;
    write_u32(strings, 6);
    write_u32(strings, 7);
    strings.insert(strings.end(), { 'P', 'r', 'o', 'g', 'r', 'a', 'm' });
    write_u32(strings, 7);
    strings.insert(strings.end(), { 'C', 'o', 'u', 'n', 't', 'e', 'r' });
    write_u32(strings, 5);
    strings.insert(strings.end(), { '.', 'c', 't', 'o', 'r' });
    write_u32(strings, 9);
    strings.insert(strings.end(), { 'I', 'n', 'c', 'r', 'e', 'm', 'e', 'n', 't' });
    write_u32(strings, 7);
    strings.insert(strings.end(), { 'C', 'u', 'r', 'r', 'e', 'n', 't' });
    write_u32(strings, 4);
    strings.insert(strings.end(), { 'M', 'a', 'i', 'n' });

    std::vector<std::uint8_t> types;
    emit_type_row(types, 1, 1, 1, 1, 4);

    std::vector<std::uint8_t> fields;
    emit_field_row(fields, 1, 2, 0);

    std::vector<std::uint8_t> code;
    emit_instruction(code, ilcvm::OpCode::st_field, 0, 1, 0, 1);
    emit_instruction(code, ilcvm::OpCode::ret, 0, 0, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_field, 2, 0, 0, 1);
    emit_instruction(code, ilcvm::OpCode::add_i32, 3, 2, 1, 0);
    emit_instruction(code, ilcvm::OpCode::st_field, 0, 3, 0, 1);
    emit_instruction(code, ilcvm::OpCode::ret, 0, 0, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_field, 1, 0, 0, 1);
    emit_instruction(code, ilcvm::OpCode::ret, 0, 0, 0, 0);
    emit_instruction(code, ilcvm::OpCode::new_obj, 1, 0, 0, 1);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 2, 0, 0, 5);
    emit_instruction(code, ilcvm::OpCode::call_virt, 1, 1, 1, 1);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 2, 0, 0, 3);
    emit_instruction(code, ilcvm::OpCode::call_virt, 1, 1, 1, 2);
    emit_instruction(code, ilcvm::OpCode::call_virt, 0, 1, 0, 3);
    emit_instruction(code, ilcvm::OpCode::ret, 0, 0, 0, 0);

    std::vector<std::uint8_t> methods;
    emit_method_row(methods, 3, 0, 2, 1, 0, 0, 0, 22);
    emit_method_row(methods, 4, 0, 4, 1, 1, 0, 22, 44);
    emit_method_row(methods, 5, 0, 2, 0, 0, 1, 66, 22);
    emit_method_row(methods, 6, (1u << 12) | (1u << 4), 3, 0, 2, 1, 88, 77);

    std::vector<std::uint8_t> entry;
    write_u32(entry, 4);
    write_u32(entry, 0);

    const auto align = [](std::uint32_t value) { return (value + 7u) / 8u * 8u; };
    auto payload_offset = align(header_size + section_count * directory_entry_size);
    const auto string_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(strings.size()));
    const auto type_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(types.size()));
    const auto field_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(fields.size()));
    const auto method_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(methods.size()));
    const auto code_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(code.size()));
    const auto entry_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(entry.size()));

    std::vector<std::uint8_t> bytes(payload_offset, 0);
    bytes[0] = 'I';
    bytes[1] = 'L';
    bytes[2] = 'B';
    bytes[3] = '1';
    bytes[4] = 1;
    bytes[8] = static_cast<std::uint8_t>(header_size);
    bytes[12] = static_cast<std::uint8_t>(section_count);
    bytes[16] = static_cast<std::uint8_t>(directory_offset);
    bytes[20] = static_cast<std::uint8_t>(payload_offset & 0xFF);
    bytes[21] = static_cast<std::uint8_t>((payload_offset >> 8) & 0xFF);

    auto write_directory = [&bytes](std::size_t offset, ilcvm::SectionKind kind, std::uint32_t section_offset, std::uint32_t size, std::uint32_t count)
    {
        bytes[offset + 0] = static_cast<std::uint8_t>(static_cast<std::uint32_t>(kind) & 0xFF);
        bytes[offset + 4] = static_cast<std::uint8_t>(section_offset & 0xFF);
        bytes[offset + 5] = static_cast<std::uint8_t>((section_offset >> 8) & 0xFF);
        bytes[offset + 8] = static_cast<std::uint8_t>(size & 0xFF);
        bytes[offset + 9] = static_cast<std::uint8_t>((size >> 8) & 0xFF);
        bytes[offset + 12] = static_cast<std::uint8_t>(count & 0xFF);
        bytes[offset + 16] = 8;
    };

    write_directory(directory_offset + 0 * directory_entry_size, ilcvm::SectionKind::string_table, string_offset, static_cast<std::uint32_t>(strings.size()), 6);
    write_directory(directory_offset + 1 * directory_entry_size, ilcvm::SectionKind::type_table, type_offset, static_cast<std::uint32_t>(types.size()), 1);
    write_directory(directory_offset + 2 * directory_entry_size, ilcvm::SectionKind::field_table, field_offset, static_cast<std::uint32_t>(fields.size()), 1);
    write_directory(directory_offset + 3 * directory_entry_size, ilcvm::SectionKind::method_table, method_offset, static_cast<std::uint32_t>(methods.size()), 4);
    write_directory(directory_offset + 4 * directory_entry_size, ilcvm::SectionKind::code_section, code_offset, static_cast<std::uint32_t>(code.size()), 4);
    write_directory(directory_offset + 5 * directory_entry_size, ilcvm::SectionKind::entry_point, entry_offset, static_cast<std::uint32_t>(entry.size()), 1);

    std::copy(strings.begin(), strings.end(), bytes.begin() + string_offset);
    std::copy(types.begin(), types.end(), bytes.begin() + type_offset);
    std::copy(fields.begin(), fields.end(), bytes.begin() + field_offset);
    std::copy(methods.begin(), methods.end(), bytes.begin() + method_offset);
    std::copy(code.begin(), code.end(), bytes.begin() + code_offset);
    std::copy(entry.begin(), entry.end(), bytes.begin() + entry_offset);
    return bytes;
}

std::vector<std::uint8_t> make_string_test_ilb()
{
    constexpr std::uint32_t header_size = 64;
    constexpr std::uint32_t directory_entry_size = 24;
    constexpr std::uint32_t section_count = 4;
    const std::uint32_t directory_offset = header_size;

    std::vector<std::uint8_t> strings;
    write_u32(strings, 9);
    write_u32(strings, 4);
    strings.insert(strings.end(), { 'M', 'a', 'i', 'n' });
    write_u32(strings, 4);
    strings.insert(strings.end(), { 'w', 'x', 'y', 'z' });
    write_u32(strings, 4);
    strings.insert(strings.end(), { 'w', 'x', 'y', 'z' });
    write_u32(strings, 4);
    strings.insert(strings.end(), { 'a', 'b', 'c', 'd' });
    write_u32(strings, 2);
    strings.insert(strings.end(), { 'x', 'y' });
    write_u32(strings, 1);
    strings.insert(strings.end(), { 'z' });
    write_u32(strings, 3);
    strings.insert(strings.end(), { 'x', 'y', 'z' });
    write_u32(strings, 1);
    strings.insert(strings.end(), { 'x' });
    write_u32(strings, 1);
    strings.insert(strings.end(), { 'y' });

    std::vector<std::uint8_t> code;
    emit_instruction(code, ilcvm::OpCode::ld_str, 1, 0, 0, 2);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 2, 0, 0, 1);
    emit_instruction(code, ilcvm::OpCode::ld_elem, 3, 1, 2, 0);
    emit_instruction(code, ilcvm::OpCode::ld_len, 4, 1, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 5, 0, 0, 1);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 6, 0, 0, 2);
    emit_instruction(code, ilcvm::OpCode::slice_str, 7, 1, 5, 6);
    emit_instruction(code, ilcvm::OpCode::ld_str, 8, 0, 0, 3);
    emit_instruction(code, ilcvm::OpCode::cmp_eq_str, 9, 1, 8, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 10, 0, 0, 4);
    emit_instruction(code, ilcvm::OpCode::cmp_ne_str, 11, 1, 10, 0);
    emit_instruction(code, ilcvm::OpCode::ld_len, 12, 7, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 13, 0, 0, 5);
    emit_instruction(code, ilcvm::OpCode::cmp_eq_str, 14, 7, 13, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 15, 3, 4, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 16, 15, 9, 0);
    emit_instruction(code, ilcvm::OpCode::ld_i32, 17, 0, 0, 0);
    emit_instruction(code, ilcvm::OpCode::cmp_eq_str, 18, 17, 17, 0);
    emit_instruction(code, ilcvm::OpCode::cmp_ne_str, 19, 1, 17, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 20, 16, 11, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 21, 20, 18, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 22, 21, 19, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 24, 0, 0, 6);
    emit_instruction(code, ilcvm::OpCode::concat_str, 25, 7, 24, 0);
    emit_instruction(code, ilcvm::OpCode::ld_len, 26, 25, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 27, 0, 0, 7);
    emit_instruction(code, ilcvm::OpCode::cmp_eq_str, 28, 25, 27, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 29, 0, 0, 8);
    emit_instruction(code, ilcvm::OpCode::starts_with_str, 30, 25, 29, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 31, 0, 0, 6);
    emit_instruction(code, ilcvm::OpCode::ends_with_str, 32, 25, 31, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 33, 0, 0, 5);
    emit_instruction(code, ilcvm::OpCode::contains_str, 34, 25, 33, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 41, 0, 0, 9);
    emit_instruction(code, ilcvm::OpCode::index_of_str, 42, 25, 41, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 43, 0, 0, 6);
    emit_instruction(code, ilcvm::OpCode::last_index_of_str, 44, 25, 43, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 35, 22, 12, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 36, 35, 28, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 37, 36, 30, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 38, 37, 32, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 39, 38, 34, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 40, 39, 26, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 45, 40, 28, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 46, 45, 42, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 47, 46, 44, 0);
    emit_instruction(code, ilcvm::OpCode::ld_str, 48, 0, 0, 6);
    emit_instruction(code, ilcvm::OpCode::ld_str, 49, 0, 0, 8);
    emit_instruction(code, ilcvm::OpCode::replace_str, 50, 25, 48, 49);
    emit_instruction(code, ilcvm::OpCode::ld_len, 51, 50, 0, 0);
    emit_instruction(code, ilcvm::OpCode::ends_with_str, 52, 50, 49, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 53, 47, 51, 0);
    emit_instruction(code, ilcvm::OpCode::add_i32, 0, 53, 52, 0);
    emit_instruction(code, ilcvm::OpCode::ret, 0, 0, 0, 0);

    std::vector<std::uint8_t> methods;
    emit_method_row(methods, 1, (1u << 12) | (1u << 4), 54, 0, 53, 1, 0, 594);

    std::vector<std::uint8_t> entry;
    write_u32(entry, 1);
    write_u32(entry, 0);

    const auto align = [](std::uint32_t value) { return (value + 7u) / 8u * 8u; };
    auto payload_offset = align(header_size + section_count * directory_entry_size);
    const auto string_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(strings.size()));
    const auto method_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(methods.size()));
    const auto code_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(code.size()));
    const auto entry_offset = payload_offset;
    payload_offset = align(payload_offset + static_cast<std::uint32_t>(entry.size()));

    std::vector<std::uint8_t> bytes(payload_offset, 0);
    bytes[0] = 'I';
    bytes[1] = 'L';
    bytes[2] = 'B';
    bytes[3] = '1';
    bytes[4] = 1;
    bytes[8] = static_cast<std::uint8_t>(header_size);
    bytes[12] = static_cast<std::uint8_t>(section_count);
    bytes[16] = static_cast<std::uint8_t>(directory_offset);
    bytes[20] = static_cast<std::uint8_t>(payload_offset & 0xFF);
    bytes[21] = static_cast<std::uint8_t>((payload_offset >> 8) & 0xFF);

    auto write_directory = [&bytes](std::size_t offset, ilcvm::SectionKind kind, std::uint32_t section_offset, std::uint32_t size, std::uint32_t count)
    {
        bytes[offset + 0] = static_cast<std::uint8_t>(static_cast<std::uint32_t>(kind) & 0xFF);
        bytes[offset + 4] = static_cast<std::uint8_t>(section_offset & 0xFF);
        bytes[offset + 5] = static_cast<std::uint8_t>((section_offset >> 8) & 0xFF);
        bytes[offset + 8] = static_cast<std::uint8_t>(size & 0xFF);
        bytes[offset + 9] = static_cast<std::uint8_t>((size >> 8) & 0xFF);
        bytes[offset + 12] = static_cast<std::uint8_t>(count & 0xFF);
        bytes[offset + 16] = 8;
    };

    write_directory(directory_offset + 0 * directory_entry_size, ilcvm::SectionKind::string_table, string_offset, static_cast<std::uint32_t>(strings.size()), 9);
    write_directory(directory_offset + 1 * directory_entry_size, ilcvm::SectionKind::method_table, method_offset, static_cast<std::uint32_t>(methods.size()), 1);
    write_directory(directory_offset + 2 * directory_entry_size, ilcvm::SectionKind::code_section, code_offset, static_cast<std::uint32_t>(code.size()), 1);
    write_directory(directory_offset + 3 * directory_entry_size, ilcvm::SectionKind::entry_point, entry_offset, static_cast<std::uint32_t>(entry.size()), 1);

    std::copy(strings.begin(), strings.end(), bytes.begin() + string_offset);
    std::copy(methods.begin(), methods.end(), bytes.begin() + method_offset);
    std::copy(code.begin(), code.end(), bytes.begin() + code_offset);
    std::copy(entry.begin(), entry.end(), bytes.begin() + entry_offset);
    return bytes;
}
} // namespace

int main()
{
    ilcvm::Heap heap;
    heap.record_allocation(sizeof(ilcvm::ObjectHeader));

    if (heap.allocated_bytes() != sizeof(ilcvm::ObjectHeader))
    {
        std::cerr << "FAIL: heap accounting mismatch\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module module {
        .fields = {
            ilcvm::Field { .field_id = 1, .name = "Accumulator", .is_static = true }
        },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Add",
                .register_count = 3,
                .argument_count = 2,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::add_i32, 2, 0, 1, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "Scale",
                .register_count = 3,
                .argument_count = 2,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::add_i32, 2, 0, 1, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 3,
                .name = "Main",
                .register_count = 13,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 4 },
                    { ilcvm::OpCode::new_arr, 2, 1, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 3, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 4, 0, 0, 7 },
                    { ilcvm::OpCode::st_elem, 2, 3, 4, 0 },
                    { ilcvm::OpCode::ld_elem, 5, 2, 3, 0 },
                    { ilcvm::OpCode::ld_i32, 6, 0, 0, 2 },
                    { ilcvm::OpCode::call, 0, 5, 2, 1 },
                    { ilcvm::OpCode::st_sfield, 0, 0, 0, 1 },
                    { ilcvm::OpCode::ld_sfield, 7, 0, 0, 1 },
                    { ilcvm::OpCode::ld_len, 8, 2, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 9, 0, 0, 10 },
                    { ilcvm::OpCode::ld_i32, 10, 0, 0, 3 },
                    { ilcvm::OpCode::call_virt, 11, 9, 1, 2 },
                    { ilcvm::OpCode::add_i32, 12, 7, 8, 0 },
                    { ilcvm::OpCode::add_i32, 0, 12, 11, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 3
    };

    TestHostServices host_services;
    ilcvm::VirtualMachine vm(heap, host_services);
    const auto result = vm.execute(module);
    if (result != 26)
    {
        std::cerr << "FAIL: vm returned " << result << '\n';
        return EXIT_FAILURE;
    }

    const auto ilb_module = ilcvm::load_module_from_ilb_bytes(make_test_ilb());
    if (ilb_module.fields.size() != 1 || ilb_module.functions.size() != 3 || ilb_module.entry_function_id != 3)
    {
        std::cerr << "FAIL: ilb loader did not recover module metadata\n";
        return EXIT_FAILURE;
    }

    const auto ilb_result = vm.execute(ilb_module);
    if (ilb_result != 26)
    {
        std::cerr << "FAIL: vm returned " << ilb_result << " for ilb-loaded module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module object_module {
        .types = {
            ilcvm::Type { .type_id = 1, .name = "Program", .instance_field_count = 1 }
        },
        .fields = {
            ilcvm::Field { .field_id = 1, .name = "Counter", .owner_type_id = 1, .is_static = false, .instance_slot = 0 }
        },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = ".ctor",
                .register_count = 2,
                .argument_count = 2,
                .returns_value = false,
                .instructions = {
                    { ilcvm::OpCode::st_field, 0, 1, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "Increment",
                .register_count = 4,
                .argument_count = 2,
                .returns_value = false,
                .instructions = {
                    { ilcvm::OpCode::ld_field, 2, 0, 0, 1 },
                    { ilcvm::OpCode::add_i32, 3, 2, 1, 0 },
                    { ilcvm::OpCode::st_field, 0, 3, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 3,
                .name = "Current",
                .register_count = 2,
                .argument_count = 1,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_field, 1, 0, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 4,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::new_obj, 1, 0, 0, 1 },
                    { ilcvm::OpCode::ld_i32, 2, 0, 0, 5 },
                    { ilcvm::OpCode::call_virt, 1, 1, 1, 1 },
                    { ilcvm::OpCode::ld_i32, 2, 0, 0, 3 },
                    { ilcvm::OpCode::call_virt, 1, 1, 1, 2 },
                    { ilcvm::OpCode::call_virt, 0, 1, 0, 3 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 4
    };

    const auto object_result = vm.execute(object_module);
    if (object_result != 8)
    {
        std::cerr << "FAIL: vm returned " << object_result << " for object module\n";
        return EXIT_FAILURE;
    }

    const auto object_ilb_module = ilcvm::load_module_from_ilb_bytes(make_object_test_ilb());
    if (object_ilb_module.types.size() != 1 || object_ilb_module.fields.size() != 1 || object_ilb_module.functions.size() != 4 || object_ilb_module.entry_function_id != 4)
    {
        std::cerr << "FAIL: object ilb loader did not recover module metadata\n";
        return EXIT_FAILURE;
    }

    const auto object_ilb_result = vm.execute(object_ilb_module);
    if (object_ilb_result != 8)
    {
        std::cerr << "FAIL: vm returned " << object_ilb_result << " for object ilb-loaded module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module string_module {
        .strings = { "", "wxyz", "wxyz", "abcd", "xy", "z", "xyz", "x", "y" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
        .register_count = 54,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 1 },
                    { ilcvm::OpCode::ld_i32, 2, 0, 0, 1 },
                    { ilcvm::OpCode::ld_elem, 3, 1, 2, 0 },
                    { ilcvm::OpCode::ld_len, 4, 1, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 5, 0, 0, 1 },
                    { ilcvm::OpCode::ld_i32, 6, 0, 0, 2 },
                    { ilcvm::OpCode::slice_str, 7, 1, 5, 6 },
                    { ilcvm::OpCode::ld_str, 8, 0, 0, 2 },
                    { ilcvm::OpCode::cmp_eq_str, 9, 1, 8, 0 },
                    { ilcvm::OpCode::ld_str, 10, 0, 0, 3 },
                    { ilcvm::OpCode::cmp_ne_str, 11, 1, 10, 0 },
                    { ilcvm::OpCode::ld_len, 12, 7, 0, 0 },
                    { ilcvm::OpCode::ld_str, 13, 0, 0, 4 },
                    { ilcvm::OpCode::cmp_eq_str, 14, 7, 13, 0 },
                    { ilcvm::OpCode::add_i32, 15, 3, 4, 0 },
                    { ilcvm::OpCode::add_i32, 16, 15, 9, 0 },
                    { ilcvm::OpCode::ld_i32, 17, 0, 0, 0 },
                    { ilcvm::OpCode::cmp_eq_str, 18, 17, 17, 0 },
                    { ilcvm::OpCode::cmp_ne_str, 19, 1, 17, 0 },
                    { ilcvm::OpCode::add_i32, 20, 16, 11, 0 },
                    { ilcvm::OpCode::add_i32, 21, 20, 18, 0 },
                    { ilcvm::OpCode::add_i32, 22, 21, 19, 0 },
                    { ilcvm::OpCode::ld_str, 24, 0, 0, 5 },
                    { ilcvm::OpCode::concat_str, 25, 7, 24, 0 },
                    { ilcvm::OpCode::ld_len, 26, 25, 0, 0 },
                    { ilcvm::OpCode::ld_str, 27, 0, 0, 6 },
                    { ilcvm::OpCode::cmp_eq_str, 28, 25, 27, 0 },
                    { ilcvm::OpCode::ld_str, 29, 0, 0, 7 },
                    { ilcvm::OpCode::starts_with_str, 30, 25, 29, 0 },
                    { ilcvm::OpCode::ld_str, 31, 0, 0, 5 },
                    { ilcvm::OpCode::ends_with_str, 32, 25, 31, 0 },
                    { ilcvm::OpCode::ld_str, 33, 0, 0, 4 },
                    { ilcvm::OpCode::contains_str, 34, 25, 33, 0 },
                    { ilcvm::OpCode::ld_str, 41, 0, 0, 8 },
                    { ilcvm::OpCode::index_of_str, 42, 25, 41, 0 },
                    { ilcvm::OpCode::ld_str, 43, 0, 0, 5 },
                    { ilcvm::OpCode::last_index_of_str, 44, 25, 43, 0 },
                    { ilcvm::OpCode::add_i32, 35, 22, 12, 0 },
                    { ilcvm::OpCode::add_i32, 36, 35, 28, 0 },
                    { ilcvm::OpCode::add_i32, 37, 36, 30, 0 },
                    { ilcvm::OpCode::add_i32, 38, 37, 32, 0 },
                    { ilcvm::OpCode::add_i32, 39, 38, 34, 0 },
                    { ilcvm::OpCode::add_i32, 40, 39, 26, 0 },
                    { ilcvm::OpCode::add_i32, 45, 40, 28, 0 },
                    { ilcvm::OpCode::add_i32, 46, 45, 42, 0 },
                    { ilcvm::OpCode::add_i32, 47, 46, 44, 0 },
                    { ilcvm::OpCode::ld_str, 48, 0, 0, 5 },
                    { ilcvm::OpCode::ld_str, 49, 0, 0, 7 },
                    { ilcvm::OpCode::replace_str, 50, 25, 48, 49 },
                    { ilcvm::OpCode::ld_len, 51, 50, 0, 0 },
                    { ilcvm::OpCode::ends_with_str, 52, 50, 49, 0 },
                    { ilcvm::OpCode::add_i32, 53, 47, 51, 0 },
                    { ilcvm::OpCode::add_i32, 0, 53, 52, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 1
    };

    const auto string_result = vm.execute(string_module);
    if (string_result != 145)
    {
        std::cerr << "FAIL: vm returned " << string_result << " for string module\n";
        return EXIT_FAILURE;
    }

    const auto string_ilb_module = ilcvm::load_module_from_ilb_bytes(make_string_test_ilb());
    if (string_ilb_module.functions.size() != 1 || string_ilb_module.entry_function_id != 1)
    {
        std::cerr << "FAIL: string ilb loader did not recover module metadata\n";
        return EXIT_FAILURE;
    }

    const auto string_ilb_result = vm.execute(string_ilb_module);
    if (string_ilb_result != 145)
    {
        std::cerr << "FAIL: vm returned " << string_ilb_result << " for string ilb-loaded module\n";
        std::cerr << "loaded strings (" << string_ilb_module.strings.size() << "):\n";
        for (std::size_t index = 0; index < string_ilb_module.strings.size(); ++index)
        {
            std::cerr << "  [" << index << "] = '" << string_ilb_module.strings[index] << "'\n";
        }

        if (!string_ilb_module.functions.empty())
        {
            const auto& function = string_ilb_module.functions.front();
            std::cerr << "instruction count: " << function.instructions.size() << ", register_count: " << function.register_count << '\n';
            for (const auto instruction_index : { 25, 27, 29, 31, 33, 35 })
            {
                if (static_cast<std::size_t>(instruction_index) < function.instructions.size())
                {
                    const auto& instruction = function.instructions[static_cast<std::size_t>(instruction_index)];
                    std::cerr << "  ins[" << instruction_index << "] opcode=" << static_cast<int>(instruction.opcode)
                              << " dst=" << instruction.destination
                              << " left=" << instruction.left
                              << " right=" << instruction.right
                              << " imm=" << instruction.immediate << '\n';
                }
            }
        }
        return EXIT_FAILURE;
    }

    ilcvm::Module try_parse_module {
        .strings = {
            "18",
            "oops"
        },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 9,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::try_str_to_i32, 2, 1, 3, 0 },
                    { ilcvm::OpCode::ld_str, 4, 0, 0, 1 },
                    { ilcvm::OpCode::try_str_to_i32, 5, 4, 6, 0 },
                    { ilcvm::OpCode::ld_i32, 7, 0, 0, 0 },
                    { ilcvm::OpCode::cmp_eq_i32, 8, 6, 7, 0 },
                    { ilcvm::OpCode::add_i32, 0, 2, 3, 0 },
                    { ilcvm::OpCode::add_i32, 0, 0, 5, 0 },
                    { ilcvm::OpCode::add_i32, 0, 0, 6, 0 },
                    { ilcvm::OpCode::add_i32, 0, 0, 8, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 1
    };

    const auto try_parse_result = vm.execute(try_parse_module);
    if (try_parse_result != 20)
    {
        std::cerr << "FAIL: vm returned " << try_parse_result << " for try-parse module\n";
        return EXIT_FAILURE;
    }

    const auto expect_runtime_error = [](auto&& action, const char* expected_message) -> bool
    {
        try
        {
            action();
        }
        catch (const std::runtime_error& error)
        {
            return std::string(error.what()) == expected_message;
        }

        return false;
    };

    ilcvm::Module null_call_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Current",
                .register_count = 1,
                .argument_count = 1,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "Main",
                .register_count = 2,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 0 },
                    { ilcvm::OpCode::call_virt, 0, 1, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 2
    };

    if (!expect_runtime_error([&]() { (void)vm.execute(null_call_module); }, "null reference method call"))
    {
        std::cerr << "FAIL: null method call should raise a targeted runtime error\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module null_field_module {
        .types = {
            ilcvm::Type { .type_id = 1, .name = "Program", .instance_field_count = 1 }
        },
        .fields = {
            ilcvm::Field { .field_id = 1, .name = "Counter", .owner_type_id = 1, .is_static = false, .instance_slot = 0 }
        },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 2,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_field, 0, 1, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 1
    };

    if (!expect_runtime_error([&]() { (void)vm.execute(null_field_module); }, "null reference object access"))
    {
        std::cerr << "FAIL: null field access should raise a targeted runtime error\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module null_element_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 2, 0, 0, 0 },
                    { ilcvm::OpCode::ld_elem, 0, 1, 2, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 1
    };

    if (!expect_runtime_error([&]() { (void)vm.execute(null_element_module); }, "null reference element access"))
    {
        std::cerr << "FAIL: null element access should raise a targeted runtime error\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module null_length_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 2,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 1
    };

    if (!expect_runtime_error([&]() { (void)vm.execute(null_length_module); }, "null reference length access"))
    {
        std::cerr << "FAIL: null length access should raise a targeted runtime error\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module catch_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 41 },
                    { ilcvm::OpCode::throw_, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 42 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                },
                .exception_handlers = {
                    { .try_start = 0, .try_end = 2, .handler_start = 4, .handler_end = 6, .target_register = 0xFFFF }
                }
            }
        },
        .entry_function_id = 1
    };

    const auto catch_result = vm.execute(catch_module);
    if (catch_result != 42)
    {
        std::cerr << "FAIL: vm returned " << catch_result << " for catch module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module unhandled_throw_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 2,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 5 },
                    { ilcvm::OpCode::throw_, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 1
    };

    if (!expect_runtime_error([&]() { (void)vm.execute(unhandled_throw_module); }, "unhandled managed exception"))
    {
        std::cerr << "FAIL: unhandled throw should raise a targeted runtime error\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module rethrow_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 4,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 1, 0, 0, 5 },
                    { ilcvm::OpCode::throw_, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 },
                    { ilcvm::OpCode::rethrow, 0, 0, 0, 0 }
                },
                .exception_handlers = {
                    { .try_start = 0, .try_end = 2, .handler_start = 4, .handler_end = 5, .target_register = 0xFFFF }
                }
            }
        },
        .entry_function_id = 1
    };

    if (!expect_runtime_error([&]() { (void)vm.execute(rethrow_module); }, "unhandled managed exception"))
    {
        std::cerr << "FAIL: rethrow should propagate the active exception\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module typed_catch_module {
        .types = {
            { .type_id = 1, .name = "Program", .instance_field_count = 0 },
            { .type_id = 2, .name = "String", .instance_field_count = 0 }
        },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::new_obj, 1, 0, 0, 1 },
                    { ilcvm::OpCode::throw_, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 77 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                },
                .exception_handlers = {
                    { .try_start = 0, .try_end = 2, .handler_start = 4, .handler_end = 6, .target_register = 2, .catch_type_id = 1 }
                }
            }
        },
        .entry_function_id = 1
    };

    const auto typed_catch_result = vm.execute(typed_catch_module);
    if (typed_catch_result != 77)
    {
        std::cerr << "FAIL: vm returned " << typed_catch_result << " for typed catch module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module typed_mismatch_module {
        .types = {
            { .type_id = 1, .name = "Program", .instance_field_count = 0 },
            { .type_id = 2, .name = "String", .instance_field_count = 0 }
        },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::new_obj, 1, 0, 0, 1 },
                    { ilcvm::OpCode::throw_, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 88 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                },
                .exception_handlers = {
                    { .try_start = 0, .try_end = 2, .handler_start = 4, .handler_end = 6, .target_register = 2, .catch_type_id = 2 }
                }
            }
        },
        .entry_function_id = 1
    };

    if (!expect_runtime_error([&]() { (void)vm.execute(typed_mismatch_module); }, "unhandled managed exception"))
    {
        std::cerr << "FAIL: mismatched typed catch should not handle the exception\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_console_module {
        .strings = { "Hello World" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 2,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::call, 0, 1, 1, 2 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 42 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "WriteLine",
                .register_count = 1,
                .argument_count = 1,
                .returns_value = false,
                .host_import_kind = ilcvm::HostImportKind::console_write_line
            }
        },
        .entry_function_id = 1
    };

    const auto host_console_result = vm.execute(host_console_module);
    if (host_console_result != 42)
    {
        std::cerr << "FAIL: vm returned " << host_console_result << " for host console module\n";
        return EXIT_FAILURE;
    }

    if (host_services.output != "Hello World\n")
    {
        std::cerr << "FAIL: host console output mismatch: '" << host_services.output << "'\n";
        return EXIT_FAILURE;
    }

    host_services.command_line_args = { "--help", "demo.txt" };
    ilcvm::Module host_environment_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::call, 1, 0, 0, 2 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetCommandLineArgs",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::environment_get_command_line_args
            }
        },
        .entry_function_id = 1
    };

    const auto host_environment_result = vm.execute(host_environment_module);
    if (host_environment_result != 2)
    {
        std::cerr << "FAIL: vm returned " << host_environment_result << " for host environment module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_user_name_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::call, 1, 0, 0, 2 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetUserName",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::environment_get_user_name
            }
        },
        .entry_function_id = 1
    };

    const auto host_user_name_result = vm.execute(host_user_name_module);
    if (host_user_name_result != 6)
    {
        std::cerr << "FAIL: vm returned " << host_user_name_result << " for host user name module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_machine_name_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::call, 1, 0, 0, 2 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetMachineName",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::environment_get_machine_name
            }
        },
        .entry_function_id = 1
    };

    const auto host_machine_name_result = vm.execute(host_machine_name_module);
    if (host_machine_name_result != 6)
    {
        std::cerr << "FAIL: vm returned " << host_machine_name_result << " for host machine name module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_home_directory_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::call, 1, 0, 0, 2 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetHomeDirectory",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::environment_get_home_directory
            }
        },
        .entry_function_id = 1
    };

    const auto host_home_directory_result = vm.execute(host_home_directory_module);
    if (host_home_directory_result != 10)
    {
        std::cerr << "FAIL: vm returned " << host_home_directory_result << " for host home directory module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_temp_directory_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::call, 1, 0, 0, 2 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetTempDirectory",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::environment_get_temp_directory
            }
        },
        .entry_function_id = 1
    };

    const auto host_temp_directory_result = vm.execute(host_temp_directory_module);
    if (host_temp_directory_result != 4)
    {
        std::cerr << "FAIL: vm returned " << host_temp_directory_result << " for host temp directory module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_set_environment_variable_module {
        .strings = { "HOME", "/alt-home" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 4,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_str, 2, 0, 0, 1 },
                    { ilcvm::OpCode::call, 0, 1, 2, 2 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "SetEnvironmentVariable",
                .register_count = 3,
                .argument_count = 2,
                .returns_value = false,
                .host_import_kind = ilcvm::HostImportKind::environment_set_environment_variable
            }
        },
        .entry_function_id = 1
    };

    const auto host_set_environment_variable_result = vm.execute(host_set_environment_variable_module);
    if (host_set_environment_variable_result != 1)
    {
        std::cerr << "FAIL: vm returned " << host_set_environment_variable_result << " for host set environment variable module\n";
        return EXIT_FAILURE;
    }

    if (host_services.environment_variable_value != "/alt-home")
    {
        std::cerr << "FAIL: host environment variable write mismatch: '" << host_services.environment_variable_value << "'\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_current_directory_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::call, 1, 0, 0, 2 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetCurrentWorkingDirectory",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::environment_get_current_working_directory
            }
        },
        .entry_function_id = 1
    };

    const auto host_current_directory_result = vm.execute(host_current_directory_module);
    if (host_current_directory_result != 5)
    {
        std::cerr << "FAIL: vm returned " << host_current_directory_result << " for host current directory module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_environment_variable_module {
        .strings = { "HOME" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 4,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::call, 2, 1, 1, 2 },
                    { ilcvm::OpCode::ld_len, 0, 2, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetEnvironmentVariable",
                .register_count = 2,
                .argument_count = 1,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::environment_get_environment_variable
            }
        },
        .entry_function_id = 1
    };

    const auto host_environment_variable_result = vm.execute(host_environment_variable_module);
    if (host_environment_variable_result != 9)
    {
        std::cerr << "FAIL: vm returned " << host_environment_variable_result << " for host environment variable module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_monotonic_clock_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::call, 1, 0, 0, 2 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetMonotonicMillisecondsText",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::clock_get_monotonic_milliseconds_text
            }
        },
        .entry_function_id = 1
    };

    const auto host_monotonic_clock_result = vm.execute(host_monotonic_clock_module);
    if (host_monotonic_clock_result != 1)
    {
        std::cerr << "FAIL: vm returned " << host_monotonic_clock_result << " for host monotonic clock module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_wall_clock_module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::call, 1, 0, 0, 2 },
                    { ilcvm::OpCode::ld_len, 0, 1, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetWallMillisecondsText",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::clock_get_wall_milliseconds_text
            }
        },
        .entry_function_id = 1
    };

    const auto host_wall_clock_result = vm.execute(host_wall_clock_module);
    if (host_wall_clock_result != 1)
    {
        std::cerr << "FAIL: vm returned " << host_wall_clock_result << " for host wall clock module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_file_exists_module {
        .strings = { "/test/input.txt" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::call, 0, 1, 1, 2 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "Exists",
                .register_count = 2,
                .argument_count = 1,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::file_exists
            }
        },
        .entry_function_id = 1
    };

    const auto host_file_exists_result = vm.execute(host_file_exists_module);
    if (host_file_exists_result != 1)
    {
        std::cerr << "FAIL: vm returned " << host_file_exists_result << " for host file exists module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_file_read_all_text_module {
        .strings = { "/test/input.txt" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 4,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::call, 2, 1, 1, 2 },
                    { ilcvm::OpCode::ld_len, 0, 2, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "ReadAllText",
                .register_count = 2,
                .argument_count = 1,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::file_read_all_text
            }
        },
        .entry_function_id = 1
    };

    const auto host_file_read_all_text_result = vm.execute(host_file_read_all_text_module);
    if (host_file_read_all_text_result != 9)
    {
        std::cerr << "FAIL: vm returned " << host_file_read_all_text_result << " for host file read module\n";
        return EXIT_FAILURE;
    }

    host_services.output.clear();
    ilcvm::Module host_file_write_all_text_module {
        .strings = { "/test/output.txt", "written" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 4,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_str, 2, 0, 0, 1 },
                    { ilcvm::OpCode::call, 0, 1, 2, 2 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "WriteAllText",
                .register_count = 3,
                .argument_count = 2,
                .returns_value = false,
                .host_import_kind = ilcvm::HostImportKind::file_write_all_text
            }
        },
        .entry_function_id = 1
    };

    const auto host_file_write_all_text_result = vm.execute(host_file_write_all_text_module);
    if (host_file_write_all_text_result != 1)
    {
        std::cerr << "FAIL: vm returned " << host_file_write_all_text_result << " for host file write module\n";
        return EXIT_FAILURE;
    }

    if (host_services.output != "written")
    {
        std::cerr << "FAIL: host file write output mismatch: '" << host_services.output << "'\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_file_append_all_text_module {
        .strings = { "/test/output.txt", "-more" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 4,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_str, 2, 0, 0, 1 },
                    { ilcvm::OpCode::call, 0, 1, 2, 2 },
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 1 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "AppendAllText",
                .register_count = 3,
                .argument_count = 2,
                .returns_value = false,
                .host_import_kind = ilcvm::HostImportKind::file_append_all_text
            }
        },
        .entry_function_id = 1
    };

    const auto host_file_append_all_text_result = vm.execute(host_file_append_all_text_module);
    if (host_file_append_all_text_result != 1)
    {
        std::cerr << "FAIL: vm returned " << host_file_append_all_text_result << " for host file append module\n";
        return EXIT_FAILURE;
    }

    if (host_services.output != "written-more")
    {
        std::cerr << "FAIL: host file append output mismatch: '" << host_services.output << "'\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_path_combine_module {
        .strings = { "/test", "input.txt" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 4,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::ld_str, 2, 0, 0, 1 },
                    { ilcvm::OpCode::call, 3, 1, 2, 2 },
                    { ilcvm::OpCode::ld_len, 0, 3, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "Combine",
                .register_count = 3,
                .argument_count = 2,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::path_combine
            }
        },
        .entry_function_id = 1
    };

    const auto host_path_combine_result = vm.execute(host_path_combine_module);
    if (host_path_combine_result != 15)
    {
        std::cerr << "FAIL: vm returned " << host_path_combine_result << " for host path combine module\n";
        return EXIT_FAILURE;
    }

    ilcvm::Module host_path_get_extension_module {
        .strings = { "/test/input.txt" },
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 3,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_str, 1, 0, 0, 0 },
                    { ilcvm::OpCode::call, 2, 1, 1, 2 },
                    { ilcvm::OpCode::ld_len, 0, 2, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            },
            ilcvm::Function {
                .function_id = 2,
                .name = "GetExtension",
                .register_count = 2,
                .argument_count = 1,
                .returns_value = true,
                .host_import_kind = ilcvm::HostImportKind::path_get_extension
            }
        },
        .entry_function_id = 1
    };

    const auto host_path_get_extension_result = vm.execute(host_path_get_extension_module);
    if (host_path_get_extension_result != 4)
    {
        std::cerr << "FAIL: vm returned " << host_path_get_extension_result << " for host path extension module\n";
        return EXIT_FAILURE;
    }

    ilcvm::StandardHostServices standard_host_services;
    if (standard_host_services.get_monotonic_timestamp_ms() == 0)
    {
        std::cerr << "FAIL: standard host should expose a monotonic timestamp\n";
        return EXIT_FAILURE;
    }

    if (standard_host_services.get_wall_timestamp_ms() == 0)
    {
        std::cerr << "FAIL: standard host should expose a wall timestamp\n";
        return EXIT_FAILURE;
    }

    if (standard_host_services.get_current_working_directory().empty())
    {
        std::cerr << "FAIL: standard host should expose the current working directory\n";
        return EXIT_FAILURE;
    }

    std::cout << "Runtime bootstrap checks passed.\n";
    return EXIT_SUCCESS;
}
