default: compile

compile: bin/shade.exe

server: compile
	mono --debug bin/shade.exe --server

bin/shade.exe: Makefile src/*.cs bin/ICSharpCode.NRefactory.dll bin/ICSharpCode.NRefactory.CSharp.dll
	mcs -debug src/*.cs -r:bin/ICSharpCode.NRefactory.dll -r:bin/ICSharpCode.NRefactory.CSharp.dll -out:bin/shade.exe

bin/ICSharpCode.NRefactory.dll:
	make unzip

bin/ICSharpCode.NRefactory.CSharp.dll:
	make unzip

unzip: bin/icsharpcode.nrefactory.5.5.1.nupkg
	mkdir -p bin/temp
	unzip bin/icsharpcode.nrefactory.5.5.1.nupkg -d bin/temp
	mv bin/temp/lib/Net40/ICSharpCode.NRefactory.dll bin/temp/lib/Net40/ICSharpCode.NRefactory.CSharp.dll bin
	rm -fr bin/temp

bin/icsharpcode.nrefactory.5.5.1.nupkg: | bin
	curl https://www.nuget.org/api/v2/package/ICSharpCode.NRefactory/5.5.1 -Lo bin/icsharpcode.nrefactory.5.5.1.nupkg

bin:
	mkdir -p bin

clean:
	rm -fr bin/shade.exe*

reset:
	rm -fr bin
