# netvtk overlay: community arm64-osx-dynamic, release-only
set(VCPKG_TARGET_ARCHITECTURE arm64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_CMAKE_SYSTEM_NAME Darwin)
set(VCPKG_OSX_ARCHITECTURES arm64)
# oldest macOS the libraries load on (their minos); build.py native builds its own libraries for it too
set(VCPKG_OSX_DEPLOYMENT_TARGET 14.0)
# ld64 records every dylib a library links, used or not; keep those a symbol comes from, as MSVC and Ubuntu's GCC do
set(VCPKG_LINKER_FLAGS "-Wl,-dead_strip_dylibs")
set(VCPKG_BUILD_TYPE release)
