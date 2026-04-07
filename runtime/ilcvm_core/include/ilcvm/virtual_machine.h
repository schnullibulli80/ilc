#pragma once

#include "ilcvm/host_services.h"
#include "ilcvm/heap.h"
#include "ilcvm/module.h"

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

namespace ilcvm
{
class VirtualMachine
{
public:
    struct DebugBreakpoint
    {
        std::uint32_t function_id {};
        std::uint32_t vm_ip {};
    };

    struct DebugFrame
    {
        std::uint32_t function_id {};
        std::string function_name;
        std::uint32_t vm_ip {};
    };

    struct DebugInstruction
    {
        OpCode opcode {};
        std::uint16_t destination {};
        std::uint16_t left {};
        std::uint16_t right {};
        std::int32_t immediate {};
    };

    struct DebugEvent
    {
        DebugFrame frame;
        DebugInstruction instruction;
        std::vector<std::int32_t> registers;
        std::vector<std::string> register_displays;
        std::vector<DebugFrame> call_stack;
        bool breakpoint_hit {};
    };

    enum class DebugAction : std::uint8_t
    {
        none = 0,
        continue_execution = 1,
        step_into = 2,
        step_over = 3,
        quit = 4
    };

    struct DebugOptions
    {
        std::vector<DebugBreakpoint> breakpoints;
        bool break_on_entry {};
        std::uint64_t step_count_after_break {};
    };

    using DebugSink = std::function<DebugAction(const DebugEvent&)>;
    using StackTraceFormatter = std::function<std::string(const DebugFrame&)>;

    struct ExecutionProfile
    {
        std::uint64_t total_execution_ns {};
        std::uint64_t host_import_execution_ns {};
        std::uint64_t call_execution_ns {};
        std::uint64_t call_virt_execution_ns {};
        std::uint64_t array_execution_ns {};
        std::uint64_t ld_elem_execution_ns {};
        std::uint64_t st_elem_execution_ns {};
        std::uint64_t ld_len_execution_ns {};
        std::uint64_t new_arr_execution_ns {};
        std::uint64_t mov_execution_ns {};
        std::uint64_t compare_execution_ns {};
        std::uint64_t branch_execution_ns {};
        std::uint64_t instructions_executed {};
        std::uint64_t functions_executed {};
        std::uint64_t host_import_calls {};
        std::uint64_t call_count {};
        std::uint64_t call_virt_count {};
        std::uint64_t new_obj_count {};
        std::uint64_t new_arr_count {};
        std::uint64_t ld_elem_count {};
        std::uint64_t st_elem_count {};
        std::uint64_t ld_len_count {};
        std::uint64_t mov_count {};
        std::uint64_t compare_count {};
        std::uint64_t branch_count {};
        std::uint64_t strings_created {};
        std::uint64_t arrays_created {};
        std::uint64_t objects_created {};
        std::uint64_t leaf_fastpath_calls {};
        std::uint64_t leaf_fastpath_execution_ns {};
        std::uint64_t specialized_leaf_fastpath_calls {};
        std::uint64_t max_call_depth {};
    };

    VirtualMachine(Heap& heap, const IHostServices& host_services) noexcept;

    [[nodiscard]] std::int32_t execute(const Module& module) const;
    [[nodiscard]] std::int32_t execute(const Module& module, ExecutionProfile& profile) const;
    [[nodiscard]] std::int32_t execute(const Module& module, const StackTraceFormatter& stack_trace_formatter) const;
    [[nodiscard]] std::int32_t execute(const Module& module, ExecutionProfile& profile, const StackTraceFormatter& stack_trace_formatter) const;
    [[nodiscard]] std::int32_t execute(const Module& module, const DebugOptions& debug_options, const DebugSink& debug_sink) const;
    [[nodiscard]] std::int32_t execute(const Module& module, ExecutionProfile& profile, const DebugOptions& debug_options, const DebugSink& debug_sink) const;
    [[nodiscard]] std::int32_t execute(const Module& module, const DebugOptions& debug_options, const DebugSink& debug_sink, const StackTraceFormatter& stack_trace_formatter) const;
    [[nodiscard]] std::int32_t execute(const Module& module, ExecutionProfile& profile, const DebugOptions& debug_options, const DebugSink& debug_sink, const StackTraceFormatter& stack_trace_formatter) const;

private:
    [[nodiscard]] std::int32_t execute(const Module& module, ExecutionProfile* profile, const DebugOptions* debug_options, const DebugSink* debug_sink, const StackTraceFormatter* stack_trace_formatter) const;

    Heap& heap_;
    const IHostServices& host_services_;
};
} // namespace ilcvm
