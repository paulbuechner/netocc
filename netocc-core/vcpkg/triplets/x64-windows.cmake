# netocc overlay: stock x64-windows, release-only
set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_BUILD_TYPE release)

if(PORT STREQUAL "opencascade")
  # keep OCCT precondition checks (Standard_*_Raise_if) in release: NetOcc turns them into OcctException
  list(APPEND VCPKG_CMAKE_CONFIGURE_OPTIONS "-DBUILD_RELEASE_DISABLE_EXCEPTIONS=OFF")
endif()
