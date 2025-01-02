#pragma once
#include "bebop.hpp"

namespace bebop {
    template<typename T, typename W>
    size_t encodeInto(const T& message, W& writer) {
        return T::encodeInto(message, writer);
    }

    template<typename T>
    size_t decodeInto(::bebop::Reader& reader, ::bebop::Optional<T>& target) {
        return T::decodeInto(reader, target.value_or_emplace());
    }
}
