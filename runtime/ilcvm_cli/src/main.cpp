#include "ilcvm/heap.h"
#include "ilcvm/module.h"
#include "ilcvm/std_host_services.h"
#include "ilcvm/virtual_machine.h"

#include <filesystem>
#include <fstream>
#include <cctype>
#include <iostream>
#include <memory>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace
{
struct DebugSymbolVariable
{
    std::string function_name;
    std::string name;
    std::string type_name;
    std::uint32_t register_index {};
    std::uint32_t vm_ip_start {};
    std::uint32_t vm_ip_end {};
    bool is_argument {};
};

struct DebugSymbolFunction
{
    std::uint32_t function_id {};
    std::string short_name;
    std::string display_name;
    std::string owner_name;
    std::uint32_t instruction_count {};
    std::uint32_t vm_ip_end {};
    std::string source_path;
};

struct DebugSourceLocation
{
    std::uint32_t function_id {};
    std::string function_name;
    std::uint32_t vm_ip_start {};
    std::uint32_t vm_ip_end {};
    std::string source_path;
    std::uint32_t start_line {};
    std::uint32_t start_column {};
    std::uint32_t end_line {};
    std::uint32_t end_column {};
    std::uint32_t span_start {};
    std::uint32_t span_length {};
};

struct DebugSymbols
{
    std::string module_path;
    std::unordered_map<std::uint32_t, DebugSymbolFunction> functions;
    std::unordered_map<std::uint32_t, std::vector<DebugSymbolVariable>> variables_by_function;
    std::unordered_map<std::uint32_t, std::vector<DebugSourceLocation>> source_locations_by_function;
};

const ilcvm::Type* try_find_type(const ilcvm::Module& module, std::uint32_t type_id)
{
    for (const auto& type : module.types)
    {
        if (type.type_id == type_id)
        {
            return &type;
        }
    }

    return nullptr;
}

std::string get_function_display_name(const ilcvm::Module& module, const ilcvm::Function& function)
{
    if (function.owner_type_id == 0)
    {
        return function.name;
    }

    if (const auto* owner_type = try_find_type(module, function.owner_type_id); owner_type != nullptr)
    {
        return owner_type->name + "." + function.name;
    }

    return function.name;
}

bool is_all_digits(std::string_view text)
{
    if (text.empty())
    {
        return false;
    }

    for (const auto ch : text)
    {
        if (ch < '0' || ch > '9')
        {
            return false;
        }
    }

    return true;
}

std::string trim_copy(std::string_view text)
{
    std::size_t start = 0;
    while (start < text.size() && std::isspace(static_cast<unsigned char>(text[start])) != 0)
    {
        ++start;
    }

    std::size_t end = text.size();
    while (end > start && std::isspace(static_cast<unsigned char>(text[end - 1])) != 0)
    {
        --end;
    }

    return std::string(text.substr(start, end - start));
}

std::optional<DebugSymbols> try_load_debug_symbols(const std::string& ilb_path)
{
    const auto debug_symbols_path = std::filesystem::path(ilb_path).replace_extension(".ildbg");
    std::ifstream input(debug_symbols_path);
    if (!input.is_open())
    {
        return std::nullopt;
    }

    DebugSymbols symbols;
    std::string line;
    while (std::getline(input, line))
    {
        if (line.empty())
        {
            continue;
        }

        std::vector<std::string> parts;
        std::size_t start = 0;
        for (;;)
        {
            const auto separator = line.find('\t', start);
            if (separator == std::string::npos)
            {
                parts.push_back(line.substr(start));
                break;
            }

            parts.push_back(line.substr(start, separator - start));
            start = separator + 1;
        }

        if (parts.empty())
        {
            continue;
        }

        if (parts[0] == "module")
        {
            if (parts.size() >= 2)
            {
                symbols.module_path = parts[1];
            }

            continue;
        }

        if (parts[0] == "function" && parts.size() >= 7)
        {
            const auto function_id = static_cast<std::uint32_t>(std::stoul(parts[1]));
            symbols.functions.emplace(
                function_id,
                DebugSymbolFunction {
                    .function_id = function_id,
                    .short_name = parts[2],
                    .display_name = parts[3],
                    .owner_name = parts[4],
                    .instruction_count = static_cast<std::uint32_t>(std::stoul(parts[5])),
                    .vm_ip_end = static_cast<std::uint32_t>(std::stoul(parts[6])),
                    .source_path = parts.size() >= 8 ? parts[7] : std::string {}
                });
            continue;
        }

        if ((parts[0] == "arg" || parts[0] == "local") && parts.size() >= 8)
        {
            const auto function_id = static_cast<std::uint32_t>(std::stoul(parts[1]));
            symbols.variables_by_function[function_id].push_back(DebugSymbolVariable {
                .function_name = parts[2],
                .name = parts[3],
                .type_name = parts[4],
                .register_index = static_cast<std::uint32_t>(std::stoul(parts[5])),
                .vm_ip_start = static_cast<std::uint32_t>(std::stoul(parts[6])),
                .vm_ip_end = static_cast<std::uint32_t>(std::stoul(parts[7])),
                .is_argument = parts[0] == "arg"
            });
            continue;
        }

        if (parts[0] == "map" && parts.size() >= 12)
        {
            const auto function_id = static_cast<std::uint32_t>(std::stoul(parts[1]));
            symbols.source_locations_by_function[function_id].push_back(DebugSourceLocation {
                .function_id = function_id,
                .function_name = parts[2],
                .vm_ip_start = static_cast<std::uint32_t>(std::stoul(parts[3])),
                .vm_ip_end = static_cast<std::uint32_t>(std::stoul(parts[4])),
                .source_path = parts[5],
                .start_line = static_cast<std::uint32_t>(std::stoul(parts[6])),
                .start_column = static_cast<std::uint32_t>(std::stoul(parts[7])),
                .end_line = static_cast<std::uint32_t>(std::stoul(parts[8])),
                .end_column = static_cast<std::uint32_t>(std::stoul(parts[9])),
                .span_start = static_cast<std::uint32_t>(std::stoul(parts[10])),
                .span_length = static_cast<std::uint32_t>(std::stoul(parts[11]))
            });
        }
    }

    return symbols;
}

std::uint32_t parse_u32(std::string_view text, const char* description)
{
    try
    {
        return static_cast<std::uint32_t>(std::stoul(std::string(text)));
    }
    catch (const std::exception&)
    {
        throw std::runtime_error(std::string("invalid ") + description + ": " + std::string(text));
    }
}

const DebugSourceLocation* find_source_location(const DebugSymbols& debug_symbols, std::uint32_t function_id, std::uint32_t vm_ip)
{
    const auto locations_it = debug_symbols.source_locations_by_function.find(function_id);
    if (locations_it == debug_symbols.source_locations_by_function.end())
    {
        return nullptr;
    }

    const DebugSourceLocation* best_location = nullptr;
    for (const auto& location : locations_it->second)
    {
        if (vm_ip >= location.vm_ip_start && vm_ip <= location.vm_ip_end)
        {
            if (best_location == nullptr || location.span_length < best_location->span_length)
            {
                best_location = &location;
            }
        }
    }

    return best_location;
}

std::optional<ilcvm::VirtualMachine::DebugBreakpoint> try_parse_source_breakpoint_spec(
    const DebugSymbols* debug_symbols,
    std::string_view spec)
{
    if (debug_symbols == nullptr)
    {
        return std::nullopt;
    }

    std::string source_path;
    std::uint32_t line = 0;
    const auto separator = spec.rfind(':');
    if (separator == std::string_view::npos)
    {
        if (!is_all_digits(spec))
        {
            return std::nullopt;
        }

        source_path = debug_symbols->module_path;
        line = parse_u32(spec, "debugger source line");
    }
    else
    {
        const auto file_part = spec.substr(0, separator);
        const auto line_part = spec.substr(separator + 1);
        if (!is_all_digits(line_part))
        {
            return std::nullopt;
        }

        source_path = std::string(file_part);
        line = parse_u32(line_part, "debugger source line");
    }

    if (source_path.empty())
    {
        return std::nullopt;
    }

    ilcvm::VirtualMachine::DebugBreakpoint best_breakpoint {};
    const DebugSourceLocation* best_location = nullptr;
    bool found = false;
    for (const auto& [function_id, locations] : debug_symbols->source_locations_by_function)
    {
        for (const auto& location : locations)
        {
            const auto matches_source =
                location.source_path == source_path
                || std::filesystem::path(location.source_path).filename() == std::filesystem::path(source_path).filename();
            if (!matches_source || line < location.start_line || line > location.end_line)
            {
                continue;
            }

            if (!found
                || location.start_line < best_location->start_line
                || (location.start_line == best_location->start_line && location.start_column < best_location->start_column)
                || (location.start_line == best_location->start_line && location.start_column == best_location->start_column && location.vm_ip_start < best_breakpoint.vm_ip))
            {
                best_breakpoint = ilcvm::VirtualMachine::DebugBreakpoint {
                    .function_id = function_id,
                    .vm_ip = location.vm_ip_start
                };
                best_location = &location;
                found = true;
            }
        }
    }

    if (!found)
    {
        return std::nullopt;
    }

    return best_breakpoint;
}

ilcvm::VirtualMachine::DebugBreakpoint parse_breakpoint_spec(const ilcvm::Module& module, const DebugSymbols* debug_symbols, std::string_view spec)
{
    const auto separator = spec.rfind(':');
    if (separator == std::string_view::npos)
    {
        if (const auto source_breakpoint = try_parse_source_breakpoint_spec(debug_symbols, spec); source_breakpoint.has_value())
        {
            return *source_breakpoint;
        }

        throw std::runtime_error("invalid debugger breakpoint, expected <function|id>:<vm-ip> or [file:]<line>");
    }

    if (separator == 0 || separator + 1 >= spec.size())
    {
        throw std::runtime_error("invalid debugger breakpoint, expected <function|id>:<vm-ip> or [file:]<line>");
    }

    const auto function_part = spec.substr(0, separator);
    const auto ip_part = spec.substr(separator + 1);
    if (const auto source_breakpoint = try_parse_source_breakpoint_spec(debug_symbols, spec); source_breakpoint.has_value())
    {
        return *source_breakpoint;
    }

    const auto vm_ip = parse_u32(ip_part, "debugger vm-ip");

    if (is_all_digits(function_part))
    {
        return ilcvm::VirtualMachine::DebugBreakpoint {
            .function_id = parse_u32(function_part, "debugger function id"),
            .vm_ip = vm_ip
        };
    }

    for (const auto& function : module.functions)
    {
        if (get_function_display_name(module, function) == function_part || function.name == function_part)
        {
            return ilcvm::VirtualMachine::DebugBreakpoint {
                .function_id = function.function_id,
                .vm_ip = vm_ip
            };
        }
    }

    throw std::runtime_error("unknown debugger function in breakpoint: " + std::string(function_part));
}

void load_debug_script(
    const ilcvm::Module& module,
    const DebugSymbols* debug_symbols,
    const std::string& script_path,
    bool& debug_break_on_entry,
    std::uint64_t& debug_step_count,
    std::string& debug_log_path,
    std::vector<std::string>& debug_break_specs)
{
    std::ifstream input(script_path);
    if (!input.is_open())
    {
        throw std::runtime_error("failed to open debugger script file");
    }

    std::string line;
    std::size_t line_number = 0;
    while (std::getline(input, line))
    {
        ++line_number;
        const auto trimmed = trim_copy(line);
        if (trimmed.empty() || trimmed[0] == '#')
        {
            continue;
        }

        std::istringstream tokens(trimmed);
        std::string command;
        tokens >> command;

        if (command == "break")
        {
            std::string spec;
            tokens >> spec;
            if (spec.empty())
            {
                throw std::runtime_error("debugger script break requires <function|id>:<vm-ip> at line " + std::to_string(line_number));
            }

            (void)parse_breakpoint_spec(module, debug_symbols, spec);
            debug_break_specs.push_back(spec);
            continue;
        }

        if (command == "break-on-entry")
        {
            debug_break_on_entry = true;
            continue;
        }

        if (command == "steps")
        {
            std::string value;
            tokens >> value;
            if (value.empty())
            {
                throw std::runtime_error("debugger script steps requires <n> at line " + std::to_string(line_number));
            }

            debug_step_count = static_cast<std::uint64_t>(std::stoull(value));
            continue;
        }

        if (command == "log")
        {
            std::string value;
            tokens >> value;
            if (value.empty())
            {
                throw std::runtime_error("debugger script log requires <path> at line " + std::to_string(line_number));
            }

            debug_log_path = value;
            continue;
        }

        if (command == "run" || command == "continue" || command == "quit")
        {
            continue;
        }

        throw std::runtime_error(
            "unknown debugger script command '" + command + "' at line " + std::to_string(line_number));
    }
}

void write_debug_event(std::ostream& output, const ilcvm::VirtualMachine::DebugEvent& event)
{
    output << "debug.event "
           << (event.breakpoint_hit ? "breakpoint" : "step")
           << " function=" << event.frame.function_id
           << " functionName=" << event.frame.function_name
           << " vm-ip=" << event.frame.vm_ip
           << " opcode=" << static_cast<int>(event.instruction.opcode)
           << " dst=" << event.instruction.destination
           << " left=" << event.instruction.left
           << " right=" << event.instruction.right
           << " imm=" << event.instruction.immediate
           << '\n';

    output << "debug.callstack depth=" << event.call_stack.size() << '\n';
    for (const auto& frame : event.call_stack)
    {
        output << "  frame function=" << frame.function_id
               << " functionName=" << frame.function_name
               << " vm-ip=" << frame.vm_ip
               << '\n';
    }

    output << "debug.registers\n";
    bool wrote_register = false;
    for (std::size_t index = 0; index < event.registers.size(); ++index)
    {
        if (event.registers[index] == 0)
        {
            continue;
        }

        output << "  r" << index << '=' << event.registers[index] << '\n';
        wrote_register = true;
    }
    if (!wrote_register)
    {
        output << "  <all-zero>\n";
    }
    output << std::flush;
}

void write_debug_callstack(std::ostream& output, const ilcvm::VirtualMachine::DebugEvent& event)
{
    output << "debug.callstack depth=" << event.call_stack.size() << '\n';
    for (const auto& frame : event.call_stack)
    {
        output << "  frame function=" << frame.function_id
               << " functionName=" << frame.function_name
               << " vm-ip=" << frame.vm_ip
               << '\n';
    }
    output << std::flush;
}

void write_debug_callstack(
    std::ostream& output,
    const ilcvm::VirtualMachine::DebugEvent& event,
    const DebugSymbols* debug_symbols)
{
    output << "debug.callstack depth=" << event.call_stack.size() << '\n';
    for (const auto& frame : event.call_stack)
    {
        output << "  frame function=" << frame.function_id
               << " functionName=" << frame.function_name
               << " vm-ip=" << frame.vm_ip;
        if (debug_symbols != nullptr)
        {
            if (const auto* location = find_source_location(*debug_symbols, frame.function_id, frame.vm_ip); location != nullptr)
            {
                output << " source=" << location->source_path
                       << ':' << location->start_line
                       << ':' << location->start_column;
            }
        }
        output << '\n';
    }
    output << std::flush;
}

void write_debug_registers(std::ostream& output, const ilcvm::VirtualMachine::DebugEvent& event)
{
    output << "debug.registers\n";
    bool wrote_register = false;
    for (std::size_t index = 0; index < event.registers.size(); ++index)
    {
        if (event.registers[index] == 0)
        {
            continue;
        }

        output << "  r" << index << '=' << event.registers[index];
        if (index < event.register_displays.size())
        {
            output << " [" << event.register_displays[index] << ']';
        }
        output << '\n';
        wrote_register = true;
    }
    if (!wrote_register)
    {
        output << "  <all-zero>\n";
    }
    output << std::flush;
}

void write_debug_help(std::ostream& output)
{
    output << "debug.help commands:\n"
           << "  step | s       execute one instruction\n"
           << "  next | n       step over calls\n"
           << "  continue | c   continue until next breakpoint\n"
           << "  breakpoints    CLI accepts <function|id>:<vm-ip> or [file:]<line>\n"
           << "  where          print current function and vm-ip\n"
           << "  args           print visible arguments\n"
           << "  locals         print visible locals\n"
           << "  print <name>   print one visible variable\n"
           << "  bt             print callstack\n"
           << "  regs           print non-zero registers\n"
           << "  event          print current event again\n"
           << "  help | h | ?   show help\n"
           << "  quit | q       stop execution\n";
    output << std::flush;
}

void write_debug_where(
    std::ostream& output,
    const ilcvm::VirtualMachine::DebugEvent& event,
    const DebugSymbols* debug_symbols)
{
    output << "debug.where function=" << event.frame.function_id
           << " functionName=" << event.frame.function_name
           << " vm-ip=" << event.frame.vm_ip;
    if (debug_symbols != nullptr)
    {
        if (const auto it = debug_symbols->functions.find(event.frame.function_id); it != debug_symbols->functions.end())
        {
            output << " owner=" << it->second.owner_name
                   << " vm-ip-end=" << it->second.vm_ip_end;
        }

        if (const auto* location = find_source_location(*debug_symbols, event.frame.function_id, event.frame.vm_ip); location != nullptr)
        {
            output << " source=" << location->source_path
                   << ':' << location->start_line
                   << ':' << location->start_column;
        }
    }
    output << '\n' << std::flush;
}

void write_debug_variables(
    std::ostream& output,
    const ilcvm::VirtualMachine::DebugEvent& event,
    const DebugSymbols* debug_symbols,
    bool arguments_only)
{
    if (debug_symbols == nullptr)
    {
        output << "debug.variables unavailable: no .ildbg loaded\n" << std::flush;
        return;
    }

    const auto function_it = debug_symbols->variables_by_function.find(event.frame.function_id);
    if (function_it == debug_symbols->variables_by_function.end())
    {
        output << "debug.variables none\n" << std::flush;
        return;
    }

    output << (arguments_only ? "debug.args" : "debug.locals") << '\n';
    bool wrote_any = false;
    for (const auto& variable : function_it->second)
    {
        if (variable.is_argument != arguments_only)
        {
            continue;
        }

        if (event.frame.vm_ip < variable.vm_ip_start || event.frame.vm_ip > variable.vm_ip_end)
        {
            continue;
        }

        const auto value =
            variable.register_index < event.registers.size()
                ? event.registers[variable.register_index]
                : 0;
        output << "  " << variable.name
               << ": " << variable.type_name
               << " = " << value;
        if (variable.register_index < event.register_displays.size())
        {
            output << " [" << event.register_displays[variable.register_index] << ']';
        }
        output << " (r" << variable.register_index << ")\n";
        wrote_any = true;
    }

    if (!wrote_any)
    {
        output << "  <none>\n";
    }

    output << std::flush;
}

void write_debug_print(
    std::ostream& output,
    const ilcvm::VirtualMachine::DebugEvent& event,
    const DebugSymbols* debug_symbols,
    std::string_view name)
{
    if (debug_symbols == nullptr)
    {
        output << "debug.print unavailable: no .ildbg loaded\n" << std::flush;
        return;
    }

    const auto function_it = debug_symbols->variables_by_function.find(event.frame.function_id);
    if (function_it == debug_symbols->variables_by_function.end())
    {
        output << "debug.print unknown name: " << name << '\n' << std::flush;
        return;
    }

    for (const auto& variable : function_it->second)
    {
        if (variable.name != name)
        {
            continue;
        }

        if (event.frame.vm_ip < variable.vm_ip_start || event.frame.vm_ip > variable.vm_ip_end)
        {
            continue;
        }

        const auto value =
            variable.register_index < event.registers.size()
                ? event.registers[variable.register_index]
                : 0;
        output << "debug.print\n";
        output << "  " << variable.name
               << ": " << variable.type_name
               << " = " << value;
        if (variable.register_index < event.register_displays.size())
        {
            output << " [" << event.register_displays[variable.register_index] << ']';
        }
        output << " (r" << variable.register_index << ")\n";
        output << std::flush;
        return;
    }

    output << "debug.print unknown name: " << name << '\n' << std::flush;
}

std::string format_stack_trace_frame(
    const ilcvm::VirtualMachine::DebugFrame& frame,
    const DebugSymbols* debug_symbols)
{
    std::string function_name = frame.function_name;
    if (debug_symbols != nullptr)
    {
        if (const auto function_it = debug_symbols->functions.find(frame.function_id); function_it != debug_symbols->functions.end())
        {
            function_name = function_it->second.display_name;
        }
    }

    if (debug_symbols != nullptr)
    {
        if (const auto* location = find_source_location(*debug_symbols, frame.function_id, frame.vm_ip); location != nullptr)
        {
            return "   at " + function_name + "() in " + location->source_path + ":line " + std::to_string(location->start_line);
        }
    }

    return "   at " + function_name + "() [function=" + std::to_string(frame.function_id) + ", vm-ip=" + std::to_string(frame.vm_ip) + ']';
}

ilcvm::VirtualMachine::DebugAction run_debugger_repl(
    std::istream& input,
    std::ostream& output,
    const ilcvm::VirtualMachine::DebugEvent& event,
    const DebugSymbols* debug_symbols)
{
    write_debug_event(output, event);
    for (;;)
    {
        output << "ilcvm(debug)> " << std::flush;

        std::string line;
        if (!std::getline(input, line))
        {
            return ilcvm::VirtualMachine::DebugAction::quit;
        }

        const auto trimmed = trim_copy(line);
        if (trimmed.empty())
        {
            return ilcvm::VirtualMachine::DebugAction::step_into;
        }

        if (trimmed == "step" || trimmed == "s")
        {
            return ilcvm::VirtualMachine::DebugAction::step_into;
        }

        if (trimmed == "next" || trimmed == "n")
        {
            return ilcvm::VirtualMachine::DebugAction::step_over;
        }

        if (trimmed == "continue" || trimmed == "c")
        {
            return ilcvm::VirtualMachine::DebugAction::continue_execution;
        }

        if (trimmed == "quit" || trimmed == "q")
        {
            return ilcvm::VirtualMachine::DebugAction::quit;
        }

        if (trimmed == "bt")
        {
            write_debug_callstack(output, event, debug_symbols);
            continue;
        }

        if (trimmed == "where")
        {
            write_debug_where(output, event, debug_symbols);
            continue;
        }

        if (trimmed == "args")
        {
            write_debug_variables(output, event, debug_symbols, true);
            continue;
        }

        if (trimmed == "locals")
        {
            write_debug_variables(output, event, debug_symbols, false);
            continue;
        }

        if (trimmed.rfind("print ", 0) == 0)
        {
            write_debug_print(output, event, debug_symbols, trim_copy(trimmed.substr(6)));
            continue;
        }

        if (trimmed == "regs")
        {
            write_debug_registers(output, event);
            continue;
        }

        if (trimmed == "event")
        {
            write_debug_event(output, event);
            continue;
        }

        if (trimmed == "help" || trimmed == "h" || trimmed == "?")
        {
            write_debug_help(output);
            continue;
        }

        output << "debug.error unknown command: " << trimmed << '\n';
        write_debug_help(output);
    }
}
}

int main(int argc, char** argv)
{
    try
    {
        ilcvm::Heap heap;

        if (argc > 1)
        {
            bool run = false;
            bool trace = false;
            bool performance = false;
            bool debug = false;
            bool vm_debug = false;
            bool debug_repl = false;
            bool debug_break_on_entry = false;
            std::uint64_t debug_step_count = 0;
            std::string debug_log_path;
            std::string debug_script_path;
            std::vector<std::string> debug_break_specs;
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

                    if (argument == "--debug")
                    {
                        debug = true;
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

                if (argument == "--vm-debug")
                {
                    vm_debug = true;
                    continue;
                }

                if (argument == "--debug-repl")
                {
                    vm_debug = true;
                    debug_repl = true;
                    continue;
                }

                if (argument == "--debug-break-on-entry")
                {
                    vm_debug = true;
                    debug_break_on_entry = true;
                    continue;
                }

                if (argument == "--debug-break")
                {
                    if (index + 1 >= argc)
                    {
                        throw std::runtime_error("missing value for --debug-break");
                    }

                    vm_debug = true;
                    debug_break_specs.emplace_back(argv[++index]);
                    continue;
                }

                if (argument == "--debug-steps")
                {
                    if (index + 1 >= argc)
                    {
                        throw std::runtime_error("missing value for --debug-steps");
                    }

                    vm_debug = true;
                    debug_step_count = static_cast<std::uint64_t>(std::stoull(argv[++index]));
                    continue;
                }

                if (argument == "--debug-log")
                {
                    if (index + 1 >= argc)
                    {
                        throw std::runtime_error("missing value for --debug-log");
                    }

                    vm_debug = true;
                    debug_log_path = argv[++index];
                    continue;
                }

                if (argument == "--debug-script")
                {
                    if (index + 1 >= argc)
                    {
                        throw std::runtime_error("missing value for --debug-script");
                    }

                    vm_debug = true;
                    debug_script_path = argv[++index];
                    continue;
                }

                if (argument == "--performance")
                {
                    performance = true;
                    continue;
                }

                throw std::runtime_error("unknown ilcvm runtime option");
            }

            const auto module = ilcvm::load_module_from_ilb_file(argv[1]);
            const auto debug_symbols = try_load_debug_symbols(argv[1]);
            if (debug)
            {
                std::cout << "loaded ilb module: functions=" << module.functions.size()
                          << " types=" << module.types.size()
                          << " fields=" << module.fields.size()
                          << " sections=" << module.sections.size()
                          << " entry=" << module.entry_function_id << '\n';
            }

            if (run)
            {
                ilcvm::StandardHostServices host_services(program_arguments);
                ilcvm::VirtualMachine vm(heap, host_services);
                (void)trace;
                ilcvm::VirtualMachine::ExecutionProfile execution_profile;

                if (!debug_script_path.empty())
                {
                    load_debug_script(
                        module,
                        debug_symbols ? &*debug_symbols : nullptr,
                        debug_script_path,
                        debug_break_on_entry,
                        debug_step_count,
                        debug_log_path,
                        debug_break_specs);
                }

                std::unique_ptr<std::ofstream> debug_file;
                std::ostream* debug_output = &std::cout;
                if (vm_debug && !debug_repl && !debug_log_path.empty())
                {
                    debug_file = std::make_unique<std::ofstream>(debug_log_path, std::ios::out | std::ios::trunc);
                    if (!debug_file->is_open())
                    {
                        throw std::runtime_error("failed to open debugger log file");
                    }
                    debug_output = debug_file.get();
                }

                ilcvm::VirtualMachine::DebugOptions debug_options {
                    .breakpoints = {},
                    .break_on_entry = debug_break_on_entry,
                    .step_count_after_break = debug_step_count
                };
                for (const auto& spec : debug_break_specs)
                {
                    debug_options.breakpoints.push_back(parse_breakpoint_spec(module, debug_symbols ? &*debug_symbols : nullptr, spec));
                }

                const auto debug_sink = [debug_repl, debug_output, &debug_symbols](const ilcvm::VirtualMachine::DebugEvent& event)
                {
                    if (debug_repl)
                    {
                        return run_debugger_repl(std::cin, *debug_output, event, debug_symbols ? &*debug_symbols : nullptr);
                    }

                    write_debug_event(*debug_output, event);
                    return ilcvm::VirtualMachine::DebugAction::none;
                };

                const auto stack_trace_formatter = [&debug_symbols](const ilcvm::VirtualMachine::DebugFrame& frame)
                {
                    return format_stack_trace_frame(frame, debug_symbols ? &*debug_symbols : nullptr);
                };

                const auto execution_result = performance
                    ? (vm_debug
                        ? vm.execute(module, execution_profile, debug_options, debug_sink, stack_trace_formatter)
                        : vm.execute(module, execution_profile, stack_trace_formatter))
                    : (vm_debug
                        ? vm.execute(module, debug_options, debug_sink, stack_trace_formatter)
                        : vm.execute(module, stack_trace_formatter));
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
