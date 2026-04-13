add_library(ilc_qtbridge SHARED
    src/qtquick_bridge.cpp)

target_include_directories(ilc_qtbridge
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_compile_definitions(ilc_qtbridge
    PRIVATE
        ILC_QTBRIDGE_EXPORTS)

set_target_properties(ilc_qtbridge PROPERTIES
    OUTPUT_NAME ilc_qtbridge)
