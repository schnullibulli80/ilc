#include "ilcvm/heap.h"

namespace ilcvm
{
ExecutionState::ExecutionState(std::vector<std::string> initial_strings, const std::size_t static_field_count)
    : static_fields(static_field_count, 0),
      arrays(1),
      objects(1),
      strings(std::move(initial_strings)),
      native_handles(1, nullptr)
{
}

ExecutionState::~ExecutionState()
{
    for (auto& [thread_id, thread_state] : managed_threads)
    {
        (void)thread_id;
        if (thread_state != nullptr && thread_state->worker.joinable())
        {
            thread_state->worker.join();
        }
    }
}

std::size_t Heap::allocated_bytes() const noexcept
{
    return allocated_bytes_;
}

void Heap::record_allocation(const std::size_t bytes)
{
    allocated_bytes_ += bytes;
}

std::shared_ptr<ExecutionState> Heap::create_execution_state(
    std::vector<std::string> initial_strings,
    const std::size_t static_field_count) const
{
    return std::make_shared<ExecutionState>(std::move(initial_strings), static_field_count);
}
} // namespace ilcvm
