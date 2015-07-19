# MiniSharp

*An optimizing compiler from a subset of C# to JavaScript*

This is an experiment to see if C# can be a good language for the web.
There are lots of existing C# to JavaScript compilers out there but they are focused on compatibility instead of generating tight code.
Most of them are also proprietary and/or Windows-only.

Goals:
* Cross-platform (using [Mono](http://www.mono-project.com/))
* Readable debug output
* Source maps
* Minification
* Tree shaking
* Constant folding
* Devirtualization
* Inlining

Non-goals:
* Support all C# language features (reflection, for example)
* Compile to asm.js/WebAssembly
