fn main() {
    // Generate C# bindings into ../../bindings/NativeMethods.g.cs
    let out_dir = std::path::Path::new("../../bindings");
    let _ = std::fs::create_dir_all(out_dir);

    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .csharp_dll_name("myelin_ffi")
        .csharp_namespace("Myelin.Core.Native")
        .csharp_class_name("NativeMethods")
        .generate_csharp_file(out_dir.join("NativeMethods.g.cs"))
        .unwrap();
}
