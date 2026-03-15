add_executable(ilcvm_cli
    src/main.cpp)

target_link_libraries(ilcvm_cli
    PRIVATE
        ilcvm_core
        ilcvm_host)
