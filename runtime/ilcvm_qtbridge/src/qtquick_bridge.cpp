#include "ilc_qtquick_bridge.h"

#include <cstdlib>
#include <cstring>
#include <iostream>
#include <mutex>
#include <string>
#include <unordered_map>

namespace
{
    struct BackendState
    {
        std::int32_t handle = 0;
        bool ran = false;
    };

    struct WindowState
    {
        std::int32_t handle = 0;
        std::int32_t backend_handle = 0;
        bool shown = false;
        std::string title;
        std::string root_name;
    };

    bool qtbridge_debug_enabled()
    {
        static const bool enabled = []()
        {
            const char* value = std::getenv("ILC_QTBRIDGE_DEBUG");
            return value != nullptr && std::strcmp(value, "1") == 0;
        }();

        return enabled;
    }

    void qtbridge_debug(const std::string& message)
    {
        if (!qtbridge_debug_enabled())
        {
            return;
        }

        std::cerr << "[ilc_qtbridge] " << message << '\n';
    }

    std::string safe_utf8(const char* value)
    {
        return value == nullptr ? std::string() : std::string(value);
    }

    std::mutex bridge_mutex;
    std::unordered_map<std::int32_t, BackendState> backends;
    std::unordered_map<std::int32_t, WindowState> windows;
    std::int32_t next_handle = 1;

    std::int32_t allocate_handle()
    {
        return next_handle++;
    }
}

extern "C"
{
    std::int32_t ilc_qtquick_backend_create()
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const std::int32_t handle = allocate_handle();
        backends.emplace(handle, BackendState{.handle = handle, .ran = false});
        qtbridge_debug("backend_create handle=" + std::to_string(handle));
        return handle;
    }

    void ilc_qtquick_backend_destroy(std::int32_t backend_handle)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        qtbridge_debug("backend_destroy handle=" + std::to_string(backend_handle));
        backends.erase(backend_handle);

        for (auto it = windows.begin(); it != windows.end();)
        {
            if (it->second.backend_handle == backend_handle)
            {
                qtbridge_debug("backend_destroy cascade_window handle=" + std::to_string(it->first));
                it = windows.erase(it);
            }
            else
            {
                ++it;
            }
        }
    }

    std::int32_t ilc_qtquick_window_create(std::int32_t backend_handle, const char* title_utf8)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        if (!backends.contains(backend_handle))
        {
            qtbridge_debug("window_create rejected missing_backend=" + std::to_string(backend_handle));
            return 0;
        }

        const std::int32_t handle = allocate_handle();
        const std::string title = safe_utf8(title_utf8);
        windows.emplace(handle, WindowState{
            .handle = handle,
            .backend_handle = backend_handle,
            .shown = false,
            .title = title,
            .root_name = ""});
        qtbridge_debug("window_create backend=" + std::to_string(backend_handle) + " handle=" + std::to_string(handle) + " title='" + title + "'");
        return handle;
    }

    std::int32_t ilc_qtquick_window_show(std::int32_t window_handle)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = windows.find(window_handle);
        if (it == windows.end())
        {
            qtbridge_debug("window_show rejected missing_window=" + std::to_string(window_handle));
            return 0;
        }

        it->second.shown = true;
        qtbridge_debug("window_show handle=" + std::to_string(window_handle));
        return 1;
    }

    void ilc_qtquick_window_destroy(std::int32_t window_handle)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        qtbridge_debug("window_destroy handle=" + std::to_string(window_handle));
        windows.erase(window_handle);
    }

    std::int32_t ilc_qtquick_window_set_root_name(std::int32_t window_handle, const char* root_name_utf8)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = windows.find(window_handle);
        if (it == windows.end())
        {
            qtbridge_debug("window_set_root_name rejected missing_window=" + std::to_string(window_handle));
            return 0;
        }

        it->second.root_name = safe_utf8(root_name_utf8);
        qtbridge_debug("window_set_root_name handle=" + std::to_string(window_handle) + " root='" + it->second.root_name + "'");
        return 1;
    }

    std::int32_t ilc_qtquick_backend_run(std::int32_t backend_handle)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = backends.find(backend_handle);
        if (it == backends.end())
        {
            qtbridge_debug("backend_run rejected missing_backend=" + std::to_string(backend_handle));
            return 0;
        }

        it->second.ran = true;
        qtbridge_debug("backend_run handle=" + std::to_string(backend_handle));
        return 1;
    }
}
