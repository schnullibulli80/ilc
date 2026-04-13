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
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_backend_create();
    ILC_QTBRIDGE_API void ilc_qtquick_backend_destroy(std::int32_t backend_handle);

    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_create(std::int32_t backend_handle, const char* title_utf8);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_show(std::int32_t window_handle);
    ILC_QTBRIDGE_API void ilc_qtquick_window_destroy(std::int32_t window_handle);
    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_window_set_root_name(std::int32_t window_handle, const char* root_name_utf8);

    ILC_QTBRIDGE_API std::int32_t ilc_qtquick_backend_run(std::int32_t backend_handle);
}
