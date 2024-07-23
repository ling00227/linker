@echo off

rd /s /q public\\extends
rd /s /q public\\publish
rd /s /q public\\publish-zip
mkdir public\\publish-zip


cd linker.web
call npm install
call npm run build 
cd ../

for %%r in (win-x64,win-arm64) do (
	dotnet publish ./linker.service -c release -f net8.0 -o public/extends/%%r/linker-%%r/  -r %%r -p:PublishAot=true -p:PublishTrimmed=true  --self-contained true -p:TieredPGO=true  -p:DebugType=none -p:DebugSymbols=false  -p:EnableCompressionInSingleFile=true -p:DebuggerSupport=false -p:EnableUnsafeBinaryFormatterSerialization=false -p:EnableUnsafeUTF7Encoding=false -p:HttpActivityPropagationSupport=false -p:InvariantGlobalization=true  -p:MetadataUpdaterSupport=false  -p:UseSystemResourceKeys=true
	echo F|xcopy "linker.tray.win\\dist\\*" "public\\extends\\%%r\\linker-%%r\\*"  /s /f /h /y
	echo F|xcopy "linker\\msquic.dll" "public\\extends\\%%r\\linker-%%r\\msquic.dll"  /s /f /h /y
	echo F|xcopy "linker\\msquic-openssl3-x64.dll" "public\\extends\\%%r\\linker-%%r\\msquic-openssl.dll"  /s /f /h /y
        echo F|xcopy "linker\\plugins\\tuntap\\tun2socks-%%r" "public\\extends\\%%r\\linker-%%r\\plugins\\tuntap\\tun2socks.exe"  /s /f /h /y
)
echo F|xcopy "linker\\msquic-arm64.dll" "public\\extends\\win-arm64\\linker-win-arm64\\msquic.dll"  /s /f /h /y
echo F|xcopy "linker\\msquic-openssl3-arm64.dll" "public\\extends\\win-arm64\\linker-win-arm64\\msquic-openssl.dll"  /s /f /h /y


for %%r in (linux-x64,linux-arm64,osx-x64,osx-arm64) do (
	echo F|xcopy "linker\\plugins\\tuntap\\tun2socks-%%r" "public\\extends\\%%r\\linker-%%r\\plugins\\tuntap\\tun2socks"  /s /f /h /y
)
for %%r in (x64,arm64) do (
	echo F|xcopy "linker\\plugins\\tuntap\\tun2socks-linux-%%r" "public\\extends\\linux-musl-%%r\\linker-linux-musl-%%r\\plugins\\tuntap\\tun2socks"  /s /f /h /y
)

for %%r in (win-x64,win-arm64,linux-x64,linux-arm64,linux-musl-x64,linux-musl-arm64,osx-x64,osx-arm64) do (
	
	dotnet publish ./linker -c release -f net8.0 -o ./public/publish/%%r/linker-%%r  -r %%r  -p:PublishSingleFile=true -p:PublishTrimmed=true  --self-contained true  -p:TrimMode=partial -p:TieredPGO=true  -p:DebugType=none -p:DebugSymbols=false -p:EnableCompressionInSingleFile=true -p:DebuggerSupport=false -p:EnableUnsafeBinaryFormatterSerialization=false -p:EnableUnsafeUTF7Encoding=false -p:HttpActivityPropagationSupport=false -p:InvariantGlobalization=true  -p:MetadataUpdaterSupport=false  -p:UseSystemResourceKeys=true
	echo F|xcopy "public\\extends\\%%r\\linker-%%r\\*" "public\\publish\\%%r\\linker-%%r\\*"  /s /f /h /y

	echo F|xcopy "public\\extends\\any\\*" "public\\publish\\%%r\\linker-%%r\\*"  /s /f /h /y

	7z a -tzip ./public/publish-zip/linker-%%r.zip ./public/publish/%%r/*
)