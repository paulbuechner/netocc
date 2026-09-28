# netocc overlay: community arm64-osx-dynamic, release-only
set(VCPKG_TARGET_ARCHITECTURE arm64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_CMAKE_SYSTEM_NAME Darwin)
set(VCPKG_OSX_ARCHITECTURES arm64)
# oldest macOS the libraries load on (their minos); build.py native builds the NetOcc libraries for it too
set(VCPKG_OSX_DEPLOYMENT_TARGET 14.0)
set(VCPKG_BUILD_TYPE release)

if(PORT STREQUAL "opencascade")
  # keep OCCT precondition checks (Standard_*_Raise_if) in release: NetOcc turns them into OcctException
  list(APPEND VCPKG_CMAKE_CONFIGURE_OPTIONS "-DBUILD_RELEASE_DISABLE_EXCEPTIONS=OFF")
  # allocate through oneTBB's scalable allocator (vcpkg.json's tbb feature): BRepMesh takes blocks of 1 MB per face,
  # which the system allocator maps and unmaps each time (meshing 2-3.5x faster, 3-4x in parallel)
  list(APPEND VCPKG_CMAKE_CONFIGURE_OPTIONS "-DUSE_MMGR_TYPE=TBB")
endif()
