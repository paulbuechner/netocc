// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: plain data (a C# struct with public fields) that another package's struct holds.
#pragma once

struct Standard_UUID
{
  unsigned int   Data1;
  unsigned short Data2;
  unsigned short Data3;
  unsigned char  Data4[8];
};
