#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace ilcvm
{
struct ObjectHeader
{
    std::uint64_t type_handle;
    std::uint32_t flags;
    std::uint32_t aux_word;
};

static_assert(sizeof(ObjectHeader) == 16, "ILC object headers must be 16 bytes.");

class Heap
{
public:
    [[nodiscard]] std::size_t allocated_bytes() const noexcept;
    void record_allocation(std::size_t bytes);

private:
    std::size_t allocated_bytes_ = 0;
};
} // namespace ilcvm
