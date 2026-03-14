#include "ilcvm/virtual_machine.h"

#include <algorithm>
#include <cstddef>
#include <functional>
#include <stdexcept>
#include <string>
#include <vector>

namespace ilcvm
{
VirtualMachine::VirtualMachine(Heap& heap) noexcept
    : heap_(heap)
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
        if (!is_string_handle(handle))
        {
            throw std::runtime_error("instruction expected a string reference");
        }

        const auto string_id = decode_string_id(handle);
        if (string_id >= module.strings.size())
        {
            throw std::runtime_error("string reference is invalid");
        }

        return module.strings[string_id];
    };
    const auto require_object = [&](std::int32_t handle) -> ManagedObject&
    {
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

    std::function<std::int32_t(const Function&, const std::vector<std::int32_t>&)> execute_function;
    execute_function = [&](const Function& function, const std::vector<std::int32_t>& arguments) -> std::int32_t
    {
        if (arguments.size() != function.argument_count)
        {
            throw std::runtime_error("call target argument count mismatch");
        }

        std::vector<std::int32_t> registers(function.register_count == 0 ? 1 : function.register_count, 0);
        for (std::size_t index = 0; index < arguments.size(); ++index)
        {
            registers.at(index) = arguments[index];
        }

        std::size_t ip = 0;
        while (ip < function.instructions.size())
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
                    if (string_id >= module.strings.size())
                    {
                        throw std::runtime_error("string constant does not reference a known string");
                    }

                    registers.at(instruction.destination) = encode_string_handle(string_id);
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
                    auto& array = require_array(registers.at(instruction.left));
                    const auto index = require_index(registers.at(instruction.right), array.elements.size());
                    registers.at(instruction.destination) = array.elements[index];
                    ++ip;
                    break;
                }
                case OpCode::st_elem:
                {
                    auto& array = require_array(registers.at(instruction.destination));
                    const auto index = require_index(registers.at(instruction.left), array.elements.size());
                    array.elements[index] = registers.at(instruction.right);
                    ++ip;
                    break;
                }
                case OpCode::ld_len:
                {
                    const auto value = registers.at(instruction.left);
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

        return 0;
    };

    return execute_function(*entry_function, {});
}
} // namespace ilcvm
