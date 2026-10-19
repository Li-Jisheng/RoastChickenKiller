cmake_minimum_required(VERSION 3.24)

project(chicken_killer_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    add_compile_options(/W4 /permissive- /utf-8 /EHsc)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

add_library(ck_core STATIC
    src/core/combat_core.cpp
)
target_include_directories(ck_core PUBLIC include src)
target_compile_definitions(ck_core PUBLIC CK_STATIC)

add_library(ck_combat SHARED
    src/c_api.cpp
)
target_link_libraries(ck_combat PRIVATE ck_core)
target_compile_definitions(ck_combat PRIVATE CK_EXPORTS)
set_target_properties(ck_combat PROPERTIES
    OUTPUT_NAME "ck_combat"
    PREFIX ""
    FOLDER "ChickenKiller"
)

enable_testing()

add_executable(ck_native_tests
    tests/test_main.cpp
    src/c_api.cpp
)
target_link_libraries(ck_native_tests PRIVATE ck_core)
target_include_directories(ck_native_tests PRIVATE include src)
target_compile_definitions(ck_native_tests PRIVATE CK_STATIC)
set_target_properties(ck_native_tests PROPERTIES FOLDER "ChickenKiller")

add_test(NAME ck_native_tests COMMAND ck_native_tests)
