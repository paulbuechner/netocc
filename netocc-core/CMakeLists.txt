cmake_minimum_required(VERSION 3.24)

# NetOcc<Module>: the SWIG C# wrappers of one OCCT module (build/generated/cxx/NetOcc<Module>) linked against OCCT from
# vcpkg, one library each (a Windows DLL exports at most 65535 functions). NetOccRuntime: the hand-written src/Native.
# Driven by build.py:  python build.py native --triplet x64-windows
project(netocc-core LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(NETOCC_GENERATED_DIR "${CMAKE_SOURCE_DIR}/build/generated/cxx" CACHE PATH "SWIG output (C++ wrappers)")
set(NETOCC_OCCT_RUNTIME_DIR "" CACHE PATH "Directory with the OCCT + third-party shared libraries to stage")
set(NETOCC_OCCT_VERSION "" CACHE STRING "The OCCT version the modules are generated from (build.py passes it)")

if(NOT NETOCC_OCCT_VERSION)
  message(FATAL_ERROR "NETOCC_OCCT_VERSION not set; build through: python build.py native --triplet <triplet>")
endif()
find_package(OpenCASCADE ${NETOCC_OCCT_VERSION} EXACT CONFIG REQUIRED)
message(STATUS "OCCT ${OpenCASCADE_VERSION}: ${OpenCASCADE_INSTALL_PREFIX}")

file(GLOB NETOCC_LIBRARY_DIRS LIST_DIRECTORIES true CONFIGURE_DEPENDS "${NETOCC_GENERATED_DIR}/NetOcc*")
if(NOT NETOCC_LIBRARY_DIRS)
  message(FATAL_ERROR "No SWIG output in ${NETOCC_GENERATED_DIR}; run: python build.py generate")
endif()

# No dot in the names: the Windows loader would treat ".Native" as the extension.
function(netocc_library name)
  add_library(${name} SHARED ${ARGN})
  if(MSVC)
    # /bigobj: SWIG wrappers exceed the default section limit. /Zc:__cplusplus: SwigValueWrapper moves results only where
    # __cplusplus says C++11 (a move-only class returned by value); OCCT's headers go by _MSVC_LANG either way.
    # /wd4996: the wrappers call OCCT's deprecated members on purpose; C# marks them [Obsolete].
    target_compile_options(${name} PRIVATE /bigobj /EHsc /permissive- /W3 /Zc:__cplusplus /wd4996)
  else()
    target_compile_options(${name} PRIVATE -fvisibility=hidden -Wno-deprecated-declarations)
    if(APPLE)
      set_target_properties(${name} PROPERTIES BUILD_RPATH "@loader_path" INSTALL_RPATH "@loader_path")
    else()
      set_target_properties(${name} PROPERTIES BUILD_RPATH "$ORIGIN" INSTALL_RPATH "$ORIGIN")
      # GNU ld allows undefined symbols in a shared library; a member OCCT lacks here would fail at its first call
      target_link_options(${name} PRIVATE "LINKER:--no-undefined")
    endif()
  endif()
  # Stage layout = runtimes/<rid>/native: the NetOcc libraries plus every OCCT/third-party shared library.
  install(TARGETS ${name} RUNTIME DESTINATION . LIBRARY DESTINATION .)
endfunction()

foreach(dir ${NETOCC_LIBRARY_DIRS})
  get_filename_component(library ${dir} NAME)
  file(GLOB sources CONFIGURE_DEPENDS "${dir}/*_wrap.cxx")
  netocc_library(${library} ${sources})
  # the directory above OCCT's holds its third-party headers (rapidjson for RWGltf)
  target_include_directories(${library} PRIVATE
    ${OpenCASCADE_INCLUDE_DIR}
    ${OpenCASCADE_INCLUDE_DIR}/..
    ${CMAKE_SOURCE_DIR}/src/SWIG_files/headers)
  target_link_libraries(${library} PRIVATE
    ${OpenCASCADE_FoundationClasses_LIBRARIES}
    ${OpenCASCADE_ModelingData_LIBRARIES}
    ${OpenCASCADE_ModelingAlgorithms_LIBRARIES}
    ${OpenCASCADE_Visualization_LIBRARIES}
    ${OpenCASCADE_ApplicationFramework_LIBRARIES}
    ${OpenCASCADE_DataExchange_LIBRARIES})
endforeach()

# no OCCT: the memory streams of src/SWIG_files/common/Streams.i
netocc_library(NetOccRuntime ${CMAKE_SOURCE_DIR}/src/Native/NetOccStreams.cxx)
if(NETOCC_OCCT_RUNTIME_DIR)
  if(WIN32)
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.dll")
  elseif(APPLE)
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.dylib")
  else()
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "*.so*")
  endif()
endif()
