#pragma once

#include "ilcvm/host_services.h"
#include "ilcvm/heap.h"
#include "ilcvm/module.h"

#include <cstdint>
#include <string>
#include <vector>

namespace ilcvm
{
class VirtualMachine
{
public:
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

private:
    [[nodiscard]] std::int32_t execute(const Module& module, ExecutionProfile* profile) const;

    Heap& heap_;
    const IHostServices& host_services_;
};
} // namespace ilcvm
