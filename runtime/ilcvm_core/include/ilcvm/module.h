#pragma once

#include <cstdint>
#include <string_view>
#include <string>
#include <vector>

namespace ilcvm
{
enum class OpCode : std::uint8_t
{
    nop = 0x00,
    ld_i32 = 0x02,
    ld_str = 0x03,
    mov = 0x09,
    add_i32 = 0x10,
    sub_i32 = 0x11,
    mul_i32 = 0x12,
    div_i32 = 0x13,
    cmp_eq_i32 = 0x18,
    cmp_ne_i32 = 0x19,
    cmp_lt_i32 = 0x1A,
    cmp_le_i32 = 0x1B,
    cmp_gt_i32 = 0x1C,
    cmp_ge_i32 = 0x1D,
    call = 0x30,
    call_virt = 0x51,
    ld_field = 0x36,
    st_field = 0x37,
    ld_sfield = 0x38,
    st_sfield = 0x39,
    new_obj = 0x60,
    new_arr = 0x61,
    ld_elem = 0x74,
    st_elem = 0x75,
    ld_len = 0x76,
    br = 0x31,
    br_false = 0x32,
    ret = 0x34
};

enum class SectionKind : std::uint32_t
{
    string_table = 1,
    blob_table = 2,
    type_table = 3,
    field_table = 4,
    method_table = 5,
    constant_table = 6,
    code_section = 7,
    exception_table = 8,
    entry_point = 9
};

struct Instruction
{
    OpCode opcode {};
    std::uint16_t destination {};
    std::uint16_t left {};
    std::uint16_t right {};
    std::int32_t immediate {};
};

struct SectionDirectoryEntry
{
    SectionKind kind {};
    std::uint32_t offset {};
    std::uint32_t size {};
    std::uint32_t element_count {};
    std::uint32_t alignment {};
    std::uint32_t flags {};
};

struct Function
{
    std::uint32_t function_id {};
    std::string name;
    std::uint16_t register_count {};
    std::uint16_t argument_count {};
    bool returns_value {};
    std::vector<Instruction> instructions;
};

struct Type
{
    std::uint32_t type_id {};
    std::string name;
    std::uint32_t instance_field_count {};
};

struct Field
{
    std::uint32_t field_id {};
    std::string name;
    std::uint32_t owner_type_id {};
    bool is_static {};
    std::uint32_t instance_slot {};
};

struct Module
{
    std::vector<SectionDirectoryEntry> sections;
    std::vector<std::string> strings;
    std::vector<Type> types;
    std::vector<Field> fields;
    std::vector<Function> functions;
    std::uint32_t entry_function_id {};
};

[[nodiscard]] Module load_module_from_ilb_bytes(const std::vector<std::uint8_t>& bytes);
[[nodiscard]] Module load_module_from_ilb_file(const std::string& path);
} // namespace ilcvm
