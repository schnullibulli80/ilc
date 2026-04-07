#pragma once

#include <cstdint>
#include <string_view>
#include <string>
#include <vector>

namespace ilcvm
{
enum class HostImportKind : std::uint32_t
{
    none = 0,
    console_write = 1,
    console_write_line = 2,
    console_read_line = 3,
    environment_get_command_line_args = 4,
    environment_get_current_working_directory = 5,
    environment_get_environment_variable = 6,
    environment_set_environment_variable = 7,
    environment_get_user_name = 8,
    environment_get_machine_name = 9,
    environment_get_home_directory = 10,
    environment_get_temp_directory = 11,
    clock_get_monotonic_milliseconds_text = 12,
    clock_get_wall_milliseconds_text = 13,
    clock_get_wall_datetime_text = 14,
    exception_get_current_stack_trace = 15,
    file_exists = 16,
    file_read_all_text = 17,
    file_write_all_text = 18,
    file_append_all_text = 19,
    path_combine = 20,
    path_get_file_name = 21,
    path_get_directory_name = 22,
    path_get_extension = 23,
    tcp_connect = 24,
    tcp_read_line = 25,
    tcp_write_line = 26,
    tcp_close = 27,
    http_get_string = 28,
    thread_sleep = 29,
    thread_get_current_managed_id = 30,
    mutex_create = 31,
    mutex_wait_one = 32,
    mutex_release = 33,
    mutex_close = 34
};

enum class NativeCallingConvention : std::uint32_t
{
    cdecl_ = 0,
    stdcall_ = 1
};

enum class OpCode : std::uint8_t
{
    nop = 0x00,
    ld_i32 = 0x02,
    ld_str = 0x03,
    mov = 0x09,
    add_i32 = 0x10,
    shl_i32 = 0x17,
    shr_i32 = 0x54,
    and_i32 = 0x14,
    or_i32 = 0x15,
    not_i32 = 0x16,
    sub_i32 = 0x11,
    mul_i32 = 0x12,
    div_i32 = 0x13,
    mod_i32 = 0x55,
    cmp_eq_i32 = 0x18,
    cmp_ne_i32 = 0x19,
    cmp_lt_i32 = 0x1A,
    cmp_le_i32 = 0x1B,
    cmp_gt_i32 = 0x1C,
    cmp_ge_i32 = 0x1D,
    cmp_eq_str = 0x1E,
    cmp_ne_str = 0x1F,
    cmp_eq_ref = 0x20,
    cmp_ne_ref = 0x21,
    is_type_ref = 0x78,
    as_type_ref = 0x79,
    concat_str = 0x22,
    starts_with_str = 0x23,
    ends_with_str = 0x24,
    contains_str = 0x25,
    index_of_str = 0x26,
    last_index_of_str = 0x27,
    replace_str = 0x28,
    insert_str = 0x29,
    remove_str = 0x2A,
    to_upper_str = 0x2B,
    to_lower_str = 0x2C,
    trim_str = 0x2D,
    trim_start_str = 0x2E,
    trim_end_str = 0x2F,
    str_to_i32 = 0x50,
    try_str_to_i32 = 0x53,
    i32_to_str = 0x52,
    throw_ = 0x80,
    rethrow = 0x81,
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
    slice_str = 0x77,
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
    entry_point = 9,
    interface_dispatch_table = 10
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
    struct DllImport
    {
        std::string library_name;
        std::string entry_point;
        NativeCallingConvention calling_convention { NativeCallingConvention::cdecl_ };
        bool is_present {};
    };

    std::uint32_t function_id {};
    std::uint32_t owner_type_id {};
    std::uint32_t method_flags {};
    std::uint32_t return_type_id {};
    std::string name;
    std::uint16_t register_count {};
    std::uint16_t argument_count {};
    bool returns_value {};
    bool is_static {};
    bool is_virtual {};
    bool is_override {};
    bool is_extern {};
    HostImportKind host_import_kind { HostImportKind::none };
    DllImport dll_import {};
    std::vector<std::uint32_t> parameter_type_ids;
    std::vector<Instruction> instructions;
    struct ExceptionHandler
    {
        std::uint32_t try_start {};
        std::uint32_t try_end {};
        std::uint32_t handler_start {};
        std::uint32_t handler_end {};
        std::uint16_t target_register { 0xFFFF };
        std::uint32_t catch_type_id {};
    };
    std::vector<ExceptionHandler> exception_handlers;
};

struct Type
{
    std::uint32_t type_id {};
    std::string name;
    std::uint16_t kind {};
    std::uint16_t flags {};
    std::uint32_t base_type_id {};
    std::uint32_t first_field_id {};
    std::uint32_t field_count {};
    std::uint32_t first_method_id {};
    std::uint32_t method_count {};
    bool is_reference_type {};
    bool is_interface {};
    bool is_record {};
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

struct InterfaceDispatchEntry
{
    std::uint32_t owner_type_id {};
    std::uint32_t interface_type_id {};
    std::uint32_t interface_method_id {};
    std::uint32_t implementation_method_id {};
};

struct Module
{
    std::vector<SectionDirectoryEntry> sections;
    std::vector<std::string> strings;
    std::vector<Type> types;
    std::vector<Field> fields;
    std::vector<Function> functions;
    std::vector<InterfaceDispatchEntry> interface_dispatch_entries;
    std::uint32_t entry_function_id {};
};

[[nodiscard]] Module load_module_from_ilb_bytes(const std::vector<std::uint8_t>& bytes);
[[nodiscard]] Module load_module_from_ilb_file(const std::string& path);
} // namespace ilcvm
