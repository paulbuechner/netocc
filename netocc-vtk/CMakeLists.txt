cmake_minimum_required(VERSION 3.24)

# NetOccVtk: the SWIG C# wrappers of OCCT's TKIVtk (build/generated/cxx/NetOccVtk), linked against OCCT and VTK from
# netocc-core's vcpkg install (OCCT with its vtk feature, VTK from NetVtk's registry). The stage holds NetOccVtk and TKIVtk
# only: OCCT's other libraries come with NetOcc.runtime, VTK's with NetVtk.runtime.
# Driven by build.py:  python build.py native --triplet x64-windows
project(netocc-vtk LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(NETOCC_GENERATED_DIR "${CMAKE_SOURCE_DIR}/build/generated/cxx" CACHE PATH "SWIG output (C++ wrappers)")
set(NETOCC_OCCT_RUNTIME_DIR "" CACHE PATH "Directory with OCCT's shared libraries, TKIVtk's among them")
set(NETOCC_OCCT_VERSION "" CACHE STRING "The OCCT version the modules are generated from (build.py passes it)")

if(NOT NETOCC_OCCT_VERSION)
  message(FATAL_ERROR "NETOCC_OCCT_VERSION not set; build through: python build.py native --triplet <triplet>")
endif()
find_package(OpenCASCADE ${NETOCC_OCCT_VERSION} EXACT CONFIG REQUIRED)
find_package(VTK CONFIG REQUIRED)
message(STATUS "OCCT ${OpenCASCADE_VERSION}, VTK ${VTK_VERSION}: ${OpenCASCADE_INSTALL_PREFIX}")
if(NOT TARGET TKIVtk)
  message(FATAL_ERROR "OCCT has no TKIVtk: netocc-core's vcpkg.json builds opencascade with its vtk feature")
endif()

file(GLOB sources CONFIGURE_DEPENDS "${NETOCC_GENERATED_DIR}/NetOccVtk/*_wrap.cxx")
if(NOT sources)
  message(FATAL_ERROR "No SWIG output in ${NETOCC_GENERATED_DIR}/NetOccVtk; run: python build.py generate")
endif()

# No dot in the name: the Windows loader would treat ".Vtk" as the extension.
add_library(NetOccVtk SHARED ${sources})
if(MSVC)
  # as netocc-core's libraries: /bigobj for SWIG's wrappers, /Zc:__cplusplus for SwigValueWrapper's moves, OCCT's
  # deprecated members on purpose (C# marks them [Obsolete])
  target_compile_options(NetOccVtk PRIVATE /bigobj /EHsc /permissive- /W3 /Zc:__cplusplus /wd4996)
else()
  target_compile_options(NetOccVtk PRIVATE -fvisibility=hidden -Wno-deprecated-declarations)
  if(APPLE)
    set_target_properties(NetOccVtk PROPERTIES BUILD_RPATH "@loader_path" INSTALL_RPATH "@loader_path")
  else()
    set_target_properties(NetOccVtk PROPERTIES BUILD_RPATH "$ORIGIN" INSTALL_RPATH "$ORIGIN")
    target_link_options(NetOccVtk PRIVATE "LINKER:--no-undefined")
  endif()
endif()
# NetOcc's module headers too: these modules' headers include the nested headers of the ones they import
target_include_directories(NetOccVtk PRIVATE
  ${OpenCASCADE_INCLUDE_DIR}
  ${OpenCASCADE_INCLUDE_DIR}/..
  ${CMAKE_SOURCE_DIR}/src/SWIG_files/headers
  ${CMAKE_SOURCE_DIR}/../netocc-core/src/SWIG_files/headers)
target_link_libraries(NetOccVtk PRIVATE
  TKIVtk
  ${OpenCASCADE_FoundationClasses_LIBRARIES}
  ${OpenCASCADE_ModelingData_LIBRARIES}
  ${OpenCASCADE_ModelingAlgorithms_LIBRARIES}
  ${OpenCASCADE_Visualization_LIBRARIES}
  ${VTK_LIBRARIES})

install(TARGETS NetOccVtk RUNTIME DESTINATION . LIBRARY DESTINATION .)
if(NETOCC_OCCT_RUNTIME_DIR)
  if(WIN32)
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "TKIVtk.dll")
  elseif(APPLE)
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "libTKIVtk.*dylib")
  else()
    install(DIRECTORY "${NETOCC_OCCT_RUNTIME_DIR}/" DESTINATION . FILES_MATCHING PATTERN "libTKIVtk.so*")
  endif()
endif()
