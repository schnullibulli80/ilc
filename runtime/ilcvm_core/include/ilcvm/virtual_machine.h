#pragma once

#include "ilcvm/heap.h"
#include "ilcvm/module.h"

#include <cstdint>

namespace ilcvm
{
class VirtualMachine
{
public:
    explicit VirtualMachine(Heap& heap) noexcept;

    [[nodiscard]] std::int32_t execute(const Module& module) const;

private:
    Heap& heap_;
};
} // namespace ilcvm
