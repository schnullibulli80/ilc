#include "ilcvm/virtual_machine.h"

#include <algorithm>
#include <cctype>
#include <cstddef>
#include <functional>
#include <stdexcept>
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
    if (module.functions.empty())
    {
        throw std::runtime_error("module contains no functions");
    }

    const auto* entry_function = &module.functions.front();
    if (module.entry_function_id != 0)
    {
        const auto it = std::find_if(
            module.functions.begin(),
            module.functions.end(),
            [&module](const Function& candidate)
            {
                return candidate.function_id == module.entry_function_id;
            });
        if (it == module.functions.end())
        {
            throw std::runtime_error("module entry point does not reference a known function");
        }

        entry_function = &*it;
    }

    const auto find_function = [&module](std::uint32_t function_id) -> const Function&
    {
        const auto it = std::find_if(
            module.functions.begin(),
            module.functions.end(),
            [function_id](const Function& candidate)
            {
                return candidate.function_id == function_id;
            });
        if (it == module.functions.end())
        {
            throw std::runtime_error("call target does not reference a known function");
        }

        return *it;
    };

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
    const auto require_type = [&module](std::uint32_t type_id) -> const Type&
    {
        const auto it = std::find_if(
            module.types.begin(),
            module.types.end(),
            [type_id](const Type& candidate)
            {
                return candidate.type_id == type_id;
            });
        if (it == module.types.end())
        {
            throw std::runtime_error("object allocation references an unknown type");
        }

        return *it;
    };
    const auto require_field = [&module](std::uint32_t field_id) -> const Field&
    {
        const auto it = std::find_if(
            module.fields.begin(),
            module.fields.end(),
            [field_id](const Field& candidate)
            {
                return candidate.field_id == field_id;
            });
        if (it == module.fields.end())
        {
            throw std::runtime_error("field reference is invalid");
        }

        return *it;
    };
    const auto require_index = [](std::int32_t index, std::size_t length) -> std::size_t
    {
        if (index < 0 || static_cast<std::size_t>(index) >= length)
        {
            throw std::runtime_error("index out of bounds");
        }

        return static_cast<std::size_t>(index);
    };
    const auto get_string_type_id = [&module]() -> std::uint32_t
    {
        const auto it = std::find_if(
            module.types.begin(),
            module.types.end(),
            [](const Type& candidate)
            {
                return candidate.name == "String";
            });
        return it == module.types.end() ? 0u : it->type_id;
    };
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
            return get_string_type_id();
        }

        return 0u;
    };

    std::function<std::int32_t(const Function&, const std::vector<std::int32_t>&)> execute_function;
    execute_function = [&](const Function& function, const std::vector<std::int32_t>& arguments) -> std::int32_t
    {
        if (arguments.size() != function.argument_count)
        {
            throw std::runtime_error("call target argument count mismatch");
        }

        if (function.host_import_kind != HostImportKind::none)
        {
            switch (function.host_import_kind)
            {
                case HostImportKind::console_write:
                    host_services_.console_write(require_string(arguments.at(0)));
                    return 0;
                case HostImportKind::console_write_line:
                    host_services_.console_write_line(require_string(arguments.at(0)));
                    return 0;
                case HostImportKind::console_read_line:
                    strings.push_back(host_services_.console_read_line());
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_command_line_args:
                {
                    const auto command_line_args = host_services_.get_command_line_args();
                    arrays.push_back(ArrayObject {
                        .elements = std::vector<std::int32_t>(command_line_args.size(), 0)
                    });
                    heap_.record_allocation(sizeof(std::int32_t) * command_line_args.size());
                    auto& array = arrays.back();
                    for (std::size_t index = 0; index < command_line_args.size(); ++index)
                    {
                        strings.push_back(command_line_args[index]);
                        array.elements[index] = encode_string_handle(strings.size() - 1);
                    }

                    return encode_array_handle(arrays.size() - 1);
                }
                case HostImportKind::environment_get_current_working_directory:
                    strings.push_back(host_services_.get_current_working_directory());
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_environment_variable:
                    strings.push_back(host_services_.get_environment_variable(require_string(arguments.at(0))));
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_set_environment_variable:
                    host_services_.set_environment_variable(require_string(arguments.at(0)), require_string(arguments.at(1)));
                    return 0;
                case HostImportKind::environment_get_user_name:
                    strings.push_back(host_services_.get_user_name());
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_machine_name:
                    strings.push_back(host_services_.get_machine_name());
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_home_directory:
                    strings.push_back(host_services_.get_home_directory());
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::environment_get_temp_directory:
                    strings.push_back(host_services_.get_temp_directory());
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::clock_get_monotonic_milliseconds_text:
                    strings.push_back(std::to_string(host_services_.get_monotonic_timestamp_ms()));
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::clock_get_wall_milliseconds_text:
                    strings.push_back(std::to_string(host_services_.get_wall_timestamp_ms()));
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::file_exists:
                    return host_services_.file_exists(require_string(arguments.at(0))) ? 1 : 0;
                case HostImportKind::file_read_all_text:
                    strings.push_back(host_services_.file_read_all_text(require_string(arguments.at(0))));
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::file_write_all_text:
                    host_services_.file_write_all_text(require_string(arguments.at(0)), require_string(arguments.at(1)));
                    return 0;
                case HostImportKind::file_append_all_text:
                    host_services_.file_append_all_text(require_string(arguments.at(0)), require_string(arguments.at(1)));
                    return 0;
                case HostImportKind::path_combine:
                    strings.push_back(host_services_.path_combine(require_string(arguments.at(0)), require_string(arguments.at(1))));
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::path_get_file_name:
                    strings.push_back(host_services_.path_get_file_name(require_string(arguments.at(0))));
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::path_get_directory_name:
                    strings.push_back(host_services_.path_get_directory_name(require_string(arguments.at(0))));
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::path_get_extension:
                    strings.push_back(host_services_.path_get_extension(require_string(arguments.at(0))));
                    return encode_string_handle(strings.size() - 1);
                case HostImportKind::none:
                    break;
            }
        }

        std::vector<std::int32_t> registers(function.register_count == 0 ? 1 : function.register_count, 0);
        bool has_current_exception = false;
        std::int32_t current_exception_value = 0;
        for (std::size_t index = 0; index < arguments.size(); ++index)
        {
            registers.at(index) = arguments[index];
        }

        std::size_t ip = 0;
        while (ip < function.instructions.size())
        {
            try
            {
                const auto& instruction = function.instructions[ip];
                switch (instruction.opcode)
                {
                    case OpCode::nop:
                        ++ip;
                        break;
                    case OpCode::ld_i32:
                        registers.at(instruction.destination) = instruction.immediate;
                        ++ip;
                        break;
                    case OpCode::ld_str:
                    {
                        const auto string_id = static_cast<std::size_t>(instruction.immediate);
                        if (string_id >= strings.size())
                        {
                            throw std::runtime_error("string constant does not reference a known string");
                        }

                        registers.at(instruction.destination) = encode_string_handle(string_id);
                        ++ip;
                        break;
                    }
                    case OpCode::slice_str:
                    {
                        const auto& text = require_string(registers.at(instruction.left));
                        const auto start = require_index(registers.at(instruction.right), text.size());
                        const auto end = require_index(registers.at(static_cast<std::size_t>(instruction.immediate)), text.size());
                        if (end < start)
                        {
                            throw std::runtime_error("string slice range is invalid");
                        }

                        strings.push_back(text.substr(start, end - start + 1));
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::mov:
                        registers.at(instruction.destination) = registers.at(instruction.left);
                        ++ip;
                        break;
                    case OpCode::add_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) + registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::shl_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) << registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::shr_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) >> registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::and_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) & registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::or_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) | registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::not_i32:
                        registers.at(instruction.destination) = ~registers.at(instruction.left);
                        ++ip;
                        break;
                    case OpCode::sub_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) - registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::mul_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) * registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::div_i32:
                        if (registers.at(instruction.right) == 0)
                        {
                            throw std::runtime_error("division by zero");
                        }

                        registers.at(instruction.destination) = registers.at(instruction.left) / registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::mod_i32:
                        if (registers.at(instruction.right) == 0)
                        {
                            throw std::runtime_error("division by zero");
                        }

                        registers.at(instruction.destination) = registers.at(instruction.left) % registers.at(instruction.right);
                        ++ip;
                        break;
                    case OpCode::cmp_eq_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) == registers.at(instruction.right) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_ne_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) != registers.at(instruction.right) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_lt_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) < registers.at(instruction.right) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_le_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) <= registers.at(instruction.right) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_gt_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) > registers.at(instruction.right) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_ge_i32:
                        registers.at(instruction.destination) = registers.at(instruction.left) >= registers.at(instruction.right) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_eq_str:
                        registers.at(instruction.destination) = compare_strings(registers.at(instruction.left), registers.at(instruction.right)) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_ne_str:
                        registers.at(instruction.destination) = compare_strings(registers.at(instruction.left), registers.at(instruction.right)) ? 0 : 1;
                        ++ip;
                        break;
                    case OpCode::cmp_eq_ref:
                        registers.at(instruction.destination) = registers.at(instruction.left) == registers.at(instruction.right) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::cmp_ne_ref:
                        registers.at(instruction.destination) = registers.at(instruction.left) != registers.at(instruction.right) ? 1 : 0;
                        ++ip;
                        break;
                    case OpCode::concat_str:
                    {
                        const auto& left = require_string(registers.at(instruction.left));
                        const auto& right = require_string(registers.at(instruction.right));
                        strings.push_back(left + right);
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::starts_with_str:
                    {
                        const auto& left = require_string(registers.at(instruction.left));
                        const auto& right = require_string(registers.at(instruction.right));
                        registers.at(instruction.destination) =
                            left.size() >= right.size() && left.compare(0, right.size(), right) == 0 ? 1 : 0;
                        ++ip;
                        break;
                    }
                    case OpCode::ends_with_str:
                    {
                        const auto& left = require_string(registers.at(instruction.left));
                        const auto& right = require_string(registers.at(instruction.right));
                        registers.at(instruction.destination) =
                            left.size() >= right.size() &&
                            left.compare(left.size() - right.size(), right.size(), right) == 0 ? 1 : 0;
                        ++ip;
                        break;
                    }
                    case OpCode::contains_str:
                    {
                        const auto& left = require_string(registers.at(instruction.left));
                        const auto& right = require_string(registers.at(instruction.right));
                        registers.at(instruction.destination) = left.find(right) != std::string::npos ? 1 : 0;
                        ++ip;
                        break;
                    }
                    case OpCode::index_of_str:
                    {
                        const auto& left = require_string(registers.at(instruction.left));
                        const auto& right = require_string(registers.at(instruction.right));
                        const auto position = left.find(right);
                        registers.at(instruction.destination) =
                            position == std::string::npos ? -1 : static_cast<std::int32_t>(position);
                        ++ip;
                        break;
                    }
                    case OpCode::last_index_of_str:
                    {
                        const auto& left = require_string(registers.at(instruction.left));
                        const auto& right = require_string(registers.at(instruction.right));
                        const auto position = left.rfind(right);
                        registers.at(instruction.destination) =
                            position == std::string::npos ? -1 : static_cast<std::int32_t>(position);
                        ++ip;
                        break;
                    }
                    case OpCode::replace_str:
                    {
                        const auto& source = require_string(registers.at(instruction.left));
                        const auto& old_value = require_string(registers.at(instruction.right));
                        const auto& new_value = require_string(registers.at(static_cast<std::size_t>(instruction.immediate)));
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
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::insert_str:
                    {
                        const auto& source = require_string(registers.at(instruction.left));
                        const auto index = registers.at(instruction.right);
                        if (index < 0 || static_cast<std::size_t>(index) > source.size())
                        {
                            throw std::runtime_error("string insert index out of bounds");
                        }

                        const auto& value = require_string(registers.at(static_cast<std::size_t>(instruction.immediate)));
                        std::string inserted = source;
                        inserted.insert(static_cast<std::size_t>(index), value);
                        strings.push_back(std::move(inserted));
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::remove_str:
                    {
                        const auto& source = require_string(registers.at(instruction.left));
                        const auto index = registers.at(instruction.right);
                        const auto length = registers.at(static_cast<std::size_t>(instruction.immediate));
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
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::to_upper_str:
                    {
                        const auto& source = require_string(registers.at(instruction.left));
                        std::string upper = source;
                        std::transform(
                            upper.begin(),
                            upper.end(),
                            upper.begin(),
                            [](unsigned char character) { return static_cast<char>(std::toupper(character)); });
                        strings.push_back(std::move(upper));
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::to_lower_str:
                    {
                        const auto& source = require_string(registers.at(instruction.left));
                        std::string lower = source;
                        std::transform(
                            lower.begin(),
                            lower.end(),
                            lower.begin(),
                            [](unsigned char character) { return static_cast<char>(std::tolower(character)); });
                        strings.push_back(std::move(lower));
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::trim_str:
                    case OpCode::trim_start_str:
                    case OpCode::trim_end_str:
                    {
                        const auto& source = require_string(registers.at(instruction.left));
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
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                    case OpCode::str_to_i32:
                    {
                        const auto& source = require_string(registers.at(instruction.left));
                        std::size_t consumed = 0;
                        const auto value = std::stoi(source, &consumed, 10);
                        if (consumed != source.size())
                        {
                            throw std::runtime_error("string does not contain a valid integer");
                        }

                        registers.at(instruction.destination) = value;
                        ++ip;
                        break;
                    }
                    case OpCode::try_str_to_i32:
                    {
                        const auto& source = require_string(registers.at(instruction.left));
                        try
                        {
                            std::size_t consumed = 0;
                            const auto value = std::stoi(source, &consumed, 10);
                            if (consumed != source.size())
                            {
                                registers.at(instruction.right) = 0;
                                registers.at(instruction.destination) = 0;
                            }
                            else
                            {
                                registers.at(instruction.right) = value;
                                registers.at(instruction.destination) = 1;
                            }
                        }
                        catch (const std::exception&)
                        {
                            registers.at(instruction.right) = 0;
                            registers.at(instruction.destination) = 0;
                        }

                        ++ip;
                        break;
                    }
                    case OpCode::i32_to_str:
                    {
                        strings.push_back(std::to_string(registers.at(instruction.left)));
                        registers.at(instruction.destination) = encode_string_handle(strings.size() - 1);
                        ++ip;
                        break;
                    }
                case OpCode::call:
                {
                    const auto& callee = find_function(static_cast<std::uint32_t>(instruction.immediate));
                    std::vector<std::int32_t> call_arguments;
                    call_arguments.reserve(instruction.right);
                    for (std::uint16_t arg_index = 0; arg_index < instruction.right; ++arg_index)
                    {
                        call_arguments.push_back(registers.at(instruction.left + arg_index));
                    }

                    const auto result = execute_function(callee, call_arguments);
                    if (callee.returns_value)
                    {
                        registers.at(instruction.destination) = result;
                    }

                    ++ip;
                    break;
                }
                case OpCode::call_virt:
                {
                    if (registers.at(instruction.left) == 0)
                    {
                        throw std::runtime_error("null reference method call");
                    }

                    const auto& callee = find_function(static_cast<std::uint32_t>(instruction.immediate));
                    std::vector<std::int32_t> call_arguments;
                    call_arguments.reserve(static_cast<std::size_t>(instruction.right) + 1);
                    call_arguments.push_back(registers.at(instruction.left));
                    for (std::uint16_t arg_index = 0; arg_index < instruction.right; ++arg_index)
                    {
                        call_arguments.push_back(registers.at(instruction.left + 1 + arg_index));
                    }

                    const auto result = execute_function(callee, call_arguments);
                    if (callee.returns_value)
                    {
                        registers.at(instruction.destination) = result;
                    }

                    ++ip;
                    break;
                }
                case OpCode::new_obj:
                {
                    const auto& type = require_type(static_cast<std::uint32_t>(instruction.immediate));
                    objects.push_back(ManagedObject {
                        .type_id = type.type_id,
                        .fields = std::vector<std::int32_t>(type.instance_field_count, 0)
                    });
                    heap_.record_allocation(sizeof(ObjectHeader) + type.instance_field_count * sizeof(std::int32_t));
                    registers.at(instruction.destination) = encode_object_handle(objects.size() - 1);
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

                    auto& object = require_object(registers.at(instruction.left));
                    if (object.type_id != field.owner_type_id)
                    {
                        throw std::runtime_error("field load targets the wrong receiver type");
                    }

                    if (field.instance_slot >= object.fields.size())
                    {
                        throw std::runtime_error("field slot is outside the object layout");
                    }

                    registers.at(instruction.destination) = object.fields[field.instance_slot];
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

                    auto& object = require_object(registers.at(instruction.destination));
                    if (object.type_id != field.owner_type_id)
                    {
                        throw std::runtime_error("field store targets the wrong receiver type");
                    }

                    if (field.instance_slot >= object.fields.size())
                    {
                        throw std::runtime_error("field slot is outside the object layout");
                    }

                    object.fields[field.instance_slot] = registers.at(instruction.left);
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

                    registers.at(instruction.destination) = static_fields[field_id];
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

                    static_fields[field_id] = registers.at(instruction.left);
                    ++ip;
                    break;
                }
                case OpCode::new_arr:
                {
                    const auto length = registers.at(instruction.left);
                    if (length < 0)
                    {
                        throw std::runtime_error("array length cannot be negative");
                    }

                    arrays.push_back(ArrayObject { .elements = std::vector<std::int32_t>(static_cast<std::size_t>(length), 0) });
                    registers.at(instruction.destination) = encode_array_handle(arrays.size() - 1);
                    ++ip;
                    break;
                }
                case OpCode::ld_elem:
                {
                    const auto source = registers.at(instruction.left);
                    const auto index_value = registers.at(instruction.right);
                    if (source == 0)
                    {
                        throw std::runtime_error("null reference element access");
                    }

                    if (is_array_handle(source))
                    {
                        auto& array = require_array(source);
                        const auto index = require_index(index_value, array.elements.size());
                        registers.at(instruction.destination) = array.elements[index];
                    }
                    else if (is_string_handle(source))
                    {
                        const auto& text = require_string(source);
                        const auto index = require_index(index_value, text.size());
                        registers.at(instruction.destination) = static_cast<std::int32_t>(static_cast<unsigned char>(text[index]));
                    }
                    else
                    {
                        throw std::runtime_error("element load requested for unsupported value kind");
                    }

                    ++ip;
                    break;
                }
                case OpCode::st_elem:
                {
                    if (registers.at(instruction.destination) == 0)
                    {
                        throw std::runtime_error("null reference element access");
                    }

                    auto& array = require_array(registers.at(instruction.destination));
                    const auto index = require_index(registers.at(instruction.left), array.elements.size());
                    array.elements[index] = registers.at(instruction.right);
                    ++ip;
                    break;
                }
                case OpCode::ld_len:
                {
                    const auto value = registers.at(instruction.left);
                    if (value == 0)
                    {
                        throw std::runtime_error("null reference length access");
                    }

                    if (is_array_handle(value))
                    {
                        registers.at(instruction.destination) = static_cast<std::int32_t>(require_array(value).elements.size());
                    }
                    else if (is_string_handle(value))
                    {
                        registers.at(instruction.destination) = static_cast<std::int32_t>(require_string(value).size());
                    }
                    else
                    {
                        throw std::runtime_error("length requested for unsupported value kind");
                    }

                    ++ip;
                    break;
                }
                    case OpCode::throw_:
                        throw ManagedException { registers.at(instruction.destination) };
                    case OpCode::rethrow:
                        if (!has_current_exception)
                        {
                            throw std::runtime_error("rethrow used without an active exception");
                        }

                        throw ManagedException { current_exception_value };
                    case OpCode::br:
                        ip = static_cast<std::size_t>(instruction.immediate);
                        break;
                    case OpCode::br_false:
                        if (registers.at(instruction.destination) == 0)
                        {
                            ip = static_cast<std::size_t>(instruction.immediate);
                        }
                        else
                        {
                            ++ip;
                        }

                        break;
                    case OpCode::ret:
                        if (!function.returns_value)
                        {
                            return 0;
                        }

                        return registers.at(function.argument_count);
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
                    registers.at(handler->target_register) = ex.value;
                }

                ip = handler->handler_start;
            }
        }

        return 0;
    };

    try
    {
        return execute_function(*entry_function, {});
    }
    catch (const ManagedException&)
    {
        throw std::runtime_error("unhandled managed exception");
    }
}
} // namespace ilcvm
