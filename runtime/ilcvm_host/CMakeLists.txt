add_library(ilcvm_host
    src/std_host_services.cpp)

target_include_directories(ilcvm_host
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_link_libraries(ilcvm_host
    PUBLIC
        ilcvm_core)
