#include "ilc_qtquick_bridge.h"

#include <QByteArray>
#include <QColor>
#include <QCoreApplication>
#include <QFile>
#include <QGuiApplication>
#include <QMessageLogContext>
#include <QQuickItem>
#include <QQuickView>
#include <QUrl>

#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iostream>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace
{
    struct BackendState
    {
        std::int32_t handle = 0;
    };

    struct WindowState
    {
        std::int32_t handle = 0;
        std::int32_t backend_handle = 0;
        std::unique_ptr<QQuickView> view;
        std::string content_qml_path;
        std::string title;
        std::string root_name;
    };

    std::mutex bridge_mutex;
    std::unordered_map<std::int32_t, BackendState> backends;
    std::unordered_map<std::int32_t, WindowState> windows;
    std::int32_t next_handle = 1;

    QGuiApplication* qt_application = nullptr;
    std::vector<std::unique_ptr<char[]>> qt_argument_storage;
    std::vector<char*> qt_arguments;
    int qt_argc = 0;
    QtMessageHandler previous_qt_message_handler = nullptr;

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

        std::cerr << "[ilc_qtbridge/qt] " << message << '\n';
    }

    bool should_surface_qt_shutdown_warnings()
    {
        const char* value = std::getenv("ILC_QTBRIDGE_DEBUG_QT_SHUTDOWN");
        return value != nullptr && std::strcmp(value, "1") == 0;
    }

    void qtbridge_message_handler(QtMsgType type, const QMessageLogContext& context, const QString& message)
    {
        const auto text = message.toStdString();
        if (!should_surface_qt_shutdown_warnings() &&
            text.find("QThreadStorage: entry") != std::string::npos)
        {
            return;
        }

        if (previous_qt_message_handler != nullptr)
        {
            previous_qt_message_handler(type, context, message);
            return;
        }

        std::fprintf(stderr, "%s\n", text.c_str());
    }

    std::string safe_utf8(const char* value)
    {
        return value == nullptr ? std::string() : std::string(value);
    }

    bool install_initial_view_state(WindowState& window)
    {
        if (window.view == nullptr)
        {
            qtbridge_debug("install_initial_view_state rejected because view was null");
            return false;
        }

        window.view->setResizeMode(QQuickView::SizeRootObjectToView);
        window.view->setColor(QColor("#20252b"));
        window.view->setWidth(960);
        window.view->setHeight(540);
        if (window.view->contentItem() != nullptr)
        {
            window.view->contentItem()->setObjectName(QString::fromUtf8(window.root_name.empty() ? "ilcRoot" : window.root_name));
        }

        qtbridge_debug("install_initial_view_state succeeded for handle=" + std::to_string(window.handle) + " root='" + (window.root_name.empty() ? std::string("ilcRoot") : window.root_name) + "' title='" + window.title + "'");
        return true;
    }

    bool install_content_qml(WindowState& window, const std::string& qml)
    {
        if (window.view == nullptr)
        {
            qtbridge_debug("install_content_qml rejected because view was null");
            return false;
        }

        window.content_qml_path = "/tmp/ilc_qtbridge_window_" + std::to_string(window.handle) + ".qml";
        qtbridge_debug("install_content_qml handle=" + std::to_string(window.handle) + " length=" + std::to_string(qml.length()) + " path=" + window.content_qml_path);

        std::ofstream output(window.content_qml_path, std::ios::binary | std::ios::trunc);
        output.write(qml.data(), static_cast<std::streamsize>(qml.size()));
        output.close();
        if (!output)
        {
            qtbridge_debug("install_content_qml failed because qml file could not be written");
            return false;
        }

        window.view->setResizeMode(QQuickView::SizeRootObjectToView);
        window.view->setSource(QUrl::fromLocalFile(QString::fromStdString(window.content_qml_path)));
        qtbridge_debug("install_content_qml view_status=" + std::to_string(static_cast<int>(window.view->status())));
        const auto errors = window.view->errors();
        for (const auto& error : errors)
        {
            qtbridge_debug("install_content_qml qml_error=" + error.toString().toStdString());
        }

        if (window.view->status() != QQuickView::Ready)
        {
            qtbridge_debug("install_content_qml failed because view was not ready");
            return false;
        }

        qtbridge_debug("install_content_qml succeeded for handle=" + std::to_string(window.handle));
        return true;
    }

    std::int32_t allocate_handle()
    {
        return next_handle++;
    }

    bool should_force_offscreen_platform()
    {
        const char* value = std::getenv("ILC_QTBRIDGE_FORCE_OFFSCREEN");
        if (value != nullptr)
        {
            return std::strcmp(value, "1") == 0;
        }

        return qEnvironmentVariableIsEmpty("QT_QPA_PLATFORM");
    }

    bool should_run_blocking_event_loop()
    {
        const char* value = std::getenv("ILC_QTBRIDGE_BLOCKING_RUN");
        return value != nullptr && std::strcmp(value, "1") == 0;
    }

    bool ensure_application()
    {
        if (QCoreApplication::instance() != nullptr)
        {
            qtbridge_debug("reusing existing Qt application instance");
            return true;
        }

        if (should_force_offscreen_platform() && qEnvironmentVariableIsEmpty("QT_QPA_PLATFORM"))
        {
            qputenv("QT_QPA_PLATFORM", QByteArray("offscreen"));
            qtbridge_debug("forced QT_QPA_PLATFORM=offscreen");
        }

        qt_argument_storage.clear();
        qt_arguments.clear();

        auto program_name = std::make_unique<char[]>(16);
        std::strcpy(program_name.get(), "ilc_qtbridge");
        qt_arguments.push_back(program_name.get());
        qt_argument_storage.push_back(std::move(program_name));
        qt_argc = static_cast<int>(qt_arguments.size());

        try
        {
            if (previous_qt_message_handler == nullptr)
            {
                previous_qt_message_handler = qInstallMessageHandler(qtbridge_message_handler);
                qtbridge_debug("installed Qt message handler");
            }

            qt_application = new QGuiApplication(qt_argc, qt_arguments.data());
            qtbridge_debug("created QGuiApplication successfully");
            return true;
        }
        catch (const std::exception& ex)
        {
            qtbridge_debug(std::string("failed to create QGuiApplication: ") + ex.what());
            return false;
        }
        catch (...)
        {
            qtbridge_debug("failed to create QGuiApplication: unknown exception");
            return false;
        }
    }
}

extern "C"
{
    std::int32_t ilc_qtquick_backend_create()
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        if (!ensure_application())
        {
            qtbridge_debug("backend_create failed because Qt application initialization failed");
            return 0;
        }

        const std::int32_t handle = allocate_handle();
        backends.emplace(handle, BackendState{.handle = handle});
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

        if (!ensure_application())
        {
            qtbridge_debug("window_create failed because Qt application initialization failed");
            return 0;
        }

        const std::int32_t handle = allocate_handle();
        const std::string title = safe_utf8(title_utf8);

        auto view = std::make_unique<QQuickView>();
        view->setTitle(QString::fromUtf8(title));

        auto [it, inserted] = windows.emplace(handle, WindowState{
            .handle = handle,
            .backend_handle = backend_handle,
            .view = std::move(view),
            .content_qml_path = "",
            .title = title,
            .root_name = "ilcRoot"});
        if (!inserted)
        {
            qtbridge_debug("window_create failed because handle insertion collided unexpectedly");
            return 0;
        }

        if (!install_initial_view_state(it->second))
        {
            qtbridge_debug("window_create failed because initial view setup failed");
            windows.erase(it);
            return 0;
        }

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

        it->second.view->show();
        qtbridge_debug("window_show handle=" + std::to_string(window_handle));
        return 1;
    }

    void ilc_qtquick_window_destroy(std::int32_t window_handle)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        qtbridge_debug("window_destroy handle=" + std::to_string(window_handle));
        const auto it = windows.find(window_handle);
        if (it != windows.end())
        {
            if (!it->second.content_qml_path.empty())
            {
                QFile::remove(QString::fromStdString(it->second.content_qml_path));
                qtbridge_debug("window_destroy removed_qml_path=" + it->second.content_qml_path);
            }

            windows.erase(it);
        }
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
        if (it->second.view->contentItem() != nullptr)
        {
            it->second.view->contentItem()->setObjectName(QString::fromUtf8(it->second.root_name));
        }

        qtbridge_debug("window_set_root_name handle=" + std::to_string(window_handle) + " root='" + it->second.root_name + "'");
        return 1;
    }

    std::int32_t ilc_qtquick_window_set_content_qml(std::int32_t window_handle, const char* qml_utf8)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = windows.find(window_handle);
        if (it == windows.end())
        {
            qtbridge_debug("window_set_content_qml rejected missing_window=" + std::to_string(window_handle));
            return 0;
        }

        const auto qml = safe_utf8(qml_utf8);
        if (qml.empty())
        {
            qtbridge_debug("window_set_content_qml rejected empty qml for handle=" + std::to_string(window_handle));
            return 0;
        }

        return install_content_qml(it->second, qml) ? 1 : 0;
    }

    std::int32_t ilc_qtquick_backend_run(std::int32_t backend_handle)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        if (!backends.contains(backend_handle))
        {
            qtbridge_debug("backend_run rejected missing_backend=" + std::to_string(backend_handle));
            return 0;
        }

        if (QCoreApplication::instance() == nullptr)
        {
            qtbridge_debug("backend_run rejected because no QCoreApplication instance exists");
            return 0;
        }

        if (should_run_blocking_event_loop())
        {
            qtbridge_debug("backend_run entering blocking Qt event loop");
            return QCoreApplication::instance()->exec() == 0 ? 1 : 0;
        }

        QCoreApplication::processEvents();
        qtbridge_debug("backend_run processed pending Qt events without blocking");
        return 1;
    }
}
