add_executable(ilcvm_tests
    src/runtime_tests.cpp)

target_link_libraries(ilcvm_tests
    PRIVATE
        ilcvm_core
        ilcvm_host)

if (UNIX AND NOT APPLE)
    target_link_options(ilcvm_tests PRIVATE -rdynamic)
endif()
