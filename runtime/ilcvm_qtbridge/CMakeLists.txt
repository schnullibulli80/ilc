find_package(Qt6 COMPONENTS Core Gui Qml Quick QUIET)

set(ilc_qtbridge_sources)
if (Qt6_FOUND)
    list(APPEND ilc_qtbridge_sources
        src/qtquick_bridge_qt.cpp)
    message(STATUS "ilc_qtbridge: Qt6 backend enabled")
else()
    list(APPEND ilc_qtbridge_sources
        src/qtquick_bridge.cpp)
    message(STATUS "ilc_qtbridge: Qt6 backend not found, building stub backend")
endif()

add_library(ilc_qtbridge SHARED
    ${ilc_qtbridge_sources})

target_include_directories(ilc_qtbridge
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_compile_definitions(ilc_qtbridge
    PRIVATE
        ILC_QTBRIDGE_EXPORTS)

if (Qt6_FOUND)
    target_link_libraries(ilc_qtbridge
        PRIVATE
            Qt6::Core
            Qt6::Gui
            Qt6::Qml
            Qt6::Quick)
endif()

set_target_properties(ilc_qtbridge PROPERTIES
    OUTPUT_NAME ilc_qtbridge)
