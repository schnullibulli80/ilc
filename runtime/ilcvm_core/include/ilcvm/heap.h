#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
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

struct RuntimeArray
{
    std::vector<std::int32_t> elements;
};

struct RuntimeObject
{
    std::uint32_t type_id {};
    std::vector<std::int32_t> fields;
};

class ExecutionState
{
public:
    struct ManagedThreadState
    {
        std::thread worker;
        bool completed {};
        bool started {};
        std::string failure_message;
    };

    ExecutionState(std::vector<std::string> initial_strings, std::size_t static_field_count);
    ~ExecutionState();

    mutable std::mutex sync_root;
    std::vector<std::int32_t> static_fields;
    std::vector<RuntimeArray> arrays;
    std::vector<RuntimeObject> objects;
    std::vector<std::string> strings;
    std::vector<void*> native_handles;
    std::unordered_map<std::string, void*> native_library_handles;
    std::unordered_map<std::string, void*> native_symbol_handles;
    std::unordered_map<std::int32_t, std::shared_ptr<ManagedThreadState>> managed_threads;
    std::int32_t next_managed_thread_id { 1 };
};

class Heap
{
public:
    [[nodiscard]] std::size_t allocated_bytes() const noexcept;
    void record_allocation(std::size_t bytes);
    [[nodiscard]] std::shared_ptr<ExecutionState> create_execution_state(
        std::vector<std::string> initial_strings,
        std::size_t static_field_count) const;

private:
    std::size_t allocated_bytes_ = 0;
};
} // namespace ilcvm
