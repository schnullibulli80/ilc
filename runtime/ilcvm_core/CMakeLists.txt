add_library(ilcvm_core
    src/heap.cpp
    src/module.cpp
    src/virtual_machine.cpp)

target_include_directories(ilcvm_core
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)
