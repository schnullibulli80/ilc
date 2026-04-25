add_library(ilcvm_core
    src/heap.cpp
    src/module.cpp
    src/virtual_machine.cpp)

find_package(PkgConfig REQUIRED)
pkg_check_modules(LIBFFI REQUIRED IMPORTED_TARGET libffi)

target_include_directories(ilcvm_core
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_link_libraries(ilcvm_core
    PRIVATE
        PkgConfig::LIBFFI
        ${CMAKE_DL_LIBS})
