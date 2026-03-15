#include "ilcvm/module.h"

#include <array>
#include <cstring>
#include <fstream>
#include <stdexcept>
#include <unordered_map>

namespace ilcvm
{
namespace
{
constexpr std::uint32_t k_header_size = 64;
constexpr std::size_t k_directory_entry_size = 24;
constexpr std::size_t k_instruction_size = 11;

std::uint16_t read_u16(const std::vector<std::uint8_t>& bytes, std::size_t offset)
{
    return static_cast<std::uint16_t>(bytes.at(offset)) |
        (static_cast<std::uint16_t>(bytes.at(offset + 1)) << 8);
}

std::uint32_t read_u32(const std::vector<std::uint8_t>& bytes, std::size_t offset)
{
    return static_cast<std::uint32_t>(bytes.at(offset)) |
        (static_cast<std::uint32_t>(bytes.at(offset + 1)) << 8) |
        (static_cast<std::uint32_t>(bytes.at(offset + 2)) << 16) |
        (static_cast<std::uint32_t>(bytes.at(offset + 3)) << 24);
}

std::int32_t read_i32(const std::vector<std::uint8_t>& bytes, std::size_t offset)
{
    return static_cast<std::int32_t>(read_u32(bytes, offset));
}

const SectionDirectoryEntry& require_section(const std::vector<SectionDirectoryEntry>& sections, SectionKind kind)
{
    for (const auto& section : sections)
    {
        if (section.kind == kind)
        {
            return section;
        }
    }

    throw std::runtime_error("required ilb section is missing");
}

const SectionDirectoryEntry* find_section(const std::vector<SectionDirectoryEntry>& sections, SectionKind kind)
{
    for (const auto& section : sections)
    {
        if (section.kind == kind)
        {
            return &section;
        }
    }

    return nullptr;
}

void validate_section_bounds(const std::vector<std::uint8_t>& bytes, const SectionDirectoryEntry& section)
{
    const auto end = static_cast<std::size_t>(section.offset) + static_cast<std::size_t>(section.size);
    if (end > bytes.size())
    {
        throw std::runtime_error("ilb section extends beyond file bounds");
    }
}

std::vector<std::string> read_string_table(const std::vector<std::uint8_t>& bytes, const SectionDirectoryEntry& section)
{
    validate_section_bounds(bytes, section);
    std::size_t cursor = section.offset;
    const auto count = read_u32(bytes, cursor);
    cursor += 4;
    std::vector<std::string> strings;
    strings.reserve(count + 1);
    strings.emplace_back();
    for (std::uint32_t index = 0; index < count; ++index)
    {
        const auto length = read_u32(bytes, cursor);
        cursor += 4;
        if (cursor + length > static_cast<std::size_t>(section.offset + section.size))
        {
            throw std::runtime_error("invalid string table entry");
        }

        strings.emplace_back(reinterpret_cast<const char*>(bytes.data() + cursor), length);
        cursor += length;
    }

    return strings;
}

Function decode_function(
    std::uint32_t function_id,
    std::string name,
    std::uint16_t register_count,
    std::uint16_t argument_count,
    bool returns_value,
    HostImportKind host_import_kind,
    std::uint32_t code_offset,
    std::uint32_t code_size,
    std::vector<Function::ExceptionHandler> exception_handlers,
    const std::vector<std::uint8_t>& code_bytes)
{
    if (code_size % k_instruction_size != 0)
    {
        throw std::runtime_error("method code size is not aligned to instruction size");
    }

    Function function;
    function.function_id = function_id;
    function.name = std::move(name);
    function.register_count = register_count;
    function.argument_count = argument_count;
    function.returns_value = returns_value;
    function.host_import_kind = host_import_kind;
    function.exception_handlers = std::move(exception_handlers);

    std::size_t cursor = code_offset;
    const auto end = static_cast<std::size_t>(code_offset + code_size);
    while (cursor < end)
    {
        Instruction instruction;
        instruction.opcode = static_cast<OpCode>(code_bytes.at(cursor));
        instruction.destination = read_u16(code_bytes, cursor + 1);
        instruction.left = read_u16(code_bytes, cursor + 3);
        instruction.right = read_u16(code_bytes, cursor + 5);
        instruction.immediate = read_i32(code_bytes, cursor + 7);
        function.instructions.push_back(instruction);
        cursor += k_instruction_size;
    }

    return function;
}
} // namespace

Module load_module_from_ilb_bytes(const std::vector<std::uint8_t>& bytes)
{
    if (bytes.size() < k_header_size)
    {
        throw std::runtime_error("ilb file is smaller than the fixed header");
    }

    if (std::memcmp(bytes.data(), "ILB1", 4) != 0)
    {
        throw std::runtime_error("invalid ilb magic");
    }

    const auto major_version = read_u16(bytes, 4);
    const auto minor_version = read_u16(bytes, 6);
    const auto header_size = read_u16(bytes, 8);
    const auto section_count = read_u32(bytes, 12);
    const auto section_directory_offset = read_u32(bytes, 16);
    const auto file_size = read_u32(bytes, 20);

    if (major_version != 1 || minor_version != 0)
    {
        throw std::runtime_error("unsupported ilb version");
    }

    if (header_size != k_header_size)
    {
        throw std::runtime_error("unexpected ilb header size");
    }

    if (file_size != bytes.size())
    {
        throw std::runtime_error("ilb file size header does not match actual file size");
    }

    const auto directory_size = static_cast<std::size_t>(section_count) * k_directory_entry_size;
    if (static_cast<std::size_t>(section_directory_offset) + directory_size > bytes.size())
    {
        throw std::runtime_error("ilb section directory exceeds file bounds");
    }

    Module module;
    module.sections.reserve(section_count);
    for (std::uint32_t index = 0; index < section_count; ++index)
    {
        const auto offset = static_cast<std::size_t>(section_directory_offset) + index * k_directory_entry_size;
        module.sections.push_back(SectionDirectoryEntry {
            .kind = static_cast<SectionKind>(read_u32(bytes, offset)),
            .offset = read_u32(bytes, offset + 4),
            .size = read_u32(bytes, offset + 8),
            .element_count = read_u32(bytes, offset + 12),
            .alignment = read_u32(bytes, offset + 16),
            .flags = read_u32(bytes, offset + 20)
        });
    }

    const auto& string_table = require_section(module.sections, SectionKind::string_table);
    const auto& method_table = require_section(module.sections, SectionKind::method_table);
    const auto& code_section = require_section(module.sections, SectionKind::code_section);
    const auto strings = read_string_table(bytes, string_table);
    module.strings = strings;

    validate_section_bounds(bytes, method_table);
    validate_section_bounds(bytes, code_section);

    if (const auto* type_table = find_section(module.sections, SectionKind::type_table))
    {
        validate_section_bounds(bytes, *type_table);
        const auto type_row_size = static_cast<std::size_t>(60);
        if (type_table->size % type_row_size != 0)
        {
            throw std::runtime_error("invalid type table size");
        }

        std::size_t type_cursor = type_table->offset;
        for (std::uint32_t type_index = 1; type_index <= type_table->element_count; ++type_index)
        {
            const auto name_string_id = read_u32(bytes, type_cursor + 4);
            const auto declared_field_count = read_u32(bytes, type_cursor + 20);
            const auto name = name_string_id < strings.size() ? strings[name_string_id] : std::string();
            module.types.push_back(Type {
                .type_id = type_index,
                .name = name,
                .instance_field_count = declared_field_count
            });
            type_cursor += type_row_size;
        }
    }

    if (const auto* field_table = find_section(module.sections, SectionKind::field_table))
    {
        validate_section_bounds(bytes, *field_table);
        const auto field_row_size = static_cast<std::size_t>(32);
        if (field_table->size % field_row_size != 0)
        {
            throw std::runtime_error("invalid field table size");
        }

        std::unordered_map<std::uint32_t, std::uint32_t> next_instance_slot_by_type;
        std::size_t field_cursor = field_table->offset;
        for (std::uint32_t field_index = 1; field_index <= field_table->element_count; ++field_index)
        {
            const auto owner_type_id = read_u32(bytes, field_cursor + 0);
            const auto name_string_id = read_u32(bytes, field_cursor + 4);
            const auto field_flags = read_u16(bytes, field_cursor + 12);
            const auto is_static = (field_flags & (1u << 4)) != 0;
            const auto name = name_string_id < strings.size() ? strings[name_string_id] : std::string();
            const auto instance_slot = is_static
                ? 0u
                : next_instance_slot_by_type[owner_type_id]++;
            module.fields.push_back(Field {
                .field_id = field_index,
                .name = name,
                .owner_type_id = owner_type_id,
                .is_static = is_static,
                .instance_slot = instance_slot
            });
            field_cursor += field_row_size;
        }

        for (auto& type : module.types)
        {
            if (auto it = next_instance_slot_by_type.find(type.type_id); it != next_instance_slot_by_type.end())
            {
                type.instance_field_count = it->second;
            }
        }
    }

    const auto method_row_size = static_cast<std::size_t>(62);
    if (method_table.size % method_row_size != 0)
    {
        throw std::runtime_error("invalid method table size");
    }

    std::vector<std::uint8_t> code_bytes(
        bytes.begin() + code_section.offset,
        bytes.begin() + code_section.offset + code_section.size);
    std::vector<Function::ExceptionHandler> exception_rows(1);

    if (const auto* exception_table = find_section(module.sections, SectionKind::exception_table))
    {
        validate_section_bounds(bytes, *exception_table);
        const auto exception_row_size = static_cast<std::size_t>(32);
        if (exception_table->size % exception_row_size != 0)
        {
            throw std::runtime_error("invalid exception table size");
        }

        std::size_t exception_cursor = exception_table->offset;
        for (std::uint32_t index = 1; index <= exception_table->element_count; ++index)
        {
            const auto try_start = read_u32(bytes, exception_cursor + 4);
            const auto try_end = read_u32(bytes, exception_cursor + 8);
            const auto handler_start = read_u32(bytes, exception_cursor + 12);
            const auto handler_end = read_u32(bytes, exception_cursor + 16);
            const auto handler_kind = read_u16(bytes, exception_cursor + 20);
            const auto catch_type_id = read_u32(bytes, exception_cursor + 24);
            const auto target_register = read_u16(bytes, exception_cursor + 28);
            if (handler_kind != 1)
            {
                throw std::runtime_error("unsupported exception handler kind");
            }

            exception_rows.push_back(Function::ExceptionHandler {
                .try_start = try_start,
                .try_end = try_end,
                .handler_start = handler_start,
                .handler_end = handler_end,
                .target_register = target_register,
                .catch_type_id = catch_type_id
            });
            exception_cursor += exception_row_size;
        }
    }

    std::size_t cursor = method_table.offset;
    for (std::uint32_t method_index = 1; method_index <= method_table.element_count; ++method_index)
    {
        const auto name_string_id = read_u32(bytes, cursor + 4);
        const auto method_flags = read_u32(bytes, cursor + 12);
        const auto register_count = read_u16(bytes, cursor + 16);
        const auto parameter_count = read_u16(bytes, cursor + 18);
        const auto return_type_id = read_u32(bytes, cursor + 22);
        const auto code_offset = read_u32(bytes, cursor + 26);
        const auto code_size = read_u32(bytes, cursor + 30);
        const auto exception_start = read_u32(bytes, cursor + 34);
        const auto exception_count = read_u32(bytes, cursor + 38);
        const auto host_import_kind = read_u32(bytes, cursor + 42);
        const auto name = name_string_id < strings.size() ? strings[name_string_id] : std::string();
        const auto argument_count = static_cast<std::uint16_t>(
            parameter_count + ((method_flags & (1u << 4)) == 0 ? 1 : 0));

        if (code_offset + code_size > code_section.size)
        {
            throw std::runtime_error("method body exceeds code section bounds");
        }

        std::vector<Function::ExceptionHandler> function_exceptions;
        if (exception_count > 0)
        {
            if (exception_start == 0 || static_cast<std::size_t>(exception_start + exception_count - 1) >= exception_rows.size())
            {
                throw std::runtime_error("method exception range is invalid");
            }

            for (std::uint32_t row_index = exception_start; row_index < exception_start + exception_count; ++row_index)
            {
                function_exceptions.push_back(exception_rows[row_index]);
            }
        }

        module.functions.push_back(decode_function(
            method_index,
            name,
            register_count,
            argument_count,
            return_type_id != 0,
            static_cast<HostImportKind>(host_import_kind),
            code_offset,
            code_size,
            std::move(function_exceptions),
            code_bytes));
        cursor += method_row_size;
    }

    for (const auto& section : module.sections)
    {
        if (section.kind == SectionKind::entry_point)
        {
            validate_section_bounds(bytes, section);
            if (section.size < 8)
            {
                throw std::runtime_error("entry point section is too small");
            }

            module.entry_function_id = read_u32(bytes, section.offset);
            break;
        }
    }

    return module;
}

Module load_module_from_ilb_file(const std::string& path)
{
    std::ifstream stream(path, std::ios::binary);
    if (!stream)
    {
        throw std::runtime_error("failed to open ilb file");
    }

    stream.seekg(0, std::ios::end);
    const auto size = static_cast<std::size_t>(stream.tellg());
    stream.seekg(0, std::ios::beg);
    std::vector<std::uint8_t> bytes(size);
    stream.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    if (!stream)
    {
        throw std::runtime_error("failed to read ilb file");
    }

    return load_module_from_ilb_bytes(bytes);
}
} // namespace ilcvm
