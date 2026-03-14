cmake_minimum_required(VERSION 3.30)

project(ILC LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_subdirectory(runtime/ilcvm_core)
add_subdirectory(runtime/ilcvm_cli)
add_subdirectory(runtime/ilcvm_tests)
