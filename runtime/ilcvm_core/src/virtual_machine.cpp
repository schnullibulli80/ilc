#include "ilcvm/virtual_machine.h"

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cstddef>
#include <functional>
#include <limits>
#include <stdexcept>
#include <array>
#include <string>
#include <vector>

namespace ilcvm
{
namespace
{
struct ManagedException
{
    std::int32_t value {};
};
}

VirtualMachine::VirtualMachine(Heap& heap, const IHostServices& host_services) noexcept
    : heap_(heap),
      host_services_(host_services)
{
}

std::int32_t VirtualMachine::execute(const Module& module) const
{
    return execute(module, static_cast<ExecutionProfile*>(nullptr));
}

std::int32_t VirtualMachine::execute(const Module& module, ExecutionProfile& profile) const
{
    return execute(module, &profile);
}

std::int32_t VirtualMachine::execute(const Module& module, ExecutionProfile* profile) const
{
    const auto total_start = std::chrono::steady_clock::now();
    if (module.functions.empty())
    {
        throw std::runtime_error("module contains no functions");
    }

    struct ArrayObject
    {
        std::vector<std::int32_t> elements;
    };

    struct ManagedObject
    {
        std::uint32_t type_id {};
        std::vector<std::int32_t> fields;
    };

    std::vector<std::int32_t> static_fields(module.fields.size() + 1, 0);
    std::vector<ArrayObject> arrays(1);
    std::vector<ManagedObject> objects(1);
    std::vector<std::string> strings = module.strings;

    std::uint32_t max_function_id = 0;
    for (const auto& function : module.functions)
    {
        max_function_id = std::max(max_function_id, function.function_id);
    }

    std::vector<const Function*> function_lookup(static_cast<std::size_t>(max_function_id) + 1, nullptr);
    std::vector<std::uint8_t> function_returns_value(static_cast<std::size_t>(max_function_id) + 1, 0);
    std::vector<std::uint16_t> function_argument_count_lookup(static_cast<std::size_t>(max_function_id) + 1, 0);
    for (const auto& function : module.functions)
    {
        function_lookup[function.function_id] = &function;
        function_returns_value[function.function_id] = function.returns_value ? 1u : 0u;
        function_argument_count_lookup[function.function_id] = function.argument_count;
    }

    enum class LeafFastpathKind : std::uint8_t
    {
        none = 0,
        generic = 1,
        instance_field_add_argument_return = 2,
        counted_range_sum_return = 3,
        array_fill_return_last = 4,
        array_fill_and_sum_return = 5
    };

    constexpr std::uint64_t call_timing_sample_mask = 0x3FF;
    constexpr std::uint64_t array_timing_sample_mask = 0x3FF;
    constexpr std::uint64_t mov_timing_sample_mask = 0x3FF;
    constexpr std::uint64_t compare_timing_sample_mask = 0x3FF;
    constexpr std::uint64_t branch_timing_sample_mask = 0x3FF;

    const auto is_leaf_fastpath_opcode = [](OpCode opcode) -> bool
    {
        switch (opcode)
        {
            case OpCode::nop:
            case OpCode::ld_i32:
            case OpCode::mov:
            case OpCode::add_i32:
            case OpCode::shl_i32:
            case OpCode::shr_i32:
            case OpCode::and_i32:
            case OpCode::or_i32:
            case OpCode::not_i32:
            case OpCode::sub_i32:
            case OpCode::mul_i32:
            case OpCode::div_i32:
            case OpCode::mod_i32:
            case OpCode::cmp_eq_i32:
            case OpCode::cmp_ne_i32:
            case OpCode::cmp_lt_i32:
            case OpCode::cmp_le_i32:
            case OpCode::cmp_gt_i32:
            case OpCode::cmp_ge_i32:
            case OpCode::cmp_eq_ref:
            case OpCode::cmp_ne_ref:
            case OpCode::ld_field:
            case OpCode::st_field:
            case OpCode::ld_sfield:
            case OpCode::st_sfield:
            case OpCode::br:
            case OpCode::br_false:
            case OpCode::ret:
                return true;
            default:
                return false;
        }
    };

    std::vector<LeafFastpathKind> function_leaf_fastpath_kind(
        static_cast<std::size_t>(max_function_id) + 1,
        LeafFastpathKind::none);
    std::vector<std::uint32_t> function_leaf_fastpath_field_id(static_cast<std::size_t>(max_function_id) + 1, 0);
    std::vector<std::uint32_t> function_leaf_fastpath_owner_type_id(static_cast<std::size_t>(max_function_id) + 1, 0);
    std::vector<std::uint32_t> function_leaf_fastpath_instance_slot(static_cast<std::size_t>(max_function_id) + 1, 0);
    for (const auto& function : module.functions)
    {
        if (function.host_import_kind != HostImportKind::none || !function.exception_handlers.empty())
        {
            continue;
        }

        const auto& instructions = function.instructions;
        if (instructions.size() == 17 &&
            function.argument_count == 1 &&
            instructions[0].opcode == OpCode::ld_i32 &&
            instructions[1].opcode == OpCode::ld_i32 &&
            instructions[2].opcode == OpCode::mov &&
            instructions[3].opcode == OpCode::mov &&
            instructions[4].opcode == OpCode::cmp_lt_i32 &&
            instructions[5].opcode == OpCode::br_false &&
            instructions[6].opcode == OpCode::mov &&
            instructions[7].opcode == OpCode::mov &&
            instructions[8].opcode == OpCode::add_i32 &&
            instructions[9].opcode == OpCode::mov &&
            instructions[10].opcode == OpCode::mov &&
            instructions[11].opcode == OpCode::ld_i32 &&
            instructions[12].opcode == OpCode::add_i32 &&
            instructions[13].opcode == OpCode::mov &&
            instructions[14].opcode == OpCode::br &&
            instructions[15].opcode == OpCode::mov &&
            instructions[16].opcode == OpCode::ret)
        {
            function_leaf_fastpath_kind[function.function_id] = LeafFastpathKind::counted_range_sum_return;
            continue;
        }

        if (instructions.size() == 26 &&
            function.argument_count == 1 &&
            instructions[0].opcode == OpCode::mov &&
            instructions[1].opcode == OpCode::mov &&
            instructions[2].opcode == OpCode::new_arr &&
            instructions[3].opcode == OpCode::ld_i32 &&
            instructions[4].opcode == OpCode::mov &&
            instructions[5].opcode == OpCode::mov &&
            instructions[6].opcode == OpCode::cmp_lt_i32 &&
            instructions[7].opcode == OpCode::br_false &&
            instructions[8].opcode == OpCode::mov &&
            instructions[9].opcode == OpCode::ld_i32 &&
            instructions[10].opcode == OpCode::mov &&
            instructions[11].opcode == OpCode::mov &&
            instructions[12].opcode == OpCode::st_elem &&
            instructions[13].opcode == OpCode::mov &&
            instructions[14].opcode == OpCode::mov &&
            instructions[15].opcode == OpCode::ld_i32 &&
            instructions[16].opcode == OpCode::add_i32 &&
            instructions[17].opcode == OpCode::mov &&
            instructions[18].opcode == OpCode::br &&
            instructions[19].opcode == OpCode::mov &&
            instructions[20].opcode == OpCode::ld_i32 &&
            instructions[21].opcode == OpCode::sub_i32 &&
            instructions[22].opcode == OpCode::ld_i32 &&
            instructions[23].opcode == OpCode::mov &&
            instructions[24].opcode == OpCode::ld_elem &&
            instructions[25].opcode == OpCode::ret)
        {
            function_leaf_fastpath_kind[function.function_id] = LeafFastpathKind::array_fill_return_last;
            continue;
        }

        if (instructions.size() == 40 &&
            function.argument_count == 1 &&
            instructions[0].opcode == OpCode::mov &&
            instructions[1].opcode == OpCode::mov &&
            instructions[2].opcode == OpCode::new_arr &&
            instructions[3].opcode == OpCode::ld_i32 &&
            instructions[4].opcode == OpCode::mov &&
            instructions[5].opcode == OpCode::mov &&
            instructions[6].opcode == OpCode::cmp_lt_i32 &&
            instructions[7].opcode == OpCode::br_false &&
            instructions[8].opcode == OpCode::mov &&
            instructions[9].opcode == OpCode::ld_i32 &&
            instructions[10].opcode == OpCode::mov &&
            instructions[11].opcode == OpCode::mov &&
            instructions[12].opcode == OpCode::st_elem &&
            instructions[13].opcode == OpCode::mov &&
            instructions[14].opcode == OpCode::mov &&
            instructions[15].opcode == OpCode::ld_i32 &&
            instructions[16].opcode == OpCode::add_i32 &&
            instructions[17].opcode == OpCode::mov &&
            instructions[18].opcode == OpCode::br &&
            instructions[19].opcode == OpCode::ld_i32 &&
            instructions[20].opcode == OpCode::mov &&
            instructions[21].opcode == OpCode::ld_i32 &&
            instructions[22].opcode == OpCode::mov &&
            instructions[23].opcode == OpCode::mov &&
            instructions[24].opcode == OpCode::cmp_lt_i32 &&
            instructions[25].opcode == OpCode::br_false &&
            instructions[26].opcode == OpCode::mov &&
            instructions[27].opcode == OpCode::mov &&
            instructions[28].opcode == OpCode::ld_i32 &&
            instructions[29].opcode == OpCode::mov &&
            instructions[30].opcode == OpCode::ld_elem &&
            instructions[31].opcode == OpCode::add_i32 &&
            instructions[32].opcode == OpCode::mov &&
            instructions[33].opcode == OpCode::mov &&
            instructions[34].opcode == OpCode::ld_i32 &&
            instructions[35].opcode == OpCode::add_i32 &&
            instructions[36].opcode == OpCode::mov &&
            instructions[37].opcode == OpCode::br &&
            instructions[38].opcode == OpCode::mov &&
            instructions[39].opcode == OpCode::ret)
        {
            function_leaf_fastpath_kind[function.function_id] = LeafFastpathKind::array_fill_and_sum_return;
            continue;
        }

        const bool eligible =
            function.register_count > 0 &&
            function.register_count <= 16 &&
            std::all_of(
                instructions.begin(),
                instructions.end(),
                [&is_leaf_fastpath_opcode](const Instruction& instruction)
                {
                    return is_leaf_fastpath_opcode(instruction.opcode);
                });
        if (!eligible)
        {
            continue;
        }

        function_leaf_fastpath_kind[function.function_id] = LeafFastpathKind::generic;

        if (instructions.size() == 7 &&
            function.argument_count == 2 &&
            instructions[0].opcode == OpCode::ld_field &&
            instructions[1].opcode == OpCode::mov &&
            instructions[2].opcode == OpCode::add_i32 &&
            instructions[3].opcode == OpCode::st_field &&
            instructions[4].opcode == OpCode::mov &&
            instructions[5].opcode == OpCode::ld_field &&
            instructions[6].opcode == OpCode::ret &&
            instructions[0].left == 0 &&
            instructions[1].left == 1 &&
            instructions[3].destination == 0 &&
            instructions[0].immediate == instructions[3].immediate &&
            instructions[0].immediate == instructions[5].immediate)
        {
            function_leaf_fastpath_kind[function.function_id] = LeafFastpathKind::instance_field_add_argument_return;
            function_leaf_fastpath_field_id[function.function_id] = static_cast<std::uint32_t>(instructions[0].immediate);
        }
    }

    const auto find_function = [&function_lookup](std::uint32_t function_id) -> const Function&
    {
        if (function_id >= function_lookup.size() || function_lookup[function_id] == nullptr)
        {
            throw std::runtime_error("call target does not reference a known function");
        }

        return *function_lookup[function_id];
    };

    const auto* entry_function = &module.functions.front();
    if (module.entry_function_id != 0)
    {
        if (module.entry_function_id >= function_lookup.size() || function_lookup[module.entry_function_id] == nullptr)
        {
            throw std::runtime_error("module entry point does not reference a known function");
        }

        entry_function = function_lookup[module.entry_function_id];
    }

    std::uint32_t max_type_id = 0;
    for (const auto& type : module.types)
    {
        max_type_id = std::max(max_type_id, type.type_id);
    }

    std::vector<const Type*> type_lookup(static_cast<std::size_t>(max_type_id) + 1, nullptr);
    for (const auto& type : module.types)
    {
        type_lookup[type.type_id] = &type;
    }

    std::uint32_t max_field_id = 0;
    for (const auto& field : module.fields)
    {
        max_field_id = std::max(max_field_id, field.field_id);
    }

    std::vector<const Field*> field_lookup(static_cast<std::size_t>(max_field_id) + 1, nullptr);
    for (const auto& field : module.fields)
    {
        field_lookup[field.field_id] = &field;
    }
    for (const auto& function : module.functions)
    {
        if (function_leaf_fastpath_kind[function.function_id] != LeafFastpathKind::instance_field_add_argument_return)
        {
            continue;
        }

        const auto field_id = function_leaf_fastpath_field_id[function.function_id];
        if (field_id >= field_lookup.size() || field_lookup[field_id] == nullptr)
        {
            throw std::runtime_error("field reference is invalid");
        }

        const auto& field = *field_lookup[field_id];
        function_leaf_fastpath_owner_type_id[function.function_id] = field.owner_type_id;
        function_leaf_fastpath_instance_slot[function.function_id] = static_cast<std::uint32_t>(field.instance_slot);
    }

    const auto encode_array_handle = [](std::size_t array_id) -> std::int32_t
    {
        return -static_cast<std::int32_t>(array_id * 4);
    };
    const auto encode_string_handle = [](std::size_t string_id) -> std::int32_t
    {
        return -static_cast<std::int32_t>(string_id * 4 + 1);
    };
    const auto encode_object_handle = [](std::size_t object_id) -> std::int32_t
    {
        return -static_cast<std::int32_t>(object_id * 4 + 2);
    };
    const auto is_array_handle = [](std::int32_t value) -> bool
    {
        return value < 0 && ((-value) % 4) == 0;
    };
    const auto is_string_handle = [](std::int32_t value) -> bool
    {
        return value < 0 && ((-value) % 4) == 1;
    };
    const auto is_object_handle = [](std::int32_t value) -> bool
    {
        return value < 0 && ((-value) % 4) == 2;
    };
    const auto decode_array_id = [](std::int32_t value) -> std::size_t
    {
        return static_cast<std::size_t>((-value) / 4);
    };
    const auto decode_string_id = [](std::int32_t value) -> std::size_t
    {
        return static_cast<std::size_t>((-value - 1) / 4);
    };
    const auto decode_object_id = [](std::int32_t value) -> std::size_t
    {
        return static_cast<std::size_t>((-value - 2) / 4);
    };
    const auto require_array = [&](std::int32_t handle) -> ArrayObject&
    {
        if (handle == 0)
        {
            throw std::runtime_error("null reference array access");
        }

        if (!is_array_handle(handle))
        {
            throw std::runtime_error("instruction expected an array reference");
        }

        const auto array_id = decode_array_id(handle);
        if (array_id >= arrays.size())
        {
            throw std::runtime_error("array reference is invalid");
        }

        return arrays[array_id];
    };
    const auto require_string = [&](std::int32_t handle) -> const std::string&
    {
        if (handle == 0)
        {
            throw std::runtime_error("null reference string access");
        }

        if (!is_string_handle(handle))
        {
            throw std::runtime_error("instruction expected a string reference");
        }

        const auto string_id = decode_string_id(handle);
        if (string_id >= strings.size())
        {
            throw std::runtime_error("string reference is invalid");
        }

        return strings[string_id];
    };
    const auto compare_strings = [&](std::int32_t left, std::int32_t right) -> bool
    {
        if (left == 0 || right == 0)
        {
            return left == right;
        }

        if (!is_string_handle(left) || !is_string_handle(right))
        {
            throw std::runtime_error("string comparison requested for unsupported value kind");
        }

        return require_string(left) == require_string(right);
    };
    const auto require_object = [&](std::int32_t handle) -> ManagedObject&
    {
        if (handle == 0)
        {
            throw std::runtime_error("null reference object access");
        }

        if (!is_object_handle(handle))
        {
            throw std::runtime_error("instruction expected an object reference");
        }

        const auto object_id = decode_object_id(handle);
        if (object_id >= objects.size())
        {
            throw std::runtime_error("object reference is invalid");
        }

        return objects[object_id];
    };
    const auto require_type = [&type_lookup](std::uint32_t type_id) -> const Type&
    {
        if (type_id >= type_lookup.size() || type_lookup[type_id] == nullptr)
        {
            throw std::runtime_error("object allocation references an unknown type");
        }

        return *type_lookup[type_id];
    };
    const auto require_field = [&field_lookup](std::uint32_t field_id) -> const Field&
    {
        if (field_id >= field_lookup.size() || field_lookup[field_id] == nullptr)
        {
            throw std::runtime_error("field reference is invalid");
        }

        return *field_lookup[field_id];
    };
    const auto is_instance_of_type = [&require_type](std::uint32_t actual_type_id, std::uint32_t expected_type_id) -> bool
    {
        auto current_type_id = actual_type_id;
        while (current_type_id != 0)
        {
            if (current_type_id == expected_type_id)
            {
                return true;
            }

            current_type_id = require_type(current_type_id).base_type_id;
        }

        return false;
    };
    const auto resolve_virtual_callee = [&](std::uint32_t callee_id, std::int32_t receiver_handle, std::uint32_t ip) -> std::uint32_t
    {
        const auto& declared_callee = find_function(callee_id);
        if (declared_callee.owner_type_id == 0)
        {
            return callee_id;
        }

        const auto& owner_type = require_type(declared_callee.owner_type_id);
        if (!owner_type.is_interface)
        {
            return callee_id;
        }

        if (!is_object_handle(receiver_handle))
        {
            throw std::runtime_error(
                "instruction expected an object reference during call_virt at ip=" +
                std::to_string(ip) +
                " function=" +
                std::to_string(callee_id) +
                " receiver=" +
                std::to_string(receiver_handle));
        }

        auto& object = require_object(receiver_handle);
        for (const auto& entry : module.interface_dispatch_entries)
        {
            if (entry.owner_type_id == object.type_id &&
                entry.interface_type_id == declared_callee.owner_type_id &&
                entry.interface_method_id == callee_id)
            {
                return entry.implementation_method_id;
            }
        }

        std::string receiver_rows;
        for (const auto& entry : module.interface_dispatch_entries)
        {
            if (entry.owner_type_id != object.type_id)
            {
                continue;
            }

            if (!receiver_rows.empty())
            {
                receiver_rows += " | ";
            }

            const auto interface_method_name = entry.interface_method_id > 0 &&
                entry.interface_method_id <= module.functions.size()
                    ? module.functions[static_cast<std::size_t>(entry.interface_method_id - 1)].name
                    : std::to_string(entry.interface_method_id);
            const auto implementation_method_name = entry.implementation_method_id > 0 &&
                entry.implementation_method_id <= module.functions.size()
                    ? module.functions[static_cast<std::size_t>(entry.implementation_method_id - 1)].name
                    : std::to_string(entry.implementation_method_id);

            receiver_rows +=
                "iface=" + require_type(entry.interface_type_id).name +
                " method=" + interface_method_name +
                " impl=" + implementation_method_name;
        }

        throw std::runtime_error(
            "interface dispatch target is not implemented for receiver type at ip=" +
            std::to_string(ip) +
            " function=" +
            std::to_string(callee_id) +
            " functionName=" +
            declared_callee.name +
            " ownerType=" +
            owner_type.name +
            " receiverType=" +
            std::to_string(object.type_id) +
            " receiverTypeName=" +
            require_type(object.type_id).name +
            " receiverDispatchRows=[" +
            receiver_rows +
            "]");
    };
    const auto require_index = [](std::int32_t index, std::size_t length) -> std::size_t
    {
        if (index < 0 || static_cast<std::size_t>(index) >= length)
        {
            throw std::runtime_error("index out of bounds");
        }

        return static_cast<std::size_t>(index);
    };
    std::uint32_t string_type_id = 0;
    for (const auto& type : module.types)
    {
        if (type.name == "String")
        {
            string_type_id = type.type_id;
            break;
        }
    }
    const auto get_runtime_type_id = [&](std::int32_t value) -> std::uint32_t
    {
        if (value == 0)
        {
            return 0u;
        }

        if (is_object_handle(value))
        {
            return require_object(value).type_id;
        }

        if (is_string_handle(value))
        {
            return string_type_id;
        }

        return 0u;
    };
    const auto runtime_type_matches = [&](std::int32_t value, std::uint32_t target_type_id) -> bool
    {
        if (value == 0 || target_type_id == 0)
        {
            return false;
        }

        const auto& target_type = require_type(target_type_id);
        if (target_type.name == "Object")
        {
            return is_object_handle(value) || is_string_handle(value) || is_array_handle(value);
        }

        if (is_string_handle(value))
        {
            std::uint32_t current_type_id = string_type_id;
            while (current_type_id != 0)
            {
                if (current_type_id == target_type_id)
                {
                    return true;
                }

                current_type_id = require_type(current_type_id).base_type_id;
            }

            return false;
        }

        if (!is_object_handle(value))
        {
            return false;
        }

        const auto& object = require_object(value);
        if (target_type.is_interface)
        {
            for (const auto& entry : module.interface_dispatch_entries)
            {
                if (entry.owner_type_id == object.type_id &&
                    entry.interface_type_id == target_type_id)
                {
                    return true;
                }
            }

            return false;
        }

        std::uint32_t current_type_id = object.type_id;
        while (current_type_id != 0)
        {
            if (current_type_id == target_type_id)
            {
                return true;
            }

            current_type_id = require_type(current_type_id).base_type_id;
        }

        return false;
    };

    const auto execute_leaf_fastpath = [&](const Function& function, const std::int32_t* arguments) -> std::int32_t
    {
        if (function_leaf_fastpath_kind[function.function_id] == LeafFastpathKind::counted_range_sum_return)
        {
            if (profile != nullptr)
            {
                ++profile->specialized_leaf_fastpath_calls;
            }
            const auto iterations = arguments[0];
            std::int32_t index = 0;
            std::int32_t sum = 0;
            while (index < iterations)
            {
                sum += index;
                ++index;
            }

            return sum;
        }

        if (function_leaf_fastpath_kind[function.function_id] == LeafFastpathKind::array_fill_return_last)
        {
            if (profile != nullptr)
            {
                ++profile->specialized_leaf_fastpath_calls;
            }
            const auto iterations = arguments[0];
            if (iterations < 0)
            {
                throw std::runtime_error("array length cannot be negative");
            }

            const auto new_arr_start = std::chrono::steady_clock::now();
            arrays.push_back(ArrayObject {
                .elements = std::vector<std::int32_t>(static_cast<std::size_t>(iterations), 0)
            });
            heap_.record_allocation(sizeof(std::int32_t) * static_cast<std::size_t>(iterations));
            if (profile != nullptr)
            {
                ++profile->new_arr_count;
                ++profile->arrays_created;
                profile->new_arr_execution_ns += static_cast<std::uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - new_arr_start).count());
            }

            auto& array = arrays.back();
            const auto fill_start = std::chrono::steady_clock::now();
            for (std::int32_t index = 0; index < iterations; ++index)
            {
                array.elements[static_cast<std::size_t>(index)] = index;
            }
            if (profile != nullptr)
            {
                profile->st_elem_count += static_cast<std::uint64_t>(iterations);
                const auto fill_ns = static_cast<std::uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - fill_start).count());
                profile->st_elem_execution_ns += fill_ns;
                profile->array_execution_ns += fill_ns;
            }

            if (iterations == 0)
            {
                throw std::runtime_error("index out of bounds");
            }

            const auto read_start = std::chrono::steady_clock::now();
            const auto result = array.elements[static_cast<std::size_t>(iterations - 1)];
            if (profile != nullptr)
            {
                ++profile->ld_elem_count;
                const auto read_ns = static_cast<std::uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - read_start).count());
                profile->ld_elem_execution_ns += read_ns;
                profile->array_execution_ns += read_ns;
            }

            return result;
        }

        if (function_leaf_fastpath_kind[function.function_id] == LeafFastpathKind::array_fill_and_sum_return)
        {
            if (profile != nullptr)
            {
                ++profile->specialized_leaf_fastpath_calls;
            }
            const auto iterations = arguments[0];
            if (iterations < 0)
            {
                throw std::runtime_error("array length cannot be negative");
            }

            const auto new_arr_start = std::chrono::steady_clock::now();
            arrays.push_back(ArrayObject {
                .elements = std::vector<std::int32_t>(static_cast<std::size_t>(iterations), 0)
            });
            heap_.record_allocation(sizeof(std::int32_t) * static_cast<std::size_t>(iterations));
            if (profile != nullptr)
            {
                ++profile->new_arr_count;
                ++profile->arrays_created;
                profile->new_arr_execution_ns += static_cast<std::uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - new_arr_start).count());
            }

            auto& array = arrays.back();
            const auto fill_start = std::chrono::steady_clock::now();
            for (std::int32_t index = 0; index < iterations; ++index)
            {
                array.elements[static_cast<std::size_t>(index)] = index;
            }
            std::uint64_t fill_ns = 0;
            if (profile != nullptr)
            {
                profile->st_elem_count += static_cast<std::uint64_t>(iterations);
                fill_ns = static_cast<std::uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - fill_start).count());
                profile->st_elem_execution_ns += fill_ns;
            }

            const auto sum_start = std::chrono::steady_clock::now();
            std::int32_t sum = 0;
            for (std::int32_t index = 0; index < iterations; ++index)
            {
                sum += array.elements[static_cast<std::size_t>(index)];
            }
            if (profile != nullptr)
            {
                profile->ld_elem_count += static_cast<std::uint64_t>(iterations);
                const auto sum_ns = static_cast<std::uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - sum_start).count());
                profile->ld_elem_execution_ns += sum_ns;
                profile->array_execution_ns += fill_ns + sum_ns;
            }

            return sum;
        }

        if (function_leaf_fastpath_kind[function.function_id] == LeafFastpathKind::instance_field_add_argument_return)
        {
            if (arguments[0] == 0)
            {
                throw std::runtime_error(
                    "null reference object access during specialized leaf fastpath in function '" +
                    function.name +
                    "'");
            }
            if (!is_object_handle(arguments[0]))
            {
                throw std::runtime_error(
                    "instruction expected an object reference during specialized leaf fastpath in function '" +
                    function.name +
                    "' receiver=" +
                    std::to_string(arguments[0]));
            }
            auto& object = require_object(arguments[0]);
            const auto owner_type_id = function_leaf_fastpath_owner_type_id[function.function_id];
            const auto instance_slot = static_cast<std::size_t>(function_leaf_fastpath_instance_slot[function.function_id]);
            if (!is_instance_of_type(object.type_id, owner_type_id) || instance_slot >= object.fields.size())
            {
                throw std::runtime_error("specialized leaf fastpath receiver mismatch");
            }

            const auto result = object.fields[instance_slot] + arguments[1];
            object.fields[instance_slot] = result;
            if (profile != nullptr)
            {
                ++profile->specialized_leaf_fastpath_calls;
            }

            return result;
        }

        std::array<std::int32_t, 16> registers {};
        for (std::size_t index = 0; index < function.argument_count; ++index)
        {
            registers[index] = arguments[index];
        }

        std::size_t ip = 0;
        while (ip < function.instructions.size())
        {
            const auto& instruction = function.instructions[ip];
            if (profile != nullptr)
            {
                ++profile->instructions_executed;
            }
            switch (instruction.opcode)
            {
                case OpCode::nop:
                    ++ip;
                    break;
                case OpCode::ld_i32:
                    registers[instruction.destination] = instruction.immediate;
                    ++ip;
                    break;
                case OpCode::mov:
                    if (profile != nullptr)
                    {
                        ++profile->mov_count;
                    }
                    if (profile != nullptr && (profile->mov_count & mov_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        registers[instruction.destination] = registers[instruction.left];
                        profile->mov_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (mov_timing_sample_mask + 1);
                        ++ip;
                        break;
                    }
                    registers[instruction.destination] = registers[instruction.left];
                    ++ip;
                    break;
                case OpCode::add_i32:
                    registers[instruction.destination] = registers[instruction.left] + registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::shl_i32:
                    registers[instruction.destination] = registers[instruction.left] << registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::shr_i32:
                    registers[instruction.destination] = registers[instruction.left] >> registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::and_i32:
                    registers[instruction.destination] = registers[instruction.left] & registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::or_i32:
                    registers[instruction.destination] = registers[instruction.left] | registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::not_i32:
                    registers[instruction.destination] = ~registers[instruction.left];
                    ++ip;
                    break;
                case OpCode::sub_i32:
                    registers[instruction.destination] = registers[instruction.left] - registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::mul_i32:
                    registers[instruction.destination] = registers[instruction.left] * registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::div_i32:
                    if (registers[instruction.right] == 0)
                    {
                        throw std::runtime_error("division by zero");
                    }
                    registers[instruction.destination] = registers[instruction.left] / registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::mod_i32:
                    if (registers[instruction.right] == 0)
                    {
                        throw std::runtime_error("division by zero");
                    }
                    registers[instruction.destination] = registers[instruction.left] % registers[instruction.right];
                    ++ip;
                    break;
                case OpCode::cmp_eq_i32:
                    if (profile != nullptr)
                    {
                        ++profile->compare_count;
                    }
                    if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        registers[instruction.destination] = registers[instruction.left] == registers[instruction.right] ? 1 : 0;
                        profile->compare_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (compare_timing_sample_mask + 1);
                        ++ip;
                        break;
                    }
                    registers[instruction.destination] = registers[instruction.left] == registers[instruction.right] ? 1 : 0;
                    ++ip;
                    break;
                case OpCode::cmp_ne_i32:
                    if (profile != nullptr)
                    {
                        ++profile->compare_count;
                    }
                    if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        registers[instruction.destination] = registers[instruction.left] != registers[instruction.right] ? 1 : 0;
                        profile->compare_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (compare_timing_sample_mask + 1);
                        ++ip;
                        break;
                    }
                    registers[instruction.destination] = registers[instruction.left] != registers[instruction.right] ? 1 : 0;
                    ++ip;
                    break;
                case OpCode::cmp_lt_i32:
                    if (profile != nullptr)
                    {
                        ++profile->compare_count;
                    }
                    if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        registers[instruction.destination] = registers[instruction.left] < registers[instruction.right] ? 1 : 0;
                        profile->compare_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (compare_timing_sample_mask + 1);
                        ++ip;
                        break;
                    }
                    registers[instruction.destination] = registers[instruction.left] < registers[instruction.right] ? 1 : 0;
                    ++ip;
                    break;
                case OpCode::cmp_le_i32:
                    if (profile != nullptr)
                    {
                        ++profile->compare_count;
                    }
                    if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        registers[instruction.destination] = registers[instruction.left] <= registers[instruction.right] ? 1 : 0;
                        profile->compare_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (compare_timing_sample_mask + 1);
                        ++ip;
                        break;
                    }
                    registers[instruction.destination] = registers[instruction.left] <= registers[instruction.right] ? 1 : 0;
                    ++ip;
                    break;
                case OpCode::cmp_gt_i32:
                    if (profile != nullptr)
                    {
                        ++profile->compare_count;
                    }
                    if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        registers[instruction.destination] = registers[instruction.left] > registers[instruction.right] ? 1 : 0;
                        profile->compare_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (compare_timing_sample_mask + 1);
                        ++ip;
                        break;
                    }
                    registers[instruction.destination] = registers[instruction.left] > registers[instruction.right] ? 1 : 0;
                    ++ip;
                    break;
                case OpCode::cmp_ge_i32:
                    if (profile != nullptr)
                    {
                        ++profile->compare_count;
                    }
                    if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        registers[instruction.destination] = registers[instruction.left] >= registers[instruction.right] ? 1 : 0;
                        profile->compare_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (compare_timing_sample_mask + 1);
                        ++ip;
                        break;
                    }
                    registers[instruction.destination] = registers[instruction.left] >= registers[instruction.right] ? 1 : 0;
                    ++ip;
                    break;
                case OpCode::cmp_eq_ref:
                    registers[instruction.destination] = registers[instruction.left] == registers[instruction.right] ? 1 : 0;
                    ++ip;
                    break;
                case OpCode::cmp_ne_ref:
                    registers[instruction.destination] = registers[instruction.left] != registers[instruction.right] ? 1 : 0;
                    ++ip;
                    break;
                case OpCode::ld_field:
                {
                    const auto& field = require_field(static_cast<std::uint32_t>(instruction.immediate));
                    if (field.is_static)
                    {
                        throw std::runtime_error("ld_field cannot target a static field");
                    }

                    if (registers[instruction.left] == 0)
                    {
                        throw std::runtime_error(
                            "null reference object access during ld_field at ip=" +
                            std::to_string(ip) +
                            " field=" +
                                std::to_string(instruction.immediate));
                    }
                    if (!is_object_handle(registers[instruction.left]))
                    {
                        throw std::runtime_error(
                            "instruction expected an object reference during ld_field in function '" +
                            function.name +
                            "' at ip=" +
                            std::to_string(ip) +
                            " field=" +
                            std::to_string(instruction.immediate) +
                            " receiver=" +
                            std::to_string(registers[instruction.left]));
                    }

                    auto& object = require_object(registers[instruction.left]);
                    if (!is_instance_of_type(object.type_id, field.owner_type_id) || field.instance_slot >= object.fields.size())
                    {
                        throw std::runtime_error(
                            "field load targets the wrong receiver type in function '" +
                            function.name +
                            "' at ip=" +
                            std::to_string(ip) +
                            " field=" +
                            std::to_string(instruction.immediate) +
                            " receiver=" +
                            std::to_string(registers[instruction.left]) +
                            " actual_type=" +
                            std::to_string(object.type_id) +
                            " expected_type=" +
                            std::to_string(field.owner_type_id) +
                            " field_slot=" +
                            std::to_string(field.instance_slot) +
                            " field_count=" +
                            std::to_string(object.fields.size()));
                    }

                    registers[instruction.destination] = object.fields[field.instance_slot];
                    ++ip;
                    break;
                }
                case OpCode::st_field:
                {
                    const auto& field = require_field(static_cast<std::uint32_t>(instruction.immediate));
                    if (field.is_static)
                    {
                        throw std::runtime_error("st_field cannot target a static field");
                    }

                    if (registers[instruction.destination] == 0)
                    {
                        throw std::runtime_error(
                            "null reference object access during st_field at ip=" +
                            std::to_string(ip) +
                            " field=" +
                                std::to_string(instruction.immediate));
                    }
                    if (!is_object_handle(registers[instruction.destination]))
                    {
                        throw std::runtime_error(
                            "instruction expected an object reference during st_field in function '" +
                            function.name +
                            "' at ip=" +
                            std::to_string(ip) +
                            " field=" +
                            std::to_string(instruction.immediate) +
                            " receiver=" +
                            std::to_string(registers[instruction.destination]));
                    }

                    auto& object = require_object(registers[instruction.destination]);
                    if (!is_instance_of_type(object.type_id, field.owner_type_id) || field.instance_slot >= object.fields.size())
                    {
                        throw std::runtime_error(
                            "field store targets the wrong receiver type in function '" +
                            function.name +
                            "' at ip=" +
                            std::to_string(ip) +
                            " field=" +
                            std::to_string(instruction.immediate) +
                            " receiver=" +
                            std::to_string(registers[instruction.destination]) +
                            " actual_type=" +
                            std::to_string(object.type_id) +
                            " expected_type=" +
                            std::to_string(field.owner_type_id) +
                            " field_slot=" +
                            std::to_string(field.instance_slot) +
                            " field_count=" +
                            std::to_string(object.fields.size()));
                    }

                    object.fields[field.instance_slot] = registers[instruction.left];
                    ++ip;
                    break;
                }
                case OpCode::ld_sfield:
                {
                    const auto field_id = static_cast<std::size_t>(instruction.immediate);
                    if (field_id >= static_fields.size())
                    {
                        throw std::runtime_error("static field does not reference a known field");
                    }

                    registers[instruction.destination] = static_fields[field_id];
                    ++ip;
                    break;
                }
                case OpCode::st_sfield:
                {
                    const auto field_id = static_cast<std::size_t>(instruction.immediate);
                    if (field_id >= static_fields.size())
                    {
                        throw std::runtime_error("static field does not reference a known field");
                    }

                    static_fields[field_id] = registers[instruction.left];
                    ++ip;
                    break;
                }
                case OpCode::br:
                    if (profile != nullptr)
                    {
                        ++profile->branch_count;
                    }
                    if (profile != nullptr && (profile->branch_count & branch_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        ip = static_cast<std::size_t>(instruction.immediate);
                        profile->branch_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (branch_timing_sample_mask + 1);
                        break;
                    }
                    ip = static_cast<std::size_t>(instruction.immediate);
                    break;
                case OpCode::br_false:
                    if (profile != nullptr)
                    {
                        ++profile->branch_count;
                    }
                    if (profile != nullptr && (profile->branch_count & branch_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        if (registers[instruction.destination] == 0)
                        {
                            ip = static_cast<std::size_t>(instruction.immediate);
                        }
                        else
                        {
                            ++ip;
                        }
                        profile->branch_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (branch_timing_sample_mask + 1);
                        break;
                    }
                    if (registers[instruction.destination] == 0)
                    {
                        ip = static_cast<std::size_t>(instruction.immediate);
                    }
                    else
                    {
                        ++ip;
                    }
                    break;
                case OpCode::ret:
                    return function.returns_value ? registers[function.argument_count] : 0;
                default:
                    throw std::runtime_error("unsupported opcode in leaf fastpath");
            }
        }

        return 0;
    };

    std::vector<std::vector<std::int32_t>> register_pool;

    std::function<std::int32_t(const Function&, std::int32_t*, std::size_t, std::size_t)> execute_function;
    execute_function = [&](const Function& function, std::int32_t* arguments, std::size_t argument_count, std::size_t call_depth) -> std::int32_t
    {
        if (profile != nullptr)
        {
            ++profile->functions_executed;
            profile->max_call_depth = std::max(profile->max_call_depth, static_cast<std::uint64_t>(call_depth));
        }

        if (argument_count != function.argument_count)
        {
            throw std::runtime_error("call target argument count mismatch");
        }

        if (function.host_import_kind != HostImportKind::none)
        {
            const auto host_start = std::chrono::steady_clock::now();
            if (profile != nullptr)
            {
                ++profile->host_import_calls;
            }

            switch (function.host_import_kind)
            {
                case HostImportKind::console_write:
                    host_services_.console_write(require_string(arguments[0]));
                    return 0;
                case HostImportKind::console_write_line:
                    host_services_.console_write_line(require_string(arguments[0]));
                    return 0;
                case HostImportKind::console_read_line:
                    strings.push_back(host_services_.console_read_line());
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_command_line_args:
                {
                    const auto command_line_args = host_services_.get_command_line_args();
                    arrays.push_back(ArrayObject {
                        .elements = std::vector<std::int32_t>(command_line_args.size(), 0)
                    });
                    if (profile != nullptr)
                    {
                        ++profile->arrays_created;
                    }
                    heap_.record_allocation(sizeof(std::int32_t) * command_line_args.size());
                    auto& array = arrays.back();
                    for (std::size_t index = 0; index < command_line_args.size(); ++index)
                    {
                        strings.push_back(command_line_args[index]);
                        if (profile != nullptr)
                        {
                            ++profile->strings_created;
                        }
                        array.elements[index] = encode_string_handle(strings.size() - 1);
                    }

                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_array_handle(arrays.size() - 1);
                }
                case HostImportKind::environment_get_current_working_directory:
                    strings.push_back(host_services_.get_current_working_directory());
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_environment_variable:
                    strings.push_back(host_services_.get_environment_variable(require_string(arguments[0])));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_set_environment_variable:
                    host_services_.set_environment_variable(require_string(arguments[0]), require_string(arguments[1]));
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
                case HostImportKind::environment_get_user_name:
                    strings.push_back(host_services_.get_user_name());
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_machine_name:
                    strings.push_back(host_services_.get_machine_name());
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_home_directory:
                    strings.push_back(host_services_.get_home_directory());
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_temp_directory:
                    strings.push_back(host_services_.get_temp_directory());
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::clock_get_monotonic_milliseconds_text:
                    strings.push_back(std::to_string(host_services_.get_monotonic_timestamp_ms()));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::clock_get_wall_milliseconds_text:
                    strings.push_back(std::to_string(host_services_.get_wall_timestamp_ms()));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::clock_get_wall_datetime_text:
                    strings.push_back(host_services_.get_wall_datetime_text());
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::file_exists:
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return host_services_.file_exists(require_string(arguments[0])) ? 1 : 0;
                case HostImportKind::file_read_all_text:
                    strings.push_back(host_services_.file_read_all_text(require_string(arguments[0])));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::file_write_all_text:
                    host_services_.file_write_all_text(require_string(arguments[0]), require_string(arguments[1]));
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
                case HostImportKind::file_append_all_text:
                    host_services_.file_append_all_text(require_string(arguments[0]), require_string(arguments[1]));
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
                case HostImportKind::path_combine:
                    strings.push_back(host_services_.path_combine(require_string(arguments[0]), require_string(arguments[1])));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::path_get_file_name:
                    strings.push_back(host_services_.path_get_file_name(require_string(arguments[0])));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::path_get_directory_name:
                    strings.push_back(host_services_.path_get_directory_name(require_string(arguments[0])));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::path_get_extension:
                    strings.push_back(host_services_.path_get_extension(require_string(arguments[0])));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::none:
                    break;
            }
        }

        if (call_depth >= register_pool.size())
        {
            register_pool.emplace_back();
        }

        auto& registers = register_pool[call_depth];
        const auto register_count = static_cast<std::size_t>(function.register_count == 0 ? 1 : function.register_count);
        registers.resize(register_count);
        std::fill(registers.begin(), registers.end(), 0);
        auto* register_values = registers.data();
        bool has_current_exception = false;
        std::int32_t current_exception_value = 0;
        for (std::size_t index = 0; index < argument_count; ++index)
        {
            registers[index] = arguments[index];
        }

        auto copy_back_arguments = [&]()
        {
            if (arguments == nullptr)
            {
                return;
            }

            for (std::size_t index = 0; index < argument_count; ++index)
            {
                arguments[index] = register_values[index];
            }
        };

        std::size_t ip = 0;
        while (ip < function.instructions.size())
        {
            try
            {
                const auto& instruction = function.instructions[ip];
                if (profile != nullptr)
                {
                    ++profile->instructions_executed;
                }
                switch (instruction.opcode)
                {
                    case OpCode::nop:
                        ++ip;
                        break;
                    case OpCode::ld_i32:
                        register_values[instruction.destination] = instruction.immediate;
                        ++ip;
                        break;
                    case OpCode::ld_str:
                    {
                        const auto string_id = static_cast<std::size_t>(instruction.immediate);
                        if (string_id >= strings.size())
                        {
                            throw std::runtime_error("string constant does not reference a known string");
                        }

                        register_values[instruction.destination] = encode_string_handle(string_id);
                        ++ip;
                        break;
                    }
                    case OpCode::slice_str:
                    {
                        const auto& text = require_string(register_values[instruction.left]);
                        const auto start = require_index(register_values[instruction.right], text.size());
                        const auto end = require_index(register_values[static_cast<std::size_t>(instruction.immediate)], text.size());
                        if (end < start)
                        {
                            throw std::runtime_error("string slice range is invalid");
                        }

                        strings.push_back(text.substr(start, end - start + 1));
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::mov:
                        if (profile != nullptr)
                        {
                            ++profile->mov_count;
                        }
                        if (profile != nullptr && (profile->mov_count & mov_timing_sample_mask) == 0)
                        {
                            const auto op_start = std::chrono::steady_clock::now();
                            register_values[instruction.destination] = register_values[instruction.left];
                            profile->mov_execution_ns += static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                                (mov_timing_sample_mask + 1);
                            ++ip;
                            break;
                        }
                        register_values[instruction.destination] = register_values[instruction.left];
                        ++ip;
                        break;
                    case OpCode::add_i32:
                        register_values[instruction.destination] = register_values[instruction.left] + register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::shl_i32:
                        register_values[instruction.destination] = register_values[instruction.left] << register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::shr_i32:
                        register_values[instruction.destination] = register_values[instruction.left] >> register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::and_i32:
                        register_values[instruction.destination] = register_values[instruction.left] & register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::or_i32:
                        register_values[instruction.destination] = register_values[instruction.left] | register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::not_i32:
                        register_values[instruction.destination] = ~register_values[instruction.left];
                        ++ip;
                        break;
                    case OpCode::sub_i32:
                        register_values[instruction.destination] = register_values[instruction.left] - register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::mul_i32:
                        register_values[instruction.destination] = register_values[instruction.left] * register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::div_i32:
                        if (register_values[instruction.right] == 0)
                        {
                            throw std::runtime_error("division by zero");
                        }

                        register_values[instruction.destination] = register_values[instruction.left] / register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::mod_i32:
                        if (register_values[instruction.right] == 0)
                        {
                            throw std::runtime_error("division by zero");
                        }

                        register_values[instruction.destination] = register_values[instruction.left] % register_values[instruction.right];
                        ++ip;
                        break;
                    case OpCode::cmp_eq_i32:
                        if (profile != nullptr)
                        {
                            ++profile->compare_count;
                        }
                        if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                        {
                            const auto op_start = std::chrono::steady_clock::now();
                            register_values[instruction.destination] = register_values[instruction.left] == register_values[instruction.right] ? 1 : 0;
                            profile->compare_execution_ns += static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                                (compare_timing_sample_mask + 1);
                            ++ip;
                            break;
                        }
                        register_values[instruction.destination] = register_values[instruction.left] == register_values[instruction.right] ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_ne_i32:
                        if (profile != nullptr)
                        {
                            ++profile->compare_count;
                        }
                        register_values[instruction.destination] = register_values[instruction.left] != register_values[instruction.right] ? 1 : 0;
                        if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                        {
                            const auto op_start = std::chrono::steady_clock::now();
                            register_values[instruction.destination] = register_values[instruction.left] != register_values[instruction.right] ? 1 : 0;
                            profile->compare_execution_ns += static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                                (compare_timing_sample_mask + 1);
                        }
                        ++ip;
                        break;
                    case OpCode::cmp_lt_i32:
                        if (profile != nullptr)
                        {
                            ++profile->compare_count;
                        }
                        if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                        {
                            const auto op_start = std::chrono::steady_clock::now();
                            register_values[instruction.destination] = register_values[instruction.left] < register_values[instruction.right] ? 1 : 0;
                            profile->compare_execution_ns += static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                                (compare_timing_sample_mask + 1);
                            ++ip;
                            break;
                        }
                        register_values[instruction.destination] = register_values[instruction.left] < register_values[instruction.right] ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_le_i32:
                        if (profile != nullptr)
                        {
                            ++profile->compare_count;
                        }
                        register_values[instruction.destination] = register_values[instruction.left] <= register_values[instruction.right] ? 1 : 0;
                        if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                        {
                            const auto op_start = std::chrono::steady_clock::now();
                            register_values[instruction.destination] = register_values[instruction.left] <= register_values[instruction.right] ? 1 : 0;
                            profile->compare_execution_ns += static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                                (compare_timing_sample_mask + 1);
                        }
                        ++ip;
                        break;
                    case OpCode::cmp_gt_i32:
                        if (profile != nullptr)
                        {
                            ++profile->compare_count;
                        }
                        register_values[instruction.destination] = register_values[instruction.left] > register_values[instruction.right] ? 1 : 0;
                        if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                        {
                            const auto op_start = std::chrono::steady_clock::now();
                            register_values[instruction.destination] = register_values[instruction.left] > register_values[instruction.right] ? 1 : 0;
                            profile->compare_execution_ns += static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                                (compare_timing_sample_mask + 1);
                        }
                        ++ip;
                        break;
                    case OpCode::cmp_ge_i32:
                        if (profile != nullptr)
                        {
                            ++profile->compare_count;
                        }
                        if (profile != nullptr && (profile->compare_count & compare_timing_sample_mask) == 0)
                        {
                            const auto op_start = std::chrono::steady_clock::now();
                            register_values[instruction.destination] = register_values[instruction.left] >= register_values[instruction.right] ? 1 : 0;
                            profile->compare_execution_ns += static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                                (compare_timing_sample_mask + 1);
                            ++ip;
                            break;
                        }
                        register_values[instruction.destination] = register_values[instruction.left] >= register_values[instruction.right] ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_eq_str:
                        register_values[instruction.destination] = compare_strings(register_values[instruction.left], register_values[instruction.right]) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_ne_str:
                        register_values[instruction.destination] = compare_strings(register_values[instruction.left], register_values[instruction.right]) ? 0 : 1;
                        ++ip;
                        break;
                    case OpCode::cmp_eq_ref:
                        register_values[instruction.destination] = register_values[instruction.left] == register_values[instruction.right] ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_ne_ref:
                        register_values[instruction.destination] = register_values[instruction.left] != register_values[instruction.right] ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::is_type_ref:
                        register_values[instruction.destination] =
                            runtime_type_matches(register_values[instruction.left], static_cast<std::uint32_t>(instruction.immediate)) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::as_type_ref:
                        register_values[instruction.destination] =
                            runtime_type_matches(register_values[instruction.left], static_cast<std::uint32_t>(instruction.immediate))
                                ? register_values[instruction.left]
                                : 0;
                        ++ip;
                        break;
                    case OpCode::concat_str:
                    {
                        const auto& left = require_string(register_values[instruction.left]);
                        const auto& right = require_string(register_values[instruction.right]);
                        strings.push_back(left + right);
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::starts_with_str:
                    {
                        const auto& left = require_string(register_values[instruction.left]);
                        const auto& right = require_string(register_values[instruction.right]);
                        register_values[instruction.destination] =
                            left.size() >= right.size() && left.compare(0, right.size(), right) == 0 ? 1 : 0;
                        ++ip;
                        break;
                    }
                    case OpCode::ends_with_str:
                    {
                        const auto& left = require_string(register_values[instruction.left]);
                        const auto& right = require_string(register_values[instruction.right]);
                        register_values[instruction.destination] =
                            left.size() >= right.size() &&
                            left.compare(left.size() - right.size(), right.size(), right) == 0 ? 1 : 0;
                        ++ip;
                        break;
                    }
                    case OpCode::contains_str:
                    {
                        const auto& left = require_string(register_values[instruction.left]);
                        const auto& right = require_string(register_values[instruction.right]);
                        register_values[instruction.destination] = left.find(right) != std::string::npos ? 1 : 0;
                        ++ip;
                        break;
                    }
                    case OpCode::index_of_str:
                    {
                        const auto& left = require_string(register_values[instruction.left]);
                        const auto& right = require_string(register_values[instruction.right]);
                        const auto position = left.find(right);
                        register_values[instruction.destination] =
                            position == std::string::npos ? -1 : static_cast<std::int32_t>(position);
                        ++ip;
                        break;
                    }
                    case OpCode::last_index_of_str:
                    {
                        const auto& left = require_string(register_values[instruction.left]);
                        const auto& right = require_string(register_values[instruction.right]);
                        const auto position = left.rfind(right);
                        register_values[instruction.destination] =
                            position == std::string::npos ? -1 : static_cast<std::int32_t>(position);
                        ++ip;
                        break;
                    }
                    case OpCode::replace_str:
                    {
                        const auto& source = require_string(register_values[instruction.left]);
                        const auto& old_value = require_string(register_values[instruction.right]);
                        const auto& new_value = require_string(register_values[static_cast<std::size_t>(instruction.immediate)]);
                        std::string replaced = source;
                        if (!old_value.empty())
                        {
                            std::size_t search_position = 0;
                            while ((search_position = replaced.find(old_value, search_position)) != std::string::npos)
                            {
                                replaced.replace(search_position, old_value.size(), new_value);
                                search_position += new_value.size();
                            }
                        }

                        strings.push_back(std::move(replaced));
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::insert_str:
                    {
                        const auto& source = require_string(register_values[instruction.left]);
                        const auto index = register_values[instruction.right];
                        if (index < 0 || static_cast<std::size_t>(index) > source.size())
                        {
                            throw std::runtime_error("string insert index out of bounds");
                        }

                        const auto& value = require_string(register_values[static_cast<std::size_t>(instruction.immediate)]);
                        std::string inserted = source;
                        inserted.insert(static_cast<std::size_t>(index), value);
                        strings.push_back(std::move(inserted));
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::remove_str:
                    {
                        const auto& source = require_string(register_values[instruction.left]);
                        const auto index = register_values[instruction.right];
                        const auto length = register_values[static_cast<std::size_t>(instruction.immediate)];
                        if (index < 0 || length < 0)
                        {
                            throw std::runtime_error("string remove range is invalid");
                        }

                        const auto start = static_cast<std::size_t>(index);
                        const auto count = static_cast<std::size_t>(length);
                        if (start > source.size() || start + count > source.size())
                        {
                            throw std::runtime_error("string remove range is invalid");
                        }

                        std::string removed = source;
                        removed.erase(start, count);
                        strings.push_back(std::move(removed));
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::to_upper_str:
                    {
                        const auto& source = require_string(register_values[instruction.left]);
                        std::string upper = source;
                        std::transform(
                            upper.begin(),
                            upper.end(),
                            upper.begin(),
                            [](unsigned char character) { return static_cast<char>(std::toupper(character)); });
                        strings.push_back(std::move(upper));
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::to_lower_str:
                    {
                        const auto& source = require_string(register_values[instruction.left]);
                        std::string lower = source;
                        std::transform(
                            lower.begin(),
                            lower.end(),
                            lower.begin(),
                            [](unsigned char character) { return static_cast<char>(std::tolower(character)); });
                        strings.push_back(std::move(lower));
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::trim_str:
                    case OpCode::trim_start_str:
                    case OpCode::trim_end_str:
                    {
                        const auto& source = require_string(register_values[instruction.left]);
                        std::size_t start = 0;
                        std::size_t end = source.size();

                        if (instruction.opcode == OpCode::trim_str || instruction.opcode == OpCode::trim_start_str)
                        {
                            while (start < end && std::isspace(static_cast<unsigned char>(source[start])) != 0)
                            {
                                ++start;
                            }
                        }

                        if (instruction.opcode == OpCode::trim_str || instruction.opcode == OpCode::trim_end_str)
                        {
                            while (end > start && std::isspace(static_cast<unsigned char>(source[end - 1])) != 0)
                            {
                                --end;
                            }
                        }

                        strings.push_back(source.substr(start, end - start));
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::str_to_i32:
                    {
                        const auto& source = require_string(register_values[instruction.left]);
                        std::size_t consumed = 0;
                        const auto value = std::stoi(source, &consumed, 10);
                        if (consumed != source.size())
                        {
                            throw std::runtime_error("string does not contain a valid integer");
                        }

                        register_values[instruction.destination] = value;
                        ++ip;
                        break;
                    }
                    case OpCode::try_str_to_i32:
                    {
                        const auto& source = require_string(register_values[instruction.left]);
                        try
                        {
                            std::size_t consumed = 0;
                            const auto value = std::stoi(source, &consumed, 10);
                            if (consumed != source.size())
                            {
                                register_values[instruction.right] = 0;
                                register_values[instruction.destination] = 0;
                            }
                            else
                            {
                                register_values[instruction.right] = value;
                                register_values[instruction.destination] = 1;
                            }
                        }
                        catch (const std::exception&)
                        {
                            register_values[instruction.right] = 0;
                            register_values[instruction.destination] = 0;
                        }

                        ++ip;
                        break;
                    }
                    case OpCode::i32_to_str:
                    {
                        strings.push_back(std::to_string(register_values[instruction.left]));
                        register_values[instruction.destination] = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::call:
                    {
                        if (profile != nullptr)
                        {
                            ++profile->call_count;
                        }
                        const auto callee_id = static_cast<std::uint32_t>(instruction.immediate);
                        if (callee_id >= function_lookup.size() || function_lookup[callee_id] == nullptr)
                        {
                            throw std::runtime_error("call target does not reference a known function");
                        }

                        const bool sample_call_timing =
                            profile != nullptr && (profile->call_count & call_timing_sample_mask) == 0;
                        const auto call_start = sample_call_timing ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point {};
                        std::int32_t result = 0;
                        const auto leaf_fastpath_kind = function_leaf_fastpath_kind[callee_id];
                        if (leaf_fastpath_kind == LeafFastpathKind::instance_field_add_argument_return)
                        {
                            const auto receiver_handle = register_values[instruction.left];
                            if (receiver_handle == 0)
                            {
                                throw std::runtime_error("null reference method call");
                            }
                            if (!is_object_handle(receiver_handle))
                            {
                                throw std::runtime_error(
                                    "instruction expected an object reference during call leaf fastpath at ip=" +
                                    std::to_string(ip) +
                                    " function=" +
                                    std::to_string(callee_id) +
                                    " receiver=" +
                                    std::to_string(receiver_handle));
                            }

                            const auto object_id = decode_object_id(receiver_handle);
                            if (object_id >= objects.size())
                            {
                                throw std::runtime_error("object reference is invalid");
                            }

                            auto& object = objects[object_id];
                            const auto instance_slot = static_cast<std::size_t>(function_leaf_fastpath_instance_slot[callee_id]);
                            if (!is_instance_of_type(object.type_id, function_leaf_fastpath_owner_type_id[callee_id]) ||
                                instance_slot >= object.fields.size())
                            {
                                throw std::runtime_error("specialized leaf fastpath receiver mismatch");
                            }

                            result = object.fields[instance_slot] + register_values[static_cast<std::size_t>(instruction.left) + 1];
                            object.fields[instance_slot] = result;
                            if (profile != nullptr)
                            {
                                ++profile->leaf_fastpath_calls;
                                ++profile->specialized_leaf_fastpath_calls;
                            }
                        }
                        else if (leaf_fastpath_kind != LeafFastpathKind::none)
                        {
                            const auto& callee = *function_lookup[callee_id];
                            result = execute_leaf_fastpath(callee, register_values + instruction.left);
                            if (profile != nullptr)
                            {
                                ++profile->leaf_fastpath_calls;
                            }
                        }
                        else
                        {
                            const auto& callee = *function_lookup[callee_id];
                            result = execute_function(
                                callee,
                                register_values + instruction.left,
                                instruction.right,
                                call_depth + 1);
                        }

                        if (sample_call_timing)
                        {
                            const auto elapsed_ns = static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - call_start).count());
                            profile->call_execution_ns += elapsed_ns * (call_timing_sample_mask + 1);
                            if (leaf_fastpath_kind != LeafFastpathKind::none)
                            {
                                profile->leaf_fastpath_execution_ns += elapsed_ns * (call_timing_sample_mask + 1);
                            }
                        }
                        if (function_returns_value[callee_id] != 0)
                        {
                            register_values[instruction.destination] = result;
                        }

                        ++ip;
                        break;
                    }
                    case OpCode::call_virt:
                    {
                        if (profile != nullptr)
                        {
                            ++profile->call_virt_count;
                        }
                        if (register_values[instruction.left] == 0)
                        {
                            throw std::runtime_error(
                                "null reference method call at ip=" +
                                std::to_string(ip) +
                                " function=" +
                                std::to_string(instruction.immediate));
                        }

                        const auto declared_callee_id = static_cast<std::uint32_t>(instruction.immediate);
                        if (declared_callee_id >= function_lookup.size() || function_lookup[declared_callee_id] == nullptr)
                        {
                            throw std::runtime_error("call target does not reference a known function");
                        }

                        const auto callee_id = resolve_virtual_callee(declared_callee_id, register_values[instruction.left], ip);

                        const bool sample_call_timing =
                            profile != nullptr && (profile->call_virt_count & call_timing_sample_mask) == 0;
                        const auto call_start = sample_call_timing ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point {};
                        std::int32_t result = 0;
                        const auto leaf_fastpath_kind = function_leaf_fastpath_kind[callee_id];
                        if (leaf_fastpath_kind == LeafFastpathKind::instance_field_add_argument_return)
                        {
                            const auto receiver_handle = register_values[instruction.left];
                            if (!is_object_handle(receiver_handle))
                            {
                                throw std::runtime_error(
                                    "instruction expected an object reference during call_virt leaf fastpath at ip=" +
                                    std::to_string(ip) +
                                    " function=" +
                                    std::to_string(callee_id) +
                                    " receiver=" +
                                    std::to_string(receiver_handle));
                            }

                            const auto object_id = decode_object_id(receiver_handle);
                            if (object_id >= objects.size())
                            {
                                throw std::runtime_error("object reference is invalid");
                            }

                            auto& object = objects[object_id];
                            const auto instance_slot = static_cast<std::size_t>(function_leaf_fastpath_instance_slot[callee_id]);
                            if (!is_instance_of_type(object.type_id, function_leaf_fastpath_owner_type_id[callee_id]) ||
                                instance_slot >= object.fields.size())
                            {
                                throw std::runtime_error("specialized leaf fastpath receiver mismatch");
                            }

                            result = object.fields[instance_slot] + register_values[static_cast<std::size_t>(instruction.left) + 1];
                            object.fields[instance_slot] = result;
                            if (profile != nullptr)
                            {
                                ++profile->leaf_fastpath_calls;
                                ++profile->specialized_leaf_fastpath_calls;
                            }
                        }
                        else if (leaf_fastpath_kind != LeafFastpathKind::none)
                        {
                            const auto& callee = *function_lookup[callee_id];
                            result = execute_leaf_fastpath(
                                callee,
                                register_values + instruction.left);
                            if (profile != nullptr)
                            {
                                ++profile->leaf_fastpath_calls;
                            }
                        }
                        else
                        {
                            const auto& callee = *function_lookup[callee_id];
                            result = execute_function(
                                callee,
                                register_values + instruction.left,
                                static_cast<std::size_t>(function_argument_count_lookup[callee_id]),
                                call_depth + 1);
                        }

                        if (sample_call_timing)
                        {
                            const auto elapsed_ns = static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - call_start).count());
                            profile->call_virt_execution_ns += elapsed_ns * (call_timing_sample_mask + 1);
                            if (leaf_fastpath_kind != LeafFastpathKind::none)
                            {
                                profile->leaf_fastpath_execution_ns += elapsed_ns * (call_timing_sample_mask + 1);
                            }
                        }
                        if (function_returns_value[callee_id] != 0)
                        {
                            register_values[instruction.destination] = result;
                        }

                        ++ip;
                        break;
                    }
                    case OpCode::new_obj:
                    {
                        if (profile != nullptr)
                        {
                            ++profile->new_obj_count;
                            ++profile->objects_created;
                        }
                        const auto& type = require_type(static_cast<std::uint32_t>(instruction.immediate));
                        objects.push_back(ManagedObject {
                            .type_id = type.type_id,
                            .fields = std::vector<std::int32_t>(type.instance_field_count, 0)
                        });
                        heap_.record_allocation(sizeof(ObjectHeader) + type.instance_field_count * sizeof(std::int32_t));
                        register_values[instruction.destination] = encode_object_handle(objects.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::ld_field:
                    {
                        const auto& field = require_field(static_cast<std::uint32_t>(instruction.immediate));
                        if (field.is_static)
                        {
                            throw std::runtime_error("ld_field cannot target a static field");
                        }

                    if (register_values[instruction.left] == 0)
                    {
                        throw std::runtime_error(
                                "null reference object access during ld_field at ip=" +
                                std::to_string(ip) +
                                " field=" +
                                std::to_string(instruction.immediate));
                    }
                        if (!is_object_handle(register_values[instruction.left]))
                        {
                        throw std::runtime_error(
                            "instruction expected an object reference during ld_field at ip=" +
                            std::to_string(ip) +
                            " field=" +
                            std::to_string(instruction.immediate) +
                            " receiver=" +
                            std::to_string(register_values[instruction.left]));
                    }

                    auto& object = require_object(register_values[instruction.left]);
                        if (!is_instance_of_type(object.type_id, field.owner_type_id))
                        {
                            throw std::runtime_error(
                                "field load targets the wrong receiver type at ip=" +
                                std::to_string(ip) +
                                " field=" +
                                std::to_string(instruction.immediate) +
                                " receiver=" +
                                std::to_string(register_values[instruction.left]) +
                                " actual_type=" +
                                std::to_string(object.type_id) +
                                " expected_type=" +
                                std::to_string(field.owner_type_id));
                        }

                        if (field.instance_slot >= object.fields.size())
                        {
                            throw std::runtime_error("field slot is outside the object layout");
                        }

                        register_values[instruction.destination] = object.fields[field.instance_slot];
                        ++ip;
                        break;
                    }
                    case OpCode::st_field:
                    {
                        const auto& field = require_field(static_cast<std::uint32_t>(instruction.immediate));
                        if (field.is_static)
                        {
                            throw std::runtime_error("st_field cannot target a static field");
                        }

                    if (register_values[instruction.destination] == 0)
                    {
                        throw std::runtime_error(
                                "null reference object access during st_field at ip=" +
                                std::to_string(ip) +
                                " field=" +
                                std::to_string(instruction.immediate));
                    }
                    if (!is_object_handle(register_values[instruction.destination]))
                    {
                        throw std::runtime_error(
                            "instruction expected an object reference during st_field at ip=" +
                            std::to_string(ip) +
                            " field=" +
                            std::to_string(instruction.immediate) +
                            " receiver=" +
                            std::to_string(register_values[instruction.destination]));
                    }

                    auto& object = require_object(register_values[instruction.destination]);
                        if (!is_instance_of_type(object.type_id, field.owner_type_id))
                        {
                            throw std::runtime_error(
                                "field store targets the wrong receiver type at ip=" +
                                std::to_string(ip) +
                                " field=" +
                                std::to_string(instruction.immediate) +
                                " receiver=" +
                                std::to_string(register_values[instruction.destination]) +
                                " actual_type=" +
                                std::to_string(object.type_id) +
                                " expected_type=" +
                                std::to_string(field.owner_type_id));
                        }

                        if (field.instance_slot >= object.fields.size())
                        {
                            throw std::runtime_error("field slot is outside the object layout");
                        }

                        object.fields[field.instance_slot] = register_values[instruction.left];
                        ++ip;
                        break;
                    }
                    case OpCode::ld_sfield:
                    {
                        const auto field_id = static_cast<std::size_t>(instruction.immediate);
                        if (field_id >= static_fields.size())
                        {
                            throw std::runtime_error("static field does not reference a known field");
                        }

                        register_values[instruction.destination] = static_fields[field_id];
                        ++ip;
                        break;
                    }
                    case OpCode::st_sfield:
                    {
                        const auto field_id = static_cast<std::size_t>(instruction.immediate);
                        if (field_id >= static_fields.size())
                        {
                            throw std::runtime_error("static field does not reference a known field");
                        }

                        static_fields[field_id] = register_values[instruction.left];
                        ++ip;
                        break;
                    }
                    case OpCode::new_arr:
                    {
                        if (profile != nullptr)
                        {
                            ++profile->new_arr_count;
                            ++profile->arrays_created;
                        }
                        const bool sample_array_timing =
                            profile != nullptr && (profile->new_arr_count & array_timing_sample_mask) == 0;
                        const auto array_start = sample_array_timing ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point {};
                        const auto length = register_values[instruction.left];
                        if (length < 0)
                        {
                            throw std::runtime_error("array length cannot be negative");
                        }

                        arrays.push_back(ArrayObject { .elements = std::vector<std::int32_t>(static_cast<std::size_t>(length), 0) });
                        register_values[instruction.destination] = encode_array_handle(arrays.size() - 1);
                        if (sample_array_timing)
                        {
                            const auto elapsed_ns = static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - array_start).count());
                            profile->new_arr_execution_ns += elapsed_ns * (array_timing_sample_mask + 1);
                            profile->array_execution_ns += elapsed_ns * (array_timing_sample_mask + 1);
                        }
                        ++ip;
                        break;
                    }
                    case OpCode::ld_elem:
                    {
                        if (profile != nullptr)
                        {
                            ++profile->ld_elem_count;
                        }
                        const bool sample_array_timing =
                            profile != nullptr && (profile->ld_elem_count & array_timing_sample_mask) == 0;
                        const auto array_start = sample_array_timing ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point {};
                        const auto source = register_values[instruction.left];
                        const auto index_value = register_values[instruction.right];
                        if (source == 0)
                        {
                            throw std::runtime_error("null reference element access");
                        }

                        if (is_array_handle(source))
                        {
                            auto& array = require_array(source);
                            const auto index = require_index(index_value, array.elements.size());
                            register_values[instruction.destination] = array.elements[index];
                        }
                        else if (is_string_handle(source))
                        {
                            const auto& text = require_string(source);
                            const auto index = require_index(index_value, text.size());
                            register_values[instruction.destination] = static_cast<std::int32_t>(static_cast<unsigned char>(text[index]));
                        }
                        else
                        {
                            throw std::runtime_error("element load requested for unsupported value kind");
                        }

                        if (sample_array_timing)
                        {
                            const auto elapsed_ns = static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - array_start).count());
                            profile->ld_elem_execution_ns += elapsed_ns * (array_timing_sample_mask + 1);
                            profile->array_execution_ns += elapsed_ns * (array_timing_sample_mask + 1);
                        }
                        ++ip;
                        break;
                    }
                    case OpCode::st_elem:
                    {
                        if (profile != nullptr)
                        {
                            ++profile->st_elem_count;
                        }
                        const bool sample_array_timing =
                            profile != nullptr && (profile->st_elem_count & array_timing_sample_mask) == 0;
                        const auto array_start = sample_array_timing ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point {};
                        if (register_values[instruction.destination] == 0)
                        {
                            throw std::runtime_error(
                                "null reference element access during st_elem at ip=" +
                                std::to_string(ip));
                        }

                        if (!is_array_handle(register_values[instruction.destination]))
                        {
                            throw std::runtime_error(
                                "instruction expected an array reference during st_elem at ip=" +
                                std::to_string(ip) +
                                " handle=" +
                                std::to_string(register_values[instruction.destination]));
                        }

                        auto& array = require_array(register_values[instruction.destination]);
                        const auto index = require_index(register_values[instruction.left], array.elements.size());
                        array.elements[index] = register_values[instruction.right];
                        if (sample_array_timing)
                        {
                            const auto elapsed_ns = static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - array_start).count());
                            profile->st_elem_execution_ns += elapsed_ns * (array_timing_sample_mask + 1);
                            profile->array_execution_ns += elapsed_ns * (array_timing_sample_mask + 1);
                        }
                        ++ip;
                        break;
                    }
                    case OpCode::ld_len:
                    {
                        if (profile != nullptr)
                        {
                            ++profile->ld_len_count;
                        }
                        const bool sample_array_timing =
                            profile != nullptr && (profile->ld_len_count & array_timing_sample_mask) == 0;
                        const auto array_start = sample_array_timing ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point {};
                        const auto value = register_values[instruction.left];
                        if (value == 0)
                        {
                            throw std::runtime_error(
                                "null reference length access during ld_len at ip=" +
                                std::to_string(ip));
                        }

                        if (is_array_handle(value))
                        {
                            register_values[instruction.destination] = static_cast<std::int32_t>(require_array(value).elements.size());
                        }
                        else if (is_string_handle(value))
                        {
                            register_values[instruction.destination] = static_cast<std::int32_t>(require_string(value).size());
                        }
                        else
                        {
                            throw std::runtime_error(
                                "length requested for unsupported value kind during ld_len at ip=" +
                                std::to_string(ip) +
                                " handle=" +
                                std::to_string(value));
                        }

                        if (sample_array_timing)
                        {
                            const auto elapsed_ns = static_cast<std::uint64_t>(
                                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - array_start).count());
                            profile->ld_len_execution_ns += elapsed_ns * (array_timing_sample_mask + 1);
                            profile->array_execution_ns += elapsed_ns * (array_timing_sample_mask + 1);
                        }
                        ++ip;
                        break;
                    }
                    case OpCode::throw_:
                        throw ManagedException { register_values[instruction.destination] };
                    case OpCode::rethrow:
                        if (!has_current_exception)
                        {
                            throw std::runtime_error("rethrow used without an active exception");
                        }

                        throw ManagedException { current_exception_value };
                case OpCode::br:
                    if (profile != nullptr)
                    {
                        ++profile->branch_count;
                    }
                    if (profile != nullptr && (profile->branch_count & branch_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        ip = static_cast<std::size_t>(instruction.immediate);
                        profile->branch_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (branch_timing_sample_mask + 1);
                        break;
                    }
                    ip = static_cast<std::size_t>(instruction.immediate);
                    break;
                case OpCode::br_false:
                    if (profile != nullptr)
                    {
                        ++profile->branch_count;
                    }
                    if (profile != nullptr && (profile->branch_count & branch_timing_sample_mask) == 0)
                    {
                        const auto op_start = std::chrono::steady_clock::now();
                        if (register_values[instruction.destination] == 0)
                        {
                            ip = static_cast<std::size_t>(instruction.immediate);
                        }
                        else
                        {
                            ++ip;
                        }
                        profile->branch_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - op_start).count()) *
                            (branch_timing_sample_mask + 1);
                        break;
                    }
                    if (register_values[instruction.destination] == 0)
                    {
                        ip = static_cast<std::size_t>(instruction.immediate);
                        }
                        else
                        {
                            ++ip;
                        }

                        break;
                    case OpCode::ret:
                        copy_back_arguments();
                        if (!function.returns_value)
                        {
                            return 0;
                        }

                        return register_values[function.argument_count];
                    default:
                        throw std::runtime_error("unsupported opcode");
                }
            }
            catch (const ManagedException& ex)
            {
                const auto handler = std::find_if(
                    function.exception_handlers.begin(),
                    function.exception_handlers.end(),
                    [ip, &ex, &get_runtime_type_id](const Function::ExceptionHandler& candidate)
                    {
                        if (ip < candidate.try_start || ip >= candidate.try_end)
                        {
                            return false;
                        }

                        return candidate.catch_type_id == 0 ||
                            get_runtime_type_id(ex.value) == candidate.catch_type_id;
                    });
                if (handler == function.exception_handlers.end())
                {
                    throw;
                }

                has_current_exception = true;
                current_exception_value = ex.value;
                if (handler->target_register != 0xFFFF)
                {
                    register_values[handler->target_register] = ex.value;
                }

                ip = handler->handler_start;
            }
        }

        copy_back_arguments();
        return 0;
    };

    try
    {
        const auto result = execute_function(*entry_function, nullptr, 0, 0);
        if (profile != nullptr)
        {
            profile->total_execution_ns = static_cast<std::uint64_t>(
                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - total_start).count());
        }

        return result;
    }
    catch (const ManagedException&)
    {
        throw std::runtime_error("unhandled managed exception");
    }
}
} // namespace ilcvm
