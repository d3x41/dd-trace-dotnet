// Copyright(c) 2015-present, Gabi Melman & spdlog contributors.
// Distributed under the MIT License (http://opensource.org/licenses/MIT)

#pragma once

#include <spdlog/sinks/base_sink.h>
#include <spdlog/details/file_helper.h>
#include <spdlog/details/null_mutex.h>
#include <spdlog/details/synchronous_factory.h>

#include <chrono>
#include <mutex>
#include <string>

namespace spdlog {
namespace sinks {

//
// Rotating file sink based on size that does not create the file until a log is first written
// Basd on the sink in spdlog/sinks/rotating_file_sink.h
//
template<typename Mutex>
class lazy_rotating_file_sink final : public base_sink<Mutex>
{
public:
    lazy_rotating_file_sink(filename_t base_filename, std::size_t max_size, std::size_t max_files,
        const file_event_handlers &event_handlers = {});
    static filename_t calc_filename(const filename_t &filename, std::size_t index);
    filename_t filename();

protected:
    void sink_it_(const details::log_msg &msg) override;
    void flush_() override;

private:
    // Rotate files:
    // log.txt -> log.1.txt
    // log.1.txt -> log.2.txt
    // log.2.txt -> log.3.txt
    // log.3.txt -> delete
    void rotate_();

    void open_if_needed_();

    // delete the target if exists, and rename the src file  to target
    // return true on success, false otherwise.
    bool rename_file_(const filename_t &src_filename, const filename_t &target_filename);

    filename_t base_filename_;
    std::size_t max_size_;
    std::size_t max_files_;
    std::size_t current_size_;
    details::file_helper file_helper_;
    bool file_opened_;
};

using lazy_rotating_file_sink_mt = lazy_rotating_file_sink<std::mutex>;
using lazy_rotating_file_sink_st = lazy_rotating_file_sink<details::null_mutex>;

} // namespace sinks

//
// factory functions
//

template<typename Factory = spdlog::synchronous_factory>
inline std::shared_ptr<logger> lazy_rotating_logger_mt(const std::string &logger_name, const filename_t &filename, size_t max_file_size,
    size_t max_files, bool rotate_on_open = false, const file_event_handlers &event_handlers = {})
{
    return Factory::template create<sinks::lazy_rotating_file_sink_mt>(
        logger_name, filename, max_file_size, max_files, event_handlers);
}

template<typename Factory = spdlog::synchronous_factory>
inline std::shared_ptr<logger> lazy_rotating_logger_st(const std::string &logger_name, const filename_t &filename, size_t max_file_size,
    size_t max_files, bool rotate_on_open = false, const file_event_handlers &event_handlers = {})
{
    return Factory::template create<sinks::lazy_rotating_file_sink_st>(
        logger_name, filename, max_file_size, max_files, event_handlers);
}
} // namespace spdlog

#ifdef SPDLOG_HEADER_ONLY
#    include "lazy_rotating_file_sink-inl.h"
#endif
