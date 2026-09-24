// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: OCCT's stream typedef over a stand-in for the standard library, so libclang spells it the same on every
// platform (libc++ would add its inline namespace).
#pragma once

namespace std
{
template <class TheChar>
struct char_traits;
template <class TheChar, class TheTraits = char_traits<TheChar>>
class basic_ostream;
typedef basic_ostream<char> ostream;
} // namespace std

typedef std::ostream Standard_OStream;
