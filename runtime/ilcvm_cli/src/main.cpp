#include "ilcvm/heap.h"
#include "ilcvm/module.h"
#include "ilcvm/std_host_services.h"
#include "ilcvm/virtual_machine.h"

#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

int main(int argc, char** argv)
{
    try
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

            bool run = false;
            bool trace = false;
            std::vector<std::string> program_arguments;
            for (int index = 2; index < argc; ++index)
            {
                const std::string argument = argv[index];
                if (!run)
                {
                    if (argument == "--run")
                    {
                        run = true;
                        continue;
                    }

                    throw std::runtime_error("unknown ilcvm option before --run");
                }

                if (argument == "--")
                {
                    for (int program_index = index + 1; program_index < argc; ++program_index)
                    {
                        program_arguments.emplace_back(argv[program_index]);
                    }

                    break;
                }

                if (argument == "--trace")
                {
                    trace = true;
                    continue;
                }

                throw std::runtime_error("unknown ilcvm runtime option");
            }

            if (run)
            {
                ilcvm::StandardHostServices host_services(program_arguments);
                ilcvm::VirtualMachine vm(heap, host_services);
                (void)trace;
                const auto execution_result = vm.execute(module);
                std::cout << "execution result: " << execution_result << '\n';
                return 0;
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

        ilcvm::StandardHostServices host_services;
        ilcvm::VirtualMachine vm(heap, host_services);
        const auto execution_result = vm.execute(module);
        std::cout << "ilcvm bootstrap exit code: " << execution_result << '\n';
        return 0;
    }
    catch (const std::exception& ex)
    {
        std::cerr << "runtime error: " << ex.what() << '\n';
        return 1;
    }
}
