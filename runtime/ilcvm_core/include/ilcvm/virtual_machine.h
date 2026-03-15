#pragma once

#include "ilcvm/host_services.h"
#include "ilcvm/heap.h"
#include "ilcvm/module.h"

#include <cstdint>

namespace ilcvm
{
class VirtualMachine
{
public:
    VirtualMachine(Heap& heap, const IHostServices& host_services) noexcept;

    [[nodiscard]] std::int32_t execute(const Module& module) const;

private:
    Heap& heap_;
    const IHostServices& host_services_;
};
} // namespace ilcvm
