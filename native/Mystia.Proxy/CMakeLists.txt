cmake_minimum_required(VERSION 3.20)
project(Mystia.Proxy LANGUAGES CXX)

# The build output keeps the project name: the launcher directory must never hold a literal
# version.dll, or a launcher process that imports VERSION.dll would hijack itself. The installer in
# src/Mystia.Syringe copies this file into the game directory under the name version.dll.
add_library(Mystia.Proxy SHARED proxy.cpp version.def)
set_target_properties(Mystia.Proxy PROPERTIES PREFIX "")
target_compile_features(Mystia.Proxy PRIVATE cxx_std_17)
if(MSVC)
  target_compile_options(Mystia.Proxy PRIVATE /W4 /EHsc /utf-8)
  target_compile_definitions(Mystia.Proxy PRIVATE UNICODE _UNICODE)
endif()
