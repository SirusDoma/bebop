#pragma once

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <exception>
#include <memory>
#include <string>
#include <vector>

#ifndef BEBOPC_VER_MAJOR
#define BEBOPC_VER_MAJOR 0
#endif

#ifndef BEBOPC_VER_MINOR
#define BEBOPC_VER_MINOR 0
#endif

#ifndef BEBOPC_VER_PATCH
#define BEBOPC_VER_PATCH 0
#endif

#ifndef BEBOPC_VER
#define BEBOPC_VER \
	((std::uint32_t) (((std::uint8_t) BEBOPC_VER_MAJOR << 24u) | ((std::uint8_t) BEBOPC_VER_MINOR << 16u) | ((std::uint8_t) BEBOPC_VER_PATCH << 8u) | (std::uint8_t)0)
#endif

#ifndef BEBOPC_VER_INFO
#define BEBOPC_VER_INFO "0"
#endif

#ifndef BEBOP_ASSUME_LITTLE_ENDIAN
#define BEBOP_ASSUME_LITTLE_ENDIAN 1
#endif

namespace bebop {

/// A "tick" is a ten-millionth of a second, or 100ns.
using Tick = std::ratio<1, 10000000>;
using TickDuration = std::chrono::duration<std::int64_t, Tick>;

namespace {
    /// The number of ticks between 1/1/0001 and 1/1/1970.
    const std::int64_t ticksBetweenEpochs = 621355968000000000;
}

enum class GuidStyle {
    Dashes,
    NoDashes,
};

struct MalformedPacketException : public std::exception {
    const char* what () const throw () {
        return "malformed Bebop packet";
    }
};

#pragma pack(push, 1)
struct Guid {
    /// The GUID data is stored the way it is to match the memory layout
    /// of a _GUID in Windows: the idea is to "trick" P/Invoke into recognizing
    /// our type as corresponding to a .NET "Guid".
    std::uint32_t m_a;
    std::uint16_t m_b;
    std::uint16_t m_c;
    std::uint8_t m_d;
    std::uint8_t m_e;
    std::uint8_t m_f;
    std::uint8_t m_g;
    std::uint8_t m_h;
    std::uint8_t m_i;
    std::uint8_t m_j;
    std::uint8_t m_k;

    Guid() = default;
    Guid(const std::uint8_t* bytes) {
#if BEBOP_ASSUME_LITTLE_ENDIAN
        memcpy(&m_a, bytes + 0, sizeof(std::uint32_t));
        memcpy(&m_b, bytes + 4, sizeof(std::uint16_t));
        memcpy(&m_c, bytes + 6, sizeof(std::uint16_t));
#else
        m_a = bytes[0]
            | (static_cast<std::uint32_t>(bytes[1]) << 8)
            | (static_cast<std::uint32_t>(bytes[2]) << 16)
            | (static_cast<std::uint32_t>(bytes[3]) << 24);
        m_b = bytes[4]
            | (static_cast<std::uint16_t>(bytes[5]) << 8);
        m_c = bytes[6]
            | (static_cast<std::uint16_t>(bytes[7]) << 8);
#endif
        m_d = bytes[8];
        m_e = bytes[9];
        m_f = bytes[10];
        m_g = bytes[11];
        m_h = bytes[12];
        m_i = bytes[13];
        m_j = bytes[14];
        m_k = bytes[15];
    }
    Guid(Guid const& other) {
        m_a = other.m_a;
        m_b = other.m_b;
        m_c = other.m_c;
        m_d = other.m_d;
        m_e = other.m_e;
        m_f = other.m_f;
        m_g = other.m_g;
        m_h = other.m_h;
        m_i = other.m_i;
        m_j = other.m_j;
        m_k = other.m_k;
    }

    static Guid fromString(const std::string& string) {
        std::uint8_t bytes[16];
        const char* s = string.c_str();

        for (const auto i : layout) {
            if (i == dash) {
                // Skip over a possible dash in the string.
                if (*s == '-') s++;
            } else {
                // Read two hex digits from the string.
                std::uint8_t high = *s++;
                std::uint8_t low = *s++;
                bytes[i] = (asciiToHex[high] << 4) | asciiToHex[low];
            }
        }

        return Guid(bytes);
    }

    std::string toString(GuidStyle style = GuidStyle::Dashes) const {
        int size = style == GuidStyle::Dashes ? 36 : 32;
        const char* dash = style == GuidStyle::Dashes ? "-" : "";
        std::unique_ptr<char[]> buffer(new char[size+1]);
        snprintf(buffer.get(), size+1, "%08x%s%04x%s%04x%s%02x%02x%s%02x%02x%02x%02x%02x%02x",
            m_a, dash, m_b, dash, m_c, dash, m_d, m_e, dash, m_f, m_g, m_h, m_i, m_j, m_k);
        return std::string(buffer.get(), buffer.get() + size);
    }

    bool operator<(const Guid& other) const {
        if (m_a < other.m_a) return true;
        if (m_a > other.m_a) return false;
        if (m_b < other.m_b) return true;
        if (m_b > other.m_b) return false;
        if (m_c < other.m_c) return true;
        if (m_c > other.m_c) return false;
        if (m_d < other.m_d) return true;
        if (m_d > other.m_d) return false;
        if (m_e < other.m_e) return true;
        if (m_e > other.m_e) return false;
        if (m_f < other.m_f) return true;
        if (m_f > other.m_f) return false;
        if (m_g < other.m_g) return true;
        if (m_g > other.m_g) return false;
        if (m_h < other.m_h) return true;
        if (m_h > other.m_h) return false;
        if (m_i < other.m_i) return true;
        if (m_i > other.m_i) return false;
        if (m_j < other.m_j) return true;
        if (m_j > other.m_j) return false;
        if (m_k < other.m_k) return true;
        if (m_k > other.m_k) return false;
        return false;
    }

    bool operator==(const Guid& other) const {
        if (m_a != other.m_a) return false;
        if (m_b != other.m_b) return false;
        if (m_c != other.m_c) return false;
        if (m_d != other.m_d) return false;
        if (m_e != other.m_e) return false;
        if (m_f != other.m_f) return false;
        if (m_g != other.m_g) return false;
        if (m_h != other.m_h) return false;
        if (m_i != other.m_i) return false;
        if (m_j != other.m_j) return false;
        if (m_k != other.m_k) return false;
        return true;
    }

private:
    static constexpr int dash = -1;
    static constexpr int layout[] = {3, 2, 1, 0, dash, 5, 4, dash, 7, 6, dash, 8, 9, dash, 10, 11, 12, 13, 14, 15};
    static constexpr char nibbleToHex[16] = {'0','1','2','3','4','5','6','7','8','9','a','b','c','d','e','f'};
    static constexpr std::uint8_t asciiToHex[256] = {
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0,  1,  2,  3,  4,  5,  6,  7,  8,  9,  0,  0,  0,  0,  0,  0,
        0, 10, 11, 12, 13, 14, 15,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0, 10, 11, 12, 13, 14, 15,  // and the rest is zeroes
    };
};
#pragma pack(pop)

class Reader {
    const std::uint8_t* m_start;
    const std::uint8_t* m_pointer;
    const std::uint8_t* m_end;
public:
    Reader(const std::uint8_t* buffer, size_t bufferLength) : m_start(buffer), m_pointer(buffer), m_end(buffer + bufferLength) {}
    Reader(Reader const&) = delete;
    void operator=(Reader const&) = delete;

    const std::uint8_t* pointer() const { return m_pointer; }
    size_t bytesRead() const { return m_pointer - m_start; }
    void seek(const std::uint8_t* pointer) { m_pointer = pointer; }

    void skip(size_t amount) { m_pointer += amount; }

    std::uint8_t readByte() {
        if (m_pointer + sizeof(std::uint8_t) > m_end) throw MalformedPacketException();
        return *m_pointer++;
    }

    std::uint16_t readUint16() {
        if (m_pointer + sizeof(std::uint16_t) > m_end) throw MalformedPacketException();
#if BEBOP_ASSUME_LITTLE_ENDIAN
        std::uint16_t v;
        memcpy(&v, m_pointer, sizeof(std::uint16_t));
        m_pointer += sizeof(std::uint16_t);
        return v;
#else
        const std::uint16_t b0 = *m_pointer++;
        const std::uint16_t b1 = *m_pointer++;
        return (b1 << 8) | b0;
#endif
    }

    std::uint32_t readUint32() {
        if (m_pointer + sizeof(std::uint32_t) > m_end) throw MalformedPacketException();
#if BEBOP_ASSUME_LITTLE_ENDIAN
        std::uint32_t v;
        memcpy(&v, m_pointer, sizeof(std::uint32_t));
        m_pointer += sizeof(std::uint32_t);
        return v;
#else
        const std::uint32_t b0 = *m_pointer++;
        const std::uint32_t b1 = *m_pointer++;
        const std::uint32_t b2 = *m_pointer++;
        const std::uint32_t b3 = *m_pointer++;
        return (b3 << 24) | (b2 << 16) | (b1 << 8) | b0;
#endif
    }

    std::uint64_t readUint64() {
        if (m_pointer + sizeof(std::uint64_t) > m_end) throw MalformedPacketException();
#if BEBOP_ASSUME_LITTLE_ENDIAN
        std::uint64_t v;
        memcpy(&v, m_pointer, sizeof(std::uint64_t));
        m_pointer += sizeof(std::uint64_t);
        return v;
#else
        const std::uint64_t b0 = *m_pointer++;
        const std::uint64_t b1 = *m_pointer++;
        const std::uint64_t b2 = *m_pointer++;
        const std::uint64_t b3 = *m_pointer++;
        const std::uint64_t b4 = *m_pointer++;
        const std::uint64_t b5 = *m_pointer++;
        const std::uint64_t b6 = *m_pointer++;
        const std::uint64_t b7 = *m_pointer++;
        return (b7 << 0x38) | (b6 << 0x30) | (b5 << 0x28) | (b4 << 0x20) | (b3 << 0x18) | (b2 << 0x10) | (b1 << 0x08) | b0;
#endif
    }

    std::int16_t readInt16() { return static_cast<std::uint16_t>(readUint16()); }
    std::int32_t readInt32() { return static_cast<std::uint32_t>(readUint32()); }
    std::int64_t readInt64() { return static_cast<std::uint64_t>(readUint64()); }

    float readFloat32() {
        if (m_pointer + sizeof(float) > m_end) throw MalformedPacketException();
        float f;
        const std::uint32_t v = readUint32();
        memcpy(&f, &v, sizeof(float));
        return f;
    }

    double readFloat64() {
        if (m_pointer + sizeof(double) > m_end) throw MalformedPacketException();
        double f;
        const std::uint64_t v = readUint64();
        memcpy(&f, &v, sizeof(double));
        return f;
    }

    bool readBool() {
        return readByte() != 0;
    }

    std::uint32_t readLengthPrefix() {
        const auto length = readUint32();
        if (m_pointer + length > m_end) {
            throw MalformedPacketException();
        }
        return length;
    }

    std::vector<std::uint8_t> readBytes() {
        const auto length = readLengthPrefix();
        std::vector<std::uint8_t> v(m_pointer, m_pointer + length);
        m_pointer += length;
        return v;
    }

    std::string readString() {
        const auto length = readLengthPrefix();
        std::string v(m_pointer, m_pointer + length);
        m_pointer += length;
        return v;
    }

    Guid readGuid() {
        if (m_pointer + sizeof(Guid) > m_end) throw MalformedPacketException();
        Guid guid { m_pointer };
        m_pointer += sizeof(Guid);
        return guid;
    }

    // Read a date (as ticks since the Unix Epoch).
    TickDuration readDate() {
        const std::uint64_t ticks = readUint64() & 0x3fffffffffffffff;
        return TickDuration(ticks - ticksBetweenEpochs);
    }
};

class Writer {
    std::vector<std::uint8_t>& m_buffer;
public:
    Writer(std::vector<std::uint8_t>& buffer) : m_buffer(buffer) {}
    Writer(Writer const&) = delete;
    void operator=(Writer const&) = delete;

    std::vector<std::uint8_t>& buffer() {
        return m_buffer;
    }

    size_t length() { return m_buffer.size(); }

    void writeByte(std::uint8_t value) { m_buffer.push_back(value); }
    void writeUint16(std::uint16_t value) {
#if BEBOP_ASSUME_LITTLE_ENDIAN
        const auto position = m_buffer.size();
        m_buffer.resize(position + sizeof(value));
        memcpy(m_buffer.data() + position, &value, sizeof(value));
#else
        m_buffer.push_back(value);
        m_buffer.push_back(value >> 8);
#endif
    }
    void writeUint32(std::uint32_t value) {
#if BEBOP_ASSUME_LITTLE_ENDIAN
        const auto position = m_buffer.size();
        m_buffer.resize(position + sizeof(value));
        memcpy(m_buffer.data() + position, &value, sizeof(value));
#else
        m_buffer.push_back(value);
        m_buffer.push_back(value >> 8);
        m_buffer.push_back(value >> 16);
        m_buffer.push_back(value >> 24);
#endif
    }
    void writeUint64(std::uint64_t value) {
#if BEBOP_ASSUME_LITTLE_ENDIAN
        const auto position = m_buffer.size();
        m_buffer.resize(position + sizeof(value));
        memcpy(m_buffer.data() + position, &value, sizeof(value));
#else
        m_buffer.push_back(value);
        m_buffer.push_back(value >> 0x08);
        m_buffer.push_back(value >> 0x10);
        m_buffer.push_back(value >> 0x18);
        m_buffer.push_back(value >> 0x20);
        m_buffer.push_back(value >> 0x28);
        m_buffer.push_back(value >> 0x30);
        m_buffer.push_back(value >> 0x38);
#endif
    }

    void writeInt16(std::int16_t value) { writeUint16(static_cast<std::uint16_t>(value)); }
    void writeInt32(std::int32_t value) { writeUint32(static_cast<std::uint32_t>(value)); }
    void writeInt64(std::int64_t value) { writeUint64(static_cast<std::uint64_t>(value)); }
    void writeFloat32(float value) {
        std::uint32_t temp;
        memcpy(&temp, &value, sizeof(float));
        writeUint32(temp);
    }
    void writeFloat64(double value) {
        std::uint64_t temp;
        memcpy(&temp, &value, sizeof(double));
        writeUint64(temp);
    }
    void writeBool(bool value) { writeByte(value); }

    void writeBytes(std::vector<std::uint8_t> value) {
        const auto byteCount = value.size();
        writeUint32(byteCount);
        m_buffer.insert(m_buffer.end(), value.begin(), value.end());
    }

    void writeString(std::string value) {
        const auto byteCount = value.size();
        writeUint32(byteCount);
        m_buffer.insert(m_buffer.end(), value.begin(), value.end());
    }

    void writeGuid(Guid value) {
        writeUint32(value.m_a);
        writeUint16(value.m_b);
        writeUint16(value.m_c);
        writeByte(value.m_d);
        writeByte(value.m_e);
        writeByte(value.m_f);
        writeByte(value.m_g);
        writeByte(value.m_h);
        writeByte(value.m_i);
        writeByte(value.m_j);
        writeByte(value.m_k);
    }

    void writeDate(TickDuration duration) {
        writeUint64((duration.count() + ticksBetweenEpochs) & 0x3fffffffffffffff);
    }

    /// Reserve some space to write a message's length prefix, and return its index.
    /// The length is stored as a little-endian fixed-width unsigned 32-bit integer, so 4 bytes are reserved.
    size_t reserveMessageLength() {
        const auto n = m_buffer.size();
        m_buffer.resize(n + 4);
        return n;
    }

    /// Fill in a message's length prefix.
    void fillMessageLength(size_t position, std::uint32_t messageLength) {
#if BEBOP_ASSUME_LITTLE_ENDIAN
        memcpy(m_buffer.data() + position, &messageLength, sizeof(std::uint32_t));
#else
        m_buffer[position++] = messageLength;
        m_buffer[position++] = messageLength >> 8;
        m_buffer[position++] = messageLength >> 16;
        m_buffer[position++] = messageLength >> 24;
#endif
    }
};

class ByteCounter {
    size_t m_bytes;
public:
    ByteCounter() : m_bytes(0) {}
    ByteCounter(ByteCounter const&) = delete;
    void operator=(ByteCounter const&) = delete;

    size_t length() { return m_bytes; }

    void writeByte(std::uint8_t value) { m_bytes += sizeof(value); }
    void writeUint16(std::uint16_t value) { m_bytes += sizeof(value); }
    void writeUint32(std::uint32_t value) { m_bytes += sizeof(value); }
    void writeUint64(std::uint64_t value) { m_bytes += sizeof(value); }
    void writeInt16(std::int16_t value) { m_bytes += sizeof(value); }
    void writeInt32(std::int32_t value) { m_bytes += sizeof(value); }
    void writeInt64(std::int64_t value) { m_bytes += sizeof(value); }
    void writeFloat32(float value) { m_bytes += sizeof(value); }
    void writeFloat64(double value) { m_bytes += sizeof(value); }
    void writeBool(bool value) { writeByte(value); }
    void writeBytes(std::vector<std::uint8_t> value) { m_bytes += sizeof(std::uint32_t) + value.size(); }
    void writeString(std::string value) { m_bytes += sizeof(std::uint32_t) + value.size(); }
    void writeGuid(Guid value) { m_bytes += sizeof(value); }
    void writeDate(TickDuration duration) { m_bytes += sizeof(std::uint64_t); }
    size_t reserveMessageLength() { m_bytes += sizeof(std::uint32_t); return 0; }
    void fillMessageLength(size_t position, std::uint32_t messageLength) { }
};

template<typename T>
class Optional {
private:
    std::unique_ptr<T> instance;

public:
    constexpr Optional() noexcept : instance(nullptr) {}

    constexpr Optional(std::nullptr_t) noexcept : instance(nullptr) {}

    constexpr Optional(std::nullopt_t) noexcept : instance(nullptr) {}

    Optional(const Optional& other) : instance(other ? std::make_unique<T>(*other) : nullptr) {}

    Optional(Optional&& other) noexcept = default;

    template<typename... Args>
    explicit Optional(std::in_place_t, Args&&... args) : instance(std::make_unique<T>(std::forward<Args>(args)...)) {}

    template<typename U = T>
    Optional(U&& value) : instance(std::make_unique<T>(std::forward<U>(value))) {}

    Optional& operator=(const Optional& other) {
        if (this != &other)
        {
            if (other)
                instance = other.instance ? std::make_unique<T>(*other.instance) : nullptr;
            else
                instance.reset();
        }
        return *this;
    }

    Optional& operator=(Optional&& other) noexcept = default;

    template<typename U>
    Optional& operator=(U&& value) {
        instance = std::make_unique<T>(std::forward<U>(value));
        return *this;
    }

    ~Optional() = default;

    operator std::optional<T>() const & {
        if (!instance) return std::nullopt;
        return std::optional<T>(*instance);
    }

    operator std::optional<T>() && {
        if (!instance) return std::nullopt;
        return std::optional<T>(std::move(*instance));
    }

    Optional(const std::optional<T>& other): instance(other.has_value() ? std::make_unique<T>(*other) : nullptr) {}

    Optional(std::optional<T>&& other): instance(other.has_value() ? std::make_unique<T>(std::move(*other)) : nullptr) {}

    constexpr explicit operator bool() const noexcept {
        return static_cast<bool>(instance);
    }

    [[nodiscard]] constexpr bool has_value() const noexcept {
        return static_cast<bool>(instance);
    }

    constexpr T& value() & {
        if (!instance) throw std::bad_optional_access();
        return *instance;
    }

    constexpr const T& value() const & {
        if (!instance) throw std::bad_optional_access();
        return *instance;
    }

    constexpr T&& value() && {
        if (!instance) throw std::bad_optional_access();
        return std::move(*instance);
    }

    constexpr const T&& value() const && {
        if (!instance) throw std::bad_optional_access();
        return std::move(*instance);
    }

    template<typename U>
    constexpr T value_or(U&& default_value) const & {
        return instance ? *instance : static_cast<T>(std::forward<U>(default_value));
    }

    template<typename U>
    constexpr T value_or(U&& default_value) && {
        return instance ? std::move(*instance) : static_cast<T>(std::forward<U>(default_value));
    }

    template<typename... Args>
    constexpr T& value_or_emplace(Args&&... args) & {
        return instance ? value() : emplace(std::forward<Args>(args)...);
    }

    template<typename... Args>
    constexpr const T& value_or_emplace(Args&&... args) const & {
        return instance ? value() : emplace(std::forward<Args>(args)...);
    }

    constexpr T* operator->() {
        if (!instance) throw std::bad_optional_access();
        return instance.get();
    }

    constexpr const T* operator->() const {
        if (!instance) throw std::bad_optional_access();
        return instance.get();
    }

    constexpr T& operator*() & {
        if (!instance) throw std::bad_optional_access();
        return *instance;
    }

    constexpr const T& operator*() const & {
        if (!instance) throw std::bad_optional_access();
        return *instance;
    }

    constexpr T&& operator*() && {
        return std::move(*instance);
    }

    constexpr const T&& operator*() const && {
        return std::move(*instance);
    }

    void reset() noexcept {
        instance.reset();
    }

    template<typename... Args>
    T& emplace(Args&&... args) {
        instance = std::make_unique<T>(std::forward<Args>(args)...);
        return *instance;
    }

    void swap(Optional& other) noexcept {
        instance.swap(other.instance);
    }
};

template<typename T>
void swap(Optional<T>& lhs, Optional<T>& rhs) noexcept {
    lhs.swap(rhs);
}

template<typename T>
bool operator==(const Optional<T>& lhs, const Optional<T>& rhs) {
    if (lhs.has_value() != rhs.has_value()) return false;
    if (!lhs.has_value()) return true;
    return *lhs == *rhs;
}

template<typename T>
bool operator!=(const Optional<T>& lhs, const Optional<T>& rhs) {
    return !(lhs == rhs);
}

template<typename T>
bool operator<(const Optional<T>& lhs, const Optional<T>& rhs) {
    if (!rhs.has_value()) return false;
    if (!lhs.has_value()) return true;
    return *lhs < *rhs;
}

template<typename T>
bool operator<=(const Optional<T>& lhs, const Optional<T>& rhs) {
    return !(rhs < lhs);
}

template<typename T>
bool operator>(const Optional<T>& lhs, const Optional<T>& rhs) {
    return rhs < lhs;
}

template<typename T>
bool operator>=(const Optional<T>& lhs, const Optional<T>& rhs) {
    return !(lhs < rhs);
}

template<typename T>
bool operator==(const Optional<T>& opt, std::nullptr_t) noexcept {
    return !opt.has_value();
}

template<typename T>
bool operator==(std::nullptr_t, const Optional<T>& opt) noexcept {
    return !opt.has_value();
}

template<typename T>
bool operator!=(const Optional<T>& opt, std::nullptr_t) noexcept {
    return opt.has_value();
}

template<typename T>
bool operator!=(std::nullptr_t, const Optional<T>& opt) noexcept {
    return opt.has_value();
}

template<typename T, typename W>
size_t encodeInto(const T& message, W& writer);

template<typename T>
size_t decodeInto(::bebop::Reader& reader, ::bebop::Optional<T>& target);

static_assert(sizeof(std::uint8_t) == 1, "sizeof(std::uint8_t) should be 1");
static_assert(sizeof(std::uint16_t) == 2, "sizeof(std::uint16_t) should be 2");
static_assert(sizeof(std::uint32_t) == 4, "sizeof(std::uint32_t) should be 4");
static_assert(sizeof(std::uint64_t) == 8, "sizeof(std::uint64_t) should be 8");
static_assert(sizeof(float) == 4, "sizeof(float) should be 4");
static_assert(sizeof(double) == 8, "sizeof(double) should be 8");
static_assert(sizeof(Guid) == 16, "sizeof(Guid) should be 16");

} // namespace bebop
