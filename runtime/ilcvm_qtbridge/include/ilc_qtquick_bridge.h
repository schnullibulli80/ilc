#pragma once

#include <cstdint>

#if defined(_WIN32)
#  if defined(ILC_QTBRIDGE_EXPORTS)
#    define ILC_QTBRIDGE_API __declspec(dllexport)
#  else
#    define ILC_QTBRIDGE_API __declspec(dllimport)
#  endif
#else
#  define ILC_QTBRIDGE_API
#endif

extern "C"
{
    using ilc_qtquick_i32_callback = std::int32_t(*)(std::int32_t);

    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_backend_create();
    ILC_QTBRIDGE_API void ilc_qtquick_backend_destroy(std::int32_t backend_handle);

    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_create(std::int32_t backend_handle, const char* title_utf8);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_show(std::int32_t window_handle);
    ILC_QTBRIDGE_API void ilc_qtquick_window_destroy(std::int32_t window_handle);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_set_size(std::int32_t window_handle, std::int32_t width, std::int32_t height);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_set_minimum_size(std::int32_t window_handle, std::int32_t min_width, std::int32_t min_height);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_set_background(std::int32_t window_handle, const char* color_utf8);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_set_root_name(std::int32_t window_handle, const char* root_name_utf8);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_set_content_qml(std::int32_t window_handle, const char* qml_utf8);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_set_element_text(std::int32_t window_handle, const char* object_name_utf8, const char* text_utf8);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_set_element_color(std::int32_t window_handle, const char* object_name_utf8, const char* color_utf8);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_focus_text_input(std::int32_t window_handle, std::int32_t text_box_id);
    ILC_QTBRIDGE_API char* ilc_qtquick_backend_read_text_input_value(std::int32_t backend_handle, std::int32_t text_box_id);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_backend_read_slider_value(std::int32_t backend_handle, std::int32_t slider_id);
    ILC_QTBRIDGE_API void ilc_qtquick_string_free(char* value);

    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_backend_run(std::int32_t backend_handle);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_backend_run_with_click_callback(std::int32_t backend_handle, ilc_qtquick_i32_callback callback);
}
