# netocc overlay: community x64-linux-dynamic, release-only
set(VCPKG_TARGET_ARCHITECTURE x64)
set(VCPKG_CRT_LINKAGE dynamic)
set(VCPKG_LIBRARY_LINKAGE dynamic)
set(VCPKG_CMAKE_SYSTEM_NAME Linux)
set(VCPKG_FIXUP_ELF_RPATH ON)
set(VCPKG_BUILD_TYPE release)

if(PORT STREQUAL "opencascade")
  # keep OCCT precondition checks (Standard_*_Raise_if) in release: NetOcc turns them into OcctException
  list(APPEND VCPKG_CMAKE_CONFIGURE_OPTIONS "-DBUILD_RELEASE_DISABLE_EXCEPTIONS=OFF")
  # allocate through oneTBB's scalable allocator (vcpkg.json's tbb feature): BRepMesh takes blocks of 1 MB per face,
  # which the system allocator maps and unmaps each time (meshing 2-3.5x faster, 3-4x in parallel)
  list(APPEND VCPKG_CMAKE_CONFIGURE_OPTIONS "-DUSE_MMGR_TYPE=TBB")
endif()
