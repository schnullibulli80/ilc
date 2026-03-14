#include "ilcvm/heap.h"
#include "ilcvm/module.h"
#include "ilcvm/virtual_machine.h"

#include <iostream>
#include <string>

int main(int argc, char** argv)
{
    ilcvm::Heap heap;

    if (argc > 1)
    {
        const auto module = ilcvm::load_module_from_ilb_file(argv[1]);
        std::cout << "loaded ilb module: functions=" << module.functions.size()
                  << " types=" << module.types.size()
                  << " fields=" << module.fields.size()
                  << " sections=" << module.sections.size()
                  << " entry=" << module.entry_function_id << '\n';

        if (argc > 2 && std::string(argv[2]) == "--run")
        {
            ilcvm::VirtualMachine vm(heap);
            const auto exit_code = vm.execute(module);
            std::cout << "execution result: " << exit_code << '\n';
            return exit_code;
        }

        return 0;
    }

    ilcvm::Module module {
        .functions = {
            ilcvm::Function {
                .function_id = 1,
                .name = "Main",
                .register_count = 1,
                .argument_count = 0,
                .returns_value = true,
                .instructions = {
                    { ilcvm::OpCode::ld_i32, 0, 0, 0, 0 },
                    { ilcvm::OpCode::ret, 0, 0, 0, 0 }
                }
            }
        },
        .entry_function_id = 1
    };

    ilcvm::VirtualMachine vm(heap);
    const auto exit_code = vm.execute(module);
    std::cout << "ilcvm bootstrap exit code: " << exit_code << '\n';
    return exit_code;
}
