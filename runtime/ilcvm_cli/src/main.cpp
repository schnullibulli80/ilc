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
            bool performance = false;
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

                if (argument == "--performance")
                {
                    performance = true;
                    continue;
                }

                throw std::runtime_error("unknown ilcvm runtime option");
            }

            if (run)
            {
                ilcvm::StandardHostServices host_services(program_arguments);
                ilcvm::VirtualMachine vm(heap, host_services);
                (void)trace;
                ilcvm::VirtualMachine::ExecutionProfile execution_profile;
                const auto execution_result = performance
                    ? vm.execute(module, execution_profile)
                    : vm.execute(module);
                if (performance)
                {
                    std::cout << "performance.total_execution_ns=" << execution_profile.total_execution_ns << '\n';
                    std::cout << "performance.host_import_execution_ns=" << execution_profile.host_import_execution_ns << '\n';
                    std::cout << "performance.call_execution_ns=" << execution_profile.call_execution_ns << '\n';
                    std::cout << "performance.call_virt_execution_ns=" << execution_profile.call_virt_execution_ns << '\n';
                    std::cout << "performance.array_execution_ns=" << execution_profile.array_execution_ns << '\n';
                    std::cout << "performance.ld_elem_execution_ns=" << execution_profile.ld_elem_execution_ns << '\n';
                    std::cout << "performance.st_elem_execution_ns=" << execution_profile.st_elem_execution_ns << '\n';
                    std::cout << "performance.ld_len_execution_ns=" << execution_profile.ld_len_execution_ns << '\n';
                    std::cout << "performance.new_arr_execution_ns=" << execution_profile.new_arr_execution_ns << '\n';
                    std::cout << "performance.mov_execution_ns=" << execution_profile.mov_execution_ns << '\n';
                    std::cout << "performance.compare_execution_ns=" << execution_profile.compare_execution_ns << '\n';
                    std::cout << "performance.branch_execution_ns=" << execution_profile.branch_execution_ns << '\n';
                    std::cout << "performance.instructions_executed=" << execution_profile.instructions_executed << '\n';
                    std::cout << "performance.functions_executed=" << execution_profile.functions_executed << '\n';
                    std::cout << "performance.host_import_calls=" << execution_profile.host_import_calls << '\n';
                    std::cout << "performance.call_count=" << execution_profile.call_count << '\n';
                    std::cout << "performance.call_virt_count=" << execution_profile.call_virt_count << '\n';
                    std::cout << "performance.new_obj_count=" << execution_profile.new_obj_count << '\n';
                    std::cout << "performance.new_arr_count=" << execution_profile.new_arr_count << '\n';
                    std::cout << "performance.ld_elem_count=" << execution_profile.ld_elem_count << '\n';
                    std::cout << "performance.st_elem_count=" << execution_profile.st_elem_count << '\n';
                    std::cout << "performance.ld_len_count=" << execution_profile.ld_len_count << '\n';
                    std::cout << "performance.mov_count=" << execution_profile.mov_count << '\n';
                    std::cout << "performance.compare_count=" << execution_profile.compare_count << '\n';
                    std::cout << "performance.branch_count=" << execution_profile.branch_count << '\n';
                    std::cout << "performance.strings_created=" << execution_profile.strings_created << '\n';
                    std::cout << "performance.arrays_created=" << execution_profile.arrays_created << '\n';
                    std::cout << "performance.objects_created=" << execution_profile.objects_created << '\n';
                    std::cout << "performance.leaf_fastpath_calls=" << execution_profile.leaf_fastpath_calls << '\n';
                    std::cout << "performance.leaf_fastpath_execution_ns=" << execution_profile.leaf_fastpath_execution_ns << '\n';
                    std::cout << "performance.specialized_leaf_fastpath_calls=" << execution_profile.specialized_leaf_fastpath_calls << '\n';
                    std::cout << "performance.max_call_depth=" << execution_profile.max_call_depth << '\n';
                }
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
