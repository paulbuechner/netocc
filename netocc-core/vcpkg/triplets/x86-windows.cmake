# netvtk overlay: stock x86-windows, release-only
set(VCPKG_TARGET_ARCHITECTURE x86)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_BUILD_TYPE release)
# MSVC's x86 code at /O2 kept a loop's `x = std::min(v, x)` comparing against x's first value: vtkActor::GetBounds and its
# copies took the box's corners' min and max wrong, so actors were culled and cameras reset off the scene. /Os after
# /O2 (vcpkg appends these to its release flags) favours small code over fast and doesn't, and keeps /O2's other
# optimizations.
set(VCPKG_C_FLAGS_RELEASE "/Os")
set(VCPKG_CXX_FLAGS_RELEASE "/Os")
