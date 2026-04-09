#include "ilcvm/virtual_machine.h"

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <dlfcn.h>
#include <functional>
#include <limits>
#include <memory>
#include <stdexcept>
#include <array>
#include <string>
#include <unordered_map>
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
    return execute(module, static_cast<ExecutionProfile*>(nullptr), nullptr, nullptr, nullptr);
}

std::int32_t VirtualMachine::execute(const Module& module, ExecutionProfile& profile) const
{
    return execute(module, &profile, nullptr, nullptr, nullptr);
}

std::int32_t VirtualMachine::execute(const Module& module, const StackTraceFormatter& stack_trace_formatter) const
{
    return execute(module, static_cast<ExecutionProfile*>(nullptr), nullptr, nullptr, &stack_trace_formatter);
}

std::int32_t VirtualMachine::execute(const Module& module, ExecutionProfile& profile, const StackTraceFormatter& stack_trace_formatter) const
{
    return execute(module, &profile, nullptr, nullptr, &stack_trace_formatter);
}

std::int32_t VirtualMachine::execute(const Module& module, const DebugOptions& debug_options, const DebugSink& debug_sink) const
{
    return execute(module, static_cast<ExecutionProfile*>(nullptr), &debug_options, &debug_sink, nullptr);
}

std::int32_t VirtualMachine::execute(const Module& module, ExecutionProfile& profile, const DebugOptions& debug_options, const DebugSink& debug_sink) const
{
    return execute(module, &profile, &debug_options, &debug_sink, nullptr);
}

std::int32_t VirtualMachine::execute(const Module& module, const DebugOptions& debug_options, const DebugSink& debug_sink, const StackTraceFormatter& stack_trace_formatter) const
{
    return execute(module, static_cast<ExecutionProfile*>(nullptr), &debug_options, &debug_sink, &stack_trace_formatter);
}

std::int32_t VirtualMachine::execute(const Module& module, ExecutionProfile& profile, const DebugOptions& debug_options, const DebugSink& debug_sink, const StackTraceFormatter& stack_trace_formatter) const
{
    return execute(module, &profile, &debug_options, &debug_sink, &stack_trace_formatter);
}

std::int32_t VirtualMachine::execute(const Module& module, ExecutionProfile* profile, const DebugOptions* debug_options, const DebugSink* debug_sink, const StackTraceFormatter* stack_trace_formatter) const
{
    auto execution_state = heap_.create_execution_state(module.strings, module.fields.size() + 1);
    return execute(module, execution_state, profile, debug_options, debug_sink, stack_trace_formatter, 0u, nullptr);
}

std::int32_t VirtualMachine::execute(
    const Module& module,
    const std::shared_ptr<ExecutionState>& execution_state,
    ExecutionProfile* profile,
    const DebugOptions* debug_options,
    const DebugSink* debug_sink,
    const StackTraceFormatter* stack_trace_formatter,
    const std::uint32_t entry_function_override,
    const std::vector<std::int32_t>* entry_arguments) const
{
    const auto total_start = std::chrono::steady_clock::now();
    if (module.functions.empty())
    {
        throw std::runtime_error("module contains no functions");
    }

    auto& static_fields = execution_state->static_fields;
    auto& arrays = execution_state->arrays;
    auto& objects = execution_state->objects;
    auto& strings = execution_state->strings;
    auto& native_handles = execution_state->native_handles;
    auto& native_library_handles = execution_state->native_library_handles;
    auto& native_symbol_handles = execution_state->native_symbol_handles;

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
    std::vector<DebugFrame> debug_call_stack;
    std::vector<std::string> last_stack_trace_lines;
    bool last_stack_trace_from_runtime_error = false;
    bool debug_tracing_active = false;
    std::uint64_t debug_steps_remaining = 0;
    bool debug_step_over_active = false;
    std::size_t debug_step_over_depth = 0;
    bool debug_abort_requested = false;

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
        if (function.host_import_kind != HostImportKind::none || function.dll_import.is_present || !function.exception_handlers.empty())
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
    const auto selected_entry_function_id = entry_function_override != 0 ? entry_function_override : module.entry_function_id;
    if (selected_entry_function_id != 0)
    {
        if (selected_entry_function_id >= function_lookup.size() || function_lookup[selected_entry_function_id] == nullptr)
        {
            throw std::runtime_error("module entry point does not reference a known function");
        }

        entry_function = function_lookup[selected_entry_function_id];
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
    const auto require_array = [&](std::int32_t handle) -> RuntimeArray&
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
    const auto require_object = [&](std::int32_t handle) -> RuntimeObject&
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
            throw std::runtime_error(
                "object allocation references an unknown type: type_id=" +
                std::to_string(type_id) +
                " type_table_size=" +
                std::to_string(type_lookup.size()));
        }

        return *type_lookup[type_id];
    };
    enum class NativeFfiValueKind : std::uint8_t
    {
        void_ = 0,
        i32 = 1,
        bool32 = 2,
        utf8_string = 3,
        native_handle = 4
    };
    const auto get_native_ffi_value_kind = [&require_type](std::uint32_t type_id) -> NativeFfiValueKind
    {
        if (type_id == 0)
        {
            return NativeFfiValueKind::void_;
        }

        const auto& type = require_type(type_id);
        if (type.name == "Integer")
        {
            return NativeFfiValueKind::i32;
        }

        if (type.name == "Boolean")
        {
            return NativeFfiValueKind::bool32;
        }

        if (type.name == "String")
        {
            return NativeFfiValueKind::utf8_string;
        }

        if (type.name == "NativeHandle")
        {
            return NativeFfiValueKind::native_handle;
        }

        throw std::runtime_error("native ffi type is not supported: " + type.name);
    };
    const auto store_native_handle = [&native_handles](void* handle) -> std::int32_t
    {
        if (handle == nullptr)
        {
            return 0;
        }

        native_handles.push_back(handle);
        return static_cast<std::int32_t>(native_handles.size() - 1);
    };
    const auto require_native_handle = [&native_handles](std::int32_t handle_id) -> void*
    {
        if (handle_id == 0)
        {
            return nullptr;
        }

        if (handle_id < 0 || static_cast<std::size_t>(handle_id) >= native_handles.size())
        {
            throw std::runtime_error("native handle is invalid");
        }

        return native_handles[static_cast<std::size_t>(handle_id)];
    };
    const auto load_native_library = [&](const std::string& library_name) -> void*
    {
        if (const auto it = native_library_handles.find(library_name); it != native_library_handles.end())
        {
            return it->second;
        }

        dlerror();
        auto* handle = dlopen(library_name.c_str(), RTLD_LAZY | RTLD_LOCAL);
        if (handle == nullptr)
        {
            const auto* error = dlerror();
            throw std::runtime_error(
                "failed to load native library '" + library_name + "': " + (error == nullptr ? "unknown error" : std::string(error)));
        }

        native_library_handles.emplace(library_name, handle);
        return handle;
    };
    const auto resolve_native_symbol = [&](const Function& function) -> void*
    {
        const auto cache_key = function.dll_import.library_name + '\n' + function.dll_import.entry_point;
        if (const auto it = native_symbol_handles.find(cache_key); it != native_symbol_handles.end())
        {
            return it->second;
        }

        auto* library_handle = load_native_library(function.dll_import.library_name);
        dlerror();
        auto* symbol = dlsym(library_handle, function.dll_import.entry_point.c_str());
        if (symbol == nullptr)
        {
            const auto* error = dlerror();
            throw std::runtime_error(
                "failed to resolve native symbol '" + function.dll_import.entry_point +
                "' from '" + function.dll_import.library_name +
                "': " + (error == nullptr ? "unknown error" : std::string(error)));
        }

        native_symbol_handles.emplace(cache_key, symbol);
        return symbol;
    };
    const auto invoke_native_import = [&](const Function& function, std::int32_t* arguments) -> std::int32_t
    {
        if (!function.dll_import.is_present)
        {
            throw std::runtime_error("native ffi invocation requested without dll import metadata");
        }

        if (function.dll_import.calling_convention != NativeCallingConvention::cdecl_ &&
            function.dll_import.calling_convention != NativeCallingConvention::stdcall_)
        {
            throw std::runtime_error("native ffi calling convention is not supported");
        }

        if (function.parameter_type_ids.size() != function.argument_count)
        {
            throw std::runtime_error("native ffi signature metadata does not match argument count");
        }

        const auto return_kind = get_native_ffi_value_kind(function.return_type_id);
        std::vector<std::string> marshaled_strings;
        std::vector<NativeFfiValueKind> parameter_kinds;
        parameter_kinds.reserve(function.parameter_type_ids.size());
        for (const auto parameter_type_id : function.parameter_type_ids)
        {
            parameter_kinds.push_back(get_native_ffi_value_kind(parameter_type_id));
        }

        auto get_string_argument = [&](std::size_t index) -> const char*
        {
            marshaled_strings.push_back(require_string(arguments[index]));
            return marshaled_strings.back().c_str();
        };

        auto get_i32_argument = [&](std::size_t index) -> std::int32_t
        {
            return arguments[index];
        };

        auto get_native_handle_argument = [&](std::size_t index) -> void*
        {
            return require_native_handle(arguments[index]);
        };

        void* symbol = resolve_native_symbol(function);

        if (parameter_kinds.empty())
        {
            switch (return_kind)
            {
                case NativeFfiValueKind::void_:
                    reinterpret_cast<void(*)()>(symbol)();
                    return 0;
                case NativeFfiValueKind::i32:
                    return reinterpret_cast<std::int32_t(*)()>(symbol)();
                case NativeFfiValueKind::bool32:
                    return reinterpret_cast<std::int32_t(*)()>(symbol)() != 0 ? 1 : 0;
                case NativeFfiValueKind::native_handle:
                    return store_native_handle(reinterpret_cast<void*(*)()>(symbol)());
                default:
                    break;
            }
        }
        else if (parameter_kinds.size() == 1)
        {
            if (parameter_kinds[0] == NativeFfiValueKind::utf8_string)
            {
                const auto* arg0 = get_string_argument(0);
                switch (return_kind)
                {
                    case NativeFfiValueKind::void_:
                        reinterpret_cast<void(*)(const char*)>(symbol)(arg0);
                        return 0;
                    case NativeFfiValueKind::i32:
                        return reinterpret_cast<std::int32_t(*)(const char*)>(symbol)(arg0);
                    case NativeFfiValueKind::bool32:
                        return reinterpret_cast<std::int32_t(*)(const char*)>(symbol)(arg0) != 0 ? 1 : 0;
                    case NativeFfiValueKind::native_handle:
                        return store_native_handle(reinterpret_cast<void*(*)(const char*)>(symbol)(arg0));
                    default:
                        break;
                }
            }
            else if (parameter_kinds[0] == NativeFfiValueKind::i32 || parameter_kinds[0] == NativeFfiValueKind::bool32)
            {
                const auto arg0 = get_i32_argument(0);
                switch (return_kind)
                {
                    case NativeFfiValueKind::void_:
                        reinterpret_cast<void(*)(std::int32_t)>(symbol)(arg0);
                        return 0;
                    case NativeFfiValueKind::i32:
                        return reinterpret_cast<std::int32_t(*)(std::int32_t)>(symbol)(arg0);
                    case NativeFfiValueKind::bool32:
                        return reinterpret_cast<std::int32_t(*)(std::int32_t)>(symbol)(arg0) != 0 ? 1 : 0;
                    case NativeFfiValueKind::native_handle:
                        return store_native_handle(reinterpret_cast<void*(*)(std::int32_t)>(symbol)(arg0));
                    default:
                        break;
                }
            }
            else if (parameter_kinds[0] == NativeFfiValueKind::native_handle)
            {
                auto* arg0 = get_native_handle_argument(0);
                switch (return_kind)
                {
                    case NativeFfiValueKind::void_:
                        reinterpret_cast<void(*)(void*)>(symbol)(arg0);
                        return 0;
                    case NativeFfiValueKind::i32:
                        return reinterpret_cast<std::int32_t(*)(void*)>(symbol)(arg0);
                    case NativeFfiValueKind::bool32:
                        return reinterpret_cast<std::int32_t(*)(void*)>(symbol)(arg0) != 0 ? 1 : 0;
                    case NativeFfiValueKind::native_handle:
                        return store_native_handle(reinterpret_cast<void*(*)(void*)>(symbol)(arg0));
                    default:
                        break;
                }
            }
        }
        else if (parameter_kinds.size() == 2)
        {
            if ((parameter_kinds[0] == NativeFfiValueKind::i32 || parameter_kinds[0] == NativeFfiValueKind::bool32) &&
                (parameter_kinds[1] == NativeFfiValueKind::i32 || parameter_kinds[1] == NativeFfiValueKind::bool32))
            {
                const auto arg0 = get_i32_argument(0);
                const auto arg1 = get_i32_argument(1);
                switch (return_kind)
                {
                    case NativeFfiValueKind::void_:
                        reinterpret_cast<void(*)(std::int32_t, std::int32_t)>(symbol)(arg0, arg1);
                        return 0;
                    case NativeFfiValueKind::i32:
                        return reinterpret_cast<std::int32_t(*)(std::int32_t, std::int32_t)>(symbol)(arg0, arg1);
                    case NativeFfiValueKind::bool32:
                        return reinterpret_cast<std::int32_t(*)(std::int32_t, std::int32_t)>(symbol)(arg0, arg1) != 0 ? 1 : 0;
                    default:
                        break;
                }
            }
        }

        throw std::runtime_error(
            "native ffi signature is not yet supported for '" +
            function.name +
            "' (" +
            function.dll_import.entry_point +
            ")");
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
    const auto resolve_runnable_run_target = [&](std::int32_t receiver_handle) -> std::uint32_t
    {
        std::uint32_t runnable_type_id = 0;
        for (const auto& type : module.types)
        {
            if (type.is_interface && type.name == "IRunnable")
            {
                runnable_type_id = type.type_id;
                break;
            }
        }

        if (runnable_type_id == 0)
        {
            throw std::runtime_error("IRunnable type is not present in the module");
        }

        std::uint32_t runnable_run_function_id = 0;
        for (const auto& function : module.functions)
        {
            if (function.owner_type_id == runnable_type_id && function.name == "Run")
            {
                runnable_run_function_id = function.function_id;
                break;
            }
        }

        if (runnable_run_function_id == 0)
        {
            throw std::runtime_error("IRunnable.Run is not present in the module");
        }

        return resolve_virtual_callee(runnable_run_function_id, receiver_handle, 0u);
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
    const auto describe_managed_exception = [&](std::int32_t value) -> std::string
    {
        if (value == 0)
        {
            return "value=0";
        }

        if (!is_object_handle(value))
        {
            return "value=" + std::to_string(value) + " kind=non-object";
        }

        const auto& object = require_object(value);
        const auto& object_type = require_type(object.type_id);

        std::string description =
            "value=" + std::to_string(value) +
            " type=" + object_type.name;

        std::uint32_t current_type_id = object.type_id;
        while (current_type_id != 0)
        {
            const auto& current_type = require_type(current_type_id);
            const auto field_it = std::find_if(
                module.fields.begin(),
                module.fields.end(),
                [&](const Field& field)
                {
                    return !field.is_static &&
                        field.owner_type_id == current_type_id &&
                        (field.name == "Message" ||
                         field.name == "__auto_Message" ||
                         field.name == "MessageValue" ||
                         field.name == "__auto_MessageValue");
                });

            if (field_it != module.fields.end() && field_it->instance_slot < object.fields.size())
            {
                const auto message_value = object.fields[field_it->instance_slot];
                if (message_value == 0)
                {
                    description += " message=<null>";
                }
                else if (is_string_handle(message_value))
                {
                    description += " message=" + require_string(message_value);
                }
                else
                {
                    description += " messageValue=" + std::to_string(message_value);
                }
                break;
            }

            current_type_id = current_type.base_type_id;
        }

        return description;
    };
    const auto summarize_runtime_value = [&](std::int32_t value) -> std::string
    {
        if (value == 0)
        {
            return "null";
        }

        if (is_string_handle(value))
        {
            const auto& text = require_string(value);
            const auto preview = text.size() > 64 ? text.substr(0, 64) + "..." : text;
            return "string(\"" + preview + "\")";
        }

        if (is_array_handle(value))
        {
            const auto& array = require_array(value);
            return "array(len=" + std::to_string(array.elements.size()) + ")";
        }

        if (is_object_handle(value))
        {
            const auto& object = require_object(value);
            return "object(" + require_type(object.type_id).name + ")";
        }

        return std::to_string(value);
    };
    const auto format_raw_stack_frame = [](const DebugFrame& frame) -> std::string
    {
        return "   at " + frame.function_name + "() [function=" + std::to_string(frame.function_id) + ", vm-ip=" + std::to_string(frame.vm_ip) + ']';
    };
    const auto format_stack_frame = [&](const DebugFrame& frame) -> std::string
    {
        if (stack_trace_formatter != nullptr)
        {
            return (*stack_trace_formatter)(frame);
        }

        return format_raw_stack_frame(frame);
    };
    const auto capture_stack_trace_lines = [&](std::uint32_t function_id, const std::string& function_name, std::uint32_t vm_ip) -> std::vector<std::string>
    {
        std::vector<DebugFrame> frames = debug_call_stack;
        if (frames.empty() ||
            frames.back().function_id != function_id)
        {
            frames.push_back(DebugFrame {
                .function_id = function_id,
                .function_name = function_name,
                .vm_ip = vm_ip
            });
        }
        else
        {
            frames.back().vm_ip = vm_ip;
        }

        std::vector<std::string> lines;
        lines.reserve(frames.size());
        for (auto frame_it = frames.rbegin(); frame_it != frames.rend(); ++frame_it)
        {
            lines.push_back(format_stack_frame(*frame_it));
        }

        return lines;
    };
    const auto create_string_array = [&](const std::vector<std::string>& values) -> std::int32_t
    {
        arrays.push_back(RuntimeArray {
            .elements = std::vector<std::int32_t>(values.size(), 0)
        });
        if (profile != nullptr)
        {
            ++profile->arrays_created;
        }
        heap_.record_allocation(sizeof(std::int32_t) * values.size());
        auto& array = arrays.back();
        for (std::size_t index = 0; index < values.size(); ++index)
        {
            strings.push_back(values[index]);
            if (profile != nullptr)
            {
                ++profile->strings_created;
            }
            array.elements[index] = encode_string_handle(strings.size() - 1);
        }

        return encode_array_handle(arrays.size() - 1);
    };
    const auto try_set_exception_stack_trace = [&](std::int32_t value, const std::vector<std::string>& lines)
    {
        if (value == 0 || !is_object_handle(value))
        {
            return;
        }

        auto& object = require_object(value);
        std::uint32_t current_type_id = object.type_id;
        while (current_type_id != 0)
        {
            const auto field_it = std::find_if(
                module.fields.begin(),
                module.fields.end(),
                [&](const Field& field)
                {
                    return !field.is_static &&
                        field.owner_type_id == current_type_id &&
                        (field.name == "StackTraceLinesValue" || field.name == "__auto_StackTraceLinesValue");
                });

            if (field_it != module.fields.end() && field_it->instance_slot < object.fields.size())
            {
                if (object.fields[field_it->instance_slot] == 0)
                {
                    object.fields[field_it->instance_slot] = create_string_array(lines);
                }

                return;
            }

            current_type_id = require_type(current_type_id).base_type_id;
        }
    };
    const auto append_stack_trace_text = [](const std::string& message, const std::vector<std::string>& lines) -> std::string
    {
        if (lines.empty())
        {
            return message;
        }

        std::string text = message;
        text += "\nstack trace:";
        for (const auto& line : lines)
        {
            text += '\n';
            text += line;
        }

        return text;
    };
    const auto try_create_managed_exception_with_message = [&](const std::string& message) -> std::int32_t
    {
        std::uint32_t exception_type_id = 0;
        for (const auto& type : module.types)
        {
            if (type.name == "Exception")
            {
                exception_type_id = type.type_id;
                break;
            }
        }

        if (exception_type_id == 0)
        {
            return 0;
        }

        const auto& exception_type = require_type(exception_type_id);
        objects.push_back(RuntimeObject {
            .type_id = exception_type.type_id,
            .fields = std::vector<std::int32_t>(exception_type.instance_field_count, 0)
        });
        heap_.record_allocation(sizeof(ObjectHeader) + exception_type.instance_field_count * sizeof(std::int32_t));
        const auto object_handle = encode_object_handle(objects.size() - 1);

        strings.push_back(message);
        const auto message_handle = encode_string_handle(strings.size() - 1);

        auto& object = require_object(object_handle);
        std::uint32_t current_type_id = exception_type.type_id;
        while (current_type_id != 0)
        {
            const auto& current_type = require_type(current_type_id);
            for (const auto& field : module.fields)
            {
                if (field.is_static || field.owner_type_id != current_type_id || field.instance_slot >= object.fields.size())
                {
                    continue;
                }

                if (field.name == "MessageValue" || field.name == "__auto_MessageValue")
                {
                    object.fields[field.instance_slot] = message_handle;
                }
            }

            current_type_id = current_type.base_type_id;
        }

        return object_handle;
    };
    const auto capture_current_stack_trace_lines = [&]() -> std::vector<std::string>
    {
        if (!debug_call_stack.empty())
        {
            const auto& frame = debug_call_stack.back();
            return capture_stack_trace_lines(frame.function_id, frame.function_name, frame.vm_ip);
        }

        return capture_stack_trace_lines(entry_function->function_id, entry_function->name, 0u);
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
            arrays.push_back(RuntimeArray {
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
            arrays.push_back(RuntimeArray {
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

        if (function.dll_import.is_present)
        {
            const auto host_start = std::chrono::steady_clock::now();
            if (profile != nullptr)
            {
                ++profile->host_import_calls;
            }

            const auto result = invoke_native_import(function, arguments);
            if (profile != nullptr)
            {
                profile->host_import_execution_ns += static_cast<std::uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
            }
            return result;
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
                    arrays.push_back(RuntimeArray {
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
                case HostImportKind::exception_get_current_stack_trace:
                {
                    std::vector<std::string> stack_trace_lines;
                    stack_trace_lines.reserve(debug_call_stack.size());
                    for (auto frame_it = debug_call_stack.rbegin(); frame_it != debug_call_stack.rend(); ++frame_it)
                    {
                        stack_trace_lines.push_back(format_stack_frame(*frame_it));
                    }
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return create_string_array(stack_trace_lines);
                }
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
                case HostImportKind::tcp_connect:
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return host_services_.tcp_connect(require_string(arguments[0]), arguments[1]);
                case HostImportKind::tcp_read_line:
                    strings.push_back(host_services_.tcp_read_line(arguments[0]));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::tcp_write_line:
                    host_services_.tcp_write_line(arguments[0], require_string(arguments[1]));
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
                case HostImportKind::tcp_close:
                    host_services_.tcp_close(arguments[0]);
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
                case HostImportKind::http_get_string:
                    strings.push_back(host_services_.http_get_string(require_string(arguments[0])));
                    if (profile != nullptr)
                    {
                        ++profile->strings_created;
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::thread_sleep:
                    host_services_.thread_sleep(arguments[0]);
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
                case HostImportKind::thread_get_current_managed_id:
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return host_services_.thread_get_current_managed_id();
                case HostImportKind::thread_start_runnable:
                {
                    if (arguments[0] == 0)
                    {
                        throw std::runtime_error("thread start target must not be null");
                    }

                    if (!is_object_handle(arguments[0]))
                    {
                        throw std::runtime_error("thread start target must be an object implementing IRunnable");
                    }

                    const auto target_handle = arguments[0];
                    const auto target_function_id = resolve_runnable_run_target(target_handle);
                    const auto module_snapshot = std::make_shared<Module>(module);
                    const auto formatter_snapshot =
                        stack_trace_formatter != nullptr
                            ? std::make_shared<StackTraceFormatter>(*stack_trace_formatter)
                            : nullptr;
                    const auto thread_state = std::make_shared<ExecutionState::ManagedThreadState>();

                    std::int32_t thread_id = 0;
                    {
                        std::lock_guard<std::mutex> state_lock(execution_state->sync_root);
                        thread_id = execution_state->next_managed_thread_id++;
                        execution_state->managed_threads.emplace(thread_id, thread_state);
                    }

                    thread_state->started = true;
                    thread_state->worker = std::thread(
                        [this, module_snapshot, execution_state, thread_state, target_handle, target_function_id, formatter_snapshot]()
                        {
                            try
                            {
                                std::vector<std::int32_t> worker_arguments { target_handle };
                                if (formatter_snapshot != nullptr)
                                {
                                    execute(
                                        *module_snapshot,
                                        execution_state,
                                        nullptr,
                                        nullptr,
                                        nullptr,
                                        formatter_snapshot.get(),
                                        target_function_id,
                                        &worker_arguments);
                                }
                                else
                                {
                                    execute(
                                        *module_snapshot,
                                        execution_state,
                                        nullptr,
                                        nullptr,
                                        nullptr,
                                        nullptr,
                                        target_function_id,
                                        &worker_arguments);
                                }
                            }
                            catch (const std::exception& exception)
                            {
                                std::lock_guard<std::mutex> state_lock(execution_state->sync_root);
                                thread_state->failure_message = exception.what();
                            }

                            std::lock_guard<std::mutex> state_lock(execution_state->sync_root);
                            thread_state->completed = true;
                        });

                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }

                    return thread_id;
                }
                case HostImportKind::thread_join:
                {
                    std::shared_ptr<ExecutionState::ManagedThreadState> thread_state;
                    {
                        std::lock_guard<std::mutex> state_lock(execution_state->sync_root);
                        const auto thread_it = execution_state->managed_threads.find(arguments[0]);
                        if (thread_it == execution_state->managed_threads.end())
                        {
                            throw std::runtime_error("thread handle is invalid");
                        }

                        thread_state = thread_it->second;
                    }

                    if (thread_state != nullptr && thread_state->worker.joinable())
                    {
                        thread_state->worker.join();
                    }

                    std::string failure_message;
                    {
                        std::lock_guard<std::mutex> state_lock(execution_state->sync_root);
                        if (thread_state != nullptr)
                        {
                            failure_message = thread_state->failure_message;
                        }
                    }

                    if (!failure_message.empty())
                    {
                        if (const auto exception_handle = try_create_managed_exception_with_message(failure_message); exception_handle != 0)
                        {
                            throw ManagedException { exception_handle };
                        }

                        throw std::runtime_error(failure_message);
                    }

                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
                }
                case HostImportKind::thread_is_alive:
                {
                    bool is_alive = false;
                    {
                        std::lock_guard<std::mutex> state_lock(execution_state->sync_root);
                        const auto thread_it = execution_state->managed_threads.find(arguments[0]);
                        if (thread_it != execution_state->managed_threads.end() && thread_it->second != nullptr)
                        {
                            is_alive = thread_it->second->started && !thread_it->second->completed;
                        }
                    }

                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return is_alive ? 1 : 0;
                }
                case HostImportKind::mutex_create:
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return host_services_.mutex_create();
                case HostImportKind::mutex_wait_one:
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return host_services_.mutex_wait_one(arguments[0]) ? 1 : 0;
                case HostImportKind::mutex_release:
                    host_services_.mutex_release(arguments[0]);
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
                case HostImportKind::mutex_close:
                    host_services_.mutex_close(arguments[0]);
                    if (profile != nullptr)
                    {
                        profile->host_import_execution_ns += static_cast<std::uint64_t>(
                            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - host_start).count());
                    }
                    return 0;
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
        debug_call_stack.push_back(DebugFrame {
            .function_id = function.function_id,
            .function_name = function.name,
            .vm_ip = 0
        });
        const auto pop_debug_frame = [&debug_call_stack]()
        {
            if (!debug_call_stack.empty())
            {
                debug_call_stack.pop_back();
            }
        };
        struct DebugFrameGuard
        {
            const std::function<void()> pop;
            ~DebugFrameGuard()
            {
                pop();
            }
        } debug_frame_guard { pop_debug_frame };
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
                if (!debug_call_stack.empty())
                {
                    debug_call_stack.back().vm_ip = static_cast<std::uint32_t>(ip);
                }

                const auto& instruction = function.instructions[ip];
                if (debug_options != nullptr && debug_sink != nullptr)
                {
                    const bool breakpoint_hit =
                        (debug_options->break_on_entry && ip == 0) ||
                        std::any_of(
                            debug_options->breakpoints.begin(),
                            debug_options->breakpoints.end(),
                            [&](const DebugBreakpoint& breakpoint)
                            {
                                return breakpoint.function_id == function.function_id &&
                                    breakpoint.vm_ip == ip;
                            });
                    const bool step_over_hit =
                        debug_step_over_active &&
                        debug_call_stack.size() <= debug_step_over_depth;

                    if (breakpoint_hit)
                    {
                        debug_tracing_active = true;
                        debug_steps_remaining = debug_options->step_count_after_break;
                        debug_step_over_active = false;
                    }

                    if (breakpoint_hit || debug_tracing_active || step_over_hit)
                    {
                        std::vector<std::string> register_displays;
                        register_displays.reserve(register_count);
                        for (std::size_t register_index = 0; register_index < register_count; ++register_index)
                        {
                            register_displays.push_back(summarize_runtime_value(register_values[register_index]));
                        }

                        DebugEvent event {
                            .frame = DebugFrame {
                                .function_id = function.function_id,
                                .function_name = function.name,
                                .vm_ip = static_cast<std::uint32_t>(ip)
                            },
                            .instruction = DebugInstruction {
                                .opcode = instruction.opcode,
                                .destination = instruction.destination,
                                .left = instruction.left,
                                .right = instruction.right,
                                .immediate = instruction.immediate
                            },
                            .registers = std::vector<std::int32_t>(register_values, register_values + register_count),
                            .register_displays = std::move(register_displays),
                            .call_stack = debug_call_stack,
                            .breakpoint_hit = breakpoint_hit
                        };
                        const auto debug_action = (*debug_sink)(event);

                        switch (debug_action)
                        {
                            case DebugAction::none:
                                break;
                            case DebugAction::continue_execution:
                                debug_tracing_active = false;
                                debug_steps_remaining = 0;
                                debug_step_over_active = false;
                                break;
                            case DebugAction::step_into:
                                debug_tracing_active = true;
                                debug_steps_remaining = 1;
                                debug_step_over_active = false;
                                break;
                            case DebugAction::step_over:
                                debug_tracing_active = false;
                                debug_steps_remaining = 0;
                                debug_step_over_active = true;
                                debug_step_over_depth = debug_call_stack.size();
                                break;
                            case DebugAction::quit:
                                debug_abort_requested = true;
                                break;
                        }

                        if (debug_abort_requested)
                        {
                            return 0;
                        }

                        if (!breakpoint_hit && !step_over_hit && debug_tracing_active)
                        {
                            if (debug_steps_remaining > 0)
                            {
                                --debug_steps_remaining;
                            }

                            if (debug_steps_remaining == 0)
                            {
                                debug_tracing_active = false;
                            }
                        }
                    }
                }
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
                        objects.push_back(RuntimeObject {
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

                        arrays.push_back(RuntimeArray { .elements = std::vector<std::int32_t>(static_cast<std::size_t>(length), 0) });
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
                            if (index_value < 0 || static_cast<std::size_t>(index_value) >= array.elements.size())
                            {
                                throw std::runtime_error(
                                    "index out of bounds during ld_elem at ip=" +
                                    std::to_string(ip) +
                                    " function=" +
                                    std::to_string(function.function_id) +
                                    " functionName=" +
                                    function.name +
                                    " sourceHandle=" +
                                    std::to_string(source) +
                                    " index=" +
                                    std::to_string(index_value) +
                                    " length=" +
                                    std::to_string(array.elements.size()) +
                                    " dst=" +
                                    std::to_string(instruction.destination) +
                                    " left=" +
                                    std::to_string(instruction.left) +
                                    " right=" +
                                    std::to_string(instruction.right));
                            }
                            const auto index = static_cast<std::size_t>(index_value);
                            register_values[instruction.destination] = array.elements[index];
                        }
                        else if (is_string_handle(source))
                        {
                            const auto& text = require_string(source);
                            if (index_value < 0 || static_cast<std::size_t>(index_value) >= text.size())
                            {
                                throw std::runtime_error(
                                    "index out of bounds during ld_elem(string) at ip=" +
                                    std::to_string(ip) +
                                    " function=" +
                                    std::to_string(function.function_id) +
                                    " functionName=" +
                                    function.name +
                                    " sourceHandle=" +
                                    std::to_string(source) +
                                    " index=" +
                                    std::to_string(index_value) +
                                    " length=" +
                                    std::to_string(text.size()) +
                                    " dst=" +
                                    std::to_string(instruction.destination) +
                                    " left=" +
                                    std::to_string(instruction.left) +
                                    " right=" +
                                    std::to_string(instruction.right));
                            }
                            const auto index = static_cast<std::size_t>(index_value);
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
                        const auto index_value = register_values[instruction.left];
                        if (index_value < 0 || static_cast<std::size_t>(index_value) >= array.elements.size())
                        {
                            throw std::runtime_error(
                                "index out of bounds during st_elem at ip=" +
                                std::to_string(ip) +
                                " function=" +
                                std::to_string(function.function_id) +
                                " functionName=" +
                                function.name +
                                " arrayHandle=" +
                                std::to_string(register_values[instruction.destination]) +
                                " index=" +
                                std::to_string(index_value) +
                                " length=" +
                                std::to_string(array.elements.size()) +
                                " value=" +
                                std::to_string(register_values[instruction.right]) +
                                " dst=" +
                                std::to_string(instruction.destination) +
                                " left=" +
                                std::to_string(instruction.left) +
                                " right=" +
                                std::to_string(instruction.right));
                        }
                        const auto index = static_cast<std::size_t>(index_value);
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
                last_stack_trace_lines = capture_stack_trace_lines(function.function_id, function.name, static_cast<std::uint32_t>(ip));
                last_stack_trace_from_runtime_error = false;
                try_set_exception_stack_trace(ex.value, last_stack_trace_lines);
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
            catch (const std::runtime_error&)
            {
                last_stack_trace_lines = capture_stack_trace_lines(function.function_id, function.name, static_cast<std::uint32_t>(ip));
                last_stack_trace_from_runtime_error = true;
                throw;
            }
        }

        copy_back_arguments();
        return 0;
    };

    try
    {
        const auto result = execute_function(
            *entry_function,
            entry_arguments == nullptr || entry_arguments->empty() ? nullptr : const_cast<std::int32_t*>(entry_arguments->data()),
            entry_arguments == nullptr ? 0 : entry_arguments->size(),
            0);
        if (profile != nullptr)
        {
            profile->total_execution_ns = static_cast<std::uint64_t>(
                std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - total_start).count());
        }

        return result;
    }
    catch (const std::runtime_error& ex)
    {
        throw std::runtime_error(
            append_stack_trace_text(
                ex.what(),
                last_stack_trace_from_runtime_error
                    ? last_stack_trace_lines
                    : capture_current_stack_trace_lines()));
    }
    catch (const ManagedException& ex)
    {
        throw std::runtime_error(
            append_stack_trace_text(
                "unhandled managed exception: " + describe_managed_exception(ex.value),
                last_stack_trace_lines.empty()
                    ? capture_stack_trace_lines(entry_function->function_id, entry_function->name, debug_call_stack.empty() ? 0u : debug_call_stack.back().vm_ip)
                    : last_stack_trace_lines));
    }
}
} // namespace ilcvm
