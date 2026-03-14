#include "ilcvm/heap.h"
#include "ilcvm/module.h"
#include "ilcvm/virtual_machine.h"

#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <vector>

namespace
{
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
    write_u32(strings, 5);
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

    ilcvm::VirtualMachine vm(heap);
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

    std::cout << "Runtime bootstrap checks passed.\n";
    return EXIT_SUCCESS;
}
