#include "ilc_qtquick_bridge.h"

#include <QByteArray>
#include <QColor>
#include <QCoreApplication>
#include <QFile>
#include <QGuiApplication>
#include <QMessageLogContext>
#include <QTimer>
#include <QQmlContext>
#include <QQuickItem>
#include <QQuickView>
#include <QUrl>
#include <QObject>

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
    bool qtbridge_debug_enabled();
    void qtbridge_debug(const std::string& message);

    struct BackendState
    {
        std::int32_t handle = 0;
    };

    struct WindowState
    {
        std::int32_t handle = 0;
        std::int32_t backend_handle = 0;
        std::unique_ptr<QQuickView> view;
        std::unique_ptr<class ClickBridgeObject> click_bridge;
        std::string content_qml_path;
        std::vector<std::string> generated_qml_paths;
        std::int32_t content_qml_revision = 0;
        std::string title;
        std::string root_name;
    };

    std::mutex bridge_mutex;
    std::unordered_map<std::int32_t, BackendState> backends;
    std::unordered_map<std::int32_t, WindowState> windows;
    std::int32_t next_handle = 1;

    void restore_text_input_focus(WindowState& window, std::int32_t text_box_id);

    class ClickBridgeObject final : public QObject
    {
        Q_OBJECT

    public:
        explicit ClickBridgeObject(std::int32_t window_handle, QObject* parent = nullptr)
            : QObject(parent), window_handle_(window_handle)
        {
        }

        void set_callback(ilc_qtquick_i32_callback callback) noexcept
        {
            callback_ = callback;
        }

        std::string read_text_value(std::int32_t text_box_id) const
        {
            const auto it = text_values_.find(text_box_id);
            if (it == text_values_.end())
            {
                return {};
            }

            return it->second;
        }

        bool has_text_value(std::int32_t text_box_id) const
        {
            return text_values_.find(text_box_id) != text_values_.end();
        }

        std::int32_t read_slider_value(std::int32_t slider_id) const
        {
            const auto it = slider_values_.find(slider_id);
            if (it == slider_values_.end())
            {
                return 0;
            }

            return it->second;
        }

        bool has_slider_value(std::int32_t slider_id) const
        {
            return slider_values_.find(slider_id) != slider_values_.end();
        }

        Q_INVOKABLE std::int32_t onButtonClicked(std::int32_t click_count)
        {
            qtbridge_debug(
                "button_click window=" + std::to_string(window_handle_) +
                " clickCount=" + std::to_string(click_count) +
                " hasCallback=" + std::to_string(callback_ != nullptr ? 1 : 0));

            if (callback_ == nullptr)
            {
                return 0;
            }

            const auto callback = callback_;
            const auto window_handle = window_handle_;
            QTimer::singleShot(0, this, [callback, click_count, window_handle]()
            {
                qtbridge_debug(
                    "button_click_dispatch window=" + std::to_string(window_handle) +
                    " clickCount=" + std::to_string(click_count));
                const auto result = callback(click_count);
                qtbridge_debug(
                    "button_click_result window=" + std::to_string(window_handle) +
                    " clickCount=" + std::to_string(click_count) +
                    " result=" + std::to_string(result));
            });

            qtbridge_debug(
                "button_click_queued window=" + std::to_string(window_handle_) +
                " clickCount=" + std::to_string(click_count));
            return 1;
        }

        Q_INVOKABLE std::int32_t onTextEdited(std::int32_t text_box_id, const QString& text)
        {
            const auto utf8_text = text.toUtf8().toStdString();
            text_values_[text_box_id] = utf8_text;
            qtbridge_debug(
                "text_edited window=" + std::to_string(window_handle_) +
                " textBoxId=" + std::to_string(text_box_id) +
                " length=" + std::to_string(utf8_text.length()) +
                " hasCallback=" + std::to_string(callback_ != nullptr ? 1 : 0));

            if (callback_ == nullptr)
            {
                return 0;
            }

            const auto callback = callback_;
            const auto window_handle = window_handle_;
            QTimer::singleShot(0, this, [callback, text_box_id, window_handle]()
            {
                const auto interaction_token = 0 - text_box_id;
                qtbridge_debug(
                    "text_edited_dispatch window=" + std::to_string(window_handle) +
                    " textBoxId=" + std::to_string(text_box_id) +
                    " interactionToken=" + std::to_string(interaction_token));
                const auto result = callback(interaction_token);
                qtbridge_debug(
                    "text_edited_result window=" + std::to_string(window_handle) +
                    " textBoxId=" + std::to_string(text_box_id) +
                    " result=" + std::to_string(result));

                QTimer::singleShot(0, [window_handle, text_box_id]()
                {
                    std::lock_guard<std::mutex> lock(bridge_mutex);
                    const auto it = windows.find(window_handle);
                    if (it == windows.end())
                    {
                        qtbridge_debug(
                            "restore_text_input_focus skipped missing_window=" +
                            std::to_string(window_handle));
                        return;
                    }

                    restore_text_input_focus(it->second, text_box_id);
                });
            });

            qtbridge_debug(
                "text_edited_queued window=" + std::to_string(window_handle_) +
                " textBoxId=" + std::to_string(text_box_id));
            return 1;
        }

        Q_INVOKABLE std::int32_t onSliderValueChanged(std::int32_t slider_id, std::int32_t value)
        {
            slider_values_[slider_id] = value;
            qtbridge_debug(
                "slider_changed window=" + std::to_string(window_handle_) +
                " sliderId=" + std::to_string(slider_id) +
                " value=" + std::to_string(value) +
                " hasCallback=" + std::to_string(callback_ != nullptr ? 1 : 0));

            if (callback_ == nullptr)
            {
                return 0;
            }

            const auto callback = callback_;
            const auto window_handle = window_handle_;
            QTimer::singleShot(0, this, [callback, slider_id, value, window_handle]()
            {
                const auto interaction_token = -2000000 - slider_id;
                qtbridge_debug(
                    "slider_changed_dispatch window=" + std::to_string(window_handle) +
                    " sliderId=" + std::to_string(slider_id) +
                    " value=" + std::to_string(value) +
                    " interactionToken=" + std::to_string(interaction_token));
                const auto result = callback(interaction_token);
                qtbridge_debug(
                    "slider_changed_result window=" + std::to_string(window_handle) +
                    " sliderId=" + std::to_string(slider_id) +
                    " value=" + std::to_string(value) +
                    " result=" + std::to_string(result));
            });

            qtbridge_debug(
                "slider_changed_queued window=" + std::to_string(window_handle_) +
                " sliderId=" + std::to_string(slider_id) +
                " value=" + std::to_string(value));
            return 1;
        }

    private:
        std::int32_t window_handle_ = 0;
        ilc_qtquick_i32_callback callback_ = nullptr;
        std::unordered_map<std::int32_t, std::string> text_values_;
        std::unordered_map<std::int32_t, std::int32_t> slider_values_;
    };

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
        if (window.click_bridge != nullptr)
        {
            window.view->rootContext()->setContextProperty("ilcBridge", window.click_bridge.get());
        }

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

        window.content_qml_revision = window.content_qml_revision + 1;
        window.content_qml_path =
            "/tmp/ilc_qtbridge_window_" +
            std::to_string(window.handle) +
            "_" +
            std::to_string(window.content_qml_revision) +
            ".qml";
        window.generated_qml_paths.push_back(window.content_qml_path);
        qtbridge_debug(
            "install_content_qml handle=" +
            std::to_string(window.handle) +
            " revision=" +
            std::to_string(window.content_qml_revision) +
            " length=" +
            std::to_string(qml.length()) +
            " path=" +
            window.content_qml_path);

        std::ofstream output(window.content_qml_path, std::ios::binary | std::ios::trunc);
        output.write(qml.data(), static_cast<std::streamsize>(qml.size()));
        output.close();
        if (!output)
        {
            qtbridge_debug("install_content_qml failed because qml file could not be written");
            return false;
        }

        if (window.click_bridge != nullptr)
        {
            window.view->rootContext()->setContextProperty("ilcBridge", window.click_bridge.get());
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

    char* duplicate_owned_utf8(const std::string& value)
    {
        auto* buffer = static_cast<char*>(std::malloc(value.size() + 1));
        if (buffer == nullptr)
        {
            qtbridge_debug("duplicate_owned_utf8 allocation_failed length=" + std::to_string(value.size()));
            return nullptr;
        }

        std::memcpy(buffer, value.c_str(), value.size() + 1);
        return buffer;
    }

    std::string build_text_input_object_name(std::int32_t text_box_id)
    {
        return "ilcTextInput_" + std::to_string(text_box_id);
    }

    QObject* find_named_object(WindowState& window, const std::string& object_name)
    {
        if (window.view == nullptr || object_name.empty())
        {
            return nullptr;
        }

        QObject* search_root = window.view->rootObject();
        if (search_root == nullptr)
        {
            search_root = window.view->contentItem();
        }

        if (search_root == nullptr)
        {
            return nullptr;
        }

        return search_root->findChild<QObject*>(QString::fromStdString(object_name), Qt::FindChildrenRecursively);
    }

    void restore_text_input_focus(WindowState& window, std::int32_t text_box_id)
    {
        if (window.view == nullptr)
        {
            qtbridge_debug("restore_text_input_focus rejected because view was null");
            return;
        }

        QObject* search_root = window.view->rootObject();
        if (search_root == nullptr)
        {
            search_root = window.view->contentItem();
        }

        if (search_root == nullptr)
        {
            qtbridge_debug(
                "restore_text_input_focus rejected because search root was null for window=" +
                std::to_string(window.handle));
            return;
        }

        const auto object_name = QString::fromStdString(build_text_input_object_name(text_box_id));
        auto* item = search_root->findChild<QQuickItem*>(object_name, Qt::FindChildrenRecursively);
        if (item == nullptr)
        {
            qtbridge_debug(
                "restore_text_input_focus missing_object window=" +
                std::to_string(window.handle) +
                " textBoxId=" +
                std::to_string(text_box_id) +
                " objectName=" +
                object_name.toStdString());
            return;
        }

        item->forceActiveFocus();
        const auto text_length = item->property("text").toString().size();
        item->setProperty("cursorPosition", text_length);
        qtbridge_debug(
            "restore_text_input_focus window=" +
            std::to_string(window.handle) +
            " textBoxId=" +
            std::to_string(text_box_id) +
            " cursorPosition=" +
            std::to_string(text_length));
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
            qt_application->setQuitOnLastWindowClosed(true);
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
        auto click_bridge = std::make_unique<ClickBridgeObject>(handle);

        auto [it, inserted] = windows.emplace(handle, WindowState{
            .handle = handle,
            .backend_handle = backend_handle,
            .view = std::move(view),
            .click_bridge = std::move(click_bridge),
            .content_qml_path = "",
            .generated_qml_paths = {},
            .content_qml_revision = 0,
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

    std::int32_t ilc_qtquick_window_set_size(std::int32_t window_handle, std::int32_t width, std::int32_t height)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = windows.find(window_handle);
        if (it == windows.end())
        {
            qtbridge_debug("window_set_size rejected missing_window=" + std::to_string(window_handle));
            return 0;
        }

        if (it->second.view == nullptr)
        {
            qtbridge_debug("window_set_size rejected missing_view=" + std::to_string(window_handle));
            return 0;
        }

        it->second.view->setWidth(width);
        it->second.view->setHeight(height);
        qtbridge_debug(
            "window_set_size handle=" +
            std::to_string(window_handle) +
            " width=" +
            std::to_string(width) +
            " height=" +
            std::to_string(height));
        return 1;
    }

    std::int32_t ilc_qtquick_window_set_minimum_size(std::int32_t window_handle, std::int32_t min_width, std::int32_t min_height)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = windows.find(window_handle);
        if (it == windows.end())
        {
            qtbridge_debug("window_set_minimum_size rejected missing_window=" + std::to_string(window_handle));
            return 0;
        }

        if (it->second.view == nullptr)
        {
            qtbridge_debug("window_set_minimum_size rejected missing_view=" + std::to_string(window_handle));
            return 0;
        }

        it->second.view->setMinimumWidth(min_width);
        it->second.view->setMinimumHeight(min_height);
        qtbridge_debug(
            "window_set_minimum_size handle=" +
            std::to_string(window_handle) +
            " minWidth=" +
            std::to_string(min_width) +
            " minHeight=" +
            std::to_string(min_height));
        return 1;
    }

    std::int32_t ilc_qtquick_window_set_background(std::int32_t window_handle, const char* color_utf8)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = windows.find(window_handle);
        if (it == windows.end())
        {
            qtbridge_debug("window_set_background rejected missing_window=" + std::to_string(window_handle));
            return 0;
        }

        if (it->second.view == nullptr || it->second.view->rootObject() == nullptr)
        {
            qtbridge_debug("window_set_background rejected missing_root=" + std::to_string(window_handle));
            return 0;
        }

        const auto color = safe_utf8(color_utf8);
        const auto applied = it->second.view->rootObject()->setProperty("color", QColor(QString::fromStdString(color)));
        qtbridge_debug(
            "window_set_background handle=" +
            std::to_string(window_handle) +
            " color='" +
            color +
            "' applied=" +
            std::to_string(applied ? 1 : 0));
        return applied ? 1 : 0;
    }

    void ilc_qtquick_window_destroy(std::int32_t window_handle)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        qtbridge_debug("window_destroy handle=" + std::to_string(window_handle));
        const auto it = windows.find(window_handle);
        if (it != windows.end())
        {
            for (const auto& generated_qml_path : it->second.generated_qml_paths)
            {
                QFile::remove(QString::fromStdString(generated_qml_path));
                qtbridge_debug("window_destroy removed_qml_path=" + generated_qml_path);
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

    std::int32_t ilc_qtquick_window_set_element_text(std::int32_t window_handle, const char* object_name_utf8, const char* text_utf8)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = windows.find(window_handle);
        if (it == windows.end())
        {
            qtbridge_debug("window_set_element_text rejected missing_window=" + std::to_string(window_handle));
            return 0;
        }

        const auto object_name = safe_utf8(object_name_utf8);
        auto* object = find_named_object(it->second, object_name);
        if (object == nullptr)
        {
            qtbridge_debug("window_set_element_text rejected missing_object window=" + std::to_string(window_handle) + " object='" + object_name + "'");
            return 0;
        }

        const auto text = safe_utf8(text_utf8);
        const auto applied = object->setProperty("text", QString::fromStdString(text));
        qtbridge_debug(
            "window_set_element_text handle=" +
            std::to_string(window_handle) +
            " object='" +
            object_name +
            "' applied=" +
            std::to_string(applied ? 1 : 0) +
            " length=" +
            std::to_string(text.length()));
        return applied ? 1 : 0;
    }

    std::int32_t ilc_qtquick_window_set_element_color(std::int32_t window_handle, const char* object_name_utf8, const char* color_utf8)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        const auto it = windows.find(window_handle);
        if (it == windows.end())
        {
            qtbridge_debug("window_set_element_color rejected missing_window=" + std::to_string(window_handle));
            return 0;
        }

        const auto object_name = safe_utf8(object_name_utf8);
        auto* object = find_named_object(it->second, object_name);
        if (object == nullptr)
        {
            qtbridge_debug("window_set_element_color rejected missing_object window=" + std::to_string(window_handle) + " object='" + object_name + "'");
            return 0;
        }

        const auto color = safe_utf8(color_utf8);
        const auto applied = object->setProperty("color", QColor(QString::fromStdString(color)));
        qtbridge_debug(
            "window_set_element_color handle=" +
            std::to_string(window_handle) +
            " object='" +
            object_name +
            "' color='" +
            color +
            "' applied=" +
            std::to_string(applied ? 1 : 0));
        return applied ? 1 : 0;
    }

    std::int32_t ilc_qtquick_window_focus_text_input(std::int32_t window_handle, std::int32_t text_box_id)
    {
        {
            std::lock_guard<std::mutex> lock(bridge_mutex);
            const auto it = windows.find(window_handle);
            if (it == windows.end())
            {
                qtbridge_debug("window_focus_text_input rejected missing_window=" + std::to_string(window_handle));
                return 0;
            }
        }

        qtbridge_debug(
            "window_focus_text_input queued handle=" +
            std::to_string(window_handle) +
            " textBoxId=" +
            std::to_string(text_box_id));

        QTimer::singleShot(0, [window_handle, text_box_id]()
        {
            std::lock_guard<std::mutex> lock(bridge_mutex);
            const auto it = windows.find(window_handle);
            if (it == windows.end())
            {
                qtbridge_debug(
                    "window_focus_text_input skipped missing_window=" +
                    std::to_string(window_handle));
                return;
            }

            restore_text_input_focus(it->second, text_box_id);
        });

        return 1;
    }

    char* ilc_qtquick_backend_read_text_input_value(std::int32_t backend_handle, std::int32_t text_box_id)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        if (!backends.contains(backend_handle))
        {
            qtbridge_debug("backend_read_text_input_value rejected missing_backend=" + std::to_string(backend_handle));
            return nullptr;
        }

        for (const auto& [window_handle, window] : windows)
        {
            if (window.backend_handle != backend_handle || window.click_bridge == nullptr)
            {
                continue;
            }

            if (window.click_bridge->has_text_value(text_box_id))
            {
                const auto value = window.click_bridge->read_text_value(text_box_id);
                qtbridge_debug(
                    "backend_read_text_input_value backend=" +
                    std::to_string(backend_handle) +
                    " window=" +
                    std::to_string(window_handle) +
                    " textBoxId=" +
                    std::to_string(text_box_id) +
                    " length=" +
                    std::to_string(value.length()));
                return duplicate_owned_utf8(value);
            }
        }

        qtbridge_debug(
            "backend_read_text_input_value backend=" +
            std::to_string(backend_handle) +
            " textBoxId=" +
            std::to_string(text_box_id) +
            " value_missing");
        return duplicate_owned_utf8(std::string());
    }

    std::int32_t ilc_qtquick_backend_read_slider_value(std::int32_t backend_handle, std::int32_t slider_id)
    {
        std::lock_guard<std::mutex> lock(bridge_mutex);
        if (!backends.contains(backend_handle))
        {
            qtbridge_debug("backend_read_slider_value rejected missing_backend=" + std::to_string(backend_handle));
            return 0;
        }

        for (const auto& [window_handle, window] : windows)
        {
            if (window.backend_handle != backend_handle || window.click_bridge == nullptr)
            {
                continue;
            }

            if (window.click_bridge->has_slider_value(slider_id))
            {
                const auto value = window.click_bridge->read_slider_value(slider_id);
                qtbridge_debug(
                    "backend_read_slider_value backend=" +
                    std::to_string(backend_handle) +
                    " window=" +
                    std::to_string(window_handle) +
                    " sliderId=" +
                    std::to_string(slider_id) +
                    " value=" +
                    std::to_string(value));
                return value;
            }
        }

        qtbridge_debug(
            "backend_read_slider_value backend=" +
            std::to_string(backend_handle) +
            " sliderId=" +
            std::to_string(slider_id) +
            " value_missing");
        return 0;
    }

    void ilc_qtquick_string_free(char* value)
    {
        qtbridge_debug("string_free hasValue=" + std::to_string(value != nullptr ? 1 : 0));
        std::free(value);
    }

    std::int32_t ilc_qtquick_backend_run(std::int32_t backend_handle)
    {
        {
            std::lock_guard<std::mutex> lock(bridge_mutex);
            if (!backends.contains(backend_handle))
            {
                qtbridge_debug("backend_run rejected missing_backend=" + std::to_string(backend_handle));
                return 0;
            }
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

    std::int32_t ilc_qtquick_backend_run_with_click_callback(std::int32_t backend_handle, ilc_qtquick_i32_callback callback)
    {
        {
            std::lock_guard<std::mutex> lock(bridge_mutex);
            if (!backends.contains(backend_handle))
            {
                qtbridge_debug("backend_run_with_click_callback rejected missing_backend=" + std::to_string(backend_handle));
                return 0;
            }

            std::size_t attached_windows = 0;
            for (auto& [window_handle, window] : windows)
            {
                if (window.backend_handle != backend_handle)
                {
                    continue;
                }

                if (window.click_bridge != nullptr)
                {
                    window.click_bridge->set_callback(callback);
                }
                attached_windows = attached_windows + 1;
            }

            qtbridge_debug(
                "backend_run_with_click_callback handle=" + std::to_string(backend_handle) +
                " callback=" + std::to_string(callback != nullptr ? 1 : 0) +
                " attachedWindows=" + std::to_string(attached_windows));
        }

        if (QCoreApplication::instance() == nullptr)
        {
            qtbridge_debug("backend_run_with_click_callback rejected because no QCoreApplication instance exists");
            return 0;
        }

        if (should_run_blocking_event_loop())
        {
            qtbridge_debug("backend_run_with_click_callback entering blocking Qt event loop");
            return QCoreApplication::instance()->exec() == 0 ? 1 : 0;
        }

        QCoreApplication::processEvents();
        qtbridge_debug("backend_run_with_click_callback processed pending Qt events without blocking");
        return 1;
    }
}

#include "qtquick_bridge_qt.moc"
