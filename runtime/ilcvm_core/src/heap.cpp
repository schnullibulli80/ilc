#include "ilcvm/heap.h"

namespace ilcvm
{
std::size_t Heap::allocated_bytes() const noexcept
{
    return allocated_bytes_;
}

void Heap::record_allocation(const std::size_t bytes)
{
    allocated_bytes_ += bytes;
}
} // namespace ilcvm
