# SharpDXLab

BasicCompute11 is DirectCompute Sample but implement in C#.

https://github.com/walbourn/directx-sdk-samples/tree/master/BasicCompute11

install SharpDX

    Install-Package SharpDX.Direct3D11
    Install-Package SharpDX.D3DCompiler
    
set post build event

    copy "%ProgramFiles(x86)%\Windows Kits\10\Redist\D3D\x86\d3dcompiler_47.dll" $(TargetDir)
